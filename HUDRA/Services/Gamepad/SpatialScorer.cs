using System;
using System.Collections.Generic;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Screen-space rectangle of a focus candidate. WinUI-free mirror of Rect
    /// so the scorer can be unit tested cross-platform.
    /// </summary>
    public readonly record struct ElementRect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;
    }

    /// <summary>
    /// XYFocus-style directional scoring: among candidates whose center lies
    /// strictly in the requested direction, pick the one with the smallest
    /// primary-axis distance, weighting cross-axis misalignment - heavily so
    /// when the candidate doesn't overlap the source's projection (e.g. a
    /// control in the other column). Geometry replaces both the linear
    /// NavigationOrder walking and the hardcoded column-pair routing.
    /// </summary>
    public static class SpatialScorer
    {
        /// <summary>Cross-axis weight when projections overlap (same row/column).</summary>
        private const double AlignedOrthogonalWeight = 1.0;

        /// <summary>Cross-axis weight when they don't (diagonal neighbors).</summary>
        private const double NonOverlappingOrthogonalWeight = 4.0;

        /// <summary>Minimum primary-axis center separation to count as "in that direction".</summary>
        private const double DirectionEpsilon = 0.5;

        /// <summary>
        /// Returns the index of the best candidate for a move in
        /// <paramref name="direction"/> from <paramref name="from"/>,
        /// or -1 if no candidate lies in that direction (stop at edges).
        /// </summary>
        public static int PickBest(ElementRect from, GamepadAction direction, IReadOnlyList<ElementRect> candidates)
        {
            int bestIndex = -1;
            double bestScore = double.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                double primary;
                double orthogonal;
                bool overlaps;

                switch (direction)
                {
                    case GamepadAction.NavUp:
                        primary = from.CenterY - candidate.CenterY;
                        orthogonal = Math.Abs(candidate.CenterX - from.CenterX);
                        overlaps = RangesOverlap(candidate.X, candidate.Right, from.X, from.Right);
                        break;
                    case GamepadAction.NavDown:
                        primary = candidate.CenterY - from.CenterY;
                        orthogonal = Math.Abs(candidate.CenterX - from.CenterX);
                        overlaps = RangesOverlap(candidate.X, candidate.Right, from.X, from.Right);
                        break;
                    case GamepadAction.NavLeft:
                        primary = from.CenterX - candidate.CenterX;
                        orthogonal = Math.Abs(candidate.CenterY - from.CenterY);
                        overlaps = RangesOverlap(candidate.Y, candidate.Bottom, from.Y, from.Bottom);
                        break;
                    case GamepadAction.NavRight:
                        primary = candidate.CenterX - from.CenterX;
                        orthogonal = Math.Abs(candidate.CenterY - from.CenterY);
                        overlaps = RangesOverlap(candidate.Y, candidate.Bottom, from.Y, from.Bottom);
                        break;
                    default:
                        return -1;
                }

                if (primary <= DirectionEpsilon) continue;

                double score = primary + orthogonal *
                    (overlaps ? AlignedOrthogonalWeight : NonOverlappingOrthogonalWeight);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool RangesOverlap(double aStart, double aEnd, double bStart, double bEnd) =>
            aStart < bEnd && bStart < aEnd;
    }
}
