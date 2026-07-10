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
            float stepY = collectorSize + verticalSpacing;
            float offsetX = (queueCount - 1) * stepX * 0.5f;

            for (int queueIndex = 0; queueIndex < queueCount; queueIndex++)
            {
                var queueObject = new GameObject($"Queue_{queueIndex}");
                queueObject.transform.SetParent(transform, false);
                queueObject.transform.localPosition = new Vector3(queueIndex * stepX - offsetX, 0f, 0f);

                var queue = new CollectorQueue();
                _queues[queueIndex] = queue;

                Color color = Palette[queueIndex % Palette.Length];

                for (int rowIndex = 0; rowIndex < collectorsPerQueue; rowIndex++)
                {
                    var collectorObject = new GameObject($"Collector_{queueIndex}_{rowIndex}", typeof(CollectorView));
                    collectorObject.transform.SetParent(queueObject.transform, false);
                    collectorObject.transform.localPosition = new Vector3(0f, -rowIndex * stepY, 0f);
                    collectorObject.transform.localScale = new Vector3(collectorSize, collectorSize, 1f);

                    var spriteRenderer = collectorObject.GetComponent<SpriteRenderer>();
                    spriteRenderer.sprite = _sharedSprite;

                    var collectorView = collectorObject.GetComponent<CollectorView>();
                    collectorView.Initialize(color);

                    queue.Add(collectorView);
                }
            }
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
