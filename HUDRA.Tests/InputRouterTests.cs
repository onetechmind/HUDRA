using System;
using System.Collections.Generic;
using HUDRA.Services.GamepadInput;
using Xunit;

namespace HUDRA.Tests
{
    public class InputRouterTests
    {
        private sealed class FakeScope : IInputScope
        {
            private readonly Func<GamepadEvent, bool> _handler;
            public List<GamepadAction> Seen { get; } = new();
            public int PushedCount { get; private set; }
            public int PoppedCount { get; private set; }
            public InputRouter? Router { get; private set; }

            public FakeScope(string name, Func<GamepadEvent, bool>? handler = null,
                             int layer = ScopeLayer.Page, bool valid = true)
            {
                Name = name;
                _handler = handler ?? (_ => true);
                Layer = layer;
                Valid = valid;
            }

            public string Name { get; }

            public int Layer { get; }

            /// <summary>Mutable so tests can invalidate a scope's context.</summary>
            public bool Valid { get; set; }

            /// <summary>When set, IsStillValid throws (router must treat as valid).</summary>
            public bool ThrowOnValidCheck { get; set; }

            public bool IsStillValid => ThrowOnValidCheck
                ? throw new InvalidOperationException("predicate blew up")
                : Valid;

            public bool HandleEvent(in GamepadEvent e)
            {
                Seen.Add(e.Action);
                return _handler(e);
            }

            public void OnPushed(InputRouter router) { Router = router; PushedCount++; }
            public void OnPopped() => PoppedCount++;
        }

        private static GamepadEvent Press(GamepadAction action) => new(action, IsRepeat: false, TimeSpan.Zero);

        [Fact]
        public void Dispatch_TopScopeConsumes_LowerScopesNeverSee()
        {
            var router = new InputRouter();
            var bottom = new FakeScope("bottom");
            var top = new FakeScope("top");
            router.Push(bottom);
            router.Push(top);

            var consumer = router.Dispatch(Press(GamepadAction.Accept));

            Assert.Same(top, consumer);
            Assert.Single(top.Seen);
            Assert.Empty(bottom.Seen);
        }

        [Fact]
        public void Dispatch_FallsThrough_WhenTopDoesNotConsume()
        {
            var router = new InputRouter();
            var bottom = new FakeScope("bottom");
            var top = new FakeScope("top", e => e.Action != GamepadAction.LB);
            router.Push(bottom);
            router.Push(top);

            var consumer = router.Dispatch(Press(GamepadAction.LB));

            Assert.Same(bottom, consumer);
            Assert.Equal(new[] { GamepadAction.LB }, top.Seen);
            Assert.Equal(new[] { GamepadAction.LB }, bottom.Seen);
        }

        [Fact]
        public void Dispatch_NoConsumer_ReturnsNull()
        {
            var router = new InputRouter();
            var scope = new FakeScope("s", _ => false);
            router.Push(scope);

            Assert.Null(router.Dispatch(Press(GamepadAction.X)));
        }

        [Fact]
        public void Push_IsIdempotent()
        {
            var router = new InputRouter();
            var scope = new FakeScope("s");
            router.Push(scope);
            router.Push(scope);

            Assert.Single(router.Stack);
            Assert.Equal(1, scope.PushedCount);
        }

        [Fact]
        public void Remove_DoesNotCascade_LeavesScopesAboveIntact()
        {
            var router = new InputRouter();
            var a = new FakeScope("a");
            var b = new FakeScope("b");
            var c = new FakeScope("c");
            router.Push(a);
            router.Push(b);
            router.Push(c);

            router.Remove(b);

            // Removing a scope must never take unrelated scopes with it: the old
            // cascading behaviour is exactly how a transient scope's removal could
            // silently delete a page scope stacked above it.
            Assert.Equal(new IInputScope[] { a, c }, router.Stack);
            Assert.Equal(1, b.PoppedCount);
            Assert.Equal(0, c.PoppedCount);
            Assert.Equal(0, a.PoppedCount);
        }

        [Fact]
        public void Pop_UnknownScope_IsNoOp()
        {
            var router = new InputRouter();
            var a = new FakeScope("a");
            router.Push(a);

            router.Remove(new FakeScope("ghost"));

            Assert.Equal(new IInputScope[] { a }, router.Stack);
        }

        [Fact]
        public void RemoveWhere_RemovesAllMatchingScopes()
        {
            var router = new InputRouter();
            var keep = new FakeScope("keep");
            var t1 = new FakeScope("t1");
            var t2 = new FakeScope("t2");
            router.Push(keep);
            router.Push(t1);
            router.Push(t2);

            router.RemoveWhere(s => s.Name.StartsWith("t"));

            Assert.Equal(new IInputScope[] { keep }, router.Stack);
            Assert.Equal(1, t1.PoppedCount);
            Assert.Equal(1, t2.PoppedCount);
        }

