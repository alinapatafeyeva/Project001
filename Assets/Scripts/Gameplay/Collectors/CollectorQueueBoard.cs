using System;
using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Levels;
using Project001.Gameplay.Pixels;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Generates vertical queues of CollectorView objects on Initialize, using a
    /// single shared runtime-generated circular sprite. Queue count, per-queue
    /// collector order, each collector's MatchTypeId, and hunger capacity all
    /// come from the injected level collector-queue data.
    /// </summary>
    public class CollectorQueueBoard : MonoBehaviour, ICollectorSource
    {
        private const int SpriteDiameterPixels = 32;

        [SerializeField, Tooltip("World-space size of a single collector, in world units.")]
        [Min(0.01f)]
        private float collectorSize = 1f;

        [SerializeField, Tooltip("Gap between neighbouring queues, in world units.")]
        [Min(0f)]
        private float horizontalSpacing = 0.3f;

        [SerializeField, Tooltip("Gap between neighbouring collectors within a queue, in world units.")]
        [Min(0f)]
        private float verticalSpacing = 0.3f;

        [SerializeField, Tooltip("Grid pixel consumption reads from. Optional — if missing, collectors still generate and consumption simply does nothing.")]
        private PixelGrid pixelGrid;

        [SerializeField, Tooltip("Conveyor system whose completed laps drive each collector's lifecycle resolution. Optional — if missing, lap resolution never triggers.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Waiting line an unsatisfied collector is placed into after a completed lap. Optional — if missing, an unsatisfied collector stays on the conveyor.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("Controller notified when an unsatisfied collector completes a lap and finds no free Waiting Line slot. Optional — if missing, that situation is simply never reported.")]
        private FailureController failureController;

        private Texture2D _sharedTexture;
        private Sprite _sharedSprite;
        private CollectorQueue[] _queues;
        private float _rowStepY;
        private bool _isInitialized;

        /// <summary>
        /// Builds this board's queues from the given level collector-queue
        /// data, using matchTypeToColor for each collector's visual colour.
        /// Must be called exactly once, before any selection is attempted. A
        /// second call, or a first call on an object that already has stale
        /// baked children, is refused (logged) rather than generating
        /// duplicate queues. Existing children are never deleted.
        /// </summary>
        public void Initialize(IReadOnlyList<CollectorQueueDefinition> collectorQueues, Func<MatchTypeId, Color> matchTypeToColor)
        {
            if (collectorQueues == null)
                throw new ArgumentNullException(nameof(collectorQueues));

            if (matchTypeToColor == null)
                throw new ArgumentNullException(nameof(matchTypeToColor));

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

            CreateSharedSprite();
            GenerateBoard(collectorQueues, matchTypeToColor);
            _isInitialized = true;
        }

        private void CreateSharedSprite()
        {
            _sharedTexture = new Texture2D(SpriteDiameterPixels, SpriteDiameterPixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            float radius = SpriteDiameterPixels * 0.5f;
            var center = new Vector2(radius, radius);
            var pixels = new Color32[SpriteDiameterPixels * SpriteDiameterPixels];

            for (int y = 0; y < SpriteDiameterPixels; y++)
            {
                for (int x = 0; x < SpriteDiameterPixels; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    pixels[y * SpriteDiameterPixels + x] = distance <= radius
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            _sharedTexture.SetPixels32(pixels);
            _sharedTexture.Apply();

            _sharedSprite = Sprite.Create(
                _sharedTexture,
                new Rect(0f, 0f, SpriteDiameterPixels, SpriteDiameterPixels),
                new Vector2(0.5f, 0.5f),
                SpriteDiameterPixels);
        }

        private void GenerateBoard(IReadOnlyList<CollectorQueueDefinition> collectorQueues, Func<MatchTypeId, Color> matchTypeToColor)
        {
            _queues = new CollectorQueue[collectorQueues.Count];

            float stepX = collectorSize + horizontalSpacing;
            _rowStepY = collectorSize + verticalSpacing;
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

                    // ConveyorRider is not listed explicitly: CollectorView's
                    // [RequireComponent(typeof(ConveyorRider))] adds it first,
                    // before CollectorView's own Awake subscribes to it —
                    // listing it again here would add a second, duplicate
                    // ConveyorRider instead of reusing that one.
                    var collectorObject = new GameObject(
                        $"Collector_{queueIndex}_{rowIndex}",
                        typeof(CollectorView),
                        typeof(PixelConsumer),
                        typeof(CollectorLifecycle));
                    collectorObject.transform.SetParent(queueObject.transform, false);
                    collectorObject.transform.localPosition = new Vector3(0f, -rowIndex * _rowStepY, 0f);
                    collectorObject.transform.localScale = new Vector3(collectorSize, collectorSize, 1f);

                    var spriteRenderer = collectorObject.GetComponent<SpriteRenderer>();
                    spriteRenderer.sprite = _sharedSprite;

                    Color color = matchTypeToColor(collectorDefinition.MatchTypeId);

                    var collectorView = collectorObject.GetComponent<CollectorView>();
                    collectorView.Initialize(color);

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
                views[rowIndex].transform.localPosition = new Vector3(0f, -rowIndex * _rowStepY, 0f);
        }

        private void OnDestroy()
        {
            if (_sharedSprite != null)
                Destroy(_sharedSprite);

            if (_sharedTexture != null)
                Destroy(_sharedTexture);
        }
    }
}
