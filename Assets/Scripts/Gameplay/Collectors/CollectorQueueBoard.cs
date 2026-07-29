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
    /// MonsterColor, and hunger capacity all come from the injected level
    /// collector-queue data. Appearance is resolved here only as far as
    /// picking each collector's MonsterSkin (via monsterSkinDatabase) and
    /// showing its back-idle pose — CollectorPresentation and CollectorView
    /// own everything about how that sprite is actually displayed. Queued
    /// collectors show Back Idle until selected; CollectorSelectionController
    /// switches a collector to Front Eating the moment it boards the Conveyor
    /// (see CollectorSelectionController.TrySelect), so a collector never
    /// remains back-facing once it leaves this board.
    /// </summary>
    public class CollectorQueueBoard : MonoBehaviour, ICollectorSource
    {
        [SerializeField, Tooltip("Resolves each collector's MonsterColor to the MonsterSkin (sprite set) it should display.")]
        private MonsterSkinDatabase monsterSkinDatabase;

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
        private float _rowStepY;
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
        /// Resolves the MonsterSkin for the given MonsterColor via
        /// monsterSkinDatabase. MonsterSkinDatabase.GetSkin never returns
        /// null, so this never does either — if monsterSkinDatabase itself
        /// is unassigned, logs a clear error and returns an empty MonsterSkin
        /// (every sprite null) rather than throwing, so a collector simply
        /// shows no sprite instead of failing to build. Beyond that, this
        /// board never has to reason about a missing skin — that is entirely
        /// MonsterSkinDatabase's responsibility.
        /// </summary>
        private MonsterSkin ResolveSkin(MonsterColor monsterColor)
        {
            if (monsterSkinDatabase == null)
            {
                Debug.LogError($"CollectorQueueBoard: '{name}' has no MonsterSkinDatabase assigned; collectors will show no sprite.", this);
                return new MonsterSkin();
            }

            return monsterSkinDatabase.GetSkin(monsterColor);
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
            _rowStepY = GameplayLayout.QueueRowStep;
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
                    collectorObject.transform.localScale = new Vector3(GameplayLayout.CollectorSpriteScale, GameplayLayout.CollectorSpriteScale, 1f);

                    MonsterSkin skin = ResolveSkin(collectorDefinition.MonsterColor);

                    var collectorPresentation = collectorObject.GetComponent<CollectorPresentation>();
                    collectorPresentation.Initialize(skin.BackIdle, skin.FrontIdle, skin.FrontEating, skin.FrontSatisfied, skin.Heart);
                    collectorPresentation.ShowWaitingBackIdle();

                    var collectorView = collectorObject.GetComponent<CollectorView>();

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

        private void ShiftQueueUp(CollectorQueue queue)
        {
            IReadOnlyList<CollectorView> views = queue.Views;

            for (int rowIndex = 0; rowIndex < views.Count; rowIndex++)
                views[rowIndex].transform.localPosition = RowLocalPosition(rowIndex);
        }

        /// <summary>
        /// Row rowIndex's local position within its queue. GameplayLayout.
        /// CollectorQueueBoardPositionY is this board's region TOP edge, not
        /// a center, so row 0's own center sits half a CollectorVisibleHeight
        /// below local y = 0 — using the sprite's actual visible height, not
        /// CollectorSpriteScale, so row 0's rendered sprite (not an imaginary
        /// scale-square) has its true visible top edge at the region's top
        /// edge, rather than intruding into whatever sits directly above
        /// this board.
        /// </summary>
        private Vector3 RowLocalPosition(int rowIndex)
        {
            float y = -GameplayLayout.CollectorVisibleHeight * 0.5f - rowIndex * _rowStepY;
            return new Vector3(0f, y, 0f);
        }
    }
}
