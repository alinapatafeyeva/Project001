using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Recovery;
using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Watches RemainingCollectors — the sum of collectors still queued on
    /// CollectorQueueBoard, riding ConveyorSystem, occupying WaitingLine, and
    /// held in RecoveryRowController — and enters Endgame Cleanup, exactly
    /// once, the moment that count drops to GameplayConstants.
    /// WaitingLineCapacity or below.
    ///
    /// The very first evaluation cannot happen on any Unity lifecycle timer
    /// (Awake/Start ordering between sibling GameObjects is otherwise
    /// undefined): LevelBootstrapper calls NotifyLevelBuilt() directly, as
    /// the last step of building a level, once CollectorQueueBoard,
    /// ConveyorSystem, and WaitingLine are actually populated. Every
    /// evaluation after that is driven by CollectorLifecycle.
    /// CollectorSatisfied instead of polling every frame, since a collector
    /// being satisfied and destroyed is the only event that can ever lower
    /// RemainingCollectors.
    ///
    /// Owns only the decision to enter the phase and the one-time signal to
    /// the systems that already own each affected behaviour: FailureController
    /// (failure activation), WaitingLine (admission), and ConveyorSystem
    /// (move speed). Never reimplements their logic, and never touches
    /// CollectorLifecycle or collector selection — both already produce the
    /// desired Endgame Cleanup behaviour once WaitingLine stops accepting
    /// collectors and FailureController is permanently disabled.
    /// </summary>
    public class EndgameCleanupController : MonoBehaviour
    {
        [SerializeField, Tooltip("Source of collectors still queued (not yet launched).")]
        private CollectorQueueBoard collectorQueueBoard;

        [SerializeField, Tooltip("Conveyor whose occupancy counts toward RemainingCollectors, and whose speed increases once Endgame Cleanup is active.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Waiting line whose occupancy counts toward RemainingCollectors, and which stops accepting new collectors once Endgame Cleanup is active.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("Recovery Row whose held collectors count toward RemainingCollectors.")]
        private RecoveryRowController recoveryRowController;

        [SerializeField, Tooltip("Failure owner, permanently disabled once Endgame Cleanup is active.")]
        private FailureController failureController;

        // False until LevelBootstrapper explicitly confirms the level is
        // built. EvaluateThreshold reads stale/zero counts before that point
        // (queues not yet generated, conveyor not yet configured), so it
        // must refuse to run at all until this is true.
        private bool _levelBuilt;

        public bool IsActive { get; private set; }

        private void Awake()
        {
            CollectorLifecycle.CollectorSatisfied += HandleCollectorSatisfied;
        }

        private void OnDestroy()
        {
            CollectorLifecycle.CollectorSatisfied -= HandleCollectorSatisfied;
        }

        /// <summary>
        /// Called once by LevelBootstrapper, as the last step of BuildLevel,
        /// after CollectorQueueBoard, ConveyorSystem, and WaitingLine have
        /// all been initialized from the resolved level. Safe to call more
        /// than once by accident: _levelBuilt only ever transitions
        /// false-&gt;true, and EvaluateThreshold's own IsActive guard means a
        /// repeated call can never re-enter Endgame Cleanup or re-apply the
        /// conveyor speed multiplier.
        /// </summary>
        public void NotifyLevelBuilt()
        {
            _levelBuilt = true;
            EvaluateThreshold();
        }

        private void HandleCollectorSatisfied()
        {
            EvaluateThreshold();
        }

        private void EvaluateThreshold()
        {
            if (!_levelBuilt || IsActive)
                return;

            int remainingCollectors =
                collectorQueueBoard.RemainingCollectorCount
                + conveyorSystem.OccupiedCount
                + waitingLine.OccupiedSlotCount
                + recoveryRowController.Collectors.Count;

            if (remainingCollectors > GameplayConstants.WaitingLineCapacity)
                return;

            EnterEndgameCleanup();
        }

        private void EnterEndgameCleanup()
        {
            IsActive = true;

            failureController.DisablePermanently();
            waitingLine.StopAcceptingCollectors();
            conveyorSystem.ApplySpeedMultiplier(GameplayConstants.EndgameCleanupConveyorSpeedMultiplier);

            Debug.Log("Endgame Cleanup entered.");
        }
    }
}
