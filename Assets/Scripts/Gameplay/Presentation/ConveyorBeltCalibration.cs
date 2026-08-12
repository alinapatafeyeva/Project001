using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Measured presentation-only correction between ConveyorPath's own
    /// mathematical rounded-square line (the logical path every
    /// ConveyorRider actually moves along - unchanged, still gameplay-owned)
    /// and the ACTUAL rendered centerline of the Classic theme's
    /// Conveyor.png belt art, as currently imported (whatever its sprite
    /// pivot/PPU happen to be - never re-derived from an assumed-perfect
    /// rounded-rectangle geometry, since the art is not required to be one).
    ///
    /// ----- How this was measured -----
    /// Directly from the current Conveyor.png pixels (belt-band color vs.
    /// the corner button decals' own centers), converted to world space via
    /// the sprite's own current import pivot/PixelsPerUnit (i.e. exactly
    /// what ConveyorVisual actually renders at runtime), then compared
    /// against ConveyorPath.GetPositionAtProgress at the matching progress -
    /// see the investigation's own report for the full 8-point table
    /// (world-space and screen-space deltas at every straight-edge midpoint
    /// and every corner midpoint). Never assumes the art is a perfect
    /// rounded rectangle - each of the 8 points below is an independent
    /// real measurement, not a formula.
    ///
    /// ----- How this is applied -----
    /// ComputeOffset returns a WORLD-space (X, Y) vector to ADD to a
    /// rider's logical path position so its PRESENTATION lands on the
    /// visible belt instead - see CollectorView.PresentationOffset, the
    /// dedicated, never-rotated node this drives (a translation-only child
    /// of the collector root, sitting between it and Visual/EnergyBar/
    /// ContactShadow - so the correction is exactly world-space regardless
    /// of Visual's own yaw, and never touches the logical
    /// ConveyorRider/root transform ConveyorSystem and gameplay depend on).
    ///
    /// On a straight (FromEdge == ToEdge) the offset is that edge's own
    /// single measured value - each straight is one continuous, unbroken
    /// belt segment, so one measurement per straight is representative
    /// along its whole length. Through a corner, the two adjacent straights'
    /// own values (already continuous with their own straight at the
    /// corner's start/end, by construction - t=0 and t=1 below reproduce
    /// them exactly) are blended via a quadratic Bezier whose middle control
    /// point is solved so the curve passes exactly through the corner's own
    /// independently-measured midpoint at t=0.5 - never an arbitrary lerp
    /// that would ignore the corner's own real measured shape. This is the
    /// "small set of calibration control points, smoothly interpolated"
    /// approach - one continuous function of (FromEdge, ToEdge, CornerT),
    /// the exact same tuple ConveyorRider.RidingOrientation already
    /// provides for yaw (see CollectorAnimation.ComputeConveyorFacingRotation),
    /// not four independent per-side magic numbers with a hard seam at each
    /// boundary.
    /// </summary>
    public static class ConveyorBeltCalibration
    {
        private static readonly Vector2 TopOffset = new Vector2(0.017f, -0.124f);
        private static readonly Vector2 BottomOffset = new Vector2(0.017f, -0.256f);
        private static readonly Vector2 LeftOffset = new Vector2(0.055f, -0.208f);
        private static readonly Vector2 RightOffset = new Vector2(-0.035f, -0.208f);

        // Corner midpoints, named by the two straights they connect (see
        // ConveyorPath's own EdgeBeforeCorner/EdgeAfterCorner visit order).
        private static readonly Vector2 RightToTopCornerMid = new Vector2(-0.039f, -0.114f);
        private static readonly Vector2 TopToLeftCornerMid = new Vector2(0.069f, -0.122f);
        private static readonly Vector2 LeftToBottomCornerMid = new Vector2(0.071f, -0.217f);
        private static readonly Vector2 BottomToRightCornerMid = new Vector2(-0.029f, -0.201f);

        public static Vector2 ComputeOffset(ConveyorOrientationSample orientation)
        {
            Vector2 fromOffset = EdgeOffset(orientation.FromEdge);

            if (orientation.FromEdge == orientation.ToEdge)
                return fromOffset;

            Vector2 toOffset = EdgeOffset(orientation.ToEdge);
            Vector2 mid = CornerMidOffset(orientation.FromEdge, orientation.ToEdge);

            // Quadratic Bezier (p0, control, p2) solved so the curve passes
            // exactly through the measured midpoint at t = 0.5:
            // B(0.5) = 0.25*p0 + 0.5*control + 0.25*p2 = mid
            // => control = 2*mid - 0.5*p0 - 0.5*p2.
            Vector2 control = 2f * mid - 0.5f * fromOffset - 0.5f * toOffset;

            float t = orientation.CornerT;
            float u = 1f - t;
            return u * u * fromOffset + 2f * u * t * control + t * t * toOffset;
        }

        private static Vector2 EdgeOffset(ConveyorEdge edge)
        {
            switch (edge)
            {
                case ConveyorEdge.Top:
                    return TopOffset;
                case ConveyorEdge.Bottom:
                    return BottomOffset;
                case ConveyorEdge.Left:
                    return LeftOffset;
                case ConveyorEdge.Right:
                    return RightOffset;
                default:
                    return Vector2.zero;
            }
        }

        private static Vector2 CornerMidOffset(ConveyorEdge fromEdge, ConveyorEdge toEdge)
        {
            if (fromEdge == ConveyorEdge.Right && toEdge == ConveyorEdge.Top)
                return RightToTopCornerMid;
            if (fromEdge == ConveyorEdge.Top && toEdge == ConveyorEdge.Left)
                return TopToLeftCornerMid;
            if (fromEdge == ConveyorEdge.Left && toEdge == ConveyorEdge.Bottom)
                return LeftToBottomCornerMid;
            if (fromEdge == ConveyorEdge.Bottom && toEdge == ConveyorEdge.Right)
                return BottomToRightCornerMid;

            // Every real corner transition matches one of the four cases
            // above (see ConveyorPath's own fixed EdgeBeforeCorner/
            // EdgeAfterCorner visit order) - unreachable in practice, kept
            // as a safe midpoint-of-the-two-edges fallback rather than
            // throwing.
            return (EdgeOffset(fromEdge) + EdgeOffset(toEdge)) * 0.5f;
        }
    }
}
