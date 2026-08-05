using System;
using System.Collections;
using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Presentation-only micro-animation of a collector's Visual/VisualMotion
    /// pivots — never the collector root, which gameplay owns
    /// (CollectorQueueBoard placement, ConveyorSystem movement, WaitingLine/
    /// RecoveryRow reparenting) — see CollectorView, which creates
    /// Visual/VisualMotion as descendants of that root specifically so
    /// presentation animation never fights gameplay for the same transform.
    ///
    /// Two pivots, two disjoint owners, by construction:
    ///
    /// - Visual's localRotation (yaw only) is owned exclusively by Update()
    ///   below, every frame, for as long as this component is enabled. No
    ///   coroutine, no Play* method, and no reaction routine ever touches it.
    ///   This is deliberate: the previous design ran a facing "tracker" as
    ///   a coroutine started at the tail of the boarding-turn routine, using
    ///   the same StartCoroutine/StopCoroutine slot machinery as every
    ///   squash/punch reaction. If a reaction fired before that boarding
    ///   routine reached its own tail (StartRoutine stops whatever
    ///   coroutine is currently running before starting a new one), the
    ///   tracker was silently killed before it ever started, freezing that
    ///   collector's facing forever — the "sometimes stop rotating" bug.
    ///   Worse, every reaction's ResetToBaseline also wrote a shared
    ///   _facingRotation field, so a reaction beginning before the tracker's
    ///   own coroutine resumed that frame could momentarily reassert a stale
    ///   angle — two different code paths racing to own one field on one
    ///   Transform is what "sometimes spin" and "sometimes face away" came
    ///   from. Update() has no such race: it is not a coroutine, cannot be
    ///   stopped by StartCoroutine bookkeeping, and there is exactly one
    ///   place in this file that ever writes Visual.localRotation.
    ///
    /// - VisualMotion's localScale/localPosition (never localRotation, which
    ///   is left at identity for its entire life) is owned by whichever
    ///   Play* reaction is currently running, exactly as before. Since
    ///   VisualMotion is a different Transform than Visual, a reaction
    ///   cannot touch facing even by accident — there is no rotation field
    ///   on the node it animates to fight over.
    ///
    /// Facing itself: FacingMode.Away is the fixed, approved waiting
    /// orientation (WaitingAwayYawDegrees — see its own remarks for how that
    /// value was determined); FacingMode.Riding reads ConveyorRider.
    /// RidingOrientation — a ConveyorEdge pair plus corner progress, set
    /// every frame by ConveyorSystem from the rider's own path progress
    /// (see ConveyorPath.SampleOrientation) — and turns that into a target
    /// yaw via ComputeConveyorFacingRotation. This replaced an earlier
    /// design that aimed Visual directly at the pixel grid's world position
    /// every frame: since the grid sits at a fixed point but a rider's
    /// position sweeps along an entire straight edge, "aim at the grid"
    /// kept the yaw slowly swinging for the whole time a rider crossed a
    /// straight, not just while turning a corner — the "spinning toy"
    /// symptom this was rewritten to fix. Segment-based facing instead
    /// holds one fixed yaw per straight edge (EdgeYawDegrees) and only ever
    /// changes yaw while ConveyorRider.RidingOrientation reports a nonzero
    /// corner (FromEdge != ToEdge), interpolating FromEdge's yaw to ToEdge's
    /// by CornerT via Quaternion.Slerp — which, like RotateTowards below,
    /// always takes the shorter arc between two quaternions, so a corner
    /// turn can never become a multi-turn spin.
    /// Both modes reach their target via Quaternion.RotateTowards, which is
    /// what "always choose the shortest angular interpolation" and "no full
    /// spins" mean here: RotateTowards always turns the short way around and
    /// advances at a capped degrees-per-second rate, so it can never
    /// overshoot into a multi-turn spin, and since it is applied every frame
    /// regardless of mode, entering Riding produces the boarding turn "for
    /// free" — no separate turn coroutine is needed any more. On a straight
    /// edge the Riding target itself never moves, so RotateTowards settles
    /// on it exactly and then leaves it untouched frame after frame — yaw
    /// "remains completely fixed" in the sense the conveyor loop requires,
    /// not merely close to fixed.
    /// </summary>
    public class CollectorAnimation : MonoBehaviour
    {
        // Live-tunable in the Inspector, including on a live-selected
        // Collector in Play Mode, specifically so the waiting-idle feel can
        // be calibrated against the real portrait Game view instead of by
        // editing hardcoded constants and restarting. idleBreathingSpeed,
        // idleWidthExpansion, and idleHeightExpansion are all read fresh
        // every frame inside IdleBreathingRoutine, so editing any of them on
        // a running collector changes its very next frame.
        // idleSpeedVariance/idleAmplitudeVariance are only consulted once,
        // in Awake, to bake this collector's own persistent random factor —
        // by design, since that factor is this collector's individuality,
        // not something a live edit should retroactively rewrite; a changed
        // variance only affects collectors spawned after the change (e.g.
        // the next Play Mode run).
        //
        // Deliberately no independent vertical-float field: a waiting Mofu
        // must read as planted and breathing, not floating or rocking — see
        // IdleBreathingRoutine for the grounded-expansion approach that
        // replaced them.
        //
        // Calibrated final defaults: quick enough to read clearly at the
        // real portrait Game view scale, but well below the exaggerated
        // diagnostic values (0.8 / 0.10 / 0.12) used to confirm the motion
        // was actually reaching Visual — those made Mofus look like slowly
        // inflating balloons, which these are tuned to avoid.
        [Header("Waiting Idle (Back Idle only: Queue / WaitingLine / RecoveryRow)")]
        [SerializeField, Tooltip("Cycles-per-second speed of the shared waiting-idle breathing wave, before this collector's own random speed variance below is applied. Lower = calmer, slower breathing.")]
        [Min(0f)]
        private float idleBreathingSpeed = 1.05f;

        [SerializeField, Tooltip("Random per-collector speed variance around Idle Breathing Speed, as a fraction (0.1 = ±10%). Drawn once per collector in Awake so collectors do not breathe in lockstep.")]
        [Range(0f, 1f)]
        private float idleSpeedVariance = 0.1f;

        [SerializeField, Tooltip("How much this collector's width expands at the peak of each waiting-idle breath, as a fraction of its baseline scale (0.05 = 5%). The body only ever expands from baseline and returns — it never compresses below it.")]
        [Min(0f)]
        private float idleWidthExpansion = 0.05f;

        [SerializeField, Tooltip("How much this collector's height expands at the peak of each waiting-idle breath, as a fraction of its baseline scale (0.06 = 6%). Never compresses below baseline; the bottom edge is compensated (see IdleBreathingRoutine) so only the top visibly rises, keeping the collector planted.")]
        [Min(0f)]
        private float idleHeightExpansion = 0.06f;

        [SerializeField, Tooltip("Random per-collector amplitude variance applied to both expansion amounts above together, as a fraction (0.1 = ±10%). Drawn once per collector in Awake.")]
        [Range(0f, 1f)]
        private float idleAmplitudeVariance = 0.1f;

        private const float BoardingBounceDuration = 0.22f;
        private const float BoardingBounceAmount = 0.12f;

        private const float EatingPunchDuration = 0.16f;
        private const float EatingPunchAmount = 0.14f;

        private const float SatisfiedPunchDuration = 0.3f;
        private const float SatisfiedPunchAmount = 0.18f;
        private const float SatisfiedHopHeight = 0.05f;

        private const float HeartPulseDuration = 0.35f;
        private const float HeartPulseAmount = 0.25f;
        private const float HeartCollapseDuration = 0.22f;

        private static readonly Vector3 BaselinePosition = Vector3.zero;

        // The mesh's actual authored front, determined empirically rather
        // than assumed: a batch-mode diagnostic rendered a queued collector's
        // Visual at yaw 0/45/90/.../315 degrees (CollectorAnimation disabled
        // so nothing fought the pose) and compared each against which angle
        // shows the face (eyes/mouth) versus the fur-only back. Result: yaw 0
        // shows the back — no face at all; the face is centered on yaw 180,
        // clearly visible there and faintly visible at 135/225. This is the
        // OPPOSITE of the previous "identity = front" assumption, which
        // predated the Visual/MofuModel wrapper and pivot correction and was
        // never re-verified against them — that stale assumption, not the
        // wrapper or the pivot fix, is why queued Mofus were showing their
        // face to the camera instead of their back. At yaw 0 the mesh's face
        // points world +Z (away from the camera, deeper into the scene); at
        // yaw 180 it points -Z (toward the camera) — see
        // ComputeConveyorFacingRotation/EdgeYawDegrees, which were derived
        // from (and are only correct under) this same confirmed orientation.
        private const float WaitingAwayYawDegrees = 0f;

        // The mesh's confirmed face-toward-camera angle (see
        // WaitingAwayYawDegrees's remarks) — kept only as
        // SetFacingImmediate's unused faceAway:false fallback; the riding
        // pose is always the dynamic target from ComputeConveyorFacingRotation,
        // never this constant.
        private const float FacingCameraYawDegrees = 180f;

        // Capped turn rate applied by both Update()'s waiting-target and
        // riding-target RotateTowards calls (see class remarks) - fast
        // enough that riding tracking always keeps up with how quickly
        // ComputeConveyorFacingRotation's target can move (a corner turn
        // sweeps only 90 degrees over that corner's own travel time, well
        // under 90 degrees/second at ConveyorSystem's configured speed), and
        // that a boarding turn (worst case a full 180-degree reversal)
        // completes in a bit under a quarter second, comparable to the old
        // dedicated boarding-turn coroutine's own BoardingBounceDuration -
        // while still being an explicit, bounded rate rather than an
        // instant snap, which is what makes "no full spins" structural
        // rather than a matter of calling it correctly.
        private const float YawDegreesPerSecond = 900f;

        // How far Visual is pulled toward the camera (world -Z, since the
        // orthographic camera sits at Z=-10 looking down +Z — see
        // BootstrapSceneCreator.CreateCamera) once the terminal Satisfied/
        // Heart sequence begins, so the depth buffer places this collector's
        // model in front of an ordinary riding Mofu that happens to occupy
        // the same screen position for the brief remainder of this
        // collector's life. -1 world unit clears a full Mofu body's own
        // depth extent (~0.7 world units at MofuModel's authored scale — see
        // CollectorView.HungerTextLocalOffset's remarks for how that figure
        // was measured) with margin, so the depth test wins regardless of
        // the other rider's exact facing or breathing wobble. This is a
        // one-way move, never reset: Visual's localPosition is otherwise
        // always zero and nothing else ever writes it (see CollectorView),
        // and CollectorLifecycle destroys this collector shortly after the
        // terminal sequence completes, so there is nothing to restore.
        private const float TerminalForegroundZOffset = -1f;

        private enum FacingMode
        {
            Away,
            Riding,
        }

        private CollectorView _view;
        private ConveyorRider _conveyorRider;
        private Transform _visual;
        private Transform _visualMotion;
        private Coroutine _activeRoutine;
        private bool _terminal;
        private FacingMode _facingMode = FacingMode.Away;

        // Per-collector breathing individuality, all three drawn once in
        // Awake so collectors created together neither start from the same
        // phase, nor breathe at exactly the same speed, nor with exactly
        // the same amplitude. Purely a presentation seed — none of it ever
        // feeds into gameplay state, so this randomness cannot affect
        // deterministic gameplay. _idleSpeedFactor/_idleAmplitudeFactor are
        // multipliers baked once from idleSpeedVariance/idleAmplitudeVariance
        // at spawn time — this collector's individuality, which a later
        // Inspector edit to those two variance fields deliberately does not
        // retroactively rewrite (see the field comments above).
        private float _idlePhase;
        private float _idleSpeedFactor;
        private float _idleAmplitudeFactor;

        // VisualMotion's own authored scale, captured once the first time it
        // is resolved (see EnsureVisual/CaptureBaseline) — the Mofu prefab's
        // root carries its own non-unit scale (tuned so the model's rendered
        // footprint matches the old sprite's visible size; see
        // Mofu3DSetup.PrefabRootScale), so "baseline" here is that captured
        // scale, never a hardcoded Vector3.one. Every routine below scales
        // relative to this, the same way they used to scale relative to a
        // flat Vector3.one when Visual held a SpriteRenderer directly.
        private Vector3 _baselineScale = Vector3.one;

        // VisualMotion's mesh bounds height in its own local space (a fixed
        // asset-level value, unlike Renderer.bounds, which is a world-space
        // AABB that changes with the transform's current scale and would
        // need re-baselining every frame). Cached once, the first time
        // VisualMotion is resolved, and never recomputed afterwards — see
        // EnsureVisual/CaptureBaseline and IdleBreathingRoutine's grounding
        // compensation, which depends on this staying a fixed baseline
        // throughout.
        private float _baselineLocalHeight = 1f;

        private CollectorView View => _view != null ? _view : _view = GetComponent<CollectorView>();

        // Same GameObject as CollectorView (both live on the collector root
        // — see CollectorView's [RequireComponent(typeof(ConveyorRider))]),
        // so a plain GetComponent resolves this without any injection.
        private ConveyorRider Rider => _conveyorRider != null ? _conveyorRider : _conveyorRider = GetComponent<ConveyorRider>();

        /// <summary>
        /// True once this component can actually resolve both Visual and
        /// VisualMotion. CollectorPresentation checks this before committing
        /// to the terminal Satisfied/Heart sequence, so a missing pivot
        /// falls back to immediate destruction instead of starting a
        /// reaction that could never complete or fire its completion
        /// callback.
        /// </summary>
        public bool HasVisual => EnsureVisual() != null;

        private void Awake()
        {
            _idlePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _idleSpeedFactor = UnityEngine.Random.Range(1f - idleSpeedVariance, 1f + idleSpeedVariance);
            _idleAmplitudeFactor = UnityEngine.Random.Range(1f - idleAmplitudeVariance, 1f + idleAmplitudeVariance);
        }

        /// <summary>
        /// The sole owner of Visual's localRotation — see the class remarks
        /// for why this is a persistent Update(), not a coroutine. Every
        /// frame, regardless of which VisualMotion reaction (if any) happens
        /// to be running, this turns Visual toward whichever target the
        /// current FacingMode implies, via the shortest arc, at a capped
        /// rate — never a snap, never a multi-turn spin.
        /// </summary>
        private void Update()
        {
            if (_visual == null)
                return;

            Quaternion target = _facingMode == FacingMode.Riding
                ? ComputeConveyorFacingRotation()
                : Quaternion.Euler(0f, WaitingAwayYawDegrees, 0f);

            _visual.localRotation = Quaternion.RotateTowards(_visual.localRotation, target, YawDegreesPerSecond * Time.deltaTime);
        }

        /// <summary>
        /// Snaps facing directly to the approved waiting pose (faceAway:
        /// true, the only value ever actually passed) — used before idle
        /// breathing starts (waiting anywhere: Queue, WaitingLine,
        /// RecoveryRow), the equivalent of the old system cutting straight
        /// to the Back Idle sprite with no turn animation. faceAway: false
        /// is kept as a defined, if currently unused, fallback rather than
        /// removed. Switches FacingMode back to Away so Update() stops
        /// chasing the (now stale) grid-facing target — this is the "stop
        /// riding" half of facing ownership; the "start riding" half is
        /// PlayBoardingBounce below.
        /// </summary>
        public void SetFacingImmediate(bool faceAway)
        {
            _facingMode = FacingMode.Away;

            if (EnsureVisual() != null)
                _visual.localRotation = Quaternion.Euler(0f, faceAway ? WaitingAwayYawDegrees : FacingCameraYawDegrees, 0f);
        }

        /// <summary>
        /// Starts (or resumes) the subtle waiting-idle breathing loop, used
        /// only while a collector shows Back Idle — a Front Eating rider's
        /// own bite reactions already provide its active motion, so nothing
        /// calls this while riding. Every call re-enters the sine wave at
        /// this collector's own _idlePhase rather than phase zero, so a
        /// collector that stops and later restarts breathing (e.g. queue →
        /// board → rollback back to queue) always resumes from the same
        /// individual point other collectors don't share, instead of
        /// resyncing with them. Also runs at this collector's own
        /// _idleSpeedFactor times idleBreathingSpeed, not the shared speed
        /// alone, for the same reason — see IdleBreathingRoutine for the
        /// grounded breathing motion this actually plays.
        /// </summary>
        public void PlayIdleBreathing() =>
            StartRoutine(IdleBreathingRoutine());

        /// <summary>
        /// Plays the boarding reaction: a squash/bounce on VisualMotion.
        /// Facing is not this routine's concern any more — switching
        /// FacingMode to Riding here is enough to make Update() start
        /// turning Visual toward the pixel grid from this collector's
        /// boarding position immediately, continuing to re-aim as the
        /// conveyor moves it for the rest of this collector's ride, with no
        /// separate turn coroutine and nothing left to interrupt.
        /// </summary>
        public void PlayBoardingBounce(Action onComplete = null)
        {
            _facingMode = FacingMode.Riding;
            StartRoutine(BoardingBounceRoutine(onComplete));
        }

        public void PlayEatingPunch(Action onComplete = null) =>
            StartRoutine(SquashPunchRoutine(EatingPunchDuration, EatingPunchAmount, onComplete));

        public void PlaySatisfiedPunch(Action onComplete = null) =>
            StartRoutine(SatisfiedPunchRoutine(onComplete));

        /// <summary>
        /// Terminal: one heart pulse, then a collapse to nothing. No
        /// animation may play after this — every later Play* call becomes a
        /// no-op for the rest of this collector's lifetime.
        /// </summary>
        public void PlayHeartPulseAndCollapse(Action onComplete) =>
            StartRoutine(HeartPulseAndCollapseRoutine(onComplete), forceTerminal: true);

        /// <summary>
        /// Pulls Visual toward the camera (see TerminalForegroundZOffset) so
        /// the terminal Satisfied/Heart sequence renders in front of an
        /// ordinary riding Mofu sharing its screen position, instead of the
        /// two 3D models depth-fighting or merging. Called exactly once, by
        /// CollectorPresentation right as its terminal sequence begins —
        /// never reset, since this collector is destroyed shortly after that
        /// sequence completes (see TerminalForegroundZOffset's remarks).
        /// Touches only Visual's localPosition, never the collector root, so
        /// gameplay position and conveyor rider spacing are unaffected;
        /// harmless no-op if Visual cannot be resolved.
        /// </summary>
        public void EnterTerminalForeground()
        {
            if (EnsureVisual() == null)
                return;

            _visual.localPosition = new Vector3(0f, 0f, TerminalForegroundZOffset);
        }

        private Transform EnsureVisual()
        {
            if (_visual != null)
                return _visual;

            CollectorView view = View;
            if (view == null || view.Visual == null || view.VisualMotion == null)
                return null;

            _visual = view.Visual;
            _visualMotion = view.VisualMotion;
            CaptureBaseline();
            return _visual;
        }

        /// <summary>
        /// Captures VisualMotion's authored scale (see _baselineScale) and
        /// its mesh-local bounds height (see _baselineLocalHeight), exactly
        /// once, right after VisualMotion is first resolved.
        /// </summary>
        private void CaptureBaseline()
        {
            if (_visualMotion == null)
                return;

            _baselineScale = _visualMotion.localScale;

            Bounds? localBounds = ComputeLocalRendererBounds(_visualMotion);
            if (localBounds.HasValue)
                _baselineLocalHeight = localBounds.Value.size.y;
        }

        /// <summary>
        /// The Y-axis rotation that turns Visual's authored front to face
        /// the pixel grid from wherever ConveyorRider.RidingOrientation says
        /// this collector currently is on the loop — a fixed yaw
        /// (EdgeYawDegrees) while RidingOrientation reports a straight
        /// (FromEdge == ToEdge), or a Slerp between the two adjacent
        /// straights' fixed yaws, driven by RidingOrientation.CornerT, while
        /// it reports a corner. Quaternion.Slerp always interpolates two
        /// rotations along their shorter arc, so a corner turn can never
        /// wind up going the long way around into something resembling a
        /// spin, and since CornerT is itself derived from path progress (see
        /// ConveyorPath.SampleOrientation), the interpolation reaches
        /// ToEdge's yaw exactly when the rider reaches the corner's last
        /// point — never early, never lingering into the next straight.
        /// If this collector has no ConveyorRider (should not happen; every
        /// collector root carries one per CollectorView's
        /// RequireComponent), holds whatever yaw Visual already has rather
        /// than guessing.
        /// </summary>
        private Quaternion ComputeConveyorFacingRotation()
        {
            ConveyorRider rider = Rider;
            if (rider == null)
                return _visual.localRotation;

            ConveyorOrientationSample orientation = rider.RidingOrientation;
            float fromYaw = EdgeYawDegrees(orientation.FromEdge);

            if (orientation.FromEdge == orientation.ToEdge)
                return Quaternion.Euler(0f, fromYaw, 0f);

            float toYaw = EdgeYawDegrees(orientation.ToEdge);
            return Quaternion.Slerp(
                Quaternion.Euler(0f, fromYaw, 0f),
                Quaternion.Euler(0f, toYaw, 0f),
                orientation.CornerT);
        }

        /// <summary>
        /// The one fixed yaw a straight edge holds for its entire length —
        /// derived by solving the same Quaternion.Euler(0, yaw, 0) * (0,0,1)
        /// = normalize(toGrid.x, 0, toGrid.y) relationship the previous
        /// continuous grid-aim design used (local +Z, not -Z, because the
        /// mesh's confirmed authored face points world +Z at yaw 0 — see
        /// WaitingAwayYawDegrees's remarks), but evaluated once per edge at
        /// its pure toward-grid direction rather than every frame at a
        /// rider's exact position: Bottom's direction to the grid is
        /// straight up, (0, 1) -> yaw 0 (matching WaitingAwayYawDegrees's
        /// confirmed back-to-camera pose, coincidentally, not by design);
        /// Right's is straight left, (-1, 0) -> yaw -90; Top's is straight
        /// down, (0, -1) -> yaw 180; Left's is straight right, (1, 0) -> yaw
        /// 90.
        /// </summary>
        private static float EdgeYawDegrees(ConveyorEdge edge)
        {
            switch (edge)
            {
                case ConveyorEdge.Bottom:
                    return 0f;
                case ConveyorEdge.Right:
                    return -90f;
                case ConveyorEdge.Top:
                    return 180f;
                case ConveyorEdge.Left:
                    return 90f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Combines every MeshFilter under visual into a single bounds,
        /// expressed in visual's own local space — the 3D equivalent of the
        /// old SpriteRenderer.sprite.bounds read, which was already
        /// local-space by construction. A mesh's own Mesh.bounds is
        /// local-space to that mesh's own transform, not necessarily
        /// visual's, so each is re-expressed relative to visual before being
        /// combined (matters if the prefab nests the mesh under its own
        /// child transform, as an FBX import typically does).
        ///
        /// Public rather than private: CollectorView.Initialize reuses this
        /// same bounds computation to derive MofuModel's pivot correction
        /// from the imported model's own mesh bounds (its pivot sits at the
        /// model's feet, not its center — see CollectorView), rather than
        /// duplicating this matrix math there. Also called from the Editor
        /// assembly by CharacterAssetBuilder, so build-time root-scale
        /// measurement and this runtime pivot correction always agree on
        /// exactly the same bounds for the same character — a plain
        /// visibility promotion, not a duplicate implementation.
        ///
        /// Walks every Renderer, not just MeshFilter/MeshRenderer pairs: a
        /// rigged visual prefab (e.g. a vendor character swapped in via
        /// CollectorQueueBoard.mofuVisualPrefab) carries its body mesh on a
        /// SkinnedMeshRenderer, which has no MeshFilter sibling at all — a
        /// MeshFilter-only walk would silently see none of it and either
        /// mis-center on an unrelated static child mesh or fall back to no
        /// correction. A SkinnedMeshRenderer's bounds are computed via
        /// BakeMesh (see TryGetLocalBounds) rather than read from its
        /// .localBounds property — that property is a STATIC, pose-
        /// independent value (confirmed empirically: posing a bone up to 50
        /// degrees on every axis left it completely unchanged), so it cannot
        /// reflect a build-time pose adjustment like CharacterAssetBuilder's
        /// Crab claw tuck at all. BakeMesh snapshots the renderer's actual
        /// current skinned vertex positions in its own local space instead,
        /// so bounds always match whatever pose the bones are really in.
        /// </summary>
        public static Bounds? ComputeLocalRendererBounds(Transform visual)
        {
            Bounds? combined = null;

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (!TryGetLocalBounds(renderer, out Bounds localBounds, out Transform boundsSpace))
                    continue;

                Matrix4x4 relativeMatrix = visual.worldToLocalMatrix * boundsSpace.localToWorldMatrix;
                Bounds transformed = TransformBounds(localBounds, relativeMatrix);

                if (combined == null)
                {
                    combined = transformed;
                }
                else
                {
                    Bounds b = combined.Value;
                    b.Encapsulate(transformed);
                    combined = b;
                }
            }

            return combined;
        }

        /// <summary>
        /// SkinnedMeshRenderer.localBounds is a STATIC, pose-independent
        /// value (confirmed empirically: rotating a bone by up to 50 degrees
        /// on each axis and re-reading localBounds produced the exact same
        /// bounds every time) - it does not shrink or grow as bones are
        /// posed, whether by an Animator or, as CharacterAssetBuilder's Crab
        /// claw adjustment does, by a one-off build-time transform edit.
        /// BakeMesh snapshots the renderer's actual current skinned vertex
        /// positions instead, so bounds always reflect whatever pose the
        /// bones are actually in right now - required for
        /// CharacterAssetBuilder's Crab pose adjustment to affect the scale
        /// it computes at all, and more accurate in general for every other
        /// species too (their bind pose likely happens to already be close
        /// to whatever localBounds was authored from, which is why this was
        /// never visibly wrong for them). useScale:false keeps the baked
        /// vertices in the renderer's own unscaled local space, matching the
        /// MeshFilter branch below exactly, so boundsSpace is simply the
        /// renderer's own transform here - no rootBone-relative-space
        /// handling needed any more (BakeMesh's output space is always the
        /// renderer's own transform, unlike the old .localBounds property).
        /// </summary>
        private static bool TryGetLocalBounds(Renderer renderer, out Bounds localBounds, out Transform boundsSpace)
        {
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && skinnedMeshRenderer.sharedMesh != null)
            {
                var bakedMesh = new Mesh();
                skinnedMeshRenderer.BakeMesh(bakedMesh, useScale: false);
                localBounds = bakedMesh.bounds;
                boundsSpace = skinnedMeshRenderer.transform;
                UnityEngine.Object.DestroyImmediate(bakedMesh);
                return true;
            }

            if (renderer.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            {
                localBounds = meshFilter.sharedMesh.bounds;
                boundsSpace = renderer.transform;
                return true;
            }

            localBounds = default;
            boundsSpace = null;
            return false;
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 newCenter = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 newExtents = Vector3.zero;

            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 axisVector = matrix.GetColumn(axis) * extents[axis];
                newExtents.x += Mathf.Abs(axisVector.x);
                newExtents.y += Mathf.Abs(axisVector.y);
                newExtents.z += Mathf.Abs(axisVector.z);
            }

            return new Bounds(newCenter, newExtents * 2f);
        }

        private void StartRoutine(IEnumerator routine, bool forceTerminal = false)
        {
            if (_terminal)
                return;

            if (EnsureVisual() == null)
                return;

            if (_activeRoutine != null)
                StopCoroutine(_activeRoutine);

            ResetToBaseline();

            if (forceTerminal)
                _terminal = true;

            _activeRoutine = StartCoroutine(routine);
        }

        /// <summary>
        /// Resets VisualMotion only — localRotation is never part of this
        /// component's baseline any more (see the class remarks); Visual's
        /// facing is Update()'s concern exclusively, untouched by every
        /// reaction this resets.
        /// </summary>
        private void ResetToBaseline()
        {
            _visualMotion.localScale = _baselineScale;
            _visualMotion.localPosition = BaselinePosition;
        }

        /// <summary>
        /// Planted "breathing", not floating or rocking: a normalized
        /// breath in [0, 1] — (sin(t) + 1) * 0.5, never the raw -1..1 wave —
        /// drives width and height expansion together from the same cycle,
        /// so the body only ever grows from its authored baseline and
        /// returns to it, never compressing below baseline and never
        /// looking like jelly. Depth (Z) is left at baseline throughout, the
        /// same as width/height used to be the only axes touched when Visual
        /// held a flat sprite. No independent vertical float: VisualMotion's
        /// own local Y position is touched for exactly one reason — to
        /// compensate for the model's centered pivot, which would otherwise
        /// raise and lower the feet by the same amount as the head every
        /// time height scale changes. Shifting VisualMotion up by half of
        /// the height just gained (using _baselineLocalHeight, this
        /// collector's fixed local mesh height, never a live bounds query)
        /// keeps the bottom visible edge exactly where it was at baseline,
        /// so only the top of the fur visibly rises — reading as standing
        /// and breathing, not bouncing. Resolves to the exact authored
        /// baseline (ResetToBaseline) the moment anything else starts.
        ///
        /// idleBreathingSpeed/idleWidthExpansion/idleHeightExpansion are
        /// read directly from the serialized fields every iteration (never
        /// cached into a local), so editing any of them in the Inspector —
        /// including on a live-selected collector in Play Mode — changes
        /// this loop's very next frame.
        /// </summary>
        private IEnumerator IdleBreathingRoutine()
        {
            float t = _idlePhase;
            while (true)
            {
                t += Time.deltaTime * idleBreathingSpeed * _idleSpeedFactor;
                float breath = (Mathf.Sin(t) + 1f) * 0.5f;

                float widthScale = 1f + breath * idleWidthExpansion * _idleAmplitudeFactor;
                float heightScale = 1f + breath * idleHeightExpansion * _idleAmplitudeFactor;

                _visualMotion.localScale = Vector3.Scale(_baselineScale, new Vector3(widthScale, heightScale, 1f));
                _visualMotion.localPosition = new Vector3(0f, _baselineLocalHeight * 0.5f * (heightScale - 1f), 0f);

                yield return null;
            }
        }

        /// <summary>
        /// The squash/bounce half of boarding — facing/turning is entirely
        /// Update()'s concern now (see PlayBoardingBounce), so this only
        /// ever touches VisualMotion's scale.
        /// </summary>
        private IEnumerator BoardingBounceRoutine(Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < BoardingBounceDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / BoardingBounceDuration);
                float wave = Mathf.Sin(t * Mathf.PI);

                _visualMotion.localScale = Vector3.Scale(_baselineScale, new Vector3(1f + wave * BoardingBounceAmount * 0.5f, 1f - wave * BoardingBounceAmount, 1f));
                yield return null;
            }

            _visualMotion.localScale = _baselineScale;
            onComplete?.Invoke();
        }

        private IEnumerator SquashPunchRoutine(float duration, float amount, Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float wave = Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI);
                _visualMotion.localScale = Vector3.Scale(_baselineScale, new Vector3(1f + wave * amount * 0.5f, 1f - wave * amount, 1f));
                yield return null;
            }

            _visualMotion.localScale = _baselineScale;
            onComplete?.Invoke();
        }

        private IEnumerator SatisfiedPunchRoutine(Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < SatisfiedPunchDuration)
            {
                elapsed += Time.deltaTime;
                float wave = Mathf.Sin(Mathf.Clamp01(elapsed / SatisfiedPunchDuration) * Mathf.PI);
                _visualMotion.localScale = Vector3.Scale(_baselineScale, new Vector3(1f - wave * SatisfiedPunchAmount * 0.5f, 1f + wave * SatisfiedPunchAmount, 1f));
                _visualMotion.localPosition = new Vector3(0f, wave * SatisfiedHopHeight, 0f);
                yield return null;
            }

            _visualMotion.localScale = _baselineScale;
            _visualMotion.localPosition = BaselinePosition;
            onComplete?.Invoke();
        }

        /// <summary>
        /// Uniform on all three axes (unlike the other reactions above,
        /// which only ever touch width/height) — a solid 3D body pulsing and
        /// collapsing reads more naturally inflating/deflating evenly than
        /// squashing on just two axes. Harmless on the old flat sprite too
        /// (depth scale was never visible), so this is a small deliberate
        /// improvement for the 3D model rather than a behavior change.
        /// </summary>
        private IEnumerator HeartPulseAndCollapseRoutine(Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < HeartPulseDuration)
            {
                elapsed += Time.deltaTime;
                float wave = Mathf.Sin(Mathf.Clamp01(elapsed / HeartPulseDuration) * Mathf.PI);
                _visualMotion.localScale = _baselineScale * (1f + wave * HeartPulseAmount);
                yield return null;
            }

            _visualMotion.localScale = _baselineScale;

            elapsed = 0f;
            while (elapsed < HeartCollapseDuration)
            {
                elapsed += Time.deltaTime;
                float scale = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / HeartCollapseDuration));
                _visualMotion.localScale = _baselineScale * scale;
                yield return null;
            }

            _visualMotion.localScale = Vector3.zero;
            onComplete?.Invoke();
        }
    }
}
