using Project001.Gameplay.Conveyor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Detects pointer selection (mouse click or touch tap, via the shared
    /// Pointer device) of an eligible CollectorView and moves it from its
    /// source — a queue's front position, an occupied waiting slot, or a
    /// held Recovery Row collector — onto the ConveyorSystem.
    /// </summary>
    public class CollectorSelectionController : MonoBehaviour
    {
        [SerializeField, Tooltip("Camera used to convert pointer screen position to world position.")]
        private Camera selectionCamera;

        [SerializeField, Tooltip("Board holding the main collector queues.")]
        private CollectorQueueBoard collectorQueueBoard;

        [SerializeField, Tooltip("Waiting line collectors may also be selected from.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("Recovery Row collectors may also be selected from.")]
        private Project001.Gameplay.Recovery.RecoveryRowController recoveryRowController;

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

            ICollectorSource source = FindSource(view);
            if (source == null)
                return;

            var rider = view.GetComponent<ConveyorRider>();
            if (rider == null)
                return;

            // Captured before boarding so a failed release can be rolled back
            // to exactly where the collector visually was.
            Transform collectorTransform = view.transform;
            Transform originalParent = collectorTransform.parent;
            Vector3 originalLocalPosition = collectorTransform.localPosition;

            if (!conveyorSystem.TryAddRider(rider))
                return;

            // Approved presentation rule: Conveyor and every later gameplay
            // location show front-idle. Applied here, the moment the
            // collector boards — this is what switches a collector leaving
            // CollectorQueueBoard from back-idle to front-idle; idempotent
            // for a collector arriving from Waiting Line or Recovery Row,
            // which are already front-facing.
            view.Presentation.ShowGameplayFront();

            // ConveyorSystem now owns the rider's position; only release the
            // source side. If release fails, the source still logically holds
            // the collector while it is now also riding the conveyor — that
            // double state is not allowed, so boarding is rolled back instead.
            if (source.ReleaseCollector(view))
                return;

            bool rolledBack = conveyorSystem.TryRemoveRider(rider);
            if (rolledBack)
            {
                collectorTransform.SetParent(originalParent, true);
                collectorTransform.localPosition = originalLocalPosition;
            }

            Debug.LogError(
                $"CollectorSelectionController: '{view.name}' boarded the conveyor but its source failed to release it. "
                    + $"Conveyor rollback {(rolledBack ? "succeeded" : "FAILED — collector may be lost or duplicated")}.",
                view);
        }

        /// <summary>
        /// Finds whichever configured ICollectorSource currently considers the
        /// given view selectable — the queue board's first-available
        /// collector, an occupied waiting slot, or a collector held in the
        /// Recovery Row — without any source needing removal logic specific
        /// to the others. The interface-typed locals below are only ever
        /// truly null when a serialized field was left unassigned, never a
        /// destroyed Unity Object, so the plain null check is safe here.
        /// </summary>
        private ICollectorSource FindSource(CollectorView view)
        {
            ICollectorSource queueSource = collectorQueueBoard;
            if (queueSource != null && queueSource.CanSelect(view))
                return queueSource;

            ICollectorSource waitingLineSource = waitingLine;
            if (waitingLineSource != null && waitingLineSource.CanSelect(view))
                return waitingLineSource;

            ICollectorSource recoveryRowSource = recoveryRowController;
            if (recoveryRowSource != null && recoveryRowSource.CanSelect(view))
                return recoveryRowSource;

            return null;
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
