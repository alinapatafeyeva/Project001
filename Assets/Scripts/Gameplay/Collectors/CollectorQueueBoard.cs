using System;
using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Levels;
using Project001.Gameplay.Pixels;
using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Generates vertical queues of CollectorView objects on Initialize. Queue
    /// count, per-queue collector order, each collector's MatchTypeId,
    /// MatchId, and hunger capacity all come from the injected level
    /// collector-queue data. Appearance is resolved here only as far as
    /// resolving each collector's MatchId to a Character_XX prefab (via
    /// characterDatabase) and instantiating it as that collector's Visual,
    /// then showing its facing-away idle pose — CollectorPresentation and
    /// CollectorAnimation own everything about how that model is actually
    /// posed/animated. Queued collectors face away until selected;
    /// CollectorSelectionController switches a collector to facing toward
    /// the moment it boards the Conveyor (see
    /// CollectorSelectionController.TrySelect), so a collector never remains
    /// facing away once it leaves this board.
    /// </summary>
    public class CollectorQueueBoard : MonoBehaviour, ICollectorSource
    {
        [SerializeField, Tooltip("Resolves each collector's MatchId to the Character_XX prefab it should display.")]
        private CharacterDatabase characterDatabase;

        [SerializeField, Tooltip("Gap between neighbouring queues, in world units.")]
        [Min(0f)]
        private float horizontalSpacing = 0.3f;

        [SerializeField, Tooltip("Grid pixel consumption reads from. Optional — if missing, collectors still generate and consumption simply does nothing.")]
        private PixelGrid pixelGrid;

        [SerializeField, Tooltip("Conveyor system whose completed laps drive each collector's lifecycle resolution. Optional — if missing, lap resolution never triggers.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Waiting line an unsatisfied collector is placed into after a completed lap. Optional — if missing, an unsatisfied collector stays on the conveyor.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("Controller notified when an unsatisfied collector completes a lap and finds no free Waiting Line slot. Optional — if missing, that situation is simply never reported.")]
        private FailureController failureController;

        private CollectorQueue[] _queues;
        private bool _isInitialized;

        /// <summary>
        /// Builds this board's queues from the given level collector-queue
        /// data. Must be called exactly once, before any selection is
        /// attempted. A second call, or a first call on an object that
        /// already has stale baked children, is refused (logged) rather than
        /// generating duplicate queues. Existing children are never deleted.
        /// </summary>
        public void Initialize(IReadOnlyList<CollectorQueueDefinition> collectorQueues)
        {
            if (collectorQueues == null)
                throw new ArgumentNullException(nameof(collectorQueues));

            if (_isInitialized)
            {
                Debug.LogError($"CollectorQueueBoard: Initialize called more than once on '{name}'; ignoring to avoid duplicate queues.", this);
                return;
            }

            if (transform.childCount > 0)
            {
                Debug.LogError($"CollectorQueueBoard: '{name}' already has {transform.childCount} child object(s) before Initialize; aborting to avoid duplicate queues. Scene may contain stale baked children.", this);
                return;
            }

            GenerateBoard(collectorQueues);
            _isInitialized = true;
        }

        /// <summary>
        /// Resolves the Visual prefab for the given MatchId via
        /// characterDatabase. CharacterDatabase.GetPrefab never throws, so
        /// this never does either — if characterDatabase itself is
        /// unassigned, logs a clear error and returns null rather than
        /// throwing, so a collector simply has no Visual instead of failing
        /// to build. Beyond that, this board never has to reason about a
        /// missing prefab — that is entirely CharacterDatabase's
        /// responsibility.
        /// </summary>
        private GameObject ResolvePrefab(int matchId)
        {
            if (characterDatabase == null)
            {
                Debug.LogError($"CollectorQueueBoard: '{name}' has no CharacterDatabase assigned; collectors will have no Visual.", this);
                return null;
            }

            return characterDatabase.GetPrefab(matchId);
        }

        private void GenerateBoard(IReadOnlyList<CollectorQueueDefinition> collectorQueues)
        {
            _queues = new CollectorQueue[collectorQueues.Count];

            // CollectorVisibleWidth, not CollectorSpriteScale — the sprite
            // does not fill its full transform-scale square (see
            // GameplayLayout), so using the scale here would count that
            // square's own empty margin as part of the gap on both sides of
            // every neighbouring pair, making the real visible gap several
            // times larger than horizontalSpacing actually promises. Mirrors
            // QueueRowStep, which already derives from CollectorVisibleHeight
            // for exactly this reason, just vertically.
            float stepX = GameplayLayout.CollectorVisibleWidth + horizontalSpacing;
            float offsetX = (collectorQueues.Count - 1) * stepX * 0.5f;

            for (int queueIndex = 0; queueIndex < collectorQueues.Count; queueIndex++)
            {
                var queueObject = new GameObject($"Queue_{queueIndex}");
                queueObject.transform.SetParent(transform, false);
                queueObject.transform.localPosition = new Vector3(queueIndex * stepX - offsetX, 0f, 0f);

                var queue = new CollectorQueue();
                _queues[queueIndex] = queue;

                IReadOnlyList<CollectorDefinition> collectorDefinitions = collectorQueues[queueIndex].Collectors;

                for (int rowIndex = 0; rowIndex < collectorDefinitions.Count; rowIndex++)
                {
                    CollectorDefinition collectorDefinition = collectorDefinitions[rowIndex];

                    // ConveyorRider, CollectorPresentation, and
                    // CollectorAnimation are not listed explicitly:
                    // CollectorView's [RequireComponent] attributes add
                    // ConveyorRider and CollectorPresentation first, and
                    // CollectorPresentation's own [RequireComponent] then adds
                    // CollectorAnimation — all before CollectorView's own
                    // Awake runs. Listing any of them again here would add a
                    // second, duplicate instance instead of reusing the one
                    // already added.
                    var collectorObject = new GameObject(
                        $"Collector_{queueIndex}_{rowIndex}",
                        typeof(CollectorView),
                        typeof(PixelConsumer),
                        typeof(CollectorLifecycle));
                    collectorObject.transform.SetParent(queueObject.transform, false);
                    collectorObject.transform.localPosition = RowLocalPosition(rowIndex);
                    // Uniform scale, unlike the old (scale, scale, 1) used
                    // for a flat sprite: a 3D model needs its depth (Z)
                    // scaled along with width/height, or it renders stretched
                    // relative to its authored proportions.
                    collectorObject.transform.localScale = Vector3.one * GameplayLayout.CollectorSpriteScale;

                    GameObject visualPrefab = ResolvePrefab(collectorDefinition.MatchId);

                    var collectorView = collectorObject.GetComponent<CollectorView>();
                    collectorView.Initialize(visualPrefab);

                    var collectorPresentation = collectorObject.GetComponent<CollectorPresentation>();
                    collectorPresentation.ShowWaitingBackIdle();
                    collectorPresentation.SetQueueRowDepth(rowIndex);

                    var conveyorRider = collectorObject.GetComponent<ConveyorRider>();
                    conveyorRider.Initialize(collectorDefinition.MatchTypeId, collectorDefinition.HungerCapacity);

                    var pixelConsumer = collectorObject.GetComponent<PixelConsumer>();
                    pixelConsumer.Initialize(pixelGrid, conveyorRider);

                    var collectorLifecycle = collectorObject.GetComponent<CollectorLifecycle>();
                    collectorLifecycle.Initialize(conveyorRider, conveyorSystem, waitingLine, failureController);

                    queue.Add(collectorView);
                }
            }
        }

        /// <summary>
        /// Total collectors still queued on this board, across every queue.
        /// </summary>
        public int RemainingCollectorCount
        {
            get
            {
                if (_queues == null)
                    return 0;

                int count = 0;
                foreach (CollectorQueue queue in _queues)
                    count += queue.Views.Count;

                return count;
            }
        }

        /// <summary>
        /// True when the given view is currently the first available collector
        /// of one of the board's queues. Implicitly satisfies
        /// ICollectorSource.CanSelect.
        /// </summary>
        public bool CanSelect(CollectorView view)
        {
            if (view == null || _queues == null)
                return false;

            foreach (CollectorQueue queue in _queues)
            {
                if (queue.IsFirstAvailable(view))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Finalizes removal of a successfully selected collector: removes it
        /// from its logical queue and instantly shifts the remaining collectors
        /// upward to fill the gap. Rejects collectors that are not first in
        /// their queue. Does not touch ConveyorSystem.
        /// </summary>
        public bool TryRemoveSelected(CollectorView view)
        {
            if (view == null || _queues == null)
                return false;

            for (int queueIndex = 0; queueIndex < _queues.Length; queueIndex++)
            {
                CollectorQueue queue = _queues[queueIndex];
                if (!queue.TryRemoveFirstAvailable(view))
                    continue;

                ShiftQueueUp(queue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Explicit ICollectorSource.ReleaseCollector implementation — forwards
        /// to TryRemoveSelected without duplicating its logic. Kept explicit so
        /// the board's own public API keeps its existing, more descriptive name.
        /// </summary>
        bool ICollectorSource.ReleaseCollector(CollectorView collectorView) => TryRemoveSelected(collectorView);

        /// <summary>
        /// Re-derives the given view's current row index from whichever
        /// queue actually holds it and reapplies that row's genuine
        /// presentation depth — used only when CollectorSelectionController
        /// rolls back a boarding attempt (this board never actually
        /// released the view, so it is still exactly where
        /// TryRemoveSelected would have found it).
        /// </summary>
        void ICollectorSource.RestorePresentationDepth(CollectorView collectorView)
        {
            if (collectorView == null || _queues == null)
                return;

            foreach (CollectorQueue queue in _queues)
            {
                IReadOnlyList<CollectorView> views = queue.Views;
                for (int rowIndex = 0; rowIndex < views.Count; rowIndex++)
                {
                    if (views[rowIndex] != collectorView)
                        continue;

                    collectorView.Presentation.SetQueueRowDepth(rowIndex);
                    return;
                }
            }
        }

        private void ShiftQueueUp(CollectorQueue queue)
        {
            IReadOnlyList<CollectorView> views = queue.Views;

            for (int rowIndex = 0; rowIndex < views.Count; rowIndex++)
            {
                views[rowIndex].transform.localPosition = RowLocalPosition(rowIndex);
                views[rowIndex].Presentation.SetQueueRowDepth(rowIndex);
            }
        }

        /// <summary>
        /// Row rowIndex's local position within its queue. Every row stays on
        /// the single shared gameplay plane (Z=0) — the same plane every
        /// ConveyorRider, WaitingLine slot, and RecoveryRow slot lives on —
        /// so a collector's world/local Z never needs to change across its
        /// entire lifecycle (queue -> selected -> boarding -> riding).
        /// GameplayLayout.CollectorQueueBoardPositionY is this board's region
        /// TOP edge, not a center, and GameplayLayout.QueueUpwardPresentationOffset
        /// lifts every row uniformly closer to that edge (a pure presentation
        /// offset, not a change to the region boundary itself) so the queue
        /// reads a little higher on screen under the tilted camera — see both
        /// constants' own remarks for why depth separation is Y-only here
        /// (an earlier Z-per-row version broke Physics2D tap selection and
        /// boarding, since a collector no longer started clean at Z=0).
        /// A pure function of rowIndex alone, so ShiftQueueUp keeps working
        /// unmodified — reapplying this per remaining index never reflows a
        /// draining queue.
        /// </summary>
        private Vector3 RowLocalPosition(int rowIndex)
        {
            float y = -GameplayLayout.CollectorVisibleHeight * 0.5f
                - rowIndex * GameplayLayout.QueueRowStep
                + GameplayLayout.QueueUpwardPresentationOffset;
            return new Vector3(0f, y, 0f);
        }
    }
}
