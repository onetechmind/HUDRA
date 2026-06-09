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

            public FakeScope(string name, Func<GamepadEvent, bool>? handler = null)
            {
                Name = name;
                _handler = handler ?? (_ => true);
            }

            public string Name { get; }

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
        public void Pop_RemovesScope_AndEverythingAboveIt()
        {
            var router = new InputRouter();
            var a = new FakeScope("a");
            var b = new FakeScope("b");
            var c = new FakeScope("c");
            router.Push(a);
            router.Push(b);
            router.Push(c);

            router.Pop(b);

            Assert.Equal(new IInputScope[] { a }, router.Stack);
            Assert.Equal(1, b.PoppedCount);
            Assert.Equal(1, c.PoppedCount);
            Assert.Equal(0, a.PoppedCount);
        }

        [Fact]
        public void Pop_UnknownScope_IsNoOp()
        {
            var router = new InputRouter();
            var a = new FakeScope("a");
            router.Push(a);

            router.Pop(new FakeScope("ghost"));

            Assert.Equal(new IInputScope[] { a }, router.Stack);
        }

        [Fact]
        public void PopWhile_RemovesOnlyMatchingTopScopes()
        {
            var router = new InputRouter();
            var keep = new FakeScope("keep");
            var t1 = new FakeScope("t1");
            var t2 = new FakeScope("t2");
            router.Push(keep);
            router.Push(t1);
            router.Push(t2);

            router.PopWhile(s => s.Name.StartsWith("t"));

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
                router.Pop(self!);
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
                router.Pop(middle); // removes middle mid-dispatch
                return false;       // ...but does not consume
            });
            router.Push(bottom);
            router.Push(middle);
            router.Push(top);

            var consumer = router.Dispatch(Press(GamepadAction.Accept));

            // top was popped along with middle (tolerant pop), so bottom consumes
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
            router.Pop(scope);

            Assert.Equal(2, changes);
        }
    }
}
