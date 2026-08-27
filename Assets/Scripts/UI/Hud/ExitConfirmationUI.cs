using System;
using Project001.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Exit confirmation dialog: "Exit level?" / "Your current progress will
    /// be lost.", a Stay button, and an Exit button. Reused by both entry
    /// points that can abandon the current attempt — the top HUD's direct
    /// Exit button and the Pause modal's Exit Level button — so there is
    /// only ever one confirmation implementation, never a second one
    /// duplicated per caller.
    ///
    /// Owns only panel visibility and button presentation, exactly like
    /// VictoryUI/FailureUI. Never touches Time.timeScale or
    /// GameplayFlowController directly — Exit forwards to
    /// LevelExitFlowController.ConfirmExit, the sole owner of what a
    /// confirmed Exit actually does.
    ///
    /// Does not decide what Stay does either: Open's onStay callback is
    /// supplied by whichever caller opened the dialog (TopHudUI resumes
    /// gameplay directly via LevelExitFlowController.CancelExit; PauseUI
    /// instead just re-shows the Pause panel, since gameplay was already
    /// paused before this dialog opened and stays paused either way) — this
    /// class only ever invokes whatever callback it was given, never a
    /// hardcoded action.
    /// </summary>
    public class ExitConfirmationUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Root panel (full-screen backdrop) shown while this dialog is open and hidden again on Stay. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Closes the dialog and invokes whatever onStay callback Open was given — never resumes gameplay itself.")]
        private Button stayButton;

        [SerializeField, Tooltip("Forwards to LevelExitFlowController.ConfirmExit — the sole owner of what a confirmed Exit actually does.")]
        private Button exitButton;

        [SerializeField, Tooltip("Sole owner of what a confirmed Exit actually does — currently a stub with no destination to navigate to yet.")]
        private LevelExitFlowController levelExitFlowController;

        private Action _onStay;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (stayButton != null)
                stayButton.onClick.AddListener(OnStayPressed);

            if (exitButton != null)
                exitButton.onClick.AddListener(OnExitPressed);
        }

        private void OnDestroy()
        {
            if (stayButton != null)
                stayButton.onClick.RemoveListener(OnStayPressed);

            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExitPressed);
        }

        /// <summary>Shows the dialog. onStay is invoked once, and only once, if/when Stay is pressed — never called by Exit.</summary>
        public void Open(Action onStay)
        {
            _onStay = onStay;

            if (panel != null)
                panel.SetActive(true);
        }

        private void OnStayPressed()
        {
            if (panel != null)
                panel.SetActive(false);

            Action onStay = _onStay;
            _onStay = null;
            onStay?.Invoke();
        }

        private void OnExitPressed()
        {
            if (levelExitFlowController != null)
                levelExitFlowController.ConfirmExit();

            // Deliberately left open, and _onStay deliberately left intact:
            // ConfirmExit is currently a stub with nowhere to navigate to
            // (see its own remarks) — hiding this dialog here would silently
            // hide that nothing actually happened.
        }
    }
}
