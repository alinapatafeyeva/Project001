using Project001.Gameplay.Collectors;
using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.WaitingLine
{
    /// <summary>
    /// Generates horizontal WaitingSlot objects on Initialize, each carrying
    /// its own child WaitingSlotVisual instance showing the Classic theme
    /// WaitingSlot.png sprite (see WaitingSlotVisual) — one sprite per slot,
    /// never a single merged image, so a future capacity change (see
    /// slotCount) just generates more independently-positioned instances of
    /// the same asset. Slot count comes from the capacity passed to
    /// Initialize — a fixed global gameplay setting (see
    /// GameplayConstants.WaitingLineCapacity), not level data, since Waiting
    /// Line capacity must stay constant across every level. Each slot
    /// occupies GameplayLayout.WaitingSlotSize — deliberately independent of
    /// GameplayLayout.CollectorSpriteScale, since a slot is only ever a
    /// landing marker an arriving collector's world position snaps to (see
    /// CollectorLifecycle.ResolveLap): the collector keeps its own scale, so
    /// the slot's own size is a free visual choice, not something that must
    /// equal or bound it.
    /// </summary>
    public class WaitingLine : MonoBehaviour, ICollectorSource
    {
        [SerializeField, Tooltip("Number of waiting slots to generate.")]
        [Min(1)]
        private int slotCount = 5;

        [SerializeField, Tooltip("The Classic theme waiting slot sprite (WaitingSlot.png), applied to every generated slot's own WaitingSlotVisual child.")]
        private Sprite slotSprite;

        private WaitingSlot[] _slots;
        private bool _isInitialized;

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
            GenerateSlots();
            _isInitialized = true;
        }

        private void GenerateSlots()
        {
            _slots = new WaitingSlot[slotCount];

            float stepX = GameplayLayout.WaitingSlotSize + GameplayLayout.WaitingSlotSpacing;
            float offsetX = (slotCount - 1) * stepX * 0.5f;

            for (int index = 0; index < slotCount; index++)
            {
                var slotObject = new GameObject($"WaitingSlot_{index}", typeof(WaitingSlot));
                slotObject.transform.SetParent(transform, false);
                slotObject.transform.localPosition = new Vector3(index * stepX - offsetX, 0f, 0f);
                slotObject.transform.localScale = new Vector3(GameplayLayout.WaitingSlotSize, GameplayLayout.WaitingSlotSize, 1f);

                CreateSlotVisual(slotObject.transform);

                _slots[index] = slotObject.GetComponent<WaitingSlot>();
            }
        }

        /// <summary>
        /// A child of the slot GameObject, never a component on it directly
        /// — WaitingSlot's own transform.position is what an arriving
        /// collector snaps to (see CollectorLifecycle.ResolveLap), so the
        /// visual's own depth push (see WaitingSlotVisual) must apply to a
        /// separate child transform, exactly mirroring how ConveyorVisual is
        /// parented under Conveyor rather than added to it directly.
        /// </summary>
        private void CreateSlotVisual(Transform slotTransform)
        {
            var visualObject = new GameObject(
                "WaitingSlotVisual",
                typeof(SpriteRenderer),
                typeof(WaitingSlotVisual));
            visualObject.transform.SetParent(slotTransform, false);

            visualObject.GetComponent<WaitingSlotVisual>().Initialize(slotSprite);
        }

        /// <summary>
        /// Returns the first unoccupied slot, or null if every slot is
        /// occupied. Always evaluates real occupancy — including after
        /// Endgame Cleanup begins (see EndgameCleanupController's own
        /// remarks for why that phase never needs to close this line off
        /// early: an unsatisfied collector that completes a lap and finds
        /// every slot genuinely full still falls back to
        /// CollectorLifecycle.ResolveLap's existing
        /// FailureController.NotifyWaitingLineFull path exactly as before.
        /// </summary>
        public WaitingSlot GetFirstEmptySlot()
        {
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
        /// Number of slots currently occupied.
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

        /// <summary>
        /// Reapplies this line's own neutral (baseline) presentation depth —
        /// used only when CollectorSelectionController rolls back a
        /// boarding attempt (this line never actually released the view).
        /// </summary>
        void ICollectorSource.RestorePresentationDepth(CollectorView collectorView) => collectorView?.Presentation.ClearPresentationDepth();
    }
}
