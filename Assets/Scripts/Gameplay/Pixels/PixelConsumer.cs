using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Pixels
{
    /// <summary>
    /// Periodically asks a PixelGrid to consume one matching exposed pixel near
    /// a ConveyorRider's current position. Holds no hunger, scoring, victory,
    /// or disappearance logic of its own. After a successful consume it
    /// triggers exactly one CollectorPresentation reaction — the normal bite
    /// reaction, or (only when this consume brought RemainingHunger to zero)
    /// the full final-bite completion sequence — but owns none of the
    /// animation or sprite-swap logic that reaction actually plays; that is
    /// entirely CollectorPresentation/CollectorAnimation's responsibility.
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
        private CollectorPresentation _presentation;

        private CollectorPresentation Presentation =>
            _presentation != null ? _presentation : _presentation = rider != null ? rider.GetComponent<CollectorPresentation>() : null;

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
            bool consumed = pixelGrid.TryConsumeNearestExposed(rider.transform.position, rider.MatchTypeId, alignmentTolerance);
            if (consumed)
            {
                rider.RegisterConsumedPixel();
                TriggerBiteReaction();
            }

            _cooldownRemaining = consumptionCooldown;
        }

        /// <summary>
        /// Starts exactly one CollectorPresentation reaction for the bite
        /// that was just consumed — never both. Which one depends only on
        /// rider.IsSatisfied immediately after RegisterConsumedPixel, so this
        /// is the single point that decides "normal bite" vs "final bite":
        /// nothing else ever independently starts either sequence from a
        /// pixel consume. CollectorLifecycle separately calls
        /// PlayFinalBiteSequence too, but only as an idempotent safety net —
        /// see CollectorPresentation.PlayFinalBiteSequence.
        /// </summary>
        private void TriggerBiteReaction()
        {
            CollectorPresentation presentation = Presentation;
            if (presentation == null)
                return;

            if (rider.IsSatisfied)
                presentation.PlayFinalBiteSequence();
            else
                presentation.PlayNormalBiteReaction();
        }
    }
}
