using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Pixels;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Generates four vertical queues of five CollectorView objects on Awake, using a
    /// single shared runtime-generated circular sprite and a deterministic 4-colour palette.
    /// </summary>
    public class CollectorQueueBoard : MonoBehaviour
    {
        private const int SpriteDiameterPixels = 32;

        [SerializeField, Tooltip("Number of vertical queues to generate.")]
        [Min(1)]
        private int queueCount = 4;

        [SerializeField, Tooltip("Number of collectors within each queue.")]
        [Min(1)]
        private int collectorsPerQueue = 5;

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

        private const int HungerCapacity = 3;

        private static readonly Color[] Palette =
        {
            Color.red,
            Color.green,
            Color.blue,
            Color.yellow
        };

        private Texture2D _sharedTexture;
        private Sprite _sharedSprite;
        private CollectorQueue[] _queues;
        private float _rowStepY;

        private void Awake()
        {
            CreateSharedSprite();
            GenerateBoard();
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

        private void GenerateBoard()
        {
            _queues = new CollectorQueue[queueCount];

            float stepX = collectorSize + horizontalSpacing;
            _rowStepY = collectorSize + verticalSpacing;
            float offsetX = (queueCount - 1) * stepX * 0.5f;

            for (int queueIndex = 0; queueIndex < queueCount; queueIndex++)
            {
                var queueObject = new GameObject($"Queue_{queueIndex}");
                queueObject.transform.SetParent(transform, false);
                queueObject.transform.localPosition = new Vector3(queueIndex * stepX - offsetX, 0f, 0f);

                var queue = new CollectorQueue();
                _queues[queueIndex] = queue;

                for (int rowIndex = 0; rowIndex < collectorsPerQueue; rowIndex++)
                {
                    var collectorObject = new GameObject(
                        $"Collector_{queueIndex}_{rowIndex}",
                        typeof(CollectorView),
                        typeof(ConveyorRider),
                        typeof(PixelConsumer),
                        typeof(CollectorLifecycle));
                    collectorObject.transform.SetParent(queueObject.transform, false);
                    collectorObject.transform.localPosition = new Vector3(0f, -rowIndex * _rowStepY, 0f);
                    collectorObject.transform.localScale = new Vector3(collectorSize, collectorSize, 1f);

                    var spriteRenderer = collectorObject.GetComponent<SpriteRenderer>();
                    spriteRenderer.sprite = _sharedSprite;

                    // Mix queueIndex and rowIndex so colour varies per collector,
                    // not per queue, making individual movement easy to track.
                    Color color = Palette[(queueIndex + rowIndex) % Palette.Length];

                    var collectorView = collectorObject.GetComponent<CollectorView>();
                    collectorView.Initialize(color);

                    // Reuse the same colour for the rider's food type — never
                    // recalculate it independently. Every collector starts with
                    // the same temporary prototype hunger capacity.
                    var conveyorRider = collectorObject.GetComponent<ConveyorRider>();
                    conveyorRider.Initialize(color, HungerCapacity);

                    var pixelConsumer = collectorObject.GetComponent<PixelConsumer>();
                    pixelConsumer.Initialize(pixelGrid, conveyorRider);

                    var collectorLifecycle = collectorObject.GetComponent<CollectorLifecycle>();
                    collectorLifecycle.Initialize(conveyorRider, conveyorSystem, waitingLine);

                    queue.Add(collectorView);
                }
            }
        }

        /// <summary>
        /// True when the given view is currently the first available collector
        /// of one of the board's queues.
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
