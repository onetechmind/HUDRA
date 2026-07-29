using System;
using System.Collections.Generic;
using System.Linq;
using HUDRA.Services.GamepadInput;
using Xunit;

namespace HUDRA.Tests
{
    public class StuckInputGuardTests
    {
        private const int Device = 1;

        private static StuckInputGuard Guard() => new(
            nonRepeatableStuckAfter: TimeSpan.FromSeconds(8),
            directionalStuckAfter: TimeSpan.FromSeconds(20),
            neverNeutralSuspectAfter: TimeSpan.FromSeconds(3));

        private static HashSet<GamepadAction> Held(params GamepadAction[] actions) => new(actions);

        private static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

        /// <summary>Establishes that the device reports honestly (has been neutral).</summary>
        private static void Settle(StuckInputGuard guard) => guard.Filter(Device, Held(), Sec(0));

        [Fact]
        public void PassesThroughNormalInput()
        {
            var guard = Guard();
            Settle(guard);

            var result = guard.Filter(Device, Held(GamepadAction.LB), Sec(0.1));

            Assert.Equal(new[] { GamepadAction.LB }, result);
            Assert.Empty(guard.MaskedActions);
        }

        [Fact]
        public void NonRepeatableHeldPastThreshold_IsMasked()
        {
            var guard = Guard();
            Settle(guard);

            // A real user can hold a shoulder button for a few seconds.
            for (double t = 0.1; t < 8.0; t += 1.0)
            {
                Assert.Contains(GamepadAction.LB, guard.Filter(Device, Held(GamepadAction.LB), Sec(t)));
            }

            // Beyond the threshold it is treated as latched hardware.
            Assert.DoesNotContain(GamepadAction.LB, guard.Filter(Device, Held(GamepadAction.LB), Sec(8.2)));
            Assert.Contains(GamepadAction.LB, guard.MaskedActions);
        }

        [Fact]
        public void MaskedAction_UnmasksOnRelease_AndWorksAgain()
        {
            var guard = Guard();
            Settle(guard);
            guard.Filter(Device, Held(GamepadAction.LB), Sec(0.1));
            guard.Filter(Device, Held(GamepadAction.LB), Sec(9));
            Assert.Contains(GamepadAction.LB, guard.MaskedActions);

            // Release (another button still held, so this is not the neutral path).
            guard.Filter(Device, Held(GamepadAction.Accept), Sec(9.1));
            Assert.DoesNotContain(GamepadAction.LB, guard.MaskedActions);

            // A genuine later press passes through again.
            Assert.Contains(GamepadAction.LB, guard.Filter(Device, Held(GamepadAction.LB), Sec(9.2)));
        }

        [Fact]
        public void DirectionalHasLongerThreshold()
        {
            var guard = Guard();
            Settle(guard);
            guard.Filter(Device, Held(GamepadAction.NavDown), Sec(0.1));

            // Still legitimate at 10s: holding a direction to scroll a long list.
            Assert.Contains(GamepadAction.NavDown, guard.Filter(Device, Held(GamepadAction.NavDown), Sec(10)));

            Assert.DoesNotContain(GamepadAction.NavDown, guard.Filter(Device, Held(GamepadAction.NavDown), Sec(20.5)));
        }

        [Fact]
        public void MaskingStuckDirection_LetsOtherDirectionsThrough()
        {
            // The core of the "cannot traverse anything" symptom: a stuck Up must not
            // prevent Down from being seen. Masking happens on the raw set, so after
            // filtering, reduction picks the direction the user is actually pressing.
            var guard = Guard();
            Settle(guard);
            guard.Filter(Device, Held(GamepadAction.NavUp), Sec(0.1));
            guard.Filter(Device, Held(GamepadAction.NavUp), Sec(21));   // Up now masked

            var raw = guard.Filter(Device, Held(GamepadAction.NavUp, GamepadAction.NavDown), Sec(21.1));
            Assert.DoesNotContain(GamepadAction.NavUp, raw);
            Assert.Contains(GamepadAction.NavDown, raw);

            var reduced = new HashSet<GamepadAction>();
            DirectionReducer.Reduce(raw, reduced);
            Assert.Equal(new[] { GamepadAction.NavDown }, reduced);
        }

        [Fact]
        public void DeviceNeverNeutral_BecomesSuspect_AndIsFullyMasked()
        {
            var guard = Guard();

            // Freshly seen device already reporting a held button: classic ghost.
            guard.Filter(Device, Held(GamepadAction.LB), Sec(0));
            Assert.False(guard.IsDeviceSuspect(Device));

            var result = guard.Filter(Device, Held(GamepadAction.LB), Sec(3.1));

            Assert.True(guard.IsDeviceSuspect(Device));
            Assert.Empty(result);
        }

