using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// Drives a fixed set of evenly spaced logical slots along a ConveyorPath and
    /// moves any riders occupying them. Only handles riding and capacity — no
    /// boarding animation, selection, input, or pixel consumption.
    /// </summary>
    public class ConveyorSystem : MonoBehaviour
    {
        [SerializeField, Tooltip("Path the riders move along.")]
        private ConveyorPath conveyorPath;

        [SerializeField, Tooltip("Maximum number of riders the conveyor can hold at once.")]
        [Min(1)]
        private int capacity = 5;

        [SerializeField, Tooltip("Movement speed along the path, in world units per second.")]
        [Min(0f)]
        private float moveSpeed = 2f;

        private ConveyorRider[] _slotOccupants;
        private float[] _slotProgress;
        private int _occupiedCount;

        public int Capacity => capacity;

        public int OccupiedCount => _occupiedCount;

        public bool HasSpace => _occupiedCount < capacity;

        private void Awake()
        {
            capacity = Mathf.Max(1, capacity);

            _slotOccupants = new ConveyorRider[capacity];
            _slotProgress = new float[capacity];

            for (int i = 0; i < capacity; i++)
                _slotProgress[i] = (float)i / capacity;
        }

        private void Update()
        {
            if (conveyorPath == null)
                return;

            float pathLength = conveyorPath.PathLength;
            if (pathLength <= 0f)
                return;

            float deltaProgress = moveSpeed * Time.deltaTime / pathLength;

            for (int i = 0; i < capacity; i++)
            {
                float progress = _slotProgress[i] + deltaProgress;
                _slotProgress[i] = progress - Mathf.Floor(progress);

                ConveyorRider rider = _slotOccupants[i];
                if (rider == null)
                {
                    // Unity's null check also catches riders destroyed externally
                    // (without going through TryRemoveRider). Clear a stale slot
                    // reference so the occupied count stays accurate.
                    if (!ReferenceEquals(rider, null))
                    {
                        _slotOccupants[i] = null;
                        _occupiedCount--;
                    }

                    continue;
                }

                rider.SetPosition(GetWorldPosition(i));
            }
        }

        /// <summary>
        /// Adds a rider to the first free logical slot. Returns false and makes
        /// no changes if the rider is null, already riding, the conveyor is
        /// full, or the path reference is missing.
        /// </summary>
        public bool TryAddRider(ConveyorRider rider)
        {
            if (rider == null)
                return false;

            if (IsRiding(rider))
                return false;

            if (!HasSpace)
                return false;

            if (conveyorPath == null)
                return false;

            if (conveyorPath.PathLength <= 0f)
                return false;

            int slotIndex = FindFreeSlot();
            if (slotIndex < 0)
                return false;

            _slotOccupants[slotIndex] = rider;
            _occupiedCount++;

            rider.transform.SetParent(transform, true);
            rider.SetPosition(GetWorldPosition(slotIndex));

            return true;
        }

        /// <summary>
        /// Frees the slot occupied by the given rider, if any. Does not reorder
        /// or reposition the remaining slots.
        /// </summary>
        public bool TryRemoveRider(ConveyorRider rider)
        {
            if (rider == null)
                return false;

            for (int i = 0; i < capacity; i++)
            {
                if (_slotOccupants[i] != rider)
                    continue;

                _slotOccupants[i] = null;
                _occupiedCount--;
                return true;
            }

            return false;
        }

        private bool IsRiding(ConveyorRider rider)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (_slotOccupants[i] == rider)
                    return true;
            }

            return false;
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (_slotOccupants[i] == null)
                    return i;
            }

            return -1;
        }

        private Vector3 GetWorldPosition(int slotIndex)
        {
            // Developer safety net: every call site already guards against a
            // missing path, so this should never fail at runtime.
            Debug.Assert(conveyorPath != null, "ConveyorSystem: ConveyorPath must exist before computing world positions.");

            Vector3 localPosition = conveyorPath.GetPositionAtProgress(_slotProgress[slotIndex]);
            return conveyorPath.transform.TransformPoint(localPosition);
        }
    }
}
