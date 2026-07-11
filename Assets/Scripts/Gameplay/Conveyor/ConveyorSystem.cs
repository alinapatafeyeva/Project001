using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// Moves riders counter-clockwise around a ConveyorPath. All riders board at
    /// a single fixed progress point and move at the same speed afterwards, so
    /// their launch order is preserved for as long as they stay on the
    /// conveyor. Only handles riding and capacity — no boarding animation,
    /// selection, input, or pixel consumption.
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

        [SerializeField, Tooltip("Normalized [0,1] path progress where new riders board. Default sits in the lower-left area of a typical rectangular path.")]
        [Range(0f, 1f)]
        private float boardingProgress = 0.55f;

        [SerializeField, Tooltip("Minimum world-space distance a rider must be from the boarding point before another rider can board.")]
        [Min(0f)]
        private float boardingClearance = 1f;

        // Both lists are index-aligned and ordered by launch (add) order.
        private readonly List<ConveyorRider> _riders = new List<ConveyorRider>();
        private readonly List<float> _riderProgress = new List<float>();

        public int Capacity => capacity;

        public int OccupiedCount => _riders.Count;

        public bool HasSpace => _riders.Count < capacity;

        private void Awake()
        {
            capacity = Mathf.Max(1, capacity);
        }

        private void Update()
        {
            if (conveyorPath == null)
                return;

            float pathLength = conveyorPath.PathLength;
            if (pathLength <= 0f)
                return;

            // Positive delta progress walks the path's point order, which is
            // counter-clockwise, so movement direction is never flipped here.
            float deltaProgress = Mathf.Abs(moveSpeed) * Time.deltaTime / pathLength;

            for (int i = 0; i < _riders.Count; i++)
            {
                ConveyorRider rider = _riders[i];
                if (rider == null)
                {
                    // Every entry is non-null at insertion time, so a null here
                    // only means the rider was destroyed externally. Drop it.
                    _riders.RemoveAt(i);
                    _riderProgress.RemoveAt(i);
                    i--;
                    continue;
                }

                float progress = _riderProgress[i] + deltaProgress;
                progress -= Mathf.Floor(progress);
                _riderProgress[i] = progress;

                rider.SetPosition(GetWorldPosition(progress));
            }
        }

        /// <summary>
        /// Boards a rider at the fixed boarding progress. Returns false and
        /// makes no changes if the rider is null, already riding, the conveyor
        /// is full, the path reference is missing or invalid, or the boarding
        /// point is not yet clear of other riders.
        /// </summary>
        public bool TryAddRider(ConveyorRider rider)
        {
            if (rider == null)
                return false;

            if (rider.IsRiding)
                return false;

            if (_riders.Contains(rider))
                return false;

            if (!HasSpace)
                return false;

            if (conveyorPath == null)
                return false;

            if (conveyorPath.PathLength <= 0f)
                return false;

            if (!IsBoardingAreaClear())
                return false;

            _riders.Add(rider);
            _riderProgress.Add(boardingProgress);

            rider.transform.SetParent(transform, true);
            rider.SetPosition(GetWorldPosition(boardingProgress));
            rider.EnterRiding();

            return true;
        }

        /// <summary>
        /// Removes the given rider, if present, preserving the order of the
        /// remaining riders.
        /// </summary>
        public bool TryRemoveRider(ConveyorRider rider)
        {
            if (rider == null)
                return false;

            int index = _riders.IndexOf(rider);
            if (index < 0)
                return false;

            _riders.RemoveAt(index);
            _riderProgress.RemoveAt(index);
            rider.ExitRiding();
            return true;
        }

        private bool IsBoardingAreaClear()
        {
            float pathLength = conveyorPath.PathLength;

            for (int i = 0; i < _riderProgress.Count; i++)
            {
                float difference = Mathf.Abs(_riderProgress[i] - boardingProgress);
                difference = Mathf.Min(difference, 1f - difference);

                if (difference * pathLength < boardingClearance)
                    return false;
            }

            return true;
        }

        private Vector3 GetWorldPosition(float progress)
        {
            // Developer safety net: every call site already guards against a
            // missing path, so this should never fail at runtime.
            Debug.Assert(conveyorPath != null, "ConveyorSystem: ConveyorPath must exist before computing world positions.");

            Vector3 localPosition = conveyorPath.GetPositionAtProgress(progress);
            return conveyorPath.transform.TransformPoint(localPosition);
        }
    }
}
