using System.Collections.Generic;
using HUDRA.Services.GamepadInput;
using Xunit;

namespace HUDRA.Tests
{
    public class SpatialScorerTests
    {
        private static ElementRect Rect(double x, double y, double w, double h) => new(x, y, w, h);

        [Fact]
        public void PicksNearestElement_InDirection()
        {
            var from = Rect(0, 100, 100, 40);
            var candidates = new List<ElementRect>
            {
                Rect(0, 200, 100, 40), // below, far
                Rect(0, 150, 100, 40), // below, near
                Rect(0, 50, 100, 40),  // above
            };

            Assert.Equal(1, SpatialScorer.PickBest(from, GamepadAction.NavDown, candidates));
            Assert.Equal(2, SpatialScorer.PickBest(from, GamepadAction.NavUp, candidates));
        }

        [Fact]
        public void NoCandidateInDirection_ReturnsMinusOne_NoWrap()
        {
            var from = Rect(0, 0, 100, 40);
            var candidates = new List<ElementRect>
            {
                Rect(0, 100, 100, 40), // below only
            };

            Assert.Equal(-1, SpatialScorer.PickBest(from, GamepadAction.NavUp, candidates));
            Assert.Equal(-1, SpatialScorer.PickBest(from, GamepadAction.NavLeft, candidates));
            Assert.Equal(-1, SpatialScorer.PickBest(from, GamepadAction.NavRight, candidates));
        }

        [Fact]
        public void ColumnAlignment_PrefersSameColumn_OverDiagonal()
        {
            // The Resolution/FPS/Audio layout: two columns of controls in rows.
            // Moving DOWN from the LEFT control of a row must land on the LEFT
            // control of the next row, not the (equally near) right control.
            var resolutionLeft = Rect(10, 100, 170, 40);
            var candidates = new List<ElementRect>
            {
                Rect(200, 160, 170, 40), // fps row, RIGHT column (HDR toggle)
                Rect(10, 160, 170, 40),  // fps row, LEFT column (FPS slider)
            };

            Assert.Equal(1, SpatialScorer.PickBest(resolutionLeft, GamepadAction.NavDown, candidates));
        }

        [Fact]
        public void ColumnAlignment_RightColumn_StaysRight_GoingUp()
        {
            var hdrRight = Rect(200, 160, 170, 40);
            var candidates = new List<ElementRect>
            {
                Rect(10, 100, 170, 40),  // resolution row, LEFT (Resolution)
                Rect(200, 100, 170, 40), // resolution row, RIGHT (Refresh rate)
            };

            Assert.Equal(1, SpatialScorer.PickBest(hdrRight, GamepadAction.NavUp, candidates));
        }

        [Fact]
        public void FullWidthRow_ReachableFromEitherColumn()
        {
            // A full-width control below a two-column row is the DOWN target
            // from both columns.
            var fullWidth = new List<ElementRect> { Rect(10, 220, 360, 40) };

            Assert.Equal(0, SpatialScorer.PickBest(Rect(10, 160, 170, 40), GamepadAction.NavDown, fullWidth));
            Assert.Equal(0, SpatialScorer.PickBest(Rect(200, 160, 170, 40), GamepadAction.NavDown, fullWidth));
        }

        [Fact]
        public void HorizontalMove_PrefersSameRow()
        {
            var from = Rect(10, 100, 100, 40);
            var candidates = new List<ElementRect>
            {
                Rect(150, 300, 100, 40), // right but far below
                Rect(150, 100, 100, 40), // right, same row
            };

            Assert.Equal(1, SpatialScorer.PickBest(from, GamepadAction.NavRight, candidates));
        }

        [Fact]
        public void ElementAtSamePosition_IsNotADirectionalTarget()
        {
            var from = Rect(0, 0, 100, 40);
            var overlapping = new List<ElementRect> { Rect(0, 0, 100, 40) };

            Assert.Equal(-1, SpatialScorer.PickBest(from, GamepadAction.NavDown, overlapping));
            Assert.Equal(-1, SpatialScorer.PickBest(from, GamepadAction.NavUp, overlapping));
        }

        [Fact]
        public void SlightVerticalJitter_DoesNotBeatColumnOverlap()
        {
            // A nearer-but-diagonal candidate must not beat a slightly farther
            // candidate that's perfectly column-aligned.
            var from = Rect(10, 100, 100, 40);
            var candidates = new List<ElementRect>
            {
                Rect(130, 150, 100, 40), // diagonal, slightly nearer vertically
                Rect(10, 170, 100, 40),  // same column, slightly farther
            };

            Assert.Equal(1, SpatialScorer.PickBest(from, GamepadAction.NavDown, candidates));
        }
    }
}
