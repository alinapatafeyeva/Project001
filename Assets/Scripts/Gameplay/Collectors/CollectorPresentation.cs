using System;
using System.Collections;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Decides which of this collector's sprites is currently shown, and
    /// asks CollectorView to apply it — holds no rendering logic itself,
    /// CollectorView owns the SpriteRenderer (on its Visual child) and is
    /// the only thing that ever touches it directly. Also the sole caller
    /// into CollectorAnimation, so CollectorAnimation never has to decide
    /// which reaction fits which gameplay moment — this class is that
    /// single authority.
    ///
    /// The five sprites come from whichever MonsterSkin CollectorQueueBoard
    /// resolved for this collector's MonsterColor (see
    /// CollectorQueueBoard.GenerateBoard) — this component only ever holds
    /// and switches between them, it never looks up a skin itself. Front
    /// Idle is held but never displayed by the current lifecycle below — it
    /// is kept only for possible future use.
    ///
    /// Approved visible lifecycle: Back Idle while waiting anywhere
    /// (CollectorQueueBoard, WaitingLine, Recovery Row) with idle breathing;
    /// Front Eating for the entire time a collector actively rides the
    /// Conveyor, including every normal bite reaction; and, on the final
    /// bite only, Front Eating → Front Satisfied → Heart → collapse. A
    /// collector that leaves the Conveyor unsatisfied and lands back in
    /// WaitingLine or Recovery Row switches back to Back Idle — see
    /// ShowWaitingBackIdle, called from CollectorLifecycle.ResolveLap and
    /// RecoveryRowController.ReceiveCollectors — and the same restoration
    /// happens if a boarding attempt is rolled back (see
    /// CollectorSelectionController.TrySelect).
    ///
    /// Sequencing rule (see CollectorAnimation for the transform-level half
    /// of this): at most one sequence coroutine is active on this component
    /// at a time — starting a new one always stops whatever sequence is
    /// currently running first. Once PlayFinalBiteSequence starts, every
    /// other public method here becomes a no-op: Satisfied/Heart is
    /// terminal, matching CollectorAnimation's own terminal Play* guard.
    ///
    /// PixelConsumer is the only caller of PlayNormalBiteReaction/
    /// PlayFinalBiteSequence, and calls exactly one of the two per
    /// successfully consumed pixel, never both — see PixelConsumer.
    /// CollectorLifecycle also calls PlayFinalBiteSequence, purely as a
    /// safety net for a collector reaching RemainingHunger zero without
    /// PixelConsumer ever having triggered it (e.g. zero hunger capacity);
    /// the call is idempotent, so whichever caller reaches it first is the
    /// one that actually starts the sequence — the two can never compete.
    /// </summary>
    [RequireComponent(typeof(CollectorAnimation))]
    public class CollectorPresentation : MonoBehaviour
    {
        private Sprite _backSprite;

        // Stored but never displayed by the current lifecycle — Front Idle
        // is not part of the approved gameplay presentation, kept only so
        // it can be reintroduced without a signature change.
        private Sprite _frontSprite;

        private Sprite _frontEatingSprite;
        private Sprite _frontSatisfiedSprite;
        private Sprite _heartSprite;

        private CollectorView _view;
        private CollectorAnimation _animation;
        private Coroutine _activeSequence;
        private bool _terminalStarted;

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
        /// Assigns the sprites this collector may display. Collectors are
        /// built at runtime (CollectorQueueBoard.GenerateBoard), never from a
        /// prefab, so there is no Inspector session for this component to
        /// deserialize values into directly — the real, Inspector-assigned
        /// sprites live on the MonsterSkin CollectorQueueBoard resolved for
        /// this collector, and are injected here exactly once, the same way
        /// ConveyorRider, PixelConsumer, and CollectorLifecycle are each
        /// initialized.
        /// </summary>
        public void Initialize(Sprite backSprite, Sprite frontSprite, Sprite frontEatingSprite, Sprite frontSatisfiedSprite, Sprite heartSprite)
        {
            _backSprite = backSprite;
            _frontSprite = frontSprite;
            _frontEatingSprite = frontEatingSprite;
            _frontSatisfiedSprite = frontSatisfiedSprite;
            _heartSprite = heartSprite;
        }

        /// <summary>
        /// Shows the back-idle pose plus its subtle idle breathing. This is
        /// the pose for every waiting location — called by
        /// CollectorQueueBoard right after Initialize, by
        /// CollectorLifecycle.ResolveLap when an unsatisfied collector lands
        /// in a WaitingLine slot, by RecoveryRowController.ReceiveCollectors
        /// when a collector is transferred into the Recovery Row, and by
        /// CollectorSelectionController when a failed boarding attempt is
        /// rolled back to its original source.
        /// </summary>
        public void ShowWaitingBackIdle()
        {
            if (_terminalStarted)
                return;

            View.ApplySprite(_backSprite);
            StopActiveSequence();
            Animation.PlayIdleBreathing();
        }

        /// <summary>
        /// Plays the boarding reaction — a short squash/bounce while still
        /// showing Back Idle, reading as the Mofu turning around without any
        /// literal rotation — then switches to Front Eating, where it
        /// remains for the rest of this collector's time actively riding the
        /// Conveyor. Called by CollectorSelectionController the moment this
        /// collector boards, regardless of which source (CollectorQueueBoard,
        /// WaitingLine, or Recovery Row) it boarded from. Never leaves Front
        /// Eating for Front Idle afterwards.
        /// </summary>
        public void ShowConveyorFrontEating()
        {
            if (_terminalStarted)
                return;

            StartSequence(BoardingSequence());
        }

        /// <summary>
        /// Plays a small eating punch and stays on Front Eating — the mouth
        /// remains open for the entire time this collector actively rides
        /// the Conveyor. Called by PixelConsumer after a successful,
        /// non-final consume.
        /// </summary>
        public void PlayNormalBiteReaction()
        {
            if (_terminalStarted)
                return;

            StartSequence(NormalBiteSequence());
        }

        /// <summary>
        /// Plays the full completion sequence — Front Eating + punch, Front
        /// Satisfied + happy punch, Heart + one pulse, then a collapse —
        /// firing VisualSequenceComplete once it finishes. Terminal: once
        /// started, every other method on this component becomes a no-op.
        /// Idempotent — a second call (from either PixelConsumer or
        /// CollectorLifecycle, whichever reaches it first) is a no-op that
        /// still returns true, since the sequence is already in flight.
        /// Returns false only if the sequence cannot start at all (e.g. no
        /// Visual child to animate) — CollectorLifecycle treats false as its
        /// cue to destroy the collector immediately instead of waiting for a
        /// completion event that would never arrive.
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
            // Stays on Back Idle through the bounce itself — the pose only
            // switches once the bounce completes, which is what reads as the
            // Mofu turning around to face the Conveyor.
            yield return WaitForCallback(Animation.PlayBoardingBounce);
            View.ApplySprite(_frontEatingSprite);
        }

        private IEnumerator NormalBiteSequence()
        {
            // Sprite was already Front Eating from boarding (or the previous
            // bite) and stays Front Eating throughout — only the punch reacts.
            View.ApplySprite(_frontEatingSprite);
            yield return WaitForCallback(Animation.PlayEatingPunch);
        }

        private IEnumerator FinalBiteSequence()
        {
            // Hidden before any of Front Satisfied/Heart shows, so the
            // player never sees the RemainingHunger label read "0".
            View.HideHungerText();

            View.ApplySprite(_frontEatingSprite);
            yield return WaitForCallback(Animation.PlayEatingPunch);

            View.ApplySprite(_frontSatisfiedSprite);
            yield return WaitForCallback(Animation.PlaySatisfiedPunch);

            View.ApplySprite(_heartSprite);
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
