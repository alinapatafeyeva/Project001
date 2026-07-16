using Project001.Gameplay;
using Project001.Gameplay.Victory;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Victory
{
    /// <summary>
    /// Prototype Victory screen. Listens to VictoryController.OnVictory and
    /// owns only panel visibility and Continue-button presentation.
    /// VictoryController has no reference to or knowledge of this class; this
    /// is the only direction the dependency runs.
    ///
    /// Does not itself pause or resume gameplay — that is
    /// GameplayFlowController's job (it reacts to the same OnVictory event
    /// independently), reached here only through its small, Victory-agnostic
    /// PauseGameplay/ResumeGameplay API so this class stays presentation-only
    /// and Failure UI can reuse the same API later without touching this one.
    ///
    /// Deliberately minimal: a centered panel, "Level Complete" text, and a
    /// single Continue button that only hides the panel and logs. Future
    /// additions (Restart, Next Level, coins, stars, x2 reward, ads,
    /// animations) belong here or in sibling components reacting to the same
    /// OnVictory event — none of them require any change to VictoryController.
    /// </summary>
    public class VictoryUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Controller whose OnVictory event this UI reacts to.")]
        private VictoryController victoryController;

        [SerializeField, Tooltip("Reached only via PauseGameplay/ResumeGameplay on Continue — this UI never manipulates gameplay state (e.g. Time.timeScale) directly.")]
        private GameplayFlowController gameplayFlowController;

        [SerializeField, Tooltip("Root panel object shown on victory and hidden again on Continue. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Button pressed to dismiss the panel. Does not yet load another level.")]
        private Button continueButton;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (victoryController != null)
                victoryController.OnVictory += Show;

            if (continueButton != null)
                continueButton.onClick.AddListener(OnContinuePressed);
        }

        private void OnDestroy()
        {
            if (victoryController != null)
                victoryController.OnVictory -= Show;

            if (continueButton != null)
                continueButton.onClick.RemoveListener(OnContinuePressed);
        }

        private void Show()
        {
            if (panel != null)
                panel.SetActive(true);
        }

        private void OnContinuePressed()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();

            if (panel != null)
                panel.SetActive(false);

            Debug.Log("Continue pressed");
        }
    }
}
