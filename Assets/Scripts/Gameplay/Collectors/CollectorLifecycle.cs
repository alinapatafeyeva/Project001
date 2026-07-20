using System;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Resolves a collector's fate: a satisfied rider is removed and destroyed
    /// immediately, without waiting for a lap. An unsatisfied rider is only
    /// moved into a free WaitingLine slot once it completes a full lap back to
    /// the conveyor's boarding point, or left riding — while notifying
    /// FailureController — if no slot is free. Holds no movement logic of its
    /// own — ConveyorSystem still owns positioning while riding.
    /// </summary>
    public class CollectorLifecycle : MonoBehaviour
    {
        /// <summary>
        /// Raised right before a satisfied collector is removed from the
        /// Conveyor and destroyed — the only moment the game's total count
        /// of remaining (not-yet-satisfied) collectors can decrease. Static
        /// because a CollectorLifecycle exists per-collector, created and
        /// destroyed with it, while a listener (e.g. EndgameCleanupController)
        /// needs a single subscription point regardless of how many
        /// collector instances currently exist.
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
                {
                    CollectorSatisfied?.Invoke();
                    Destroy(gameObject);
                }

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

            // No animation: reparent and snap straight to the slot's position.
            transform.SetParent(slot.transform, true);
            transform.position = slot.transform.position;
        }
    }
}
