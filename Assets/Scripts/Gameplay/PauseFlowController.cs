using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns what the top HUD's Pause button and the Pause modal's Continue
    /// button actually do: pause/resume gameplay through
    /// GameplayFlowController — the sole owner of pause/resume state, reached
    /// only through its own API, never bypassed with a direct Time.timeScale
    /// assignment here — mirroring VictoryFlowController/
    /// FailureRecoveryController's own convention. Presentation
    /// (Project001.UI.Hud.TopHudUI, PauseUI) only ever calls OpenPause/
    /// ContinuePause; it never touches GameplayFlowController or
    /// Time.timeScale directly.
    ///
    /// Deliberately does not own the Pause modal's Exit Level button — that
    /// action hands off to LevelExitFlowController.ConfirmExit (via
    /// PauseUI/ExitConfirmationUI) so every path that abandons the current
    /// attempt goes through the same one exit implementation, never a second
    /// one duplicated here.
    /// </summary>
    public class PauseFlowController : MonoBehaviour
    {
        [SerializeField, Tooltip("Sole owner of pause/resume state — reached only through its own API, never bypassed with a direct Time.timeScale assignment here.")]
        private GameplayFlowController gameplayFlowController;

        /// <summary>Suspends gameplay for as long as the Pause modal is open.</summary>
        public void OpenPause()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.PauseGameplay();
        }

        /// <summary>Resumes gameplay — including whichever 1x/2x speed was selected before pausing, restored by GameplayFlowController itself.</summary>
        public void ContinuePause()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();
        }
    }
}
