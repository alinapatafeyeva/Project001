using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Feeding;
using Project001.Gameplay.Presentation;
using Project001.UI.EnergyBar;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns a four-level presentation
    /// hierarchy under this root:
    ///
    ///   Collector root (gameplay-owned; queue placement, Conveyor movement,
    ///   WaitingLine/RecoveryRowView reparenting) - ALWAYS exactly on
    ///   ConveyorPath/Queue/WaitingLine/RecoveryRow placement; nothing
    ///   below this node ever feeds back into where gameplay thinks this
    ///   collector is (selection, boarding, lap counting, feeding all key
    ///   off this root/its ConveyorRider, never anything under it).
    ///   └── PresentationOffset — a translation-only "presentation
    ///       transform" (see its own field remarks); ApplyConveyorPresentationOffset
    ///       is the only thing that ever touches its localPosition, and
    ///       never its localRotation/localScale (always identity/one).
    ///       Parent of the three siblings below, so a riding-only
    ///       correction moves all three together without each separately
    ///       re-deriving it.
    ///       ├── Visual — the yaw pivot; CollectorAnimation's Update() is the
    ///       │   only thing that ever touches its localRotation, and never its
    ///       │   localPosition/localScale (always identity/one)
    ///       │   └── VisualMotion — the scale/position pivot; breathing, boarding
    ///       │       bounce, eating/satisfied punches, and the heart pulse/
    ///       │       collapse are the only things that ever touch its
    ///       │       localScale/localPosition, and never its localRotation
    ///       │       (always identity)
    ///       │       └── CharacterVisual — the resolved Character_XX prefab
    ///       │           instance (injected via Initialize(), pivot-corrected
    ///       │           against its own mesh bounds once and never moved
    ///       │           again), already carrying its own baked Character_XX
    ///       │           material on its renderer(s), and its own baked
    ///       │           GroundAnchor/FeedTarget marker children (see
    ///       │           Project001.Gameplay.Presentation.GroundAnchor,
    ///       │           CharacterAssetBuilder.CreateGroundAnchor) - this
    ///       │           species' own true visual ground/contact point,
    ///       │           the sole input ApplyConveyorPresentationOffset
    ///       │           reads to decide PresentationOffset's own position.
    ///       ├── EnergyBar — see its own field group remarks below.
    ///       └── ContactShadow — see its own field group remarks below.
    ///
    /// Splitting yaw (Visual) from scale/position (VisualMotion) is what
    /// makes it structurally impossible for a reaction animation to
    /// overwrite or fight the facing rotation: they are different nodes with
    /// different owners, not different fields on the same node a routine
    /// could accidentally touch — see CollectorAnimation for why the old
    /// single-node design was the actual root cause of the rotation bugs
    /// this replaced. PresentationOffset sitting ABOVE Visual (rather than
    /// the riding correction living on CharacterVisual, below Visual, as it
    /// used to) is what makes a WORLD-space X/Y correction read correctly
    /// regardless of Visual's own current yaw — see PresentationOffset's own
    /// remarks and ConveyorBeltCalibration.
    ///
    /// This view never decides pose or facing, only which prefab to
    /// instantiate; CollectorPresentation and CollectorAnimation own the
    /// rest. The prefab it is given (resolved by CollectorQueueBoard via
    /// CharacterDatabase from the collector's MatchId) already carries its
    /// own baked Character_XX material on every renderer — there is no
    /// runtime material selection or swap here, unlike the shared-model/
    /// resolved-color scheme this replaced.
    ///
    /// Also owns this collector's EnergyBar (Assets/Prefabs/UI/EnergyBar.prefab),
    /// kept in sync via ConveyorRider.RemainingHungerChanged — this view
    /// never reads or stores hunger state itself, only displays whatever
    /// value the event last reported. EnergyBar's ValueText is the ONLY
    /// on-screen RemainingHunger indicator: an earlier standalone
    /// world-space "HungerText" TextMesh (a direct sibling of Visual,
    /// pre-dating EnergyBar entirely) was removed once EnergyBar took over
    /// that exact same responsibility - showing both at once was a
    /// confirmed-redundant duplicate number on the character body (see docs
    /// task "Refine the live Energy Bar presentation"). EnergyBar itself is
    /// a sibling of Visual under PresentationOffset (never a descendant
    /// of Visual) for the same reason HungerText always was: Visual is the only
    /// node CollectorAnimation ever rotates for facing, so a sibling of it is
    /// unaffected by that rotation (it still shares PresentationOffset's own
    /// riding-only translation with Visual, deliberately - see
    /// PresentationOffset's remarks).
    /// Holds no movement, input, or queue logic, and has no knowledge of
    /// the conveyor beyond that — a Collider2D on this root only makes it
    /// detectable via Physics2D point queries for selection.
    ///
    /// Also owns this collector's contact shadow (see the contact shadow
    /// field group's own remarks below and ContactShadowMesh) - a soft,
    /// low-opacity oval fixed under this collector wherever it goes, purely
    /// a "grounded on the surface" cue. A sibling of Visual and EnergyBar
    /// under PresentationOffset, never a descendant of either, for the same
    /// reason EnergyBar is not: it must track neither Visual's yaw nor
    /// VisualMotion's breathing/bounce/punch animation (but, like EnergyBar,
    /// deliberately does share PresentationOffset's own riding-only
    /// translation).
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(ConveyorRider))]
    [RequireComponent(typeof(CollectorPresentation))]
    public class CollectorView : MonoBehaviour
    {
        // ----- EnergyBar (see docs task "Connect the existing EnergyBar
        // prefab to live collectors") -----------------------------------
        //
        // A World Space Canvas instance (Assets/Prefabs/UI/EnergyBar.prefab
        // - see EnergyBarPrefabBuilder for why its own root carries no baked
        // world-space scale), parented directly under this collector's
        // root, as a SIBLING of Visual — never a descendant of it. Visual is
        // the ONLY node CollectorAnimation ever rotates (its continuous
        // re-aim toward the pixel grid while riding, see
        // CollectorAnimation.Update) or moves for presentation depth — the
        // collector ROOT itself is never rotated by anything, for this
        // collector's entire lifetime (queue -> boarding -> riding ->
        // waiting/recovery -> terminal). A sibling of Visual therefore never
        // inherits that yaw. On top of that (see docs task's own explicit
        // camera-facing requirement), EnergyBarLocalRotation additionally
        // counter-rotates for the camera's own fixed downward tilt
        // (GameplayLayout.CameraTiltDegrees) so the bar's face is exactly
        // perpendicular to the camera's view direction - needs computing
        // only once, here, never per-frame, since neither the camera nor
        // this root's own rotation ever changes afterward.
        private static readonly Quaternion EnergyBarLocalRotation = GameplayLayout.CameraRotation;

        // Desired apparent WORLD width, in world units, regardless of
        // GameplayLayout.CollectorSpriteScale — an explicit presentation
        // token, divided back out of whatever scale this collector's root
        // actually carries, rather than baked as a fixed Transform scale on
        // the prefab itself (see EnergyBarPrefabBuilder's own remarks on why
        // that would couple a generic reusable prefab to one specific
        // collector-integration assumption).
        //
        // Raised again from 1.15 to 1.55 after a further real Game View
        // review at portrait 1080x1920 found 1.15 still read as a tiny
        // technical label, and its ValueText number was effectively
        // unreadable without zooming into the screenshot - numeric
        // measurements passing automated verification had not actually been
        // checked against a real rendered screenshot. 1.55 sits inside this
        // review's own requested 1.45-1.60 range. Wider than the previous
        // pass's "stay comfortably narrower than the widest species" goal —
        // deliberately so, since this review explicitly prioritised
        // readability over subtlety at this stage (see docs task "Refine
        // the EnergyBar visual size and vertical placement": "it may be
        // relatively wide compared with the character"). Still narrower
        // than the widest species alone (CollectorVisibleWidth ranges
        // ~1.04-1.98 world units — see GameplayLayout, Turtle's ~1.98 being
        // the widest).
        private const float EnergyBarWorldWidth = 1.55f;

        // EnergyBar.prefab's own authored RectTransform width (see
        // EnergyBarPrefabBuilder.RootSize) — the canvas-unit denominator
        // EnergyBarWorldWidth is divided by, alongside CollectorSpriteScale,
        // to derive this instance's own compensating localScale. Kept as an
        // explicit named constant rather than reading the instantiated
        // prefab's own live RectTransform.rect.width at runtime, since that
        // width is authored/fixed by the builder tool, never resized per
        // instance.
        private const float EnergyBarPrefabCanvasWidth = 384f;

        // How far below a waiting collector's own visible bottom edge (Queue
        // /WaitingLine/RecoveryRow) and how far above a riding collector's
        // own visible top edge (Conveyor) the bar sits, in this root's own
        // LOCAL space (already divided by CollectorSpriteScale) — see
        // GameplayLayout.CollectorVisibleHeight for the measured visible
        // body height these clear (half-height ~0.75 world units at
        // CollectorSpriteScale 1.9). Deliberately two DIFFERENT values, not
        // one shared constant, because Queue and Conveyor have very
        // different neighbour-to-neighbour spacing available to push into
        // (see each constant's own remarks below) - reusing one number for
        // both was tried first and produced a real regression (see
        // EnergyBarConveyorVerticalOffset's own remarks).
        //
        // Pulled back from -0.70 toward the character after a further real
        // Game View review at portrait 1080x1920 found -0.70 read as
        // floating far below the character rather than "just below" it -
        // the earlier 0.52 -> 0.70 magnitude increase (see history above)
        // over-corrected past clearing the body into visibly detached.
        // -0.46 sat inside that review's own requested -0.42 to -0.50
        // range, confirmed clear of feet/tentacles/shell and the next
        // row's head.
        //
        // Raised again after a follow-up real Game View review asked for
        // "the bar feels visually attached to the character" - -0.46 still
        // read as a little too separated. The full requested +0.10 step
        // (-0.46 -> -0.36) was tried first and a fresh real rendered
        // screenshot showed a genuine overlap regression: several
        // characters' own feet/legs now visibly covered part of their own
        // bar's digit (Teal/Blue/Navy/Purple all confirmed), directly
        // hurting the same review's own readability goal. Landed on -0.42
        // instead (+0.04, a smaller partial step) - confirmed via a
        // further fresh screenshot to read as clearly closer/more attached
        // than -0.46 while staying clear of feet/tentacles/shell across
        // Crab/Turtle/Fish/Octopus, and still clear of the next row's head
        // (GameplayLayout.QueueRowStep, 2.001 world units center-to-center
        // - a smaller offset only INCREASES the margin to the next row).
        //
        // Raised again, a further small step, per a follow-up "the bar
        // still feels visually detached... move it slightly higher, do not
        // guess from numeric offsets" request. GameplayLayout.
        // QueueVisibleGap (the ENTIRE open space between adjacent rows'
        // visible bodies) is only 0.5 world units total, and -0.42 was
        // already consuming 0.42 of that, leaving only ~0.08 of genuine
        // spare room - this hard ceiling (not a stylistic choice) is why
        // this step is small. -0.34 (+0.08) - confirmed via a fresh real
        // rendered screenshot to read as noticeably more attached while
        // still leaving a real, visible margin before the next row's head.
        private const float EnergyBarQueueVerticalOffset = -0.34f;

        // Conveyor riders are packed far tighter than Queue rows
        // (GameplayLayout.ConveyorRiderMinimumSpacing, ~1.85 world units
        // center-to-center vs Queue's 2.001, with a smaller authored visual
        // gap on top of body height - see ConveyorRiderVisualGap). 0.75 (the
        // docs task's own suggested 0.70-0.80 starting direction) was tried
        // first alongside the larger EnergyBarWorldWidth this same pass
        // raised to 1.55, and a real rendered screenshot at portrait
        // 1080x1920 showed a genuine regression far worse than this
        // constant's own prior history: the bar now clearly overlapped
        // half of the PREVIOUS rider's own torso silhouette (not just a
        // claw grazing its top pixel, the previous known residual edge
        // case) - the larger bar's own extra height, stacked on top of a
        // larger offset, ran out of the limited vertical room
        // ConveyorRiderMinimumSpacing leaves between consecutive riders.
        // Per docs task section 6 ("visual verification is authoritative"),
        // this real screenshot result overrides the task's own numeric
        // starting direction. Pulled back to 0.40 - confirmed via a fresh
        // real rendered screenshot to clear the previous rider's own body
        // across Crab/Turtle/Fish/Octopus while still reading as clearly
        // above (not touching) this bar's own character's head, including
        // at the same tightly-packed loop corner the prior pass checked.
        //
        // The same +0.10 step applied to EnergyBarQueueVerticalOffset above
        // (0.40 -> 0.50) was tried first, for the same "keep visual spacing
        // consistent" follow-up request - a fresh real rendered screenshot
        // at the same tightly-packed loop corner showed this reopened a
        // real overlap regression: the PREVIOUS rider's own legs dipped
        // well into this bar's coloured fill area, not just the trivial
        // top-edge graze 0.40 already had. Per the same follow-up request's
        // own section 4 ("Confirm that... nothing overlaps the character"),
        // that hard requirement overrides "apply the same delta as Queue"
        // when the two conflict. Landed on 0.43 instead - a smaller,
        // partial step toward Queue's own upward move (some consistency,
        // not none) - confirmed via a further fresh screenshot to be back
        // to only the same trivial top-edge graze 0.40 already had, never
        // reaching into the previous rider's own coloured fill area.
        //
        // NOT raised further this pass despite the same "move slightly
        // higher" follow-up request: this constant's own history above
        // already shows +0.07 more (0.43 -&gt; 0.50) reopened a real overlap
        // with the previous rider - there is no further safe headroom on
        // the shared baseline alone. What actually needed to move here
        // instead was the per-species correction below (Fish/Octopus/
        // Turtle's own bars were sitting with an artificially INFLATED gap
        // above their own head specifically because of the recentering bug
        // those species sink further under - see
        // EnergyBarSpeciesConveyorCorrection), which is what "moved higher"
        // really meant for this context once actually diagnosed.
        private const float EnergyBarConveyorVerticalOffset = 0.43f;

        // Per-species correction added purely to EnergyBar's own position -
        // CharacterVisual's own Transform is never touched by this, so this
        // is strictly a presentation-only compensation, not a gameplay
        // change. Root cause (see docs task "Investigate Turtle
        // alignment"): CollectorView.Initialize's own CharacterVisual
        // recentering (-boundsCenter, a few dozen lines below) calls
        // CollectorAnimation.ComputeLocalRendererBounds on modelInstance
        // AFTER it is already parented deep in the real
        // Root->Visual->VisualMotion runtime hierarchy - exactly the "deep
        // nested collector hierarchy" CharacterVerification.
        // FinishAllTwentyOverlapCheck's own remarks already document reads
        // back WRONG, INFLATED skinned bounds through
        // SkinnedMeshRenderer.BakeMesh, by a confirmed real, per-species
        // amount (that check measured size inflation of Crab +9%, Octopus
        // +17%, Turtle +26%, Fish +34% - reproducible, not a same-frame
        // timing race). A dedicated diagnostic for this pass
        // (EnergyBarSpeciesBoundsDiagnostic, run against one representative
        // Match ID per species from a real live Feeding Flow board in real
        // Play Mode) measured what that inflation does to bounds.CENTER
        // specifically (not just size) - i.e. exactly what the recentering
        // above actually uses - by comparing each live CharacterVisual's
        // real, already-applied localPosition.y against what a reliable,
        // standalone (unparented) Instantiate of the same saved prefab says
        // it should be (the same shallow-nesting approach
        // CharacterAssetBuilder's own build-time scale measurement and
        // CharacterVerification.ComputeExpectedWorldBounds both already use
        // and trust). Result: EVERY species is shifted, not just Turtle -
        // Crab measured closest to correct (~0.049 world units), Turtle
        // 0.274, Octopus 0.333, Fish the most at 0.509 - all in the SAME
        // direction (every species renders LOWER than its own correct,
        // bug-free position).
        //
        // Queue and Conveyor need DIFFERENT-sized corrections, not one
        // shared value, because the sign of "the character renders lower"
        // has OPPOSITE consequences for each: in the Queue, a lower
        // character means its feet are CLOSER to the fixed-offset bar below
        // it (shrinking the gap toward the next row - the real, measured
        // collision risk this pass's own first attempt hit, see below), so
        // a correction that fully compensates has almost no safe headroom
        // (GameplayLayout.QueueVisibleGap is only 0.5 world units total,
        // and the baseline offset above already uses most of it). In the
        // Conveyor, a lower character means MORE clearance above its own
        // head (the bar reads as floating further away, not colliding) -
        // so the correction is safe to apply much closer to its full
        // measured value there, with no next-rider collision risk from
        // this specific correction (it only ever REDUCES how far the bar
        // reaches, since a species that needs a bigger correction sinks
        // further and needs the bar pulled IN, toward zero, to compensate).
        //
        // The full Crab-relative values above (Turtle -0.225, Octopus
        // -0.284, Fish -0.460) were tried on BOTH offsets in one shared
        // dictionary first; a real rendered screenshot showed Fish/
        // Octopus's own Queue bars pushed down far enough to visibly
        // collide with the NEXT queue row's own character - a confirmed
        // regression against this task's own "must not collide with the
        // next queue row" requirement. Split into two separate, smaller
        // tables below instead, each tuned against real rendered
        // screenshots: Queue capped well inside QueueVisibleGap's real
        // spare room (never pushing any species' own total Queue offset
        // past the old, already-confirmed-safe -0.42 baseline); Conveyor
        // allowed a larger fraction of the full measured correction since
        // it carries no equivalent collision risk.
        private static readonly Dictionary<int, float> EnergyBarSpeciesQueueCorrection = new Dictionary<int, float>
        {
            { 1, 0f }, { 2, 0f }, { 14, 0f }, { 15, 0f }, { 20, 0f }, // Crab
            { 3, -0.05f }, { 4, -0.05f }, { 5, -0.05f }, { 9, -0.05f }, { 16, -0.05f }, // Turtle
            { 6, -0.08f }, { 8, -0.08f }, { 10, -0.08f }, { 17, -0.08f }, { 19, -0.08f }, // Fish
            { 7, -0.06f }, { 11, -0.06f }, { 12, -0.06f }, { 13, -0.06f }, { 18, -0.06f }, // Octopus
        };

        private static readonly Dictionary<int, float> EnergyBarSpeciesConveyorCorrection = new Dictionary<int, float>
        {
            { 1, 0f }, { 2, 0f }, { 14, 0f }, { 15, 0f }, { 20, 0f }, // Crab
            { 3, -0.15f }, { 4, -0.15f }, { 5, -0.15f }, { 9, -0.15f }, { 16, -0.15f }, // Turtle
            { 6, -0.25f }, { 8, -0.25f }, { 10, -0.25f }, { 17, -0.25f }, { 19, -0.25f }, // Fish
            { 7, -0.18f }, { 11, -0.18f }, { 12, -0.18f }, { 13, -0.18f }, { 18, -0.18f }, // Octopus
        };

        // A flat World Space Canvas quad needs a "pull toward camera"
        // treatment to guarantee it renders in front of this collector's
        // own opaque 3D geometry (and, while queued, in front of whichever
        // nearer row's geometry might otherwise share its screen position),
        // never behind it - same value/purpose the removed HungerText label
        // already used successfully. Sourced from GameplayLayout.
        // EnergyBarDepthPull - see its own remarks for the full explicit
        // Background -> Conveyor -> ContactShadow -> Character -> EnergyBar
        // depth-layering contract this is the nearest-to-camera step of.
        private const float EnergyBarForegroundPullDistance = GameplayLayout.EnergyBarDepthPull;

        // ----- Contact shadow (see docs task "Refine collector grounding
        // with contact shadows") ---------------------------------------
        //
        // A soft, low-opacity oval sitting at this collector's own feet -
        // purely a "grounded on the surface" cue, not a directional-light
        // shadow. A sibling of Visual/EnergyBar under this same root (never
        // a Visual or VisualMotion descendant), for the same reason
        // EnergyBar is a sibling: Visual is the only node CollectorAnimation
        // ever rotates (facing) or moves for presentation depth, and
        // VisualMotion is the only node the breathing/bounce/punch/heart
        // reactions ever scale or offset - a shadow parented under either
        // would inherit that yaw or bounce, reading as pinned to the
        // character rather than fixed to the ground beneath it. Left at
        // identity local rotation (no camera-facing counter-rotation the
        // way EnergyBar applies) - EnergyBar counter-rotates because it
        // carries readable text/fill that must stay legible; a soft blurred
        // oval has no such requirement, and every other flat 2D element in
        // this project (PixelGrid cells, WaitingSlot outlines, Conveyor
        // path) already renders correctly at identity rotation under the
        // same tilted camera.
        //
        // ----- Root cause of the previous (wrong) placement -----------
        // The previous pass derived ContactShadowVerticalOffset from
        // EnergyBarQueueVerticalOffset - it pushed the shadow BELOW
        // whatever Y the Queue EnergyBar happened to sit at, so the two
        // would not spatially collide. That treated this as a Y-avoidance
        // problem ("don't share the bar's Y") when it is actually a DEPTH
        // ordering problem ("shadow behind the body, body behind the bar" -
        // see ContactShadowCameraPullDistance/EnergyBarForegroundPullDistance,
        // both already real-Z-tested against each other and against the
        // opaque body). Chasing Y-avoidance instead of the real footprint
        // moved the shadow well past the character's own feet, into the
        // dead space toward the NEXT queue row - nowhere near "under the
        // character" any more, and also tied to EnergyBar's own Queue-only
        // offset even though the shadow must look right on the Conveyor,
        // WaitingLine, and Recovery Row too.
        //
        // This pass derives both size and vertical placement from
        // CharacterVisual's own real rendered bounds instead (measured
        // once, right after Initialize's own CharacterVisual-recentering
        // block, in THIS root's local space via
        // CollectorAnimation.ComputeLocalRendererBounds(transform) - the
        // same shared helper that recentering already uses, just queried
        // relative to this root instead of CharacterVisual itself, so no
        // further CollectorSpriteScale division is needed the way the old
        // EnergyBar-derived constants required). No EnergyBar offset is
        // read anywhere in this derivation. See CreateContactShadow.
        // Deliberately a stylized presentation decal, sized directly as a
        // multiple of the character's own visible width - not a physically
        // "correct" footprint. Reference comparison (Shadow_Reference.jpg)
        // target: ~1.5-2.0x the character's visible width, extending clearly
        // past the silhouette on both sides.
        private const float ContactShadowWidthFraction = 1.7f;

        // On-screen height as a fraction of on-screen width - reference
        // target ~15-25%, a flat wide ellipse, not a round puddle.
        private const float ContactShadowHeightToWidthRatio = 0.2f;

        // Fallback used only if a species' visualPrefab has no renderers to
        // measure (CollectorAnimation.ComputeLocalRendererBounds returns
        // null) - should not happen for any approved Character_XX prefab,
        // but keeps this collector's shadow present rather than throwing.
        private const float ContactShadowFallbackWidth = 1f;

        // How far the shadow's own centre sits above the character's exact
        // measured bottom edge (bounds.min.y), as a fraction of the shadow's
        // own height - keeps a visible portion below the feet while letting
        // the rest tuck under the body, so the shadow still reads even when
        // the character overlaps its centre.
        private const float ContactShadowOcclusionBias = 0.15f;

        // Small fixed push away from the camera (see ApplyContactShadowLocalPosition)
        // so this shadow reliably Z-tests behind its own collector's opaque
        // body. Safe at this size for a camera-facing decal (no real depth
        // extent of its own) - only affects the portion overlapping the
        // character's own silhouette; the parts extending past it on either
        // side are visible regardless. Sourced from GameplayLayout.
        // ContactShadowDepthPush - see its own remarks for the full explicit
        // Background -> Conveyor -> ContactShadow -> Character -> EnergyBar
        // depth-layering contract this is one named step of.
        private const float ContactShadowCameraPullDistance = GameplayLayout.ContactShadowDepthPush;

        // Color/opacity live on ContactShadowMesh.SharedMaterial (a single
        // shared Material, since neither ever varies per collector or per
        // MatchId), not as a per-instance property here.

        private Transform _contactShadowTransform;
        private float _contactShadowDepth;

        // This instance's own measured footprint Y (root-local), set once
        // by CreateContactShadow from CharacterVisual's real rendered
        // bounds - see the contact shadow field group's own remarks above
        // for why this replaced a fixed, EnergyBar-derived constant.
        private float _contactShadowBaseY;

        // ----- Conveyor riding presentation offset (see PresentationOffset,
        // SetConveyorGroundingCorrection, ApplyConveyorPresentationOffset)
        // ---------------------------------------------------------------
        //
        // PresentationOffset is a dedicated, translation-only node between
        // this root and Visual/EnergyBar/ContactShadow (see Initialize) -
        // the "presentation transform" separating the logical
        // ConveyorRider/root position (still exactly on ConveyorPath,
        // gameplay-owned, never touched by any of this) from where the
        // character/shadow/bar actually render. Because it sits ABOVE
        // Visual (never itself rotated - only Visual's own yaw pivot
        // rotates), a world-space X/Y offset assigned to its localPosition
        // (divided by CollectorSpriteScale, the only scale between this
        // node and world space) reads correctly regardless of which way the
        // rider currently faces - unlike a correction applied to
        // CharacterVisual itself, which sits BELOW Visual and would need
        // its own yaw counter-rotated out first.
        private Transform _presentationOffset;

        // True only while this collector is actively riding the Conveyor
        // (see SetConveyorGroundingCorrection) - gates Update() so
        // PresentationOffset is only ever recomputed (and costs anything)
        // while riding; 0 the rest of this collector's lifetime.
        private bool _isRidingConveyor;

        // True only while this collector is grounded on a static point (a
        // WaitingSlot's own position - see SetGroundedPosition), mutually
        // exclusive with _isRidingConveyor. Gates Update() the same way
        // _isRidingConveyor does, for the same reason (only pay the
        // per-frame solve while it can actually matter - see
        // SetGroundedPosition's own remarks for why this must stay
        // continuous rather than a one-time snap).
        private bool _isGroundedAtFixedPoint;

        // The world-space target ApplyFixedGroundCorrection re-solves
        // toward every frame while _isGroundedAtFixedPoint - set once by
        // SetGroundedPosition, never mutated by Update() itself.
        private Vector3 _groundedTargetPosition;

        // This collector's GroundAnchor, resolved from the instantiated
        // CharacterVisual by Initialize() - this species' own true visual
        // ground/contact point (see GroundAnchor's own class remarks and
        // CharacterAssetBuilder.CreateGroundAnchor), baked once per model
        // family at build time. The sole input ApplyConveyorPresentationOffset
        // uses to decide how far PresentationOffset must move - never a
        // hand-tuned per-species runtime constant, never derived from this
        // collector's own root. Null for any species whose visualPrefab
        // predates this feature (mirroring FeedTarget's own "absent rather
        // than silently wrong" contract) - ApplyConveyorPresentationOffset
        // is a no-op while this is null, so a collector simply keeps riding
        // exactly on the logical path with no presentation correction
        // rather than crashing.
        private GroundAnchor _groundAnchor;

        // Fallback fill colour, used only if ColorPalette.TryGetByMatchId
        // fails to resolve this collector's own MatchId (an id outside the
        // approved 1-20 table - see ColorPalette) - logged as an error when
        // this happens, since every real collector's MatchId always comes
        // from approved level/debug data and should always resolve. Kept
        // distinctly different from any real Match ID colour so a fallback
        // is immediately obvious in Play Mode rather than silently
        // resembling a real result.
        private static readonly Color EnergyFillFallbackColor = Color.magenta;

        private Transform _visual;
        private Transform _visualMotion;
        private ConveyorRider _conveyorRider;
        private CollectorPresentation _presentation;
        private CharacterPosePresentation _posePresentation;
        private FeedTarget _feedTarget;
        private Transform _energyBarTransform;
        private EnergyBarView _energyBarView;
        private bool _energyBarIsAboveCharacter;
        private float _energyBarDepth;
        private int _matchId;

        /// <summary>
        /// This collector's CollectorPresentation, cached once in Awake.
        /// Exposed so callers (e.g. CollectorSelectionController, the moment
        /// a collector boards the Conveyor) can switch its pose without a
        /// runtime GetComponent call.
        /// </summary>
        public CollectorPresentation Presentation => _presentation;

        /// <summary>
        /// This collector's PresentationOffset child transform - the
        /// translation-only "presentation transform" between this root and
        /// Visual/EnergyBar/ContactShadow, created by Initialize(). Exposed
        /// read-only for verification/diagnostic tooling to inspect the
        /// currently-applied riding correction directly; gameplay never
        /// reads or writes it. See the class remarks and
        /// ApplyConveyorPresentationOffset for what actually drives it.
        /// Null until Initialize() has run.
        /// </summary>
        public Transform PresentationOffset => _presentationOffset;

        /// <summary>
        /// This collector's Visual child transform — the yaw pivot created
        /// by Initialize(). Gameplay never reads or writes this transform;
        /// it exists purely so CollectorAnimation has a facing rotation to
        /// own that is never also being driven by ConveyorSystem,
        /// CollectorQueueBoard, WaitingLine, or RecoveryRowView, all of which
        /// only ever touch this collector's root transform (this
        /// GameObject's own transform), never Visual's. Ordinarily only
        /// localRotation changes here — see VisualMotion for scale/position
        /// — with one deliberate exception: CollectorAnimation.
        /// SetPresentationDepth offsets localPosition toward the camera
        /// (queue row depth while queued, a waiting source's neutral depth,
        /// or — via its own EnterTerminalForeground caller, once and never
        /// reset — the terminal Satisfied/Heart sequence), so a nearer row
        /// or the terminal sequence never visually merges with whatever
        /// shares its screen position. Null until Initialize() has run.
        /// </summary>
        public Transform Visual => _visual;

        /// <summary>
        /// Visual's child transform — the scale/position pivot every
        /// breathing/bounce/punch/heart reaction animates, created by
        /// Initialize(). Deliberately a separate node from Visual: a
        /// reaction that sets localScale/localPosition here can never also
        /// touch Visual's localRotation, since it is a different Transform
        /// entirely. Null until Initialize() has run.
        /// </summary>
        public Transform VisualMotion => _visualMotion;

        /// <summary>
        /// This collector's claw/bone pose adapter, resolved from the
        /// instantiated CharacterVisual by Initialize() - null for any
        /// species whose visualPrefab was not configured with one (every
        /// species besides Crab today; see CharacterPosePresentation and
        /// CharacterAssetBuilder). CollectorPresentation always calls
        /// through the null-conditional operator on this, so a species with
        /// no adapter is unaffected by every pose request.
        /// </summary>
        public CharacterPosePresentation PosePresentation => _posePresentation;

        /// <summary>
        /// This collector's FeedTarget, resolved from the instantiated
        /// CharacterVisual by Initialize() - the world position a FoodPacket
        /// flies to (see Project001.Gameplay.Feeding.FoodFlightController).
        /// Null for any species whose visualPrefab was not built with one
        /// (see CharacterAssetBuilder.CreateFeedTarget); FoodFlightController
        /// falls back to this collector's own root transform when it is.
        /// </summary>
        public FeedTarget FeedTarget => _feedTarget;

        /// <summary>
        /// This collector's GroundAnchor, resolved from the instantiated
        /// CharacterVisual by Initialize() - this species' own true visual
        /// ground/contact point (see GroundAnchor's own class remarks and
        /// CharacterAssetBuilder.CreateGroundAnchor). Exposed read-only for
        /// verification/diagnostic tooling to inspect directly; gameplay
        /// never reads this. Null for any species whose visualPrefab was
        /// not built with one.
        /// </summary>
        public GroundAnchor GroundAnchor => _groundAnchor;

        /// <summary>
        /// This collector's MatchId, set once by Initialize(). Exposed
        /// read-only for verification/diagnostic tooling to identify which
        /// species a given instance is (see the species correction tables
        /// above); gameplay never reads this directly (ConveyorRider is the
        /// gameplay-facing source of matching/hunger behaviour).
        /// </summary>
        public int MatchId => _matchId;

        /// <summary>
        /// Currently displayed RemainingHunger text. Read-only — this view
        /// never sets hunger state, only mirrors whatever
        /// ConveyorRider.RemainingHungerChanged last reported. Sourced from
        /// EnergyBar's own ValueText (see EnergyBarView.DisplayText) - the
        /// property name/contract is unchanged from when a separate
        /// standalone HungerText TextMesh backed it, so existing verification
        /// tooling (CharacterVerification) needed no changes when that
        /// TextMesh was removed. Returns an empty string if EnergyBar itself
        /// is null (energyBarPrefab was never assigned - see Initialize).
        /// </summary>
        public string HungerDisplayText => _energyBarView != null ? _energyBarView.DisplayText : string.Empty;

        /// <summary>
        /// This collector's EnergyBar (Assets/Prefabs/UI/EnergyBar.prefab),
        /// resolved by Initialize() - null only if the energyBarPrefab
        /// argument passed to Initialize was itself null (logged there, not
        /// thrown). Exposed read-only for verification tooling to inspect
        /// CurrentValue/MaxValue directly.
        /// </summary>
        public EnergyBarView EnergyBar => _energyBarView;

        private void Awake()
        {
            // Selection detection only; must not participate in physical collision.
            GetComponent<Collider2D>().isTrigger = true;

            _presentation = GetComponent<CollectorPresentation>();

            _conveyorRider = GetComponent<ConveyorRider>();
            _conveyorRider.RemainingHungerChanged += OnRemainingHungerChanged;
        }

        private void OnDestroy()
        {
            if (_conveyorRider != null)
                _conveyorRider.RemainingHungerChanged -= OnRemainingHungerChanged;
        }

        /// <summary>
        /// Builds the Visual/VisualMotion/CharacterVisual hierarchy (see the class
        /// remarks). Must be called exactly once, by CollectorQueueBoard
        /// right after this collector is constructed and before
        /// CollectorPresentation.ShowWaitingBackIdle() — there is no
        /// Inspector session for this component to source a prefab
        /// reference from (collectors are still built at runtime, never from
        /// a prefab themselves), so it is injected here the same way
        /// ConveyorRider, PixelConsumer, and CollectorLifecycle are each
        /// initialized.
        ///
        /// Visual and VisualMotion are both plain, empty pivot GameObjects —
        /// never the prefab instance itself — left at local position zero,
        /// rotation identity, scale one. Neither carries the model's own
        /// authored scale or any pivot correction; both live one level
        /// deeper, on CharacterVisual, so they are never disturbed by whichever of
        /// the two pivots above them is currently being animated.
        ///
        /// CharacterVisual (VisualMotion's child) is the actual visualPrefab
        /// instance, carrying its own authored localScale (derived by
        /// CharacterAssetBuilder, preserved by leaving it untouched after
        /// Instantiate) plus a derived localPosition correction: an imported
        /// model is typically pivoted at its feet (local Y running ~0 to ~1,
        /// not centered on Y 0), while every queue/conveyor slot in
        /// GameplayLayout places a collector's root at the visual CENTER of
        /// the old, center-pivoted sprite (see GameplayLayout's symmetric
        /// CollectorVisibleHeight*0.5 usage). Left uncorrected, every queued
        /// or riding character would render shifted upward by roughly half
        /// its own visible height. The correction below re-centers CharacterVisual
        /// by reading its own combined mesh bounds (CollectorAnimation.
        /// ComputeLocalRendererBounds, shared rather than duplicated) and
        /// offsetting by the negative of that bounds center — derived from
        /// the model's actual geometry, not a hand-measured constant.
        ///
        /// No material is applied here: visualPrefab already carries its own
        /// baked Character_XX material on every renderer (built once, at
        /// editor time, by CharacterAssetBuilder), so Instantiate() alone
        /// produces the correct, fully-textured result — there is nothing
        /// left for this method to swap or tint.
        ///
        /// energyBarPrefab (Assets/Prefabs/UI/EnergyBar.prefab, injected by
        /// CollectorQueueBoard exactly like characterDatabase already is -
        /// never Resources.Load/GameObject.Find) is instantiated here too,
        /// as a sibling of Visual under this same root - see the EnergyBar
        /// field group's own class remarks for why a sibling, never a
        /// Visual descendant. Starts positioned/rotated for the Queue (the
        /// state every collector is actually created into - see
        /// CollectorQueueBoard.GenerateBoard's own call order,
        /// ShowWaitingBackIdle always runs immediately after this), with its
        /// fill tinted to matchId's own canonical ColorPalette colour (see
        /// CreateEnergyBar); CollectorPresentation.ShowWaitingBackIdle/
        /// ShowConveyorFrontEating are what move it between Queue-below and
        /// Conveyor-above afterward, never this method again.
        /// </summary>
        public void Initialize(GameObject visualPrefab, GameObject energyBarPrefab, int matchId)
        {
            _matchId = matchId;

            // The "presentation transform" between this root (gameplay-owned;
            // always exactly on ConveyorPath/Queue/WaitingLine/RecoveryRow
            // placement) and everything actually drawn - see PresentationOffset's
            // own field remarks. Translation-only, never rotated/scaled by
            // anything, so ApplyConveyorPresentationOffset's world-space X/Y
            // offset reads correctly here regardless of Visual's own current
            // yaw. Parent of Visual/EnergyBar/ContactShadow below instead of
            // this root directly - the one structural change from the
            // previous flat "three siblings under root" layout.
            var presentationOffsetObject = new GameObject("PresentationOffset");
            presentationOffsetObject.transform.SetParent(transform, false);
            _presentationOffset = presentationOffsetObject.transform;

            var visualWrapper = new GameObject("Visual");
            visualWrapper.transform.SetParent(_presentationOffset, false);

            var visualMotion = new GameObject("VisualMotion");
            visualMotion.transform.SetParent(visualWrapper.transform, false);

            var modelInstance = Instantiate(visualPrefab, visualMotion.transform, false);
            modelInstance.name = "CharacterVisual";
            modelInstance.transform.localRotation = Quaternion.identity;

            Bounds? localBounds = CollectorAnimation.ComputeLocalRendererBounds(modelInstance.transform);
            modelInstance.transform.localPosition = localBounds.HasValue
                ? -Vector3.Scale(modelInstance.transform.localScale, localBounds.Value.center)
                : Vector3.zero;

            _visual = visualWrapper.transform;
            _visualMotion = visualMotion.transform;
            _posePresentation = modelInstance.GetComponentInChildren<CharacterPosePresentation>(true);
            _feedTarget = modelInstance.GetComponentInChildren<FeedTarget>(true);
            _groundAnchor = modelInstance.GetComponentInChildren<GroundAnchor>(true);

            // Must run AFTER CharacterVisual is instantiated and recentered
            // above - it measures that same geometry (see CreateContactShadow's
            // own remarks) to place/size this collector's shadow from its
            // real footprint instead of a fixed constant.
            CreateContactShadow();

            CreateEnergyBar(energyBarPrefab, matchId);
        }

        /// <summary>
        /// Applies/clears this instance's own conveyor riding presentation
        /// offset (see PresentationOffset's own field remarks) - scoped to
        /// actively riding the Conveyor only, exactly like the grounding
        /// correction this replaced.
        ///
        /// Moves PresentationOffset itself (never Visual, which
        /// CollectorAnimation.Update owns exclusively for yaw, nor
        /// VisualMotion, which every breathing/bounce/punch/heart reaction
        /// owns for scale/position, nor CharacterVisual, whose own baseline
        /// recentering localPosition is set once at Initialize and never
        /// touched again) - since EnergyBar and the contact shadow are now
        /// BOTH children of this same PresentationOffset node (see
        /// Initialize), moving it moves character, shadow, and EnergyBar
        /// together automatically; neither needs to separately re-apply a
        /// matching offset the way they used to.
        ///
        /// Snaps immediately here (riding: true) using this rider's CURRENT
        /// RidingOrientation, so there is no one-frame pop before Update()
        /// below takes over for the rest of the ride; riding: false zeroes
        /// PresentationOffset and stops Update() from touching it again
        /// until the next ride.
        ///
        /// Called by CollectorPresentation.ShowConveyorFrontEating (riding:
        /// true, the moment any collector actually boards) and
        /// ShowWaitingBackIdle (riding: false, every return to
        /// Queue/WaitingLine/Recovery Row) — Queue/WaitingLine/Recovery Row
        /// presentation itself is deliberately left exactly as it already
        /// is, unaffected by this.
        /// </summary>
        public void SetConveyorGroundingCorrection(bool riding)
        {
            _isRidingConveyor = riding;
            _isGroundedAtFixedPoint = false;

            if (riding)
                ApplyConveyorPresentationOffset();
            else if (_presentationOffset != null)
                _presentationOffset.localPosition = Vector3.zero;
        }

        /// <summary>
        /// Grounds this collector on a STATIC world-space point (a
        /// WaitingSlot's own transform.position) using the exact same
        /// GroundAnchor-based mechanism SetConveyorGroundingCorrection uses
        /// for a moving path point - see ApplyGroundAnchorCorrection, the
        /// shared solve both call. Fixes the "positioned by body center
        /// instead of feet" WaitingLine bug generically, per-species, with
        /// no hardcoded offset: CollectorLifecycle.ResolveLap still sets
        /// this collector's ROOT to the slot's own position exactly as
        /// before (gameplay's source of truth for where this collector
        /// "is" never changes) - this only moves PresentationOffset, the
        /// same purely-presentational node conveyor riding already owns,
        /// so this collector's FEET (GroundAnchor) land exactly on the slot
        /// instead of its body center.
        ///
        /// Continues applying every frame (via Update() below) rather than
        /// snapping once: Visual's own yaw is still settling toward its new
        /// "Away" orientation for a few frames after arriving (see
        /// CollectorAnimation's own RotateTowards remarks), which moves
        /// GroundAnchor - a one-time snap would go stale exactly like the
        /// conveyor-riding case's own remarks describe for a moving corner.
        /// Mutually exclusive with conveyor riding (see
        /// SetConveyorGroundingCorrection, which clears this flag) - a
        /// collector is never grounded at a fixed point while also riding.
        /// </summary>
        public void SetGroundedPosition(Vector3 targetWorldPosition)
        {
            _isRidingConveyor = false;
            _isGroundedAtFixedPoint = true;
            _groundedTargetPosition = targetWorldPosition;
            ApplyFixedGroundCorrection();
        }

        /// <summary>
        /// Releases a fixed-point grounding correction applied by
        /// SetGroundedPosition, resetting PresentationOffset back to zero -
        /// mirrors SetConveyorGroundingCorrection(false)'s own cleanup.
        /// Not usually needed explicitly: boarding the Conveyor again
        /// already clears this via SetConveyorGroundingCorrection(true)'s
        /// own _isGroundedAtFixedPoint reset, so this exists mainly for
        /// symmetry and any future non-conveyor exit path.
        /// </summary>
        public void ClearGroundedPosition()
        {
            _isGroundedAtFixedPoint = false;

            if (_presentationOffset != null)
                _presentationOffset.localPosition = Vector3.zero;
        }

        /// <summary>
        /// The shared solve both ApplyConveyorPresentationOffset and
        /// ApplyFixedGroundCorrection use: computes the exact
        /// PresentationOffset.localPosition that puts this collector's
        /// GroundAnchor exactly at targetAnchorWorldPos (X/Y only - Z is
        /// deliberately left at whatever GroundAnchor's own baseline Z
        /// already is, so this never interferes with Visual's own separate
        /// depth-pull mechanics - queue row depth, terminal foreground).
        ///
        /// Solves directly rather than nudging toward it incrementally:
        /// because PresentationOffset only ever translates (see its own
        /// field remarks - never rotated, never scaled), moving it by a
        /// given WORLD delta moves every descendant, including GroundAnchor,
        /// by that exact same delta, regardless of Visual's own current
        /// yaw. So GroundAnchor's world position at the CURRENT
        /// PresentationOffset already tells us everything needed:
        /// subtracting PresentationOffset's own current contribution
        /// (localPosition * CollectorSpriteScale, the only scale between it
        /// and world space) recovers exactly where GroundAnchor would sit
        /// with zero correction, and the rest is one exact, non-iterative
        /// solve for the localPosition that closes the remaining gap -
        /// correct in a single assignment, every frame, regardless of
        /// whatever PresentationOffset happened to be a moment ago.
        /// </summary>
        private void ApplyGroundAnchorCorrection(Vector2 targetAnchorWorldXY)
        {
            if (_presentationOffset == null || _groundAnchor == null)
                return;

            float scale = GameplayLayout.CollectorSpriteScale;

            Vector3 currentAnchorWorldPos = _groundAnchor.WorldPosition;
            Vector3 zeroOffsetAnchorWorldPos = currentAnchorWorldPos - _presentationOffset.localPosition * scale;

            Vector3 targetAnchorWorldPos = new Vector3(
                targetAnchorWorldXY.x,
                targetAnchorWorldXY.y,
                zeroOffsetAnchorWorldPos.z);

            _presentationOffset.localPosition = (targetAnchorWorldPos - zeroOffsetAnchorWorldPos) / scale;
        }

        /// <summary>
        /// The sole owner of PresentationOffset's localPosition while
        /// riding - called once immediately by SetConveyorGroundingCorrection
        /// (riding: true) and every frame afterward by Update() below, both
        /// reading this collector's ConveyorRider.RidingOrientation fresh
        /// each time (set every frame by ConveyorSystem - see ConveyorRider),
        /// so the correction tracks continuously as the rider travels
        /// around the loop, through every corner, exactly like
        /// CollectorAnimation's own yaw tracking does from the same
        /// RidingOrientation sample. A no-op if this species has no
        /// GroundAnchor (see its own field remarks).
        ///
        /// The target this collector's GroundAnchor should land ON is the
        /// visible belt's own centerline at this rider's current position -
        /// ConveyorPath's logical point (this collector root's own current
        /// world position; ConveyorSystem already keeps it exactly there)
        /// plus ConveyorBeltCalibration.ComputeOffset, the measured
        /// world-space X/Y delta between that logical point and where the
        /// belt ART actually renders there.
        /// </summary>
        private void ApplyConveyorPresentationOffset()
        {
            if (_conveyorRider == null)
                return;

            Vector3 pathPoint = transform.position;
            Vector2 beltOffsetWorld = ConveyorBeltCalibration.ComputeOffset(_conveyorRider.RidingOrientation);
            ApplyGroundAnchorCorrection(new Vector2(pathPoint.x + beltOffsetWorld.x, pathPoint.y + beltOffsetWorld.y));
        }

        /// <summary>
        /// The sole owner of PresentationOffset's localPosition while
        /// grounded at a fixed point (see SetGroundedPosition) - the WaitingLine
        /// counterpart to ApplyConveyorPresentationOffset, targeting a
        /// static world position instead of a moving path point.
        /// </summary>
        private void ApplyFixedGroundCorrection()
        {
            if (!_isGroundedAtFixedPoint)
                return;

            ApplyGroundAnchorCorrection(new Vector2(_groundedTargetPosition.x, _groundedTargetPosition.y));
        }

        private void Update()
        {
            if (_isRidingConveyor)
                ApplyConveyorPresentationOffset();
            else if (_isGroundedAtFixedPoint)
                ApplyFixedGroundCorrection();
        }

        /// <summary>
        /// Stops Update() from ever recomputing PresentationOffset again for
        /// the rest of this collector's life, WITHOUT touching its current
        /// value at all - the fix for the terminal "Satisfied jump/drop"
        /// bug (see docs task "Fix the Satisfied transition jump").
        ///
        /// ----- Root cause -----
        /// ApplyConveyorPresentationOffset solves every frame from a LIVE
        /// read of GroundAnchor.WorldPosition - correct and necessary while
        /// genuinely riding, since GroundAnchor's position must reflect
        /// Visual's current yaw. But the terminal Satisfied sequence
        /// (CollectorPresentation.FinalBiteSequence) ALSO drives Visual
        /// (CollectorAnimation.EnterTerminalForeground, a one-way
        /// camera-forward pull) and VisualMotion (the eating/satisfied
        /// punch, heart pulse, and final collapse-to-zero-scale) - both of
        /// which GroundAnchor sits downstream of, since it is a descendant
        /// of VisualMotion. Left running, Update() kept re-solving
        /// PresentationOffset every single frame using whatever GroundAnchor
        /// position those SAME-FRAME terminal animations had just produced -
        /// and because Unity does not guarantee whether CollectorView's
        /// Update() or CollectorPresentation's coroutine step runs first
        /// within a given frame, the two systems raced: on frames where the
        /// terminal pull/punch applied before this component's Update() one
        /// frame, the fresh solve would compensate and mask it; on frames
        /// where it applied after, that frame rendered the RAW, uncompensated
        /// terminal delta - a real, visible one-frame snap, worse each time
        /// VisualMotion's own scale changed further (most dramatically as
        /// HeartPulseAndCollapseRoutine collapses VisualMotion toward zero
        /// scale, which drags GroundAnchor toward VisualMotion's own pivot
        /// and would otherwise get "corrected" by yanking the whole
        /// character sideways to compensate).
        ///
        /// ----- The fix -----
        /// Simply STOP re-deriving PresentationOffset once the terminal
        /// sequence claims Visual/VisualMotion for its own purposes -
        /// called once, at the very start of
        /// CollectorPresentation.FinalBiteSequence, before any terminal
        /// animation runs. PresentationOffset is left at EXACTLY whatever
        /// value it last correctly held (this collector's real, grounded
        /// position on the belt at that instant) and never written again -
        /// not reset to zero, not given a new "satisfied" value, nothing
        /// arbitrary. The terminal sequence's own animations then compose
        /// on top of that frozen base exactly as they already do for every
        /// other collector state, with nothing left to race against them.
        /// </summary>
        public void FreezeConveyorPresentation()
        {
            _isRidingConveyor = false;
        }

        /// <summary>
        /// Moves EnergyBar to its Queue/WaitingLine/RecoveryRow position
        /// (below this collector's own visible body) at this collector's
        /// CURRENT presentation depth - called by CollectorPresentation.
        /// ShowWaitingBackIdle, which every one of those waiting sources
        /// (initial Queue placement, an unsatisfied return, a Recovery Row
        /// transfer, or a rolled-back boarding attempt) already calls, so
        /// this needs no separate call site of its own. Safe to call
        /// whether or not this collector's presentation depth was just
        /// cleared/changed - always reads _energyBarDepth, this instance's
        /// own last-applied depth, rather than assuming 0.
        /// </summary>
        public void ShowEnergyBarBelowCharacter()
        {
            _energyBarIsAboveCharacter = false;
            ApplyEnergyBarLocalPosition();
        }

        /// <summary>
        /// Moves EnergyBar to its Conveyor position (above this collector's
        /// own visible head) - called by CollectorPresentation.
        /// ShowConveyorFrontEating, the single moment any collector actually
        /// starts riding (see CollectorSelectionController.
        /// ProcessPendingBoardings), regardless of which source it boarded
        /// from. Stays there for the rest of this collector's ride,
        /// including the satisfied/terminal sequence - nothing requests
        /// Waiting again while riding (see ShowConveyorFrontEating's own
        /// remarks), so EnergyBar reads full/0 right up until this
        /// collector is destroyed, matching the docs task's own "satisfied
        /// collectors reach full bar / 0 before disappearance" requirement
        /// with no extra code here.
        /// </summary>
        public void ShowEnergyBarAboveCharacter()
        {
            _energyBarIsAboveCharacter = true;
            ApplyEnergyBarLocalPosition();
        }

        /// <summary>
        /// Keeps EnergyBar's own genuine camera-depth pull in sync with this
        /// collector's current presentation depth (see CollectorAnimation.
        /// SetPresentationDepth) - called by CollectorPresentation.
        /// ApplyPresentationDepth alongside Animation.SetPresentationDepth,
        /// with the exact same depthWorldUnits: without this, a
        /// nearer/depth-pulled queue row's own EnergyBar would fall behind
        /// (and be genuinely Z-test-occluded by) that row's own
        /// foregrounded character.
        /// </summary>
        public void SetEnergyBarPresentationDepth(float depthWorldUnits)
        {
            _energyBarDepth = depthWorldUnits;
            ApplyEnergyBarLocalPosition();
        }

        /// <summary>
        /// Keeps this collector's contact shadow a fixed distance BEHIND
        /// wherever Visual currently is (see ContactShadowCameraPullDistance) -
        /// the mirror image of SetEnergyBarPresentationDepth above, called
        /// by the same CollectorPresentation.ApplyPresentationDepth call
        /// site with the same depthWorldUnits, so the shadow tracks every
        /// queue-row depth change and every waiting-source baseline reset
        /// automatically, in every location (Queue, Conveyor, WaitingLine,
        /// Recovery Row) - there is no separate shadow-positioning call site
        /// anywhere else.
        /// </summary>
        public void SetContactShadowPresentationDepth(float depthWorldUnits)
        {
            _contactShadowDepth = depthWorldUnits;
            ApplyContactShadowLocalPosition();
        }

        private void ApplyContactShadowLocalPosition()
        {
            if (_contactShadowTransform == null)
                return;

            Vector3 baseOffset = new Vector3(0f, _contactShadowBaseY, 0f)
                + GameplayLayout.CameraForward * ContactShadowCameraPullDistance;
            _contactShadowTransform.localPosition = baseOffset + (-GameplayLayout.CameraForward) * _contactShadowDepth;
        }

        /// <summary>
        /// Creates this collector's contact shadow as a sibling of Visual
        /// under PresentationOffset (see the contact shadow field group's
        /// own class remarks, and PresentationOffset's own remarks for why
        /// a sibling of Visual there rather than a Visual descendant) -
        /// called from Initialize AFTER CharacterVisual exists and has
        /// already been recentered, since this measures it.
        ///
        /// A stylized camera-facing presentation decal (see
        /// ContactShadowMesh), sized directly as a multiple of the
        /// character's own measured visible width (ContactShadowWidthFraction/
        /// HeightToWidthRatio) rather than derived from any ground-plane
        /// projection - visual similarity to the approved reference is the
        /// only goal here, not physical accuracy. Measured once, at this
        /// collector's rest pose, so placement/size never changes afterward.
        /// </summary>
        private void CreateContactShadow()
        {
            Bounds? footprintBounds = CollectorAnimation.ComputeLocalRendererBounds(transform);

            float width;
            float footprintY;

            if (footprintBounds.HasValue)
            {
                Bounds bounds = footprintBounds.Value;
                width = bounds.size.x * ContactShadowWidthFraction;
                footprintY = bounds.min.y;
            }
            else
            {
                Debug.LogError($"CollectorView: '{name}' has no renderers to measure a contact shadow footprint from; using a fallback size/position.", this);
                width = ContactShadowFallbackWidth;
                footprintY = -width * 0.3f;
            }

            float height = width * ContactShadowHeightToWidthRatio;

            var shadowObject = new GameObject("ContactShadow", typeof(MeshFilter), typeof(MeshRenderer));
            shadowObject.transform.SetParent(_presentationOffset, false);
            shadowObject.transform.localScale = new Vector3(width, height, 1f);

            var meshFilter = shadowObject.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = ContactShadowMesh.SharedMesh;

            var meshRenderer = shadowObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = ContactShadowMesh.SharedMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            // One order step behind the rest of this project's default-layer
            // sprites (PixelGrid cells, WaitingSlot outlines, Conveyor path)
            // as an explicit, cheap extra guarantee alongside the real Z
            // placement above.
            meshRenderer.sortingOrder = -1;

            _contactShadowTransform = shadowObject.transform;
            _contactShadowBaseY = footprintY + height * ContactShadowOcclusionBias;
            ApplyContactShadowLocalPosition();
        }

        private void ApplyEnergyBarLocalPosition()
        {
            if (_energyBarTransform == null)
                return;

            float verticalOffset = _energyBarIsAboveCharacter ? EnergyBarConveyorVerticalOffset : EnergyBarQueueVerticalOffset;
            Dictionary<int, float> speciesCorrectionTable = _energyBarIsAboveCharacter ? EnergyBarSpeciesConveyorCorrection : EnergyBarSpeciesQueueCorrection;
            float speciesCorrection = speciesCorrectionTable.TryGetValue(_matchId, out float correction) ? correction : 0f;
            // No riding-only correction term here any more: EnergyBar is
            // now a child of PresentationOffset, the same node
            // ApplyConveyorPresentationOffset moves - so it automatically
            // moves together with CharacterVisual while riding without
            // needing to separately re-add that shift to its own gap
            // (verticalOffset/speciesCorrection, both tuned against real
            // screenshots of the character's own baseline position) here.
            Vector3 baseOffset = new Vector3(0f, verticalOffset + speciesCorrection, 0f) + (-GameplayLayout.CameraForward) * EnergyBarForegroundPullDistance;
            _energyBarTransform.localPosition = baseOffset + (-GameplayLayout.CameraForward) * _energyBarDepth;
        }

        private void OnRemainingHungerChanged(int remainingHunger)
        {
            if (_energyBarView == null)
                return;

            // energyGained = MaxHunger - RemainingHunger; fill =
            // energyGained / MaxHunger (see docs task section 4) - driven
            // through SetMaxValue/SetCurrentValue's own generic current/max
            // fill fraction, then SetDisplayValue immediately overrides the
            // visible number back to the raw remainingHunger (SetCurrentValue's
            // own Refresh would otherwise show energyGained instead - see
            // SetDisplayValue's own remarks on why it must run last).
            // HungerCapacity is read fresh from the rider every call rather
            // than cached at Initialize time - it is fixed for this
            // collector's lifetime, but this avoids ever silently drifting
            // out of sync with the actual gameplay source of truth if that
            // ever changes.
            int maxHunger = Mathf.Max(1, _conveyorRider.HungerCapacity);
            int clampedRemaining = Mathf.Clamp(remainingHunger, 0, maxHunger);
            _energyBarView.SetMaxValue(maxHunger);
            _energyBarView.SetCurrentValue(maxHunger - clampedRemaining);
            _energyBarView.SetDisplayValue(clampedRemaining);
        }

        /// <summary>
        /// Instantiates energyBarPrefab as a sibling of Visual (see the
        /// EnergyBar field group's own class remarks) and applies every
        /// static, per-instance setup that never changes again afterward:
        /// the compensating localScale (cancels CollectorSpriteScale so
        /// every species reads at the same EnergyBarWorldWidth - see its own
        /// remarks), the camera-tilt-compensating localRotation, and the
        /// fill colour resolved from matchId via ColorPalette (Assets/Art/
        /// ColorPalette.md's runtime mirror - the same single source of
        /// truth CharacterAssetBuilder/CharacterDatabase already key off of
        /// for this exact Match ID, never a second/duplicated colour table -
        /// see docs task "Refine the live Energy Bar presentation" section
        /// 5). Falls back to EnergyFillFallbackColor (and logs an error) if
        /// matchId is outside ColorPalette's approved 1-20 table - every
        /// real collector's MatchId always comes from approved level/debug
        /// data, so this should never actually trigger. Logs and leaves
        /// _energyBarTransform/_energyBarView null (every EnergyBar call
        /// elsewhere already null-checks) rather than throwing if
        /// energyBarPrefab itself is null - the same "missing expected
        /// reference" convention CharacterAssetBuilder/CollectorQueueBoard
        /// already use throughout this codebase.
        /// </summary>
        private void CreateEnergyBar(GameObject energyBarPrefab, int matchId)
        {
            if (energyBarPrefab == null)
            {
                Debug.LogError($"CollectorView: '{name}' was initialized with no energyBarPrefab; this collector will show no EnergyBar.", this);
                return;
            }

            var instance = Instantiate(energyBarPrefab, _presentationOffset, false);
            instance.name = "EnergyBar";

            Transform energyBarTransform = instance.transform;
            energyBarTransform.localRotation = EnergyBarLocalRotation;
            energyBarTransform.localScale =
                Vector3.one * (EnergyBarWorldWidth / (EnergyBarPrefabCanvasWidth * GameplayLayout.CollectorSpriteScale));

            _energyBarTransform = energyBarTransform;
            _energyBarView = instance.GetComponent<EnergyBarView>();

            if (_energyBarView == null)
            {
                Debug.LogError($"CollectorView: '{name}' - energyBarPrefab '{energyBarPrefab.name}' has no EnergyBarView component.", this);
                return;
            }

            if (!ColorPalette.TryGetByMatchId(matchId, out Color fillColor))
            {
                Debug.LogError($"CollectorView: '{name}' - Match ID {matchId} has no ColorPalette entry; EnergyBar fill falls back to '{EnergyFillFallbackColor}'.", this);
                fillColor = EnergyFillFallbackColor;
            }

            _energyBarView.SetFillColor(fillColor);
            ApplyEnergyBarLocalPosition();
        }
    }
}
