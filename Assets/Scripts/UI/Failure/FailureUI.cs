using Project001.Gameplay;
using Project001.Gameplay.Failure;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Failure
{
    /// <summary>
    /// Level Failed screen. Listens to FailureController.OnFailure and owns
    /// only panel visibility and button presentation (Continue, Exit level —
    /// see reference/UI/LevelFailedModalTarget.png). FailureController has no
    /// reference to or knowledge of this class; this is the only direction
    /// the dependency runs.
    ///
    /// Continue forwards to FailureRecoveryController, the gameplay-layer
    /// owner of what it actually does (resuming the same level state); Exit
    /// level instead forwards to LevelExitFlowController.ConfirmExit — the
    /// same single Exit implementation the top HUD and Pause modal already
    /// share, never a duplicate one. This class never touches Time.timeScale,
    /// FailureController, or scene loading directly.
    ///
    /// retryButton is intentionally not wired by the current scene (see
    /// BootstrapSceneCreator.CreateFailureUI): the polished modal's MVP scope
    /// has no Retry action (Recovery Line rescue / Continue / Exit level only —
    /// see reference/UI/LevelFailedModalTargetNEW.png), but the field/method
    /// stay here, null-checked exactly like every other optional button on
    /// this class, so restoring Retry later needs no code change here, only
    /// wiring a button back in the scene. Continue is still a free prototype
    /// hook for a later monetized flow — no ad SDK or coins are wired up yet
    /// (see BootstrapSceneCreator's own remarks on the ad-row placeholder).
    ///
    /// saveMeButton (the new "Save me!" CTA replacing the old booster row)
    /// is deliberately presentation-only for the same reason retryButton is
    /// unwired-but-present: the Recovery Line mechanic it would trigger
    /// (how many times it can be bought per level, what happens to the
    /// collector currently blocked at the full Waiting Line, whether
    /// capacity persists, how it debits CoinWalletService) is not designed
    /// yet, and this class must never invent gameplay rules to make a
    /// button merely look functional. OnSaveMePressed exists as the one
    /// obvious attachment point for that future controller — mirroring
    /// OnRetryPressed's own placeholder Debug.Log before RetryCurrentLevel
    /// existed — rather than leaving the click unhandled.
    /// </summary>
    public class FailureUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Controller whose OnFailure event this UI reacts to.")]
        private FailureController failureController;

        [SerializeField, Tooltip("Owns what Retry/Continue actually do (full restart vs. resume) — this UI only forwards button presses to it and manages panel visibility.")]
        private FailureRecoveryController failureRecoveryController;

        [SerializeField, Tooltip("Sole owner of what a confirmed Exit actually does — shared with the top HUD's and Pause modal's own Exit entry points, never a duplicate implementation.")]
        private LevelExitFlowController levelExitFlowController;

        [SerializeField, Tooltip("Root panel object shown on failure and hidden again on Retry/Continue. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Restarts the current level completely from its initial state. Not wired in the current scene — see class remarks.")]
        private Button retryButton;

        [SerializeField, Tooltip("Resumes the same existing level state, after rearming Failure detection.")]
        private Button continueButton;

        [SerializeField, Tooltip("The new 'Save me!' Recovery Line CTA. Presentation-only for now — see this class's own remarks for why no gameplay behavior is attached yet.")]
        private Button saveMeButton;

        [SerializeField, Tooltip("Forwards to LevelExitFlowController.ConfirmExit. Does not hide this panel — see OnExitLevelPressed's own remarks.")]
        private Button exitLevelButton;

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

            if (saveMeButton != null)
                saveMeButton.onClick.AddListener(OnSaveMePressed);

            if (exitLevelButton != null)
                exitLevelButton.onClick.AddListener(OnExitLevelPressed);
        }

        private void OnDestroy()
        {
            if (failureController != null)
                failureController.OnFailure -= Show;

            if (retryButton != null)
                retryButton.onClick.RemoveListener(OnRetryPressed);

            if (continueButton != null)
                continueButton.onClick.RemoveListener(OnContinuePressed);

            if (saveMeButton != null)
                saveMeButton.onClick.RemoveListener(OnSaveMePressed);

            if (exitLevelButton != null)
                exitLevelButton.onClick.RemoveListener(OnExitLevelPressed);
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

        /// <summary>
        /// Placeholder only — see this class's own remarks on saveMeButton
        /// for why. Does not touch coins, the Waiting Line, or Failure
        /// state: the Recovery Line mechanic itself is not designed yet, so
        /// there is nothing real to call here. Left as the obvious call
        /// site for whatever RecoveryLine*Controller is eventually added,
        /// exactly as OnRetryPressed once stood in for RetryCurrentLevel.
        /// </summary>
        private void OnSaveMePressed()
        {
            Debug.Log("Save me pressed (Recovery Line not implemented yet)");
        }

        private void OnExitLevelPressed()
        {
            if (levelExitFlowController != null)
                levelExitFlowController.ConfirmExit();

            // Deliberately left open, exactly like ExitConfirmationUI.OnExitPressed:
            // ConfirmExit is currently a stub with nowhere to navigate to (see
            // its own remarks) — hiding this panel here would silently imply
            // something happened.
        }
    }
}
