using Project001.Gameplay.Failure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns what happens after a Failure: either a full level restart
    /// (RetryCurrentLevel) or resuming the same level state after rearming
    /// Failure detection (ContinueCurrentLevel). Presentation (FailureUI)
    /// only ever calls these two methods — it never touches Time.timeScale,
    /// FailureController, or scene loading directly. GameplayFlowController
    /// remains the sole owner of pause/resume state; this class only ever
    /// reaches it through ResumeGameplay, never Time.timeScale directly.
    /// </summary>
    public class FailureRecoveryController : MonoBehaviour
    {
        [SerializeField, Tooltip("Detector rearmed via ResetFailure so a later Waiting Line overflow can trigger Failure again after Continue.")]
        private FailureController failureController;

        [SerializeField, Tooltip("Sole owner of pause/resume state — reached only through its own API, never bypassed with a direct Time.timeScale assignment here.")]
        private GameplayFlowController gameplayFlowController;

        /// <summary>
        /// Restarts the current level completely from its initial state.
        /// Resumes gameplay through GameplayFlowController first — Time.timeScale
        /// is a global engine setting that survives a scene load, so leaving
        /// it at 0 would reload directly into a frozen level — then reloads
        /// the active scene. LevelBootstrapper.Awake naturally rebuilds
        /// pixels, queues, hunger, Waiting Line, conveyor, and Victory/Failure
        /// state from scratch on the next load; this method does not rebuild
        /// anything itself. The scene's serialized testLevelId and
        /// enableFailureTestSetup are preserved automatically, since a reload
        /// re-deserializes the same saved scene data.
        /// </summary>
        public void RetryCurrentLevel()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        /// <summary>
        /// Resumes the same existing level: rearms Failure detection so a
        /// later Waiting Line overflow can trigger a new Failure event, then
        /// resumes gameplay through GameplayFlowController. Never touches
        /// pixels, queues, hunger, conveyor contents, or Waiting Line
        /// contents.
        /// </summary>
        public void ContinueCurrentLevel()
        {
            if (failureController != null)
                failureController.ResetFailure();

            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();
        }
    }
}
