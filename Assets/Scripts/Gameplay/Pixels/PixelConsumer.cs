using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Feeding;
using UnityEngine;

namespace Project001.Gameplay.Pixels
{
    /// <summary>
    /// Periodically asks a PixelGrid to consume one matching exposed pixel near
    /// a ConveyorRider's current position. Holds no hunger, scoring, victory,
    /// or disappearance logic of its own, and no presentation logic either —
    /// a successful consume only hands the pixel's own world position and
    /// colour to this collector's own FoodFlightController (a sibling
    /// component, see CollectorQueueBoard.GenerateBoard), which owns
    /// everything about the resulting FoodPacket's flight and, on arrival,
    /// the rider's own hunger decrement (see FoodFlightController's own
    /// remarks for why that decrement must not happen here, at consume
    /// time). The pixel itself is already reserved the instant PixelGrid
    /// consumes it — see PixelGrid.TryConsumeNearestExposed — regardless of
    /// how long the resulting packet then takes to fly.
    /// </summary>
    public class PixelConsumer : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid pixels are consumed from.")]
        private PixelGrid pixelGrid;

        [SerializeField, Tooltip("Rider whose position and MatchTypeId drive consumption.")]
        private ConveyorRider rider;

        [SerializeField, Tooltip("Minimum time, in seconds, between consumption attempts.")]
        [Min(0f)]
        private float consumptionCooldown = 0.1f;

        [SerializeField, Tooltip("Maximum world-space offset, along the axis perpendicular to the rider's facing side, for a pixel to count as aligned. Tuned for the ~1-unit cell spacing.")]
        [Min(0f)]
        private float alignmentTolerance = 0.5f;

        private float _cooldownRemaining;
        private FoodFlightController _foodFlightController;

        private FoodFlightController FoodFlightController =>
            _foodFlightController != null ? _foodFlightController : _foodFlightController = rider != null ? rider.GetComponent<FoodFlightController>() : null;

        /// <summary>
        /// Assigns the grid to consume from and the rider that drives
        /// consumption. Either may be null; missing references simply make
        /// consumption a no-op.
        /// </summary>
        public void Initialize(PixelGrid grid, ConveyorRider conveyorRider)
        {
            pixelGrid = grid;
            rider = conveyorRider;
        }

        private void Update()
        {
            if (_cooldownRemaining > 0f)
                _cooldownRemaining -= Time.deltaTime;

            if (_cooldownRemaining > 0f)
                return;

            if (pixelGrid == null || rider == null)
                return;

            if (!rider.IsRiding)
                return;

            if (rider.IsSatisfied)
                return;

            // Reset after every attempt, successful or not, so a failed search
            // does not re-scan the whole grid on the very next frame.
            bool consumed = pixelGrid.TryConsumeNearestExposed(rider.transform.position, rider.MatchTypeId, alignmentTolerance, out Vector3 consumedWorldPosition, out Color consumedColor);
            if (consumed)
                FoodFlightController?.Enqueue(consumedWorldPosition, consumedColor);

            _cooldownRemaining = consumptionCooldown;
        }
    }
}
