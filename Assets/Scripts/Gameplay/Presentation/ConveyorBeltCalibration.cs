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
        // The GameplayLayout.ConveyorSize in effect when Conveyor.png's own
        // import (Pixels Per Unit, pivot) and every measured offset below
        // were calibrated: GridRegionHeight (6) + 2 * 0.8, the flat,
        // standalone GridToClusterSpacing value that predated it being
        // derived from CollectorVisibleHeight (see GameplayLayout.
        // GridToClusterSpacing's own remarks for that history) - not an
        // arbitrary magic number, the actual composition size this belt art
        // was drawn/measured against.
        //
        // ConveyorSize has since grown twice (composition/collector-scale
        // passes) without Conveyor.png itself, or this calibration data,
        // ever being redrawn or re-measured - so both now need to be scaled
        // up by the same ratio to stay correct, rather than silently
        // rendering a belt sized for the old, smaller loop under today's
        // larger ConveyorPath.
        public const float CalibratedConveyorSize = 7.6f;

        /// <summary>
        /// Uniform scale factor that maps this calibration's own authored
        /// size to GameplayLayout's CURRENT ConveyorSize - the single ratio
        /// both ConveyorVisual (the static belt sprite) and every offset
        /// ComputeOffset returns below must scale by, so the belt art's
        /// rendered footprint AND the small measured path-vs-art correction
        /// riding collectors/the belt animation both read grow together,
        /// staying mutually consistent at any ConveyorSize rather than one
        /// silently drifting relative to the other.
        /// </summary>
        public static float VisualScale => GameplayLayout.ConveyorSize / CalibratedConveyorSize;

        // Small non-uniform, visual-only correction on top of VisualScale so
        // the new Conveyor.png's own rendered belt lines up with
        // ConveyorPath's already-correct, unchanged geometry: the art's
        // drawn band currently reads a touch too wide (X) and too short (Y)
        // relative to the path. This is a PNG-fit correction only - it never
        // touches ConveyorPath/ConveyorSystem or any offset in
        // ComputeOffset above, and applies uniformly to Z (depth) so the
        // sprite's flat Z=0 footprint/depth push are unaffected. Starting
        // values from a first Game View calibration pass; nudge these two
        // constants (not the call sites) if a later pass finds the belt
        // still off on any edge/corner.
        public const float VisualWidthScale = 0.94f;
        public const float VisualHeightScale = 1.05f;

        /// <summary>
        /// The final non-uniform local scale ConveyorVisual/
        /// CreateConveyorVisual must both apply to the belt sprite so Scene
        /// View and runtime match exactly - VisualScale for the size
        /// tracking GameplayLayout.ConveyorSize, times the small per-axis
        /// PNG-fit correction above, X/Y only (Z stays uniform).
        /// </summary>
        public static Vector3 VisualScaleVector => new Vector3(
            VisualScale * VisualWidthScale,
            VisualScale * VisualHeightScale,
            VisualScale);

        // Small visual-only vertical nudge on top of VisualScaleVector so
        // the belt art's rendered band sits flush with ConveyorPath's
        // already-correct, unchanged geometry: a Game View review after the
        // 0.94/1.05 fit found the PNG reading slightly high. World-space Y
        // (added directly to ConveyorVisual's local position - see its own
        // remarks; its parent Conveyor sits at world origin with identity
        // rotation, so local Y here is exactly world Y), never touches
        // ConveyorPath/ConveyorSystem/rider position or the existing
        // CameraForward depth push. Purely an art placement correction, not
        // a gameplay geometry change.
        public const float VisualVerticalOffset = -0.10f;

        // The two horizontal straights' own small "along-belt" (X) measured
        // components, unchanged from the original calibration - only their
        // cross-belt (Y) component is corrected below.
        private static readonly Vector2 TopOffsetMeasured = new Vector2(0.017f, -0.124f);
        private static readonly Vector2 BottomOffsetMeasured = new Vector2(0.017f, -0.256f);
        private static readonly Vector2 LeftOffset = new Vector2(0.055f, -0.208f);
        private static readonly Vector2 RightOffset = new Vector2(-0.035f, -0.208f);

        // A real Game View review found riders on BOTH horizontal straights
        // (Top and Bottom) sitting too high relative to the belt's own
        // rendered band - their contact point landing near the band's
        // vertical CENTER instead of clearly within/on it (confirmed via a
        // direct measurement: a rider's own GroundAnchor, logged through
        // Camera.main.WorldToScreenPoint, landed almost exactly on the
        // belt band's own measured midline, not toward its nearer surface).
        // Left/Right were not reported as wrong and are left untouched.
        //
        // This is expressed as ONE shared correction along the direction
        // common to any horizontal straight's own cross-belt axis (world
        // Y - the axis perpendicular to travel on Top/Bottom, exactly the
        // "belt normal" a rider's contact point must sit correctly along),
        // applied identically to both edges via EdgeOffset below, rather
        // than as two more independently-tuned TopOffset/BottomOffset Y
        // magic numbers that happened to both need the same fix. In
        // CALIBRATED (pre-VisualScale) units, like every other offset here.
        private const float HorizontalStraightContactCorrection = -0.157f;

        private static readonly Vector2 TopOffset =
            TopOffsetMeasured + new Vector2(0f, HorizontalStraightContactCorrection);
        private static readonly Vector2 BottomOffset =
            BottomOffsetMeasured + new Vector2(0f, HorizontalStraightContactCorrection);

        // Corner midpoints, named by the two straights they connect (see
        // ConveyorPath's own EdgeBeforeCorner/EdgeAfterCorner visit order).
        private static readonly Vector2 RightToTopCornerMid = new Vector2(-0.039f, -0.114f);
        private static readonly Vector2 TopToLeftCornerMid = new Vector2(0.069f, -0.122f);
        private static readonly Vector2 LeftToBottomCornerMid = new Vector2(0.071f, -0.217f);
        private static readonly Vector2 BottomToRightCornerMid = new Vector2(-0.029f, -0.201f);

        public static Vector2 ComputeOffset(ConveyorOrientationSample orientation)
        {
            return ComputeOffsetAtCalibratedSize(orientation) * VisualScale;
        }

        // The original per-edge/per-corner blend, exactly as measured at
        // CalibratedConveyorSize - VisualScale above is what the public
        // ComputeOffset applies on top so callers always get a correction
        // sized for today's actual ConveyorSize.
        private static Vector2 ComputeOffsetAtCalibratedSize(ConveyorOrientationSample orientation)
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
