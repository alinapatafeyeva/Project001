using System.Collections.Generic;
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
    ///
    /// A tap is accepted the instant it lands on an eligible collector, even
    /// if the Conveyor's boarding point is not clear yet (see ConveyorSystem.
    /// boardingClearance/IsBoardingAreaClear) — rejecting it outright would
    /// mean rapid taps mostly get silently dropped. Instead the tap enqueues
    /// a pending boarding (_pendingBoardings) that Update() retries every
    /// frame until the conveyor actually accepts it, released strictly in
    /// selection order (only ever the front of the queue is attempted) so a
    /// later selection can never board ahead of, or overlap, an earlier one
    /// still waiting. Nothing about the collector's position or source
    /// changes while pending — it stays exactly where it visually was until
    /// the moment it actually boards, which is what "may wait briefly before
    /// entering" means here.
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

        private sealed class PendingBoarding
        {
            public CollectorView View;
            public ICollectorSource Source;
            public ConveyorRider Rider;
            public Transform OriginalParent;
            public Vector3 OriginalLocalPosition;
        }

        // FIFO by construction (Queue<T> + "only ever peek/dequeue the
        // front"): this is what guarantees selection order is preserved
        // even though boarding itself is delayed and retried.
        private readonly Queue<PendingBoarding> _pendingBoardings = new Queue<PendingBoarding>();

        // Guards against the same still-pending collector being enqueued a
        // second time — its source has not released it yet, so it can still
        // be found and re-tapped while waiting.
        private readonly HashSet<CollectorView> _pendingViews = new HashSet<CollectorView>();

        private void Update()
        {
            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
                TrySelect(pointer.position.ReadValue());

            ProcessPendingBoardings();
        }

        private void TrySelect(Vector2 screenPosition)
        {
            if (selectionCamera == null)
                return;

            CollectorView view = FindCollectorAt(screenPosition);
            if (view != null)
                Select(view);
        }

        /// <summary>
        /// The screen-position-independent half of selection: enqueues view
        /// as a pending boarding exactly as a real tap would, once
        /// TrySelect/FindCollectorAt has already resolved which collector
        /// was hit. Public so verification tooling can exercise this same
        /// production code path (rapid, ordered, non-overlapping boarding)
        /// directly, without needing to simulate real pointer input.
        /// </summary>
        public void Select(CollectorView view)
        {
            if (view == null || conveyorSystem == null)
                return;

            if (_pendingViews.Contains(view))
                return;

            ICollectorSource source = FindSource(view);
            if (source == null)
                return;

            var rider = view.GetComponent<ConveyorRider>();
            if (rider == null)
                return;

            Transform collectorTransform = view.transform;

            _pendingViews.Add(view);
            _pendingBoardings.Enqueue(new PendingBoarding
            {
                View = view,
                Source = source,
                Rider = rider,
                // Captured now, before boarding, so a failed release can
                // still be rolled back to exactly where the collector
                // visually was.
                OriginalParent = collectorTransform.parent,
                OriginalLocalPosition = collectorTransform.localPosition,
            });
        }

        /// <summary>
        /// Attempts to board only the front of _pendingBoardings, once per
        /// frame — never the collector behind it, even if the conveyor
        /// happens to be clear enough for a smaller/differently-timed rider:
        /// order is selection order, full stop. Leaves the queue and every
        /// entry in it untouched on failure, so this is a plain retry next
        /// frame, not a rejection.
        /// </summary>
        private void ProcessPendingBoardings()
        {
            if (_pendingBoardings.Count == 0)
                return;

            PendingBoarding next = _pendingBoardings.Peek();

            if (next.View == null || next.Rider == null)
            {
                _pendingBoardings.Dequeue();
                if (next.View != null)
                    _pendingViews.Remove(next.View);
                return;
            }

            if (!conveyorSystem.TryAddRider(next.Rider))
                return;

            _pendingBoardings.Dequeue();
            _pendingViews.Remove(next.View);

            // Approved presentation rule: boarding plays Back Idle → bounce →
            // Front Eating, and the collector stays Front Eating for the rest
            // of its time actively riding. Applied here, the moment the
            // collector actually boards — every current waiting source
            // (CollectorQueueBoard, Waiting Line, Recovery Row) shows Back
            // Idle beforehand, so this is always a genuine Back Idle → Front
            // Eating transition, never a no-op repeat.
            next.View.Presentation.ShowConveyorFrontEating();

            // ConveyorSystem now owns the rider's position; only release the
            // source side. If release fails, the source still logically holds
            // the collector while it is now also riding the conveyor — that
            // double state is not allowed, so boarding is rolled back instead.
            if (next.Source.ReleaseCollector(next.View))
                return;

            bool rolledBack = conveyorSystem.TryRemoveRider(next.Rider);
            if (rolledBack)
            {
                next.View.transform.SetParent(next.OriginalParent, true);
                next.View.transform.localPosition = next.OriginalLocalPosition;

                // All waiting sources show Back Idle, so a collector rolled
                // back to one must be restored to it too.
                next.View.Presentation.ShowWaitingBackIdle();
            }

            Debug.LogError(
                $"CollectorSelectionController: '{next.View.name}' boarded the conveyor but its source failed to release it. "
                    + $"Conveyor rollback {(rolledBack ? "succeeded" : "FAILED — collector may be lost or duplicated")}.",
                next.View);
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
