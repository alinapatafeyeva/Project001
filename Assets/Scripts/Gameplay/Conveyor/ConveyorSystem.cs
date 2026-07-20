using System;
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

        // All lists are index-aligned and ordered by launch (add) order.
        private readonly List<ConveyorRider> _riders = new List<ConveyorRider>();
        private readonly List<float> _riderProgress = new List<float>();
        private readonly List<int> _riderCompletedLaps = new List<int>();

        private float _configuredMoveSpeed;

        public int Capacity => capacity;

        public int OccupiedCount => _riders.Count;

        public bool HasSpace => _riders.Count < capacity;

        private void Awake()
        {
            capacity = Mathf.Max(1, capacity);
            moveSpeed = Mathf.Max(0f, moveSpeed);
        }

        /// <summary>
        /// Applies caller-provided capacity and move speed (GameplayConstants
        /// values, not level data — see LevelBootstrapper). Intended for
        /// one-time startup configuration, before any riders board — unlike
        /// PixelGrid/WaitingLine/CollectorQueueBoard this does not generate
        /// any children, so there is nothing to duplicate and no single-call
        /// guard is needed, but it is not safe to call arbitrarily during
        /// gameplay: lowering capacity while riders are already boarded
        /// could leave OccupiedCount greater than Capacity.
        /// </summary>
        public void Configure(int levelCapacity, float levelMoveSpeed)
        {
            capacity = Mathf.Max(1, levelCapacity);
            moveSpeed = Mathf.Max(0f, levelMoveSpeed);
            _configuredMoveSpeed = moveSpeed;
        }

        /// <summary>
        /// Sets moveSpeed to the configured base speed (from Configure)
        /// times the given multiplier — e.g. Endgame Cleanup increasing
        /// conveyor speed via GameplayConstants.
        /// EndgameCleanupConveyorSpeedMultiplier. Does not touch capacity or
        /// any rider.
        /// </summary>
        public void ApplySpeedMultiplier(float multiplier)
        {
            moveSpeed = Mathf.Max(0f, _configuredMoveSpeed * multiplier);
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
            // moveSpeed is clamped non-negative in Awake, so no Abs is needed.
            float deltaProgress = moveSpeed * Time.deltaTime / pathLength;

            for (int i = 0; i < _riders.Count; i++)
            {
                ConveyorRider rider = _riders[i];
                if (rider == null)
                {
                    // Every entry is non-null at insertion time, so a null here
                    // only means the rider was destroyed externally. Drop it.
                    _riders.RemoveAt(i);
                    _riderProgress.RemoveAt(i);
                    _riderCompletedLaps.RemoveAt(i);
                    i--;
                    continue;
                }

                // A completed lap means returning to boardingProgress, not
                // wrapping at the path's raw 0/1 seam — that seam sits near
                // the upper-right corner, while boarding happens lower-left,
                // so working in progress relative to boardingProgress is what
                // makes "one lap" mean one full trip back to the boarding
                // point. FloorToInt handles an unusually large deltaProgress
                // that completes more than one lap in a single frame in one step.
                float relativeProgress = Mathf.Repeat(_riderProgress[i] - boardingProgress, 1f);
                float rawRelativeProgress = relativeProgress + deltaProgress;
                int lapsCompletedThisFrame = Mathf.FloorToInt(rawRelativeProgress);
                float wrappedRelativeProgress = Mathf.Repeat(rawRelativeProgress, 1f);

                _riderProgress[i] = Mathf.Repeat(boardingProgress + wrappedRelativeProgress, 1f);
                _riderCompletedLaps[i] += lapsCompletedThisFrame;

                rider.SetPosition(GetWorldPosition(_riderProgress[i]));
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
            _riderCompletedLaps.Add(0);

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
            _riderCompletedLaps.RemoveAt(index);
            rider.ExitRiding();
            return true;
        }

        /// <summary>
        /// Removes every rider currently on this conveyor and returns them,
        /// transferring ownership to the caller in their existing (launch)
        /// order. Each returned rider has already had ExitRiding() called on
        /// it, exactly as TryRemoveRider does — but no Collector state
        /// (MatchTypeId, RemainingHunger, HungerCapacity) is read or changed,
        /// and no rider is cloned or recreated; these are the same instances
        /// that were riding. OccupiedCount is 0 immediately after this call.
        /// Returns an empty list, and makes no changes, if the conveyor is
        /// already empty. Intended for a future gameplay system (e.g. a
        /// Recovery Row) taking ownership of every rider at once — this
        /// method exposes no internal progress or lap-tracking state.
        /// Safe if a rider was destroyed externally and is still present in
        /// the internal list at call time (i.e. before the next Update()
        /// prunes it) — such an entry is still included in the returned
        /// list, matching Update()'s own null handling, but ExitRiding() is
        /// only called on riders that are not Unity-null.
        /// </summary>
        public IReadOnlyList<ConveyorRider> TakeAllRiders()
        {
            if (_riders.Count == 0)
                return Array.Empty<ConveyorRider>();

            var takenRiders = new List<ConveyorRider>(_riders);

            _riders.Clear();
            _riderProgress.Clear();
            _riderCompletedLaps.Clear();

            foreach (ConveyorRider rider in takenRiders)
            {
                if (rider != null)
                    rider.ExitRiding();
            }

            return takenRiders;
        }

        /// <summary>
        /// Consumes one pending completed lap for the given rider, if any.
        /// Returns true at most once per completed lap; false if the rider is
        /// not currently on this conveyor or has no unconsumed lap yet. Does
        /// not remove, destroy, or otherwise decide the rider's fate — that is
        /// left entirely to whoever calls this.
        /// </summary>
        public bool TryConsumeCompletedLap(ConveyorRider rider)
        {
            if (rider == null)
                return false;

            int index = _riders.IndexOf(rider);
            if (index < 0 || _riderCompletedLaps[index] <= 0)
                return false;

            _riderCompletedLaps[index]--;
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
