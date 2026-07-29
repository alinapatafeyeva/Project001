using System;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Resolves a collector's fate: a satisfied rider is removed from the
    /// Conveyor immediately, without waiting for a lap — that removal,
    /// CollectorSatisfied, and this collector becoming non-interactive all
    /// happen synchronously, right here, and are never delayed for
    /// presentation. Only Destroy(gameObject) is deferred: the GameObject
    /// stays alive briefly afterwards as a presentation-only shell so
    /// CollectorPresentation can play its Satisfied/Heart completion
    /// sequence, and ResolveSatisfaction is what guarantees Destroy still
    /// happens even if that presentation is unavailable or fails to start
    /// (see its own comment). An unsatisfied rider is only moved into a free
    /// WaitingLine slot once it completes a full lap back to the conveyor's
    /// boarding point, or left riding — while notifying FailureController —
    /// if no slot is free. Holds no movement logic of its own — ConveyorSystem
    /// still owns positioning while riding.
    /// </summary>
    public class CollectorLifecycle : MonoBehaviour
    {
        /// <summary>
        /// Raised right after a satisfied collector is removed from the
        /// Conveyor — the only moment the game's total count of remaining
        /// (not-yet-satisfied) collectors can decrease. This fires at that
        /// exact logical-completion instant regardless of how long the
        /// collector's own Satisfied/Heart presentation sequence then takes
        /// to finish before Destroy(gameObject) actually happens — listeners
        /// must never assume the GameObject is already gone. Static because
        /// a CollectorLifecycle exists per-collector, created and destroyed
        /// with it, while a listener (e.g. EndgameCleanupController) needs a
        /// single subscription point regardless of how many collector
        /// instances currently exist.
        /// </summary>
        public static event Action CollectorSatisfied;

        private ConveyorRider _rider;
        private ConveyorSystem _conveyorSystem;
        private Project001.Gameplay.WaitingLine.WaitingLine _waitingLine;
        private FailureController _failureController;
        private CollectorView _collectorView;

        private CollectorView CollectorViewComponent =>
            _collectorView != null ? _collectorView : _collectorView = GetComponent<CollectorView>();

        /// <summary>
        /// Assigns the rider this lifecycle tracks, the conveyor it currently
        /// rides on, the waiting line an unsatisfied collector is placed
        /// into, and the controller notified when a completed lap finds no
        /// free waiting slot. Any may be null; missing references simply skip
        /// the corresponding step.
        /// </summary>
        public void Initialize(
            ConveyorRider rider,
            ConveyorSystem conveyorSystem,
            Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
            FailureController failureController)
        {
            _rider = rider;
            _conveyorSystem = conveyorSystem;
            _waitingLine = waitingLine;
            _failureController = failureController;
        }

        private void Update()
        {
            if (_rider == null || _conveyorSystem == null)
                return;

            if (!_rider.IsRiding)
                return;

            if (_rider.IsSatisfied)
            {
                // Satisfied collectors leave immediately — never wait for a
                // lap, and never go to WaitingLine.
                if (_conveyorSystem.TryRemoveRider(_rider))
                    ResolveSatisfaction();

                return;
            }

            if (!_conveyorSystem.TryConsumeCompletedLap(_rider))
                return;

            ResolveLap();
        }

        private void ResolveLap()
        {
            if (_waitingLine == null)
                return;

            var slot = _waitingLine.GetFirstEmptySlot();
            if (slot == null)
            {
                // Still hungry, lap complete, no slot free — report the
                // situation; FailureController decides whether that actually
                // constitutes failure (e.g. not once every pixel is already
                // consumed). The collector stays riding for the prototype: no
                // teleport, removal, or destruction here.
                _failureController?.NotifyWaitingLineFull();
                return;
            }

            CollectorView collectorView = CollectorViewComponent;
            if (collectorView == null)
                return;

            if (!slot.Assign(collectorView))
                return;

            if (!_conveyorSystem.TryRemoveRider(_rider))
            {
                slot.ClearIfOccupant(collectorView);
                return;
            }

            // No movement animation: reparent and snap straight to the
            // slot's position. The pose does change, though — an unsatisfied
            // collector leaving the Conveyor switches Front Eating → Back
            // Idle, matching every other waiting source.
            transform.SetParent(slot.transform, true);
            transform.position = slot.transform.position;
            collectorView.Presentation.ShowWaitingBackIdle();
        }

        /// <summary>
        /// Called immediately after a satisfied rider is removed from the
        /// Conveyor. Finishes every logical/gameplay-facing part of
        /// completion synchronously — disabling this collector's own
        /// Collider2D (so it can no longer be found or selected while it
        /// lingers as a presentation shell) and raising CollectorSatisfied —
        /// then hands off to CollectorPresentation for the Satisfied/Heart
        /// sequence, deferring only Destroy(gameObject) to that sequence's
        /// completion.
        ///
        /// Guarantees Destroy still happens even when presentation cannot:
        /// if CollectorPresentation is missing or disabled, or
        /// PlayFinalBiteSequence reports it could not start (e.g. no Visual
        /// child to animate — see CollectorAnimation.HasVisual), this
        /// destroys the GameObject immediately instead of leaving a shell
        /// that would never receive a completion event. No timeout is used:
        /// the fallback is decided entirely from these synchronous checks,
        /// not from how long presentation takes.
        /// </summary>
        private void ResolveSatisfaction()
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
                collider.enabled = false;

            CollectorSatisfied?.Invoke();

            CollectorPresentation presentation = GetComponent<CollectorPresentation>();
            if (presentation == null || !presentation.isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }

            presentation.VisualSequenceComplete += HandleVisualSequenceComplete;

            // Idempotent: if PixelConsumer's own final bite already started
            // this same sequence moments earlier, this call is a no-op that
            // still returns true — the two callers can never compete (see
            // CollectorPresentation.PlayFinalBiteSequence).
            if (!presentation.PlayFinalBiteSequence())
            {
                presentation.VisualSequenceComplete -= HandleVisualSequenceComplete;
                Destroy(gameObject);
            }
        }

        private void HandleVisualSequenceComplete()
        {
            Destroy(gameObject);
        }
    }
}