        [Fact]
        public void Dispatch_ScopeThatPopsItself_StillConsumes()
        {
            var router = new InputRouter();
            var bottom = new FakeScope("bottom");
            FakeScope? self = null;
            self = new FakeScope("self-popping", e =>
            {
                router.Remove(self!);
                return true;
            });
            router.Push(bottom);
            router.Push(self);

            var consumer = router.Dispatch(Press(GamepadAction.Back));

            Assert.Same(self, consumer);
            Assert.Empty(bottom.Seen);
            Assert.Equal(new IInputScope[] { bottom }, router.Stack);
        }

        [Fact]
        public void Dispatch_SkipsScope_RemovedByHigherHandler()
        {
            var router = new InputRouter();
            var bottom = new FakeScope("bottom");
            var middle = new FakeScope("middle");
            var top = new FakeScope("top", e =>
            {
                router.Remove(middle); // removes middle mid-dispatch
                return false;       // ...but does not consume
            });
            router.Push(bottom);
            router.Push(middle);
            router.Push(top);

            var consumer = router.Dispatch(Press(GamepadAction.Accept));

            // top declined; middle was removed mid-dispatch so it must be skipped
            Assert.Same(bottom, consumer);
            Assert.Empty(middle.Seen);
        }

        [Fact]
        public void HasScope_And_FindScope_Work()
        {
            var router = new InputRouter();
            var scope = new FakeScope("s");
            Assert.False(router.HasScope<FakeScope>());

            router.Push(scope);

            Assert.True(router.HasScope<FakeScope>());
            Assert.Same(scope, router.FindScope<FakeScope>());
        }

        [Fact]
        public void StackChanged_FiresOnPushAndPop()
        {
            var router = new InputRouter();
            int changes = 0;
            router.StackChanged += (_, _) => changes++;

            var scope = new FakeScope("s");
            router.Push(scope);
            router.Remove(scope);

            Assert.Equal(2, changes);
        }
        // ---- Layered insertion ------------------------------------------------

        [Fact]
        public void Push_InsertsByLayer_LateLowLayerLandsBelowHigherLayer()
        {
            var router = new InputRouter();
            var page = new FakeScope("page", layer: ScopeLayer.Page);
            var modal = new FakeScope("modal", layer: ScopeLayer.Modal);
            router.Push(page);
            router.Push(modal);

            // A page-owned scope arriving late (async init continuation) must not
            // land above an already-open modal.
            var pageCustom = new FakeScope("pageCustom", layer: ScopeLayer.PageCustom);
            router.Push(pageCustom);

            Assert.Equal(new IInputScope[] { page, pageCustom, modal }, router.Stack);
            Assert.Same(modal, router.Top);
        }

        [Fact]
        public void Push_WithinSameLayer_KeepsInsertionOrder()
        {
            var router = new InputRouter();
            var first = new FakeScope("first", layer: ScopeLayer.Edit);
            var second = new FakeScope("second", layer: ScopeLayer.Edit);
            router.Push(first);
            router.Push(second);

            Assert.Equal(new IInputScope[] { first, second }, router.Stack);
        }

        [Fact]
        public void Dispatch_HighestLayerSeesEventFirst_RegardlessOfPushOrder()
        {
            var router = new InputRouter();
            var modal = new FakeScope("modal", layer: ScopeLayer.Modal);
            var shell = new FakeScope("shell", layer: ScopeLayer.Shell);
            router.Push(modal);
            router.Push(shell);

            var consumer = router.Dispatch(Press(GamepadAction.LB));

            Assert.Same(modal, consumer);
            Assert.Empty(shell.Seen);
        }

        // ---- Reaping ----------------------------------------------------------

        private static (InputRouter router, Action<TimeSpan> advance) RouterWithClock()
        {
            var now = TimeSpan.Zero;
            var router = new InputRouter(() => now);
            return (router, delta => now += delta);
        }

        [Fact]
        public void Reap_RemovesInvalidScope_OnceArmed()
        {
            var (router, advance) = RouterWithClock();
            var page = new FakeScope("page", layer: ScopeLayer.Page);
            var modal = new FakeScope("modal", layer: ScopeLayer.Modal);
            router.Push(page);
            router.Push(modal);

            // Arm it by observing a valid state, then invalidate the context.
            Assert.Equal(0, router.Reap());
            modal.Valid = false;
            advance(TimeSpan.FromMilliseconds(16));

            Assert.Equal(1, router.Reap());
            Assert.Equal(new IInputScope[] { page }, router.Stack);
            Assert.Equal(1, modal.PoppedCount);
            Assert.Equal(1, router.ReapCount);
            Assert.Equal("modal", router.LastReaped);
        }

