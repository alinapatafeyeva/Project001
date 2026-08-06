using System;
using System.Collections;
using Project001.Gameplay.Feeding;
using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Decides which pose this collector's Visual is currently in and asks
    /// CollectorAnimation to play it — holds no rendering or animation logic
    /// itself, CollectorView owns the instantiated model (on its Visual
    /// child) and CollectorAnimation owns every transform tween played on it.
    /// This is the sole caller into CollectorAnimation, so CollectorAnimation
    /// never has to decide which reaction fits which gameplay moment — this
    /// class is that single authority.
    ///
    /// With a single mesh instead of five sprites, "pose" now means facing
    /// (which way Visual is rotated) plus whichever reaction animation is
    /// playing, rather than which sprite is shown — see CollectorAnimation
    /// for the facing rotation this drives.
    ///
    /// Approved visible lifecycle: facing away (the equivalent of the old
    /// Back Idle) while waiting anywhere (CollectorQueueBoard, WaitingLine,
    /// Recovery Row) with idle breathing; facing the pixel grid (the
    /// equivalent of Front Eating, now a continuously re-aimed turn rather
    /// than a fixed camera-facing pose — see CollectorAnimation) for the
    /// entire time a collector actively rides the Conveyor, including every
    /// normal bite reaction; and, on the final bite only, still facing the
    /// grid through the eating punch, satisfied punch, and heart
    /// pulse/collapse. A collector that leaves the
    /// Conveyor unsatisfied and lands back in WaitingLine or Recovery Row
    /// switches back to facing away — see ShowWaitingBackIdle, called from
    /// CollectorLifecycle.ResolveLap and RecoveryRowController.ReceiveCollectors
    /// — and the same restoration happens if a boarding attempt is rolled
    /// back (see CollectorSelectionController.TrySelect).
    ///
    /// Sequencing rule (see CollectorAnimation for the transform-level half
    /// of this): at most one sequence coroutine is active on this component
    /// at a time — starting a new one always stops whatever sequence is
    /// currently running first. Once PlayFinalBiteSequence starts, every
    /// other public method here becomes a no-op: Satisfied/Heart is
    /// terminal, matching CollectorAnimation's own terminal Play* guard.
    ///
    /// As of the Pixel Feed Flow pipeline (see Project001.Gameplay.Feeding),
    /// PixelConsumer no longer calls PlayNormalBiteReaction/
    /// PlayFinalBiteSequence directly — a consumed pixel now becomes a
    /// FoodPacket, and RemainingHunger only decreases once that packet
    /// reaches the collector's FeedTarget (FoodFlightController), not at
    /// consume time. Phase 1 of that pipeline deliberately does not
    /// reconnect either bite reaction to FoodPacket's arrival — that
    /// reconnection (a future task, subscribing to FoodPacket.
    /// OnFoodPacketArrived) is explicitly out of this class's scope for now,
    /// so PlayNormalBiteReaction currently has no caller at all.
    /// CollectorLifecycle still calls PlayFinalBiteSequence on its own —
    /// its own Update loop already polls rider.IsSatisfied every frame
    /// rather than reacting to a specific consume, so it needed no change:
    /// it simply now observes RemainingHunger reaching zero slightly later
    /// (whenever the final packet arrives) than before, and still starts
    /// the terminal Satisfied/Heart sequence at that point either way.
    ///
    /// Phase 2 of the Pixel Feed Flow adds the actual "collector reacts"
    /// step: HandleFoodPacketArrived subscribes to FoodPacket.
    /// OnFoodPacketArrived (a static event - see Awake/OnDestroy), at the
    /// exact instant a packet reports arrival, for every packet bound for
    /// THIS collector. Deliberately a NEW reaction
    /// (CollectorAnimation.PlayFoodArrivedBounce/PlayFoodArrivedHighlight),
    /// not a reconnection of the pre-existing PlayNormalBiteReaction/
    /// PlayEatingPunch — the task that introduced this pipeline explicitly
    /// scoped "collector reacts" as its own new bounce/highlight pair, so
    /// PlayNormalBiteReaction still has no caller.
    ///
    /// Bounce is ON by default (enableFoodArrivedBounce below);
    /// highlight remains OFF (enableFoodArrivedHighlight). Both flags, and
    /// this class's own gated call sites for them, are unchanged from when
    /// both were off - only CollectorAnimation.PlayFoodArrivedBounce's OWN
    /// internal implementation was redesigned (see its own remarks): a real
    /// Play Mode staggered-sequence test of a fixed-duration squash/
    /// overshoot coroutine, restarted via StartRoutine on every single
    /// arrival, read as twitching/repeated spasms - each packet snapped
    /// VisualMotion back to baseline and restarted the curve at phase zero,
    /// so several packets close together produced several complete,
    /// independent squash cycles rather than one motion. The replacement is
    /// a persistent, retriggerable, target-driven damped value (never a
    /// coroutine restarted from zero), so a burst of arrivals now coalesces
    /// into one continuous soft "received it" pop instead. Highlight stays
    /// off for the same original reason (a highlight per packet read as
    /// unpleasant flickering) and its own implementation remains fully
    /// intact and untouched - only whether HandleFoodPacketArrived actually
    /// invokes it is gated, so a different pacing (once per feeding burst,
    /// final packet only, ...) can still be designed and tried later by
    /// changing HandleFoodPacketArrived's own trigger condition, without
    /// needing to resurrect any deleted or commented-out code.
    /// </summary>
    [RequireComponent(typeof(CollectorAnimation))]
    public class CollectorPresentation : MonoBehaviour
    {
        [Header("Food-arrived reaction (Pixel Feed Flow) - see class remarks")]
        [SerializeField, Tooltip("Whether HandleFoodPacketArrived plays CollectorAnimation.PlayFoodArrivedBounce on every FoodPacket arrival. On by default: PlayFoodArrivedBounce was redesigned as a persistent, retriggerable, target-driven damped pop (see its own remarks) specifically so this can stay on during a staggered multi-packet sequence without reading as twitching - a fixed-duration coroutine restarted per-arrival read as twitching, which is why this used to default to off.")]
        private bool enableFoodArrivedBounce = true;

        [SerializeField, Tooltip("Whether HandleFoodPacketArrived plays CollectorAnimation.PlayFoodArrivedHighlight on every FoodPacket arrival. Off by default: a highlight per packet during a staggered sequence reads as unpleasant flickering in real Play Mode testing. The highlight implementation itself is untouched - enable here to restore the old per-arrival behaviour, or leave off while a better-paced trigger is designed elsewhere.")]
        private bool enableFoodArrivedHighlight = false;

        private CollectorView _view;
        private CollectorAnimation _animation;
        private Coroutine _activeSequence;
        private bool _terminalStarted;

        private void Awake()
        {
            FoodPacket.OnFoodPacketArrived += HandleFoodPacketArrived;
        }

        private void OnDestroy()
        {
            FoodPacket.OnFoodPacketArrived -= HandleFoodPacketArrived;
        }

        private CollectorView View => _view != null ? _view : _view = GetComponent<CollectorView>();

        private CollectorAnimation Animation => _animation != null ? _animation : _animation = GetComponent<CollectorAnimation>();

        /// <summary>
        /// Raised exactly once, after the Heart collapse finishes — the only
        /// signal CollectorLifecycle needs in order to know the presentation
        /// shell's job is done and it is safe to Destroy the GameObject.
        /// Never raised for any other reason.
        /// </summary>
        public event Action VisualSequenceComplete;

        /// <summary>
        /// Shows the facing-away idle pose plus its subtle idle breathing.
        /// This is the pose for every waiting location — called by
        /// CollectorQueueBoard right after this collector's Visual is set up
        /// (CollectorView.Initialize), by CollectorLifecycle.ResolveLap when
        /// an unsatisfied collector lands in a WaitingLine slot, by
        /// RecoveryRowController.ReceiveCollectors when a collector is
        /// transferred into the Recovery Row, and by
        /// CollectorSelectionController when a failed boarding attempt is
        /// rolled back to its original source. Also requests the Waiting
        /// claw pose (see CharacterPosePresentation) on whichever species
        /// configured one — a no-op via the null-conditional operator for
        /// every other species, so this call site needs no per-species
        /// branching.
        /// </summary>
        public void ShowWaitingBackIdle()
        {
            if (_terminalStarted)
                return;

            Animation.SetFacingImmediate(faceAway: true);
            StopActiveSequence();
            Animation.PlayIdleBreathing();
            View.PosePresentation?.SetPose(CharacterPresentationPose.Waiting);
        }

        /// <summary>
        /// Sets this collector's presentation depth for its current queue
        /// row — the single call site every CollectorQueueBoard placement
        /// (initial placement and ShiftQueueUp) goes through, so a row's
        /// depth can never drift out of sync with its actual row index.
        /// Row 0 stays at the authored baseline depth (furthest back); each
        /// following row is pulled GameplayLayout.QueueRowDepthStep closer
        /// to the camera, so later rows genuinely render in front of
        /// earlier ones (a real Z-test outcome, not a sortingOrder guess —
        /// see GameplayLayout.QueueRowDepthStep's own remarks). Applies to
        /// both the character (CollectorAnimation) and its own hunger label
        /// (CollectorView) together, so the label never falls behind its
        /// own character once that character is pulled toward the camera.
        /// </summary>
        public void SetQueueRowDepth(int rowIndex)
        {
            if (_terminalStarted)
                return;

            ApplyPresentationDepth(rowIndex * GameplayLayout.QueueRowDepthStep);
        }

        /// <summary>
        /// Resets this collector's presentation depth to its authored
        /// baseline (no camera-forward pull) — the neutral depth every
        /// waiting source other than the queue itself uses: WaitingLine
        /// (CollectorLifecycle.ResolveLap), Recovery Row
        /// (RecoveryRowController.ReceiveCollectors), a rolled-back boarding
        /// restored to either, and boarding itself (CollectorSelectionController,
        /// so no stale queue-row depth ever survives onto the Conveyor).
        /// </summary>
        public void ClearPresentationDepth()
        {
            if (_terminalStarted)
                return;

            ApplyPresentationDepth(0f);
        }

        private void ApplyPresentationDepth(float depthWorldUnits)
        {
            Animation.SetPresentationDepth(depthWorldUnits);
            View.SetHungerTextPresentationDepth(depthWorldUnits);
        }

        /// <summary>
        /// Plays the boarding reaction — a short squash/bounce combined with
        /// a real turn to face the pixel grid (see
        /// CollectorAnimation.PlayBoardingBounce) — then keeps facing the
        /// grid, continuously re-aimed as the conveyor moves it, for the
        /// rest of this collector's time actively riding the Conveyor.
        /// Called by CollectorSelectionController the moment this collector
        /// boards, regardless of which source (CollectorQueueBoard,
        /// WaitingLine, or Recovery Row) it boarded from. Also requests the
        /// Conveyor claw pose (see CharacterPosePresentation) — fired
        /// immediately on boarding, in parallel with the bounce/turn below,
        /// and left in place for the rest of the ride: nothing here or in
        /// PlayNormalBiteReaction/PlayFinalBiteSequence ever requests
        /// Waiting again while riding, so the pose survives untouched
        /// through every bite reaction and the terminal sequence, right up
        /// until this collector is hidden or destroyed.
        /// </summary>
        public void ShowConveyorFrontEating()
        {
            if (_terminalStarted)
                return;

            View.PosePresentation?.SetPose(CharacterPresentationPose.Conveyor);
            StartSequence(BoardingSequence());
        }

        /// <summary>
        /// Plays a small eating punch while remaining faced toward the
        /// pixel grid for the entire time this collector actively rides the
        /// Conveyor. Called by PixelConsumer after a successful, non-final
        /// consume.
        /// </summary>
        public void PlayNormalBiteReaction()
        {
            if (_terminalStarted)
                return;

            StartSequence(NormalBiteSequence());
        }

        /// <summary>
        /// Plays the full completion sequence — eating punch, satisfied
        /// punch, one heart pulse, then a collapse, all while still facing
        /// the pixel grid — firing VisualSequenceComplete once it
        /// finishes. Terminal: once started, every other method on this
        /// component becomes a no-op. Idempotent — a second call (from either
        /// PixelConsumer or CollectorLifecycle, whichever reaches it first)
        /// is a no-op that still returns true, since the sequence is already
        /// in flight. Returns false only if the sequence cannot start at all
        /// (e.g. no Visual child to animate) — CollectorLifecycle treats
        /// false as its cue to destroy the collector immediately instead of
        /// waiting for a completion event that would never arrive.
        /// </summary>
        public bool PlayFinalBiteSequence()
        {
            if (_terminalStarted)
                return true;

            CollectorAnimation animation = Animation;
            if (animation == null || !animation.HasVisual)
                return false;

            _terminalStarted = true;
            StopActiveSequence();
            _activeSequence = StartCoroutine(FinalBiteSequence());
            return true;
        }

        /// <summary>
        /// The single subscriber every CollectorPresentation instance
        /// installs on the shared static FoodPacket.OnFoodPacketArrived
        /// event (see Awake) - filters to packets bound for THIS collector
        /// (packet.Collector == View) so a scene with many collectors and
        /// many simultaneously-arriving packets never mixes up whose
        /// reaction plays where; every other collector's own subscription
        /// runs the exact same check and reacts on its own instance only.
        /// Deliberately not routed through StartSequence/_activeSequence:
        /// the bounce/highlight reaction must never interrupt or be
        /// interrupted by the boarding/bite/final sequences those manage
        /// (both can legitimately be in flight on the same frame a packet
        /// arrives), and both CollectorAnimation methods already guard
        /// themselves independently against stacking/terminal state - see
        /// their own remarks.
        ///
        /// enableFoodArrivedBounce/enableFoodArrivedHighlight (bounce on,
        /// highlight off by default - see class remarks) gate whether this
        /// actually calls into CollectorAnimation at all: with highlight
        /// off, PlayFoodArrivedHighlight is never called for this arrival,
        /// so no coroutine starts and no MaterialPropertyBlock is ever
        /// touched - not merely played and then hidden. This method remains
        /// the single, permanent trigger point for both reactions; only
        /// which ones it currently fires is configurable.
        /// </summary>
        private void HandleFoodPacketArrived(FoodPacket packet)
        {
            if (_terminalStarted || packet.Collector != View)
                return;

            if (enableFoodArrivedBounce)
                Animation.PlayFoodArrivedBounce();

            if (enableFoodArrivedHighlight)
                Animation.PlayFoodArrivedHighlight();
        }

        private void StartSequence(IEnumerator routine)
        {
            if (_terminalStarted)
                return;

            StopActiveSequence();
            _activeSequence = StartCoroutine(routine);
        }

        private void StopActiveSequence()
        {
            if (_activeSequence == null)
                return;

            StopCoroutine(_activeSequence);
            _activeSequence = null;
        }

        private IEnumerator BoardingSequence()
        {
            // The turn itself happens inside PlayBoardingBounce — nothing
            // else to apply once it completes, unlike the old sprite swap
            // that had to happen as a separate step after the bounce.
            yield return WaitForCallback(Animation.PlayBoardingBounce);
        }

        private IEnumerator NormalBiteSequence()
        {
            // Already facing toward from boarding (or unchanged since the
            // previous bite) — only the punch reacts.
            yield return WaitForCallback(Animation.PlayEatingPunch);
        }

        private IEnumerator FinalBiteSequence()
        {
            // Hidden before the satisfied/heart reactions play, so the
            // player never sees the RemainingHunger label read "0".
            View.HideHungerText();

            // Foregrounded for the whole terminal sequence, not just the
            // Heart, so an ordinary rider that reaches this collector's
            // screen position at any point from here through the final
            // collapse can never visually merge with it — see
            // CollectorAnimation.EnterTerminalForeground.
            Animation.EnterTerminalForeground();

            yield return WaitForCallback(Animation.PlayEatingPunch);
            yield return WaitForCallback(Animation.PlaySatisfiedPunch);
            yield return WaitForCallback(Animation.PlayHeartPulseAndCollapse);

            VisualSequenceComplete?.Invoke();
        }

        private static IEnumerator WaitForCallback(Action<Action> play)
        {
            bool done = false;
            play(() => done = true);

            while (!done)
                yield return null;
        }
    }
}
