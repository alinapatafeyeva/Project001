using Project001.Gameplay;
using Project001.Gameplay.Failure;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Failure
{
    /// <summary>
    /// Prototype Failure screen. Listens to FailureController.OnFailure and
    /// owns only panel visibility and Retry/Continue-button presentation.
    /// FailureController has no reference to or knowledge of this class; this
    /// is the only direction the dependency runs.
    ///
    /// Retry and Continue have different behavior (full level restart vs.
    /// resuming the same level state), but this class does not decide either
    /// — it only forwards each button press to FailureRecoveryController,
    /// the gameplay-layer owner of what they actually do. This class never
    /// touches Time.timeScale, FailureController, or scene loading directly.
    ///
    /// Deliberately minimal: a centered panel, "Level Failed" text, a Retry
    /// button, and a Continue button. Continue is a free prototype hook for
    /// a later monetized flow — no coins, ads, or rewards here yet.
    /// </summary>
    public class FailureUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Controller whose OnFailure event this UI reacts to.")]
        private FailureController failureController;

        [SerializeField, Tooltip("Owns what Retry/Continue actually do (full restart vs. resume) — this UI only forwards button presses to it and manages panel visibility.")]
        private FailureRecoveryController failureRecoveryController;

        [SerializeField, Tooltip("Root panel object shown on failure and hidden again on Retry/Continue. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Restarts the current level completely from its initial state.")]
        private Button retryButton;

        [SerializeField, Tooltip("Resumes the same existing level state, after rearming Failure detection.")]
        private Button continueButton;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (failureController != null)
                failureController.OnFailure += Show;

            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryPressed);

            if (continueButton != null)
                continueButton.onClick.AddListener(OnContinuePressed);
        }

        private void OnDestroy()
        {
            if (failureController != null)
                failureController.OnFailure -= Show;

            if (retryButton != null)
                retryButton.onClick.RemoveListener(OnRetryPressed);

            if (continueButton != null)
                continueButton.onClick.RemoveListener(OnContinuePressed);
        }

        private void Show()
        {
            if (panel != null)
                panel.SetActive(true);
        }

        private void OnRetryPressed()
        {
            Debug.Log("Retry pressed");

            // No panel/local cleanup needed here: RetryCurrentLevel reloads
            // the scene, which destroys this object (and everything else)
            // before another frame renders.
            if (failureRecoveryController != null)
                failureRecoveryController.RetryCurrentLevel();
        }

        private void OnContinuePressed()
        {
            if (failureRecoveryController != null)
                failureRecoveryController.ContinueCurrentLevel();

            if (panel != null)
                panel.SetActive(false);

            Debug.Log("Continue after failure pressed");
        }
    }
}
