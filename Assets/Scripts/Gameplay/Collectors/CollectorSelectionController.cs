using Project001.Gameplay.Conveyor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Detects pointer selection (mouse click or touch tap, via the shared
    /// Pointer device) of an eligible CollectorView and moves it from its
    /// source — a queue's front position or an occupied waiting slot — onto
    /// the ConveyorSystem.
    /// </summary>
    public class CollectorSelectionController : MonoBehaviour
    {
        [SerializeField, Tooltip("Camera used to convert pointer screen position to world position.")]
        private Camera selectionCamera;

        [SerializeField, Tooltip("Board holding the main collector queues.")]
        private CollectorQueueBoard collectorQueueBoard;

        [SerializeField, Tooltip("Waiting line collectors may also be selected from.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("System the selected collector will board.")]
        private ConveyorSystem conveyorSystem;

        private void Update()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null || !pointer.press.wasPressedThisFrame)
                return;

            TrySelect(pointer.position.ReadValue());
        }

        private void TrySelect(Vector2 screenPosition)
        {
            if (selectionCamera == null || conveyorSystem == null)
                return;

            CollectorView view = FindCollectorAt(screenPosition);
            if (view == null)
                return;

            bool fromQueue = collectorQueueBoard != null && collectorQueueBoard.CanSelect(view);
            bool fromWaitingLine = !fromQueue && waitingLine != null && waitingLine.Contains(view);

            if (!fromQueue && !fromWaitingLine)
                return;

            var rider = view.GetComponent<ConveyorRider>();
            if (rider == null)
                return;

            if (!conveyorSystem.TryAddRider(rider))
                return;

            // ConveyorSystem now owns the rider's position; only finalize the source side.
            if (fromQueue)
                collectorQueueBoard.TryRemoveSelected(view);
            else
                waitingLine.ClearSlotContaining(view);
        }

        private CollectorView FindCollectorAt(Vector2 screenPosition)
        {
            Vector3 screenPoint = new Vector3(screenPosition.x, screenPosition.y, -selectionCamera.transform.position.z);
            Vector3 worldPoint = selectionCamera.ScreenToWorldPoint(screenPoint);

            Collider2D collider = Physics2D.OverlapPoint(worldPoint);
            return collider != null ? collider.GetComponent<CollectorView>() : null;
        }
    }
}
