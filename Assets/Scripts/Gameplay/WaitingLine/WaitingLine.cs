using Project001.Gameplay.Collectors;
using UnityEngine;

namespace Project001.Gameplay.WaitingLine
{
    /// <summary>
    /// Generates horizontal WaitingSlot objects on Initialize, using a single
    /// shared runtime-generated square-outline sprite reused by every slot.
    /// Slot count comes from the capacity passed to Initialize — a fixed
    /// global gameplay setting (see GameplayConstants.WaitingLineCapacity),
    /// not level data, since Waiting Line capacity must stay constant across
    /// every level.
    /// </summary>
    public class WaitingLine : MonoBehaviour, ICollectorSource
    {
        private const int SpriteSizePixels = 32;
        private const int OutlineThicknessPixels = 3;

        [SerializeField, Tooltip("Number of waiting slots to generate.")]
        [Min(1)]
        private int slotCount = 5;

        [SerializeField, Tooltip("World-space size of a single waiting slot, in world units.")]
        [Min(0.01f)]
        private float slotSize = 1f;

        [SerializeField, Tooltip("Gap between neighbouring slots, in world units.")]
        [Min(0f)]
        private float horizontalSpacing = 0.3f;

        private Texture2D _sharedTexture;
        private Sprite _sharedSprite;
        private WaitingSlot[] _slots;
        private bool _isInitialized;
        private bool _isAcceptingCollectors = true;

        /// <summary>
        /// Builds this line's slots using the given capacity, which is
        /// normalized to at least 1. Must be called exactly once, before any
        /// selection or boarding is attempted. A second call, or a first
        /// call on an object that already has stale baked children, is
        /// refused (logged) rather than generating duplicate slots. Existing
        /// children are never deleted.
        /// </summary>
        public void Initialize(int capacity)
        {
            if (_isInitialized)
            {
                Debug.LogError($"WaitingLine: Initialize called more than once on '{name}'; ignoring to avoid duplicate slots.", this);
                return;
            }

            if (transform.childCount > 0)
            {
                Debug.LogError($"WaitingLine: '{name}' already has {transform.childCount} child object(s) before Initialize; aborting to avoid duplicate slots. Scene may contain stale baked children.", this);
                return;
            }

            slotCount = Mathf.Max(1, capacity);
            CreateSharedSprite();
            GenerateSlots();
            _isInitialized = true;
        }

        private void CreateSharedSprite()
        {
            _sharedTexture = new Texture2D(SpriteSizePixels, SpriteSizePixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[SpriteSizePixels * SpriteSizePixels];

            for (int y = 0; y < SpriteSizePixels; y++)
            {
                for (int x = 0; x < SpriteSizePixels; x++)
                {
                    bool isBorder = x < OutlineThicknessPixels
                        || y < OutlineThicknessPixels
                        || x >= SpriteSizePixels - OutlineThicknessPixels
                        || y >= SpriteSizePixels - OutlineThicknessPixels;

                    pixels[y * SpriteSizePixels + x] = isBorder
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            _sharedTexture.SetPixels32(pixels);
            _sharedTexture.Apply();

            _sharedSprite = Sprite.Create(
                _sharedTexture,
                new Rect(0f, 0f, SpriteSizePixels, SpriteSizePixels),
                new Vector2(0.5f, 0.5f),
                SpriteSizePixels);
        }

        private void GenerateSlots()
        {
            _slots = new WaitingSlot[slotCount];

            float stepX = slotSize + horizontalSpacing;
            float offsetX = (slotCount - 1) * stepX * 0.5f;

            for (int index = 0; index < slotCount; index++)
            {
                var slotObject = new GameObject($"WaitingSlot_{index}", typeof(WaitingSlot));
                slotObject.transform.SetParent(transform, false);
                slotObject.transform.localPosition = new Vector3(index * stepX - offsetX, 0f, 0f);
                slotObject.transform.localScale = new Vector3(slotSize, slotSize, 1f);

                var spriteRenderer = slotObject.GetComponent<SpriteRenderer>();
                spriteRenderer.sprite = _sharedSprite;

                _slots[index] = slotObject.GetComponent<WaitingSlot>();
            }
        }

        public WaitingSlot GetFirstEmptySlot()
        {
            if (!_isAcceptingCollectors)
                return null;

            if (_slots == null)
                return null;

            foreach (WaitingSlot slot in _slots)
            {
                if (!slot.IsOccupied)
                    return slot;
            }

            return null;
        }

        /// <summary>
        /// Number of slots currently occupied. Independent of
        /// IsAcceptingCollectors — a slot already holding a collector still
        /// counts even after this line has stopped accepting new ones.
        /// </summary>
        public int OccupiedSlotCount
        {
            get
            {
                if (_slots == null)
                    return 0;

                int count = 0;
                foreach (WaitingSlot slot in _slots)
                {
                    if (slot.IsOccupied)
                        count++;
                }

                return count;
            }
        }

        /// <summary>
        /// Permanently stops this line from handing out empty slots (e.g.
        /// once Endgame Cleanup begins): GetFirstEmptySlot returns null from
        /// this point on, regardless of actual occupancy, so unsatisfied
        /// collectors that complete a lap are left circulating on the
        /// Conveyor instead of entering the Waiting Line. Irreversible, and
        /// never clears slots already occupied — those collectors remain
        /// exactly where they are and stay selectable.
        /// </summary>
        public void StopAcceptingCollectors()
        {
            _isAcceptingCollectors = false;
        }

        /// <summary>
        /// True when the given collector currently occupies any slot in this line.
        /// </summary>
        public bool Contains(CollectorView collectorView)
        {
            if (collectorView == null || _slots == null)
                return false;

            foreach (WaitingSlot slot in _slots)
            {
                if (slot.Occupant == collectorView)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Clears the slot occupied by the given collector, if any. Does not
        /// move or animate any collector.
        /// </summary>
        public bool ClearSlotContaining(CollectorView collectorView)
        {
            if (collectorView == null || _slots == null)
                return false;

            foreach (WaitingSlot slot in _slots)
            {
                if (slot.ClearIfOccupant(collectorView))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Explicit ICollectorSource implementation — forwards to Contains and
        /// ClearSlotContaining without duplicating their logic. Kept explicit
        /// so the line's own public API keeps its existing, more descriptive
        /// names.
        /// </summary>
        bool ICollectorSource.CanSelect(CollectorView collectorView) => Contains(collectorView);

        bool ICollectorSource.ReleaseCollector(CollectorView collectorView) => ClearSlotContaining(collectorView);

        private void OnDestroy()
        {
            if (_sharedSprite != null)
                Destroy(_sharedSprite);

            if (_sharedTexture != null)
                Destroy(_sharedTexture);
        }
    }
}
