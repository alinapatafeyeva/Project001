using System;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Feeding;
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
    ///
    /// A completed lap is never judged on a stale RemainingHunger: if this
    /// collector's own FoodFlightController still has reserved food in
    /// flight (queued or already launched, not yet arrived) the instant a
    /// lap completes, satisfaction is genuinely undecided until that food
    /// resolves — sampling IsSatisfied/RemainingHunger right then could
    /// send an about-to-be-satisfied collector to WaitingLine, where
    /// nothing ever re-checks satisfaction again (Update below only samples
    /// IsSatisfied while IsRiding is true), stranding it there forever even
    /// once fully fed. _awaitingFoodResolution is the explicit state that
    /// prevents this: it is set the instant a completed lap finds food
    /// still in flight, checked every frame instead of only once per lap,
    /// and cleared the moment that food resolves — at which point
    /// satisfaction is evaluated exactly once, without waiting for another
    /// full lap. This is deliberately a flag checked every Update, not a
    /// polling coroutine/loop: it costs the same one-comparison read every
    /// other branch here already pays per frame.
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
        private FoodFlightController _foodFlightController;

        // True from the instant a completed lap finds this rider's own food
        // still in flight until that food resolves — see this class's own
        // remarks above. Never set while satisfied (satisfaction always
        // takes priority — see Update) and never carried across riders.
        private bool _awaitingFoodResolution;

        private CollectorView CollectorViewComponent =>
            _collectorView != null ? _collectorView : _collectorView = GetComponent<CollectorView>();

        private FoodFlightController FoodFlightControllerComponent =>
            _foodFlightController != null ? _foodFlightController : _foodFlightController = GetComponent<FoodFlightController>();

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
                // lap, and never go to WaitingLine. Checked first, every
                // frame, even while _awaitingFoodResolution is true: food
                // that arrives mid-wait and completes satisfaction should
                // not sit blocked behind HasPendingFood from *other* still
                // in-flight packets for the same collector (those clamp
                // harmlessly to zero RemainingHunger once they land — see
                // ConveyorRider.RegisterConsumedPixel).
                _awaitingFoodResolution = false;

                if (_conveyorSystem.TryRemoveRider(_rider))
                    ResolveSatisfaction();

                return;
            }

            if (_awaitingFoodResolution)
            {
                if (HasFoodInFlight())
                    return;

                _awaitingFoodResolution = false;
                ResolveLap();
                return;
            }

            if (!_conveyorSystem.TryConsumeCompletedLap(_rider))
                return;

            if (HasFoodInFlight())
            {
                // Lap credit is already consumed above — deliberately not
                // given back. Re-arming ConveyorSystem's own lap counter has
                // no bearing on when this waits end: that end is driven
                // entirely by HasFoodInFlight going false, next checked on
                // the very next Update, not by completing another lap.
                _awaitingFoodResolution = true;
                return;
            }

            ResolveLap();
        }

        private bool HasFoodInFlight()
        {
            FoodFlightController controller = FoodFlightControllerComponent;
            return controller != null && controller.HasPendingFood;
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
            collectorView.Presentation.ClearPresentationDepth();

            // Grounds this collector on the slot the same generic,
            // per-species way a rider is grounded on the Conveyor belt (see
            // CollectorView.SetGroundedPosition) — this collector's ROOT
            // stays exactly on slot.transform.position (set just above,
            // gameplay's own source of truth for where it "is"), only the
            // presentation-only offset moves so its FEET land on the slot
            // instead of its body center.
            collectorView.SetGroundedPosition(slot.transform.position);
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
