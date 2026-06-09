using System;
using System.Collections.Generic;
using System.Linq;
using HUDRA.Services.GamepadInput;
using Xunit;

namespace HUDRA.Tests
{
    public class GamepadRepeatTrackerTests
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);

        private static GamepadRepeatTracker CreateTracker() => new(InitialDelay, RepeatInterval);

        private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

        private static HashSet<GamepadAction> Held(params GamepadAction[] actions) => new(actions);

        [Fact]
        public void Press_FiresOnce_OnFirstTick()
        {
            var tracker = CreateTracker();

            var events = tracker.Update(Held(GamepadAction.Accept), Ms(0));

            var ev = Assert.Single(events);
            Assert.Equal(GamepadAction.Accept, ev.Action);
            Assert.False(ev.IsRepeat);
        }

        [Fact]
        public void HeldAction_DoesNotRefire_WhileHeld()
        {
            var tracker = CreateTracker();
            tracker.Update(Held(GamepadAction.Accept), Ms(0));

            // Action buttons never repeat, no matter how long they are held
            for (double t = 16; t < 2000; t += 16)
            {
                Assert.Empty(tracker.Update(Held(GamepadAction.Accept), Ms(t)));
            }
        }

        [Fact]
        public void Directional_DoesNotRepeat_BeforeInitialDelay()
        {
            var tracker = CreateTracker();
            tracker.Update(Held(GamepadAction.NavDown), Ms(0));

            for (double t = 16; t < 400; t += 16)
            {
                Assert.Empty(tracker.Update(Held(GamepadAction.NavDown), Ms(t)));
            }
        }

        [Fact]
        public void Directional_Repeats_AfterInitialDelay_AtInterval()
        {
            var tracker = CreateTracker();
            tracker.Update(Held(GamepadAction.NavDown), Ms(0));

            var fired = new List<double>();
            for (double t = 16; t <= 1000; t += 16)
            {
                foreach (var ev in tracker.Update(Held(GamepadAction.NavDown), Ms(t)))
                {
                    Assert.True(ev.IsRepeat);
                    fired.Add(t);
                }
            }

            Assert.NotEmpty(fired);
            // First repeat lands on the first tick at/after the 400ms initial delay
            Assert.Equal(400, fired[0], precision: 0);
            // Subsequent repeats spaced >= repeat interval, roughly 110-126ms at 16ms ticks
            for (int i = 1; i < fired.Count; i++)
            {
                double gap = fired[i] - fired[i - 1];
                Assert.InRange(gap, RepeatInterval.TotalMilliseconds, RepeatInterval.TotalMilliseconds + 16);
            }
        }

        [Fact]
        public void ReleaseAndRepress_FiresNewPress()
        {
            var tracker = CreateTracker();
            tracker.Update(Held(GamepadAction.NavLeft), Ms(0));

            Assert.Empty(tracker.Update(Held(), Ms(16))); // release

            var events = tracker.Update(Held(GamepadAction.NavLeft), Ms(32));
            var ev = Assert.Single(events);
            Assert.False(ev.IsRepeat);
        }

        [Fact]
        public void Release_ResetsInitialDelay()
        {
            var tracker = CreateTracker();
            tracker.Update(Held(GamepadAction.NavUp), Ms(0));
            tracker.Update(Held(), Ms(350));                    // release just before repeat began
            tracker.Update(Held(GamepadAction.NavUp), Ms(366)); // re-press

            // No repeat until 366 + 400
            Assert.Empty(tracker.Update(Held(GamepadAction.NavUp), Ms(700)));
            var events = tracker.Update(Held(GamepadAction.NavUp), Ms(766));
            var ev = Assert.Single(events);
            Assert.True(ev.IsRepeat);
        }

        [Fact]
        public void MultipleActions_TrackedIndependently()
        {
            var tracker = CreateTracker();

            var first = tracker.Update(Held(GamepadAction.NavDown, GamepadAction.Accept), Ms(0));
            Assert.Equal(2, first.Count);
            Assert.All(first, ev => Assert.False(ev.IsRepeat));

            // After the initial delay only the directional action repeats
            var later = tracker.Update(Held(GamepadAction.NavDown, GamepadAction.Accept), Ms(500));
            var ev = Assert.Single(later);
            Assert.Equal(GamepadAction.NavDown, ev.Action);
            Assert.True(ev.IsRepeat);
        }

        [Fact]
        public void Reset_ForgetsHeldState()
        {
            var tracker = CreateTracker();
            tracker.Update(Held(GamepadAction.Accept), Ms(0));

            tracker.Reset();

            // Same action still physically held reads as a fresh press after reset
            var events = tracker.Update(Held(GamepadAction.Accept), Ms(16));
            var ev = Assert.Single(events);
            Assert.False(ev.IsRepeat);
        }

        [Theory]
        [InlineData(GamepadAction.NavUp, true)]
        [InlineData(GamepadAction.NavDown, true)]
        [InlineData(GamepadAction.NavLeft, true)]
        [InlineData(GamepadAction.NavRight, true)]
        [InlineData(GamepadAction.Accept, false)]
        [InlineData(GamepadAction.Back, false)]
        [InlineData(GamepadAction.X, false)]
        [InlineData(GamepadAction.Y, false)]
        [InlineData(GamepadAction.LB, false)]
        [InlineData(GamepadAction.RB, false)]
        [InlineData(GamepadAction.LT, false)]
        [InlineData(GamepadAction.RT, false)]
        public void OnlyDirectionalActions_AreRepeatable(GamepadAction action, bool repeatable)
        {
            Assert.Equal(repeatable, GamepadRepeatTracker.IsRepeatable(action));

            var tracker = CreateTracker();
            tracker.Update(Held(action), Ms(0));
            var repeats = tracker.Update(Held(action), Ms(600));
            Assert.Equal(repeatable, repeats.Any());
        }
    }
}
