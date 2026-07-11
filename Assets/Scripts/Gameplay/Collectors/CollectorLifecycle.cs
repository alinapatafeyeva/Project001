using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Resolves a collector's fate: a satisfied rider is removed and destroyed
    /// immediately, without waiting for a lap. An unsatisfied rider is only
    /// moved into a free WaitingLine slot once it completes a full lap back to
    /// the conveyor's boarding point, or left riding if no slot is free. Holds
    /// no movement logic of its own — ConveyorSystem still owns positioning
    /// while riding.
    /// </summary>
    public class CollectorLifecycle : MonoBehaviour
    {
        private ConveyorRider _rider;
        private ConveyorSystem _conveyorSystem;
        private Project001.Gameplay.WaitingLine.WaitingLine _waitingLine;
        private CollectorView _collectorView;

        private CollectorView CollectorViewComponent =>
            _collectorView != null ? _collectorView : _collectorView = GetComponent<CollectorView>();

        /// <summary>
        /// Assigns the rider this lifecycle tracks, the conveyor it currently
        /// rides on, and the waiting line an unsatisfied collector is placed
        /// into. Any may be null; missing references simply skip resolution.
        /// </summary>
        public void Initialize(
            ConveyorRider rider,
            ConveyorSystem conveyorSystem,
            Project001.Gameplay.WaitingLine.WaitingLine waitingLine)
        {
            _rider = rider;
            _conveyorSystem = conveyorSystem;
            _waitingLine = waitingLine;
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
                    Destroy(gameObject);

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
                return;

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
