using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns what Continue actually does after a Victory: resume gameplay
    /// through GameplayFlowController — Time.timeScale is a global engine
    /// setting that survives a scene load, so leaving it at 0 would load the
    /// next level frozen — then ask LevelProgressionController to load the
    /// next level. Presentation (VictoryUI) only ever calls LoadNextLevel —
    /// it never touches GameplayFlowController, LevelProgressionController,
    /// or scene loading directly. GameplayFlowController remains the sole
    /// owner of pause/resume state; this class only ever reaches it through
    /// ResumeGameplay, never Time.timeScale directly.
    /// </summary>
    public class VictoryFlowController : MonoBehaviour
    {
        [SerializeField, Tooltip("Sole owner of pause/resume state — reached only through its own API, never bypassed with a direct Time.timeScale assignment here.")]
        private GameplayFlowController gameplayFlowController;

        [SerializeField, Tooltip("Sole owner of progression decisions — reached only through its own API.")]
        private LevelProgressionController levelProgressionController;

        /// <summary>
        /// Resumes gameplay, then loads the next level. If there is no next
        /// level, LevelProgressionController logs and leaves the current
        /// level unchanged — gameplay simply stays resumed on it.
        /// </summary>
        public void LoadNextLevel()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();

            if (levelProgressionController != null)
                levelProgressionController.LoadNextLevel();
        }
    }
}
