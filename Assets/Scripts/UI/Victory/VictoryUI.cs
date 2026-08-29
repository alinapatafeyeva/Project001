using Project001.Gameplay;
using Project001.Gameplay.Victory;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Victory
{
    /// <summary>
    /// Level Complete screen. Listens to VictoryController.OnVictory and owns
    /// only panel visibility and button presentation (Double Coins, Exit,
    /// Next Level — see reference/UI/LevelCompleteModalTarget.png).
    /// VictoryController has no reference to or knowledge of this class;
    /// this is the only direction the dependency runs.
    ///
    /// Next Level forwards to VictoryFlowController.LoadNextLevel (resume
    /// gameplay, then load the next level via LevelProgressionController) —
    /// this class never touches Time.timeScale, LevelProgressionController,
    /// or scene loading directly. Exit instead forwards to
    /// LevelExitFlowController.ConfirmExit — the same single Exit
    /// implementation the top HUD, Pause modal, and Level Failed modal
    /// already share, never a duplicate one. Double Coins (the rewarded-ad
    /// CTA) is currently a safe no-op — see OnDoubleCoinsPressed's own
    /// remarks — no ad SDK or coin economy exists yet.
    /// </summary>
    public class VictoryUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Controller whose OnVictory event this UI reacts to.")]
        private VictoryController victoryController;

        [SerializeField, Tooltip("Owns what Next Level actually does (resume gameplay, then load the next level) — this UI only forwards the button press to it and manages panel visibility.")]
        private VictoryFlowController victoryFlowController;

        [SerializeField, Tooltip("Sole owner of what a confirmed Exit actually does — shared with the top HUD's, Pause modal's, and Level Failed modal's own Exit entry points, never a duplicate implementation.")]
        private LevelExitFlowController levelExitFlowController;

        [SerializeField, Tooltip("Root panel object shown on victory and hidden again on Next Level. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Button pressed to dismiss the panel and load the next level.")]
        private Button nextLevelButton;

        [SerializeField, Tooltip("Forwards to LevelExitFlowController.ConfirmExit. Does not hide this panel — see OnExitPressed's own remarks.")]
        private Button exitButton;

        [SerializeField, Tooltip("Rewarded-ad CTA (DoubleCoinsButton.png). Currently a safe no-op placeholder — see OnDoubleCoinsPressed's own remarks; no ad SDK or coin economy exists yet.")]
        private Button doubleCoinsButton;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (victoryController != null)
                victoryController.OnVictory += Show;

            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(OnNextLevelPressed);

            if (exitButton != null)
                exitButton.onClick.AddListener(OnExitPressed);

            if (doubleCoinsButton != null)
                doubleCoinsButton.onClick.AddListener(OnDoubleCoinsPressed);
        }

        private void OnDestroy()
        {
            if (victoryController != null)
                victoryController.OnVictory -= Show;

            if (nextLevelButton != null)
                nextLevelButton.onClick.RemoveListener(OnNextLevelPressed);

            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExitPressed);

            if (doubleCoinsButton != null)
                doubleCoinsButton.onClick.RemoveListener(OnDoubleCoinsPressed);
        }

        private void Show()
        {
            if (panel != null)
                panel.SetActive(true);
        }

        private void OnNextLevelPressed()
        {
            Debug.Log("Next Level pressed");

            // No further cleanup guaranteed needed here: if a next level
            // loads, VictoryFlowController's scene reload destroys this
            // object before another frame renders. If there is no next
            // level, gameplay simply resumes and panel.SetActive(false)
            // below still correctly dismisses the panel.
            if (victoryFlowController != null)
                victoryFlowController.LoadNextLevel();

            if (panel != null)
                panel.SetActive(false);
        }

        private void OnExitPressed()
        {
            if (levelExitFlowController != null)
                levelExitFlowController.ConfirmExit();

            // Deliberately left open, exactly like ExitConfirmationUI.OnExitPressed/
            // FailureUI.OnExitLevelPressed: ConfirmExit is currently a stub with
            // nowhere to navigate to (see its own remarks) — hiding this panel
            // here would silently imply something happened.
        }

        /// <summary>
        /// Safe no-op placeholder: no ad SDK is integrated yet and no coin
        /// economy exists yet (see class remarks — this task is UI-only), so
        /// this deliberately does nothing beyond logging. Does not hide the
        /// panel, grant any reward, or touch VictoryFlowController/
        /// LevelExitFlowController — a future rewarded-ad implementation
        /// only needs to fill in this one method body (show the ad, then on
        /// completion apply the real reward) without any other change to
        /// this class or to BootstrapSceneCreator's own wiring of this
        /// button.
        /// </summary>
        private void OnDoubleCoinsPressed()
        {
            Debug.Log("Double Coins (Watch Ad) pressed — rewarded-ad flow not implemented yet.");
        }
    }
}
