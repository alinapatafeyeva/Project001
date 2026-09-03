using System.Collections;
using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Recovery;
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
    /// still waiting. For Queue/WaitingLine sources, nothing about the
    /// collector's position changes while pending — it stays exactly where
    /// it visually was until the moment it actually boards, which is what
    /// "may wait briefly before entering" means here. A Recovery Row source
    /// is the one exception: PendingBoarding.IsReadyToBoard stays false
    /// while AnimateRecoveryRowDeparture eases that collector's own
    /// position across to the Conveyor's boarding point first — see its own
    /// remarks — so ProcessPendingBoardings only ever attempts TryAddRider
    /// once that travel has actually finished.
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

            // True the instant this entry is enqueued for every source
            // except Recovery Row, whose own departure travel (see
            // AnimateRecoveryRowDeparture) sets this only once the
            // collector has visually finished arriving at the Conveyor's
            // boarding point. ProcessPendingBoardings never attempts
            // TryAddRider for an entry while this is false — since only the
            // front of the queue is ever attempted, this blocks (never
            // skips) whatever is behind it too, preserving the same
            // strict selection-order guarantee documented on
            // _pendingBoardings itself.
            public bool IsReadyToBoard = true;
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

            var pendingBoarding = new PendingBoarding
            {
                View = view,
                Source = source,
                Rider = rider,
                // Captured now, before boarding, so a failed release can
                // still be rolled back to exactly where the collector
                // visually was.
                OriginalParent = collectorTransform.parent,
                OriginalLocalPosition = collectorTransform.localPosition,
            };

            _pendingViews.Add(view);
            _pendingBoardings.Enqueue(pendingBoarding);

            // Recovery Row is the one source whose collectors sit visibly
            // far enough from the Conveyor's boarding point that boarding
            // instantly (TryAddRider's own plain position snap — see its
            // own remarks) would read as a teleport. Queue/WaitingLine keep
            // their existing instant-boarding behavior untouched — their
            // own boarding point already sits close enough that the snap is
            // not visually perceptible, and STRICT SCOPE for this task is
            // Recovery Row's own movement only.
            if (source is RecoveryRowController recoveryRowSource)
            {
                pendingBoarding.IsReadyToBoard = false;
                recoveryRowSource.SetReservedForDeparture(rider, true);
                StartCoroutine(AnimateRecoveryRowDeparture(pendingBoarding, collectorTransform.position));
            }
        }

        /// <summary>
        /// The visual half of a Recovery Row departure: eases view's own
        /// root Transform.position from its current (Recovery Row) position
        /// to ConveyorSystem.BoardingWorldPosition — the exact point
        /// TryAddRider will place it at once boarding actually happens —
        /// then marks pendingBoarding ready. Because the collector is not
        /// yet reparented onto the Conveyor and RecoveryRowView excludes a
        /// reserved collector from its own layout (see
        /// RecoveryRowController.SetReservedForDeparture), nothing else
        /// writes this transform's position while this coroutine runs, so
        /// there is nothing to fight over. The moment ProcessPendingBoardings
        /// later calls TryAddRider, it reparents onto the Conveyor with
        /// worldPositionStays:true and sets this exact same boarding
        /// position — a continuation, not a jump, since this coroutine
        /// already moved it there. Guards every step against the view
        /// having been destroyed mid-travel rather than assuming it is
        /// still valid after a yield.
        /// </summary>
        private IEnumerator AnimateRecoveryRowDeparture(PendingBoarding pendingBoarding, Vector3 startWorldPosition)
        {
            if (conveyorSystem == null)
            {
                pendingBoarding.IsReadyToBoard = true;
                yield break;
            }

            Vector3 targetWorldPosition = conveyorSystem.BoardingWorldPosition;

            yield return CollectorPositionTravel.Travel(
                startWorldPosition,
                targetWorldPosition,
                CollectorPositionTravel.DefaultDuration,
                position =>
                {
                    if (pendingBoarding.View != null)
                        pendingBoarding.View.transform.position = position;
                });

            pendingBoarding.IsReadyToBoard = true;
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

            if (!next.IsReadyToBoard)
                return;

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

            // Same moment: clear whatever queue-row/waiting-source
            // presentation depth this collector previously had, so no stale
            // depth survives onto the Conveyor (a rider that boarded from a
            // deep queue row must not keep rendering pulled toward the
            // camera once it's an ordinary Conveyor rider).
            next.View.Presentation.ClearPresentationDepth();

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
                // back to one must be restored to it too — and its
                // presentation depth along with it, so it doesn't stay
                // stuck at the Conveyor's neutral depth
                // ClearPresentationDepth just gave it above.
                next.View.Presentation.ShowWaitingBackIdle();
                next.Source.RestorePresentationDepth(next.View);
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

        /// <summary>
        /// Reverse-projects a screen tap onto the world Z=0 plane — the
        /// single shared gameplay plane every collector lives on regardless
        /// of state (queued, in WaitingLine, in RecoveryRow, or riding the
        /// Conveyor — see CollectorQueueBoard.RowLocalPosition's own
        /// remarks), then finds whichever 2D collider occupies that X/Y
        /// (Physics2D compares only X/Y, never Z). There is exactly one
        /// target plane here, never a per-row or per-state plane — an
        /// earlier version placed queue rows at different Z depths, which
        /// made this reverse-projection ambiguous (a tap resolved against
        /// the wrong row) and was removed for exactly that reason.
        ///
        /// This still can't be the simple "screenPoint.z = -camera.position.z"
        /// calculation a non-tilted camera would allow, purely because the
        /// camera itself is tilted (GameplayLayout.CameraTiltDegrees), not
        /// because of anything about the queue: under that downward tilt
        /// the camera's own up axis has a nonzero Z component, so "distance
        /// along forward to reach Z=0" is not a single constant independent
        /// of screen position the way it is at zero tilt — it depends on the
        /// tap's screen Y too (a tap higher on screen carries more of that
        /// up-axis Z with it). Rather than reasoning about orthographicSize/
        /// aspect to solve for that directly, this asks ScreenToWorldPoint
        /// for any one point on the tap's ray (at the near clip plane), then
        /// walks the remaining, purely orthographic (parallel-rays) distance
        /// along the camera's forward direction to reach Z=0. At zero tilt
        /// (forward == (0,0,1)) this reduces to exactly the straight-on
        /// calculation it replaced: the walk only changes Z, never X/Y.
        /// </summary>
        private CollectorView FindCollectorAt(Vector2 screenPosition)
        {
            Transform cameraTransform = selectionCamera.transform;
            Vector3 referencePoint = selectionCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, selectionCamera.nearClipPlane));

            Vector3 forward = cameraTransform.forward;
            float distanceToZeroPlane = -referencePoint.z / forward.z;
            Vector3 worldPoint = referencePoint + forward * distanceToZeroPlane;

            Collider2D collider = Physics2D.OverlapPoint(worldPoint);
            return collider != null ? collider.GetComponent<CollectorView>() : null;
        }
    }
}
