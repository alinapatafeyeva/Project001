using Project001.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Pause modal: Continue and Exit Level. Owns only panel visibility and
    /// button presentation, exactly like VictoryUI/FailureUI — Continue
    /// forwards to PauseFlowController.ContinuePause (resumes gameplay,
    /// including whichever 1x/2x speed was selected before pausing);
    /// Exit Level hands off to the shared ExitConfirmationUI instead of
    /// exiting directly, so Pause never duplicates Exit's own confirmation
    /// or navigation logic.
    ///
    /// Exit Level does not call PauseFlowController or
    /// LevelExitFlowController at all: gameplay is already paused by the
    /// time this panel is visible (Show is only ever called after
    /// PauseFlowController.OpenPause), and stays paused throughout the
    /// Pause -&gt; Exit confirmation -&gt; Stay round trip, so there is no
    /// pause/resume transition for either controller to own here — only a
    /// presentation-level panel swap (this panel hides, ExitConfirmationUI's
    /// shows; Stay reverses exactly that swap via the Show callback passed
    /// to Open).
    /// </summary>
    public class PauseUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Root panel (full-screen backdrop) shown while paused and hidden again on Continue/Exit Level. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Resumes gameplay via PauseFlowController and hides this panel.")]
        private Button continueButton;

        [SerializeField, Tooltip("Hides this panel and opens the shared Exit confirmation dialog instead of exiting directly.")]
        private Button exitLevelButton;

        [SerializeField, Tooltip("Sole owner of what Continue actually does — reached only through its own API, never a direct Time.timeScale/GameplayFlowController call here.")]
        private PauseFlowController pauseFlowController;

        [SerializeField, Tooltip("Shared confirmation dialog Exit Level hands off to, so Pause never implements a second, independent exit flow.")]
        private ExitConfirmationUI exitConfirmationUI;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (continueButton != null)
                continueButton.onClick.AddListener(OnContinuePressed);

            if (exitLevelButton != null)
                exitLevelButton.onClick.AddListener(OnExitLevelPressed);
        }

        private void OnDestroy()
        {
            if (continueButton != null)
                continueButton.onClick.RemoveListener(OnContinuePressed);

            if (exitLevelButton != null)
                exitLevelButton.onClick.RemoveListener(OnExitLevelPressed);
        }

        /// <summary>Shows the Pause panel. Does not itself pause gameplay — callers pause first (see TopHudUI.OnPausePressed).</summary>
        public void Show()
        {
            if (panel != null)
                panel.SetActive(true);
        }

        private void OnContinuePressed()
        {
            if (panel != null)
                panel.SetActive(false);

            if (pauseFlowController != null)
                pauseFlowController.ContinuePause();
        }

        private void OnExitLevelPressed()
        {
            if (panel != null)
                panel.SetActive(false);

            if (exitConfirmationUI != null)
                exitConfirmationUI.Open(Show);
        }
    }
}