        [Fact]
        public void SuspectDevice_ClearedByOneNeutralReading()
        {
            var guard = Guard();
            guard.Filter(Device, Held(GamepadAction.LB), Sec(0));
            guard.Filter(Device, Held(GamepadAction.LB), Sec(3.1));
            Assert.True(guard.IsDeviceSuspect(Device));

            guard.Filter(Device, Held(), Sec(3.2));

            Assert.False(guard.IsDeviceSuspect(Device));
            Assert.Contains(GamepadAction.LB, guard.Filter(Device, Held(GamepadAction.LB), Sec(3.3)));
        }

        [Fact]
        public void DeviceThatGoesNeutralEarly_IsNeverSuspect()
        {
            var guard = Guard();
            guard.Filter(Device, Held(), Sec(0));

            // Held continuously for longer than the suspect window, but the device
            // has proven it can report neutral, so only the stuck thresholds apply.
            guard.Filter(Device, Held(GamepadAction.Accept), Sec(0.1));
            var result = guard.Filter(Device, Held(GamepadAction.Accept), Sec(4));

            Assert.False(guard.IsDeviceSuspect(Device));
            Assert.Contains(GamepadAction.Accept, result);
        }

        [Fact]
        public void DevicesAreTrackedIndependently()
        {
            var guard = Guard();
            guard.Filter(1, Held(), Sec(0));
            guard.Filter(2, Held(), Sec(0));

            guard.Filter(1, Held(GamepadAction.LB), Sec(0.1));
            guard.Filter(1, Held(GamepadAction.LB), Sec(9));      // device 1 stuck
            var second = guard.Filter(2, Held(GamepadAction.LB), Sec(9));

            Assert.Contains(GamepadAction.LB, second);
        }

        [Fact]
        public void ForgetDevice_DropsItsMaskState()
        {
            var guard = Guard();
            Settle(guard);
            guard.Filter(Device, Held(GamepadAction.LB), Sec(0.1));
            guard.Filter(Device, Held(GamepadAction.LB), Sec(9));
            Assert.NotEmpty(guard.MaskedActions);

            guard.ForgetDevice(Device);

            Assert.Empty(guard.MaskedActions);
            Assert.False(guard.IsDeviceSuspect(Device));
        }

        [Fact]
        public void Reset_DropsAllState()
        {
            var guard = Guard();
            Settle(guard);
            guard.Filter(Device, Held(GamepadAction.LB), Sec(0.1));
            guard.Filter(Device, Held(GamepadAction.LB), Sec(9));

            guard.Reset();

            Assert.Empty(guard.MaskedActions);
        }

        [Fact]
        public void MaskChanged_FiresOnMaskAndOnRelease()
        {
            var guard = Guard();
            var messages = new List<string>();
            guard.MaskChanged += messages.Add;

            Settle(guard);
            guard.Filter(Device, Held(GamepadAction.LB), Sec(0.1));
            guard.Filter(Device, Held(GamepadAction.LB), Sec(9));
            Assert.Single(messages);

            guard.Filter(Device, Held(), Sec(9.1));
            Assert.Equal(2, messages.Count);
        }
    }

    public class DirectionReducerTests
    {
        private static HashSet<GamepadAction> Set(params GamepadAction[] actions) => new(actions);

        [Fact]
        public void KeepsAtMostOneDirection()
        {
            var result = new HashSet<GamepadAction>();
            DirectionReducer.Reduce(Set(GamepadAction.NavUp, GamepadAction.NavRight), result);

            Assert.Single(result, DirectionReducer.IsDirectional);
        }

        [Fact]
        public void KeepsAllNonDirectionalActions()
        {
            var result = new HashSet<GamepadAction>();
            DirectionReducer.Reduce(
                Set(GamepadAction.Accept, GamepadAction.LB, GamepadAction.RT, GamepadAction.NavDown), result);

            Assert.Contains(GamepadAction.Accept, result);
            Assert.Contains(GamepadAction.LB, result);
            Assert.Contains(GamepadAction.RT, result);
            Assert.Contains(GamepadAction.NavDown, result);
        }

        [Fact]
        public void PrefersUpThenDownThenLeftThenRight()
        {
            var result = new HashSet<GamepadAction>();

            DirectionReducer.Reduce(Set(GamepadAction.NavDown, GamepadAction.NavUp), result);
            Assert.Equal(new[] { GamepadAction.NavUp }, result);

            DirectionReducer.Reduce(Set(GamepadAction.NavLeft, GamepadAction.NavDown), result);
            Assert.Equal(new[] { GamepadAction.NavDown }, result);

            DirectionReducer.Reduce(Set(GamepadAction.NavRight, GamepadAction.NavLeft), result);
            Assert.Equal(new[] { GamepadAction.NavLeft }, result);
        }

        [Fact]
        public void EmptyInputProducesEmptyOutput()
        {
            var result = new HashSet<GamepadAction> { GamepadAction.Accept };
            DirectionReducer.Reduce(Set(), result);
            Assert.Empty(result);
        }
    }
}
