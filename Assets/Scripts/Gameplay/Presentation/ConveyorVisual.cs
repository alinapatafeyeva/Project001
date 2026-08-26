using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Classic theme conveyor visual: a single SpriteRenderer showing
    /// Conveyor.png, parented to the Conveyor GameObject (see
    /// BootstrapSceneCreator.CreateConveyor) so it always sits at the same
    /// world origin as ConveyorPath/ConveyorSystem without touching either.
    /// Purely presentational - owns no path, riding, or speed logic, and
    /// reads nothing from ConveyorSystem/ConveyorPath - so a later animated
    /// conveyor presentation can replace this component entirely without any
    /// gameplay-side change.
    ///
    /// Unlike GameplayBackground, this needs no runtime Fit(): the art is
    /// fixed gameplay content at the shared Z=0 plane (like PixelGrid and
    /// ConveyorPath itself), not something that must cover the viewport at
    /// any aspect ratio. Its BASE size comes from the sprite's own import
    /// calibration - Pixels Per Unit and pivot chosen so the belt's drawn
    /// centerline lined up with ConveyorPath's authored footprint at
    /// ConveyorBeltCalibration.CalibratedConveyorSize, the ConveyorSize in
    /// effect when that calibration was done (localScale (1,1,1) at that
    /// size only - see ConveyorBeltCalibration's own remarks).
    ///
    /// ConveyorSize has since grown beyond that calibrated size (composition
    /// passes that widened GridToClusterSpacing), so a uniform
    /// ConveyorBeltCalibration.VisualScale is applied here on top of that
    /// base calibration - the same ratio ConveyorBeltCalibration.
    /// ComputeOffset itself now scales its own small path-vs-art
    /// corrections by, so the visible belt's rendered footprint and the
    /// measured offset riding collectors/the belt animation align to it
    /// both stay mutually consistent at any ConveyorSize, growing and
    /// shrinking together rather than one silently drifting from the other.
    /// On top of that uniform scale, ConveyorBeltCalibration.
    /// VisualScaleVector also applies a small non-uniform X/Y-only
    /// correction (VisualWidthScale/VisualHeightScale) so a Conveyor.png
    /// whose own drawn proportions don't perfectly match ConveyorPath's
    /// geometry can be fit to the path without ever touching the path
    /// itself - purely a sprite-rendering correction, never gameplay
    /// geometry.
    ///
    /// Placed on the dedicated "Conveyor" Sorting Layer (Project Settings >
    /// Tags and Layers), ordered after "Background" and before "Default" -
    /// the layer every other gameplay SpriteRenderer (PixelGrid cells,
    /// WaitingSlot outlines) and every collector/EnergyBar still use. Kept as
    /// a cheap, secondary tie-breaker only - see DepthPushDistance below for
    /// what actually guarantees collectors render above this.
    ///
    /// ----- Root cause of the "collector renders under Conveyor" regression -----
    /// A previous pass relied on the Sorting Layer alone (its own comment
    /// claimed this "can never render over collectors regardless of Z") -
    /// that assumption is false for a SpriteRenderer against opaque 3D
    /// geometry: Sorting Layer/Order only controls DRAW ORDER within the
    /// GPU's queue; this sprite's default Sprites-Default material still has
    /// ZTest enabled against the depth buffer opaque collectors already
    /// wrote (queue Transparent, drawn after queue Geometry). Wherever a
    /// collector's own real camera-space depth at some pixel happened to sit
    /// farther from the camera than this flat sprite's own Z=0-plane depth
    /// at that pixel (e.g. a collector not perfectly on-plane, or simply the
    /// natural depth variation across a tilted camera's view of a large flat
    /// quad), ZTest let the sprite win and paint over that collector -
    /// exactly the reported "some collectors render under the conveyor"
    /// regression, and exactly the failure mode GameplayLayout.
    /// QueueRowDepthStep's own remarks already document under "the
    /// now-removed SortingGroup attempt's own lesson: sortingOrder alone
    /// cannot [win an opaque Z-test]". Every OTHER flat presentation plane in
    /// this project that must never occlude a collector already follows the
    /// correct, established pattern instead - a real, structural Z push
    /// along -CameraForward/+CameraForward (never sortingOrder alone): see
    /// BootstrapSceneCreator.GameplayBackgroundLocalDepth and
    /// QueueGroundingZone.DepthPushDistance. This class previously had no
    /// such push at all, unlike either of those siblings - the actual bug.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ConveyorVisual : MonoBehaviour
    {
        public const string SortingLayerNameValue = "Conveyor";

        // Real, structural fix for the regression above: pushes this sprite
        // away from the camera along CameraForward (never a raw world -Z
        // offset - see GameplayLayout.CameraForward's own remarks on why
        // that would also drift screen Y under this project's tilted
        // camera), so normal opaque-vs-transparent depth testing alone keeps
        // every collector in front of it, structurally, regardless of any
        // per-collector alignment variance - not merely "usually" via
        // sorting layer draw order. Because the camera is orthographic, this
        // changes only depth, never this sprite's screen position/size (see
        // GameplayLayout.CameraForward's own remarks) - so the belt reads
        // exactly as before, just guaranteed to lose every opaque-vs-
        // transparent Z-test against a collector.
        //
        // Sourced from GameplayLayout.ConveyorVisualDepthPush - see its own
        // remarks for the full explicit Background -> Conveyor ->
        // ContactShadow -> Character -> EnergyBar depth-layering contract
        // this is one named step of, and why 1.5 is comfortably past the
        // largest real depth extent any Conveyor rider can present (matches
        // QueueGroundingZone.DepthPushDistance, the same push amount already
        // proven safe for another flat plane on this same shared Z=0
        // gameplay plane).
        public const float DepthPushDistanceValue = GameplayLayout.ConveyorVisualDepthPush;

        [SerializeField, Tooltip("The Classic theme conveyor sprite (Conveyor.png).")]
        private Sprite sprite;

        private void Awake()
        {
            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingLayerName = SortingLayerNameValue;
            spriteRenderer.sortingOrder = 0;

            // Parent (Conveyor) always sits at world origin with identity
            // rotation (see BootstrapSceneCreator.CreateConveyor), so this
            // local-space offset is exactly the same world-space push
            // QueueGroundingZone computes explicitly, without needing this
            // component to read its own parent's transform. The small
            // world-space Y term on top is a visual-only art placement nudge
            // (ConveyorBeltCalibration.VisualVerticalOffset) - it never
            // touches the Conveyor GameObject/ConveyorPath, so it cannot
            // affect rider positions, only where this sprite itself is
            // drawn.
            transform.localPosition = GameplayLayout.CameraForward * DepthPushDistanceValue
                + new Vector3(0f, ConveyorBeltCalibration.VisualVerticalOffset, 0f);

            // Rescales this sprite's calibrated base size up to today's
            // actual ConveyorSize - see this class's own remarks and
            // ConveyorBeltCalibration.VisualScale - then applies the small
            // non-uniform PNG-fit correction (VisualWidthScale/
            // VisualHeightScale) so the belt art's rendered band lines up
            // with ConveyorPath's own unchanged geometry. Applied here
            // (Awake), not baked as a fixed scene value, so it can never
            // drift stale if ConveyorSize or the fit correction is tuned
            // again later without this file being touched.
            transform.localScale = ConveyorBeltCalibration.VisualScaleVector;
        }
    }
}
