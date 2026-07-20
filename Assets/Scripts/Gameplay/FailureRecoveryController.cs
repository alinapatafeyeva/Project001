using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Recovery;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns what happens after a Failure: either a full level restart
    /// (RetryCurrentLevel) or resuming the same level state after rearming
    /// Failure detection and transferring every Conveyor rider into the
    /// Recovery Row (ContinueCurrentLevel). Presentation (FailureUI) only
    /// ever calls these two methods — it never touches Time.timeScale,
    /// FailureController, ConveyorSystem, RecoveryRowController, or scene
    /// loading directly. GameplayFlowController remains the sole owner of
    /// pause/resume state; this class only ever reaches it through
    /// ResumeGameplay, never Time.timeScale directly.
    /// </summary>
    public class FailureRecoveryController : MonoBehaviour
    {
        [SerializeField, Tooltip("Detector rearmed via ResetFailure so a later Waiting Line overflow can trigger Failure again after Continue.")]
        private FailureController failureController;

        [SerializeField, Tooltip("Sole owner of pause/resume state — reached only through its own API, never bypassed with a direct Time.timeScale assignment here.")]
        private GameplayFlowController gameplayFlowController;

        [SerializeField, Tooltip("Source of the riders transferred into the Recovery Row on Continue, via its own TakeAllRiders API.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Destination for the riders taken off the Conveyor on Continue, via its own ReceiveCollectors API.")]
        private RecoveryRowController recoveryRowController;

        /// <summary>
        /// Restarts the current level completely from its initial state.
        /// Resumes gameplay through GameplayFlowController first — Time.timeScale
        /// is a global engine setting that survives a scene load, so leaving
        /// it at 0 would reload directly into a frozen level — then reloads
        /// the active scene. LevelBootstrapper.Awake naturally rebuilds
        /// pixels, queues, hunger, Waiting Line, conveyor, and Victory/Failure
        /// state from scratch on the next load, asking LevelProgressionController
        /// for the same current LevelId (its static state survives the
        /// reload, so Retry lands back on the same level, not level_001);
        /// this method does not rebuild anything itself. The scene's
        /// serialized enableFailureTestSetup is preserved automatically,
        /// since a reload re-deserializes the same saved scene data.
        /// </summary>
        public void RetryCurrentLevel()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        /// <summary>
        /// Resumes the same existing level: transfers every current Conveyor
        /// rider into the Recovery Row, rearms Failure detection so a later
        /// Waiting Line overflow can trigger a new Failure event, then
        /// resumes gameplay through GameplayFlowController. Never touches
        /// pixels, queues, hunger, or Waiting Line contents. The recovered
        /// collectors are left in the Recovery Row for manual relaunch — this
        /// method does not relaunch them itself.
        /// </summary>
        public void ContinueCurrentLevel()
        {
            TransferConveyorRidersToRecoveryRow();

            if (failureController != null)
                failureController.ResetFailure();

            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();
        }

        /// <summary>
        /// Moves every current Conveyor rider into the Recovery Row as one
        /// explicit step: the same ConveyorRider instances, never cloned or
        /// recreated, so MatchTypeId, RemainingHunger, HungerCapacity, and
        /// existing visuals carry over untouched. A no-op — and, critically,
        /// never calls TakeAllRiders — if either dependency is missing, so a
        /// missing RecoveryRowController can never strand riders that were
        /// already removed from the Conveyor with nowhere to go. Safe to call
        /// with an empty Conveyor: TakeAllRiders simply returns an empty
        /// list. RecoveryRowController.ReceiveCollectors already skips
        /// collectors it already holds, so calling this twice cannot
        /// duplicate anything.
        /// </summary>
        private void TransferConveyorRidersToRecoveryRow()
        {
            if (conveyorSystem == null || recoveryRowController == null)
                return;

            IReadOnlyList<ConveyorRider> riders = conveyorSystem.TakeAllRiders();
            recoveryRowController.ReceiveCollectors(riders);
        }
    }
}
