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
    /// Does not itself decide what Continue does — that is
    /// VictoryFlowController's job (resume gameplay, then load the next
    /// level via LevelProgressionController). This class only forwards the
    /// button press to it and manages panel visibility; it never touches
    /// Time.timeScale, LevelProgressionController, or scene loading directly.
    ///
    /// Deliberately minimal: a centered panel, "Level Complete" text, and a
    /// single Continue button. Future additions (Restart, coins, stars, x2
    /// reward, ads, animations) belong here or in sibling components
    /// reacting to the same OnVictory event — none of them require any
    /// change to VictoryController.
    /// </summary>
    public class VictoryUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Controller whose OnVictory event this UI reacts to.")]
        private VictoryController victoryController;

        [SerializeField, Tooltip("Owns what Continue actually does (resume gameplay, then load the next level) — this UI only forwards the button press to it and manages panel visibility.")]
        private VictoryFlowController victoryFlowController;

        [SerializeField, Tooltip("Root panel object shown on victory and hidden again on Continue. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Button pressed to dismiss the panel and load the next level.")]
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
            Debug.Log("Continue pressed");

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
    }
}