        [Fact]
        public void Reap_LeavesUnarmedScopeAloneDuringGrace_ThenReapsIt()
        {
            var (router, advance) = RouterWithClock();
            // Invalid from the moment it is pushed: models a dialog scope pushed
            // before ShowAsync has realized the dialog's template.
            var opening = new FakeScope("opening", layer: ScopeLayer.Modal, valid: false);
            router.Push(opening);

            advance(TimeSpan.FromMilliseconds(500));
            Assert.Equal(0, router.Reap());
            Assert.Contains(opening, router.Stack);

            advance(TimeSpan.FromMilliseconds(400));   // past the 750ms grace
            Assert.Equal(1, router.Reap());
            Assert.DoesNotContain(opening, router.Stack);
        }

        [Fact]
        public void Reap_ArmedScopeIsNotProtectedByGrace()
        {
            var (router, advance) = RouterWithClock();
            var modal = new FakeScope("modal", layer: ScopeLayer.Modal);
            router.Push(modal);

            router.Reap();          // arms
            modal.Valid = false;    // context dies immediately after opening

            Assert.Equal(1, router.Reap());
        }

        [Fact]
        public void Reap_NeverRemovesShellOrPage()
        {
            var (router, _) = RouterWithClock();
            var shell = new FakeScope("shell", layer: ScopeLayer.Shell, valid: false);
            var page = new FakeScope("page", layer: ScopeLayer.Page, valid: false);
            router.Push(shell);
            router.Push(page);

            Assert.Equal(0, router.Reap());
            Assert.Equal(new IInputScope[] { shell, page }, router.Stack);
        }

        [Fact]
        public void Reap_TreatsThrowingPredicateAsValid()
        {
            var (router, advance) = RouterWithClock();
            var modal = new FakeScope("modal", layer: ScopeLayer.Modal) { ThrowOnValidCheck = true };
            router.Push(modal);

            advance(TimeSpan.FromSeconds(5));

            Assert.Equal(0, router.Reap());
            Assert.Contains(modal, router.Stack);
        }

        [Fact]
        public void Reap_RemovesEveryInvalidScope_IncludingBelowAValidOne()
        {
            var (router, advance) = RouterWithClock();
            var page = new FakeScope("page", layer: ScopeLayer.Page);
            var stalePageCustom = new FakeScope("stale", layer: ScopeLayer.PageCustom);
            var liveModal = new FakeScope("modal", layer: ScopeLayer.Modal);
            router.Push(page);
            router.Push(stalePageCustom);
            router.Push(liveModal);

            router.Reap();                  // arm both
            stalePageCustom.Valid = false;  // page navigated away underneath the modal
            advance(TimeSpan.FromMilliseconds(16));

            Assert.Equal(1, router.Reap());
            Assert.Equal(new IInputScope[] { page, liveModal }, router.Stack);
        }

        [Fact]
        public void Dispatch_ReapsStaleScopeSoLowerScopeReceivesEvent()
        {
            var (router, advance) = RouterWithClock();
            var shell = new FakeScope("shell", layer: ScopeLayer.Shell);
            var stale = new FakeScope("stale", layer: ScopeLayer.Modal);
            router.Push(shell);
            router.Push(stale);

            router.Reap();          // arm
            stale.Valid = false;    // dialog closed but nothing removed the scope
            advance(TimeSpan.FromMilliseconds(16));

            // This is the self-heal: input reaches the shell again on the very
            // next press instead of being swallowed until the app restarts.
            var consumer = router.Dispatch(Press(GamepadAction.LB));

            Assert.Same(shell, consumer);
            Assert.Empty(stale.Seen);
        }

        [Fact]
        public void Reap_FiresStackChangedOnce()
        {
            var (router, advance) = RouterWithClock();
            var a = new FakeScope("a", layer: ScopeLayer.Edit);
            var b = new FakeScope("b", layer: ScopeLayer.Modal);
            router.Push(a);
            router.Push(b);
            router.Reap();

            int changes = 0;
            router.StackChanged += (_, _) => changes++;
            a.Valid = false;
            b.Valid = false;
            advance(TimeSpan.FromMilliseconds(16));

            Assert.Equal(2, router.Reap());
            Assert.Equal(1, changes);
        }

        [Fact]
        public void Describe_ReportsLayersAndStaleness()
        {
            var (router, advance) = RouterWithClock();
            var page = new FakeScope("Page", layer: ScopeLayer.Page);
            var modal = new FakeScope("Dialog", layer: ScopeLayer.Modal);
            router.Push(page);
            router.Push(modal);
            router.Reap();

            Assert.Equal("Page(10) / Dialog(50)", router.Describe());

            modal.Valid = false;
            Assert.Contains("Dialog(50):STALE", router.Describe());
        }

        [Fact]
        public void Describe_MarksUnarmedScopeAsOpening()
        {
            var (router, _) = RouterWithClock();
            router.Push(new FakeScope("Dialog", layer: ScopeLayer.Modal, valid: false));

            Assert.Contains(":opening", router.Describe());
        }
    }
}
