using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Classic theme waiting slot visual: a single SpriteRenderer showing
    /// WaitingSlot.png, parented to one WaitingSlot GameObject (see
    /// WaitingLine.GenerateSlots) so it always sits at exactly that slot's
    /// world position without touching WaitingLine or WaitingSlot itself.
    /// Purely presentational - owns no occupancy state and reads nothing
    /// from WaitingSlot, so a later slot art pass can replace this
    /// component entirely without any gameplay-side change. Mirrors
    /// ConveyorVisual's own contract exactly, one instance per slot rather
    /// than one instance for the whole line, since WaitingLine has many
    /// independent slot positions where Conveyor has only one static frame.
    ///
    /// One sprite instance per slot - never one merged wide image - so
    /// WaitingLine's existing per-slot layout (GenerateSlots' own evenly
    /// spaced positions, derived from GameplayLayout.WaitingSlotSize/
    /// WaitingSlotSpacing) stays the single source of truth for where each
    /// slot sits, including a future 6th slot: this component only ever
    /// reacts to wherever GenerateSlots places its parent WaitingSlot, never
    /// computing or caching a position of its own.
    ///
    /// Unlike ConveyorVisual, sizing needs no dedicated per-asset Pixels Per
    /// Unit calibration against a GameplayLayout constant: the WaitingSlot
    /// GameObject this is parented to already carries
    /// GameplayLayout.WaitingSlotSize as its own localScale (see
    /// GenerateSlots), and WaitingSlot.png is imported at native size
    /// (Pixels Per Unit == its own pixel width, pivot (0.5, 0.5) - see
    /// WaitingSlot.png.meta), so it reads as exactly a 1x1 world unit
    /// square before any further scaling. If a future layout pass shrinks
    /// WaitingSlotSize to fit more slots, every slot's art shrinks with it
    /// automatically, for the same reason.
    ///
    /// This component's own localScale is set to
    /// GameplayLayout.WaitingSlotVisualScale (uniform, so aspect ratio is
    /// always preserved) — a second, purely presentational multiplier on
    /// top of the inherited WaitingSlotSize, deliberately never read by
    /// WaitingLine.GenerateSlots' own position/spacing math. This is what
    /// lets the art read as a real platform under a standing collector
    /// (see WaitingSlotVisualScale's own remarks for why 1x alone read too
    /// small) without moving a single logical slot position or narrowing
    /// the authored gap between slot centers - only this child's own
    /// rendered size changes, never WaitingSlot's own transform.
    ///
    /// ----- GroundPlaneLocalRotation: lying flat, not billboarded -------
    /// Every other flat presentation plane in this project (ConveyorVisual,
    /// ContactShadow, PixelGrid cells) stays at identity local rotation -
    /// a camera-facing decal on the shared Z=0 plane, deliberately never
    /// projected onto any literal 3D ground surface (see ContactShadowMesh's
    /// own remarks: "camera-facing... not a physically projected
    /// ground-plane shape"). A WaitingSlot is different in kind: it is
    /// meant to read as a real PLATFORM a collector's feet rest ON, not a
    /// vertical card floating behind it - so this is the one flat element
    /// in the project deliberately rotated onto an actual horizontal
    /// ground plane (local X 90°: the quad's face normal turns from world
    /// Z, camera-facing, to world Y, "up") instead of staying camera-facing
    /// like every sibling above.
    ///
    /// This is what produces real foreshortening rather than the ~13%
    /// (cos(GameplayLayout.CameraTiltDegrees)) an identity-rotation sprite
    /// already gets for free just from the camera's own tilt - too mild to
    /// read as anything but "a square, slightly tipped" (the exact
    /// complaint this fixes). Laid fully flat instead, the angle between
    /// this plane's own normal (world Y) and CameraForward becomes
    /// (90° - CameraTiltDegrees) off nadir, i.e. CameraTiltDegrees off
    /// grazing - the same elevation angle a real floor tile would be seen
    /// at under this camera. The resulting on-screen vertical compression
    /// is sin(CameraTiltDegrees) ≈ 0.5 at the project's authored 30°: a
    /// real, visible squash into a short wide platform shape, not a mild
    /// tip. No change to WaitingSlot.png itself, no billboard-toward-camera
    /// behaviour (this rotation is a fixed value, not derived from
    /// CameraRotation at all - the whole point is that it does NOT track
    /// the camera), and no change to localPosition/localScale above - pure
    /// orientation only, confirmed via real Play Mode Game View review
    /// against Crab/Turtle/Fish/Octopus (the widest body-shape spread) for
    /// correct axis/sign, since a wrong sign here would show the art from
    /// its back face or upside down rather than merely at a different
    /// angle.
    ///
    /// Depth push mirrors ConveyorVisual/ContactShadowMesh's own convention
    /// (see GameplayLayout's presentation depth layering contract): pushed
    /// away from the camera along CameraForward so a collector standing in
    /// this slot always wins the opaque-vs-transparent Z-test against this
    /// backdrop, structurally, never merely via sortingOrder. Expressed in
    /// this component's own LOCAL space, the same deliberate convention
    /// GameplayLayout.QueueRowDepthStep's own remarks already document for
    /// every other camera-forward pull nested under a scaled parent - the
    /// real world-space push is DepthPushDistanceValue * WaitingSlotSize.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class WaitingSlotVisual : MonoBehaviour
    {
        public const float DepthPushDistanceValue = GameplayLayout.WaitingSlotVisualDepthPush;

        // See this class's own remarks above for why this exists at all -
        // a fixed orientation (never derived from CameraRotation, never
        // recomputed per-frame) that lays this sprite's quad flat, face up,
        // instead of the camera-facing identity rotation every other flat
        // element in this project uses.
        private static readonly Quaternion GroundPlaneLocalRotation = Quaternion.Euler(90f, 0f, 0f);

        [SerializeField, Tooltip("The Classic theme waiting slot sprite (WaitingSlot.png).")]
        private Sprite sprite;

        private void Awake()
        {
            Apply();
        }

        /// <summary>
        /// Assigns the sprite this instance should show and applies it
        /// immediately. WaitingLine.GenerateSlots calls this right after
        /// creating each slot's visual child, since these objects are
        /// generated purely at runtime (never scene-baked), so Awake alone
        /// - which would already have run with sprite left unset by the
        /// time GenerateSlots could reach it - cannot be relied on to pick
        /// up a value assigned afterward.
        /// </summary>
        public void Initialize(Sprite spriteValue)
        {
            sprite = spriteValue;
            Apply();
        }

        private void Apply()
        {
            var spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = 0;

            transform.localPosition = GameplayLayout.CameraForward * DepthPushDistanceValue;
            transform.localScale = Vector3.one * GameplayLayout.WaitingSlotVisualScale;
            transform.localRotation = GroundPlaneLocalRotation;
        }
    }
}
