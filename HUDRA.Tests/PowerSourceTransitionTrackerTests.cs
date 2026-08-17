using HUDRA.Services.Power;
using Xunit;

namespace HUDRA.Tests
{
    public class PowerSourceTransitionTrackerTests
    {
        [Fact]
        public void FirstObservation_EstablishesBaselineWithoutReportingATransition()
        {
            var tracker = new PowerSourceTransitionTracker();

            // Windows delivers this immediately on registration; startup has already
            // applied TDP, so it must not trigger a re-apply.
            Assert.False(tracker.TryObserve(0, out var state));
            Assert.Equal(PowerSourceState.Ac, state);
            Assert.True(tracker.HasBaseline);
            Assert.Equal(PowerSourceState.Ac, tracker.Current);
        }

        [Fact]
        public void NoObservations_HasNoBaseline()
        {
            var tracker = new PowerSourceTransitionTracker();

            Assert.False(tracker.HasBaseline);
            Assert.Null(tracker.Current);
        }

        [Fact]
        public void AcToDc_IsATransition()
        {
            var tracker = new PowerSourceTransitionTracker();
            tracker.TryObserve(0, out _);

            Assert.True(tracker.TryObserve(1, out var state));
            Assert.Equal(PowerSourceState.Dc, state);
            Assert.Equal(PowerSourceState.Dc, tracker.Current);
        }

        [Fact]
        public void DcToAc_IsATransition()
        {
            var tracker = new PowerSourceTransitionTracker();
            tracker.TryObserve(1, out _);

            Assert.True(tracker.TryObserve(0, out var state));
            Assert.Equal(PowerSourceState.Ac, state);
            Assert.Equal(PowerSourceState.Ac, tracker.Current);
        }

        [Fact]
        public void RepeatedSameState_IsIgnored()
        {
            var tracker = new PowerSourceTransitionTracker();
            tracker.TryObserve(1, out _);

            Assert.False(tracker.TryObserve(1, out _));
            Assert.False(tracker.TryObserve(1, out _));
            Assert.Equal(PowerSourceState.Dc, tracker.Current);
        }

        [Fact]
        public void ShortTermUps_CountsAsDc()
        {
            var tracker = new PowerSourceTransitionTracker();
            tracker.TryObserve(0, out _);

            Assert.True(tracker.TryObserve(2, out var state));
            Assert.Equal(PowerSourceState.Dc, state);

            // Already on battery power - moving between UPS and battery is not a change.
            Assert.False(tracker.TryObserve(1, out _));
        }

        [Fact]
        public void AlternatingEvents_ReportEveryRealChange()
        {
            var tracker = new PowerSourceTransitionTracker();
            tracker.TryObserve(0, out _);

            Assert.True(tracker.TryObserve(1, out _));
            Assert.True(tracker.TryObserve(0, out _));
            Assert.True(tracker.TryObserve(1, out _));
            Assert.Equal(PowerSourceState.Dc, tracker.Current);
        }

        [Theory]
        [InlineData(0, PowerSourceState.Ac)]
        [InlineData(1, PowerSourceState.Dc)]
        [InlineData(2, PowerSourceState.Dc)]
        public void Classify_MapsRawPayloadValues(int rawData, PowerSourceState expected)
        {
            Assert.Equal(expected, PowerSourceTransitionTracker.Classify(rawData));
        }
    }
}
