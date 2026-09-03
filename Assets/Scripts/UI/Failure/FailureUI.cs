using Project001.Gameplay;
using Project001.Gameplay.Failure;
using Project001.Services.Economy;
using Project001.UI.Store;
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
    /// saveMeButton ("Save me!") spends EconomyConfig.RecoveryLinePrice
    /// through CoinWalletService.TrySpendCoins (CoinTransactionReason.
    /// RecoveryLinePurchase) and, only if that succeeds, applies the
    /// Recovery Line rescue. "Recovery Line" is not a separate,
    /// newly-invented mechanic: this project's ONLY existing
    /// post-Failure gameplay rescue is FailureRecoveryController.
    /// ContinueCurrentLevel (transfer every Conveyor rider into the
    /// Recovery Row, rearm Failure detection, resume gameplay) — the same
    /// method continueButton already calls for the free, ad-gated path.
    /// No other "Recovery Line" implementation exists anywhere in this
    /// project's current code, docs, or git history (searched before
    /// writing this) — "Recovery Line" only ever appears as this modal's
    /// own product/UI copy, never as a distinct gameplay system. Save me!
    /// is therefore treated as a second, PAID entry point to the exact
    /// same rescue Continue already performs, not a different mechanic —
    /// see this class's own OnSaveMePressed remarks for the one
    /// interpretive assumption this rests on.
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

        [SerializeField, Tooltip("The 'Save me!' Recovery Line CTA — spends EconomyConfig.RecoveryLinePrice through coinWalletService, then applies the same rescue continueButton does.")]
        private Button saveMeButton;

        [SerializeField, Tooltip("Forwards to LevelExitFlowController.ConfirmExit. Does not hide this panel — see OnExitLevelPressed's own remarks.")]
        private Button exitLevelButton;

        [SerializeField, Tooltip("The single authoritative coin wallet — saveMeButton only ever spends through TrySpendCoins here, never a second balance/store of its own.")]
        private CoinWalletService coinWalletService;

        [SerializeField, Tooltip("Placeholder coin-store shell opened when saveMeButton is pressed with an insufficient balance.")]
        private CoinStoreUI coinStoreUI;

        // Guards saveMeButton against a double-click/repeated-activation
        // firing TrySpendCoins twice for what the player experiences as one
        // press — see OnSaveMePressed's own remarks. Reset in Show(), the
        // start of every new failure occurrence this UI could possibly be
        // pressed for again.
        private bool _isSaveMeProcessing;

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

            // A fresh failure occurrence — any lock left over from a
            // previous one (there shouldn't be one; this is defensive, not
            // load-bearing, since a successful Save me already closes this
            // panel and an insufficient-funds one already clears the lock
            // itself) must not carry over.
            _isSaveMeProcessing = false;

            if (saveMeButton != null)
                saveMeButton.interactable = true;
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
        /// CASE A (balance &lt; EconomyConfig.RecoveryLinePrice): TrySpendCoins
        /// itself rejects the spend — CoinWallet guarantees the balance is
        /// left completely unchanged (see its own remarks) — so nothing
        /// here needs to "undo" a charge; this class just opens coinStoreUI
        /// as the insufficient-coins signal and leaves this panel exactly
        /// as it was (Continue/Exit level both stay available).
        ///
        /// CASE B (balance &gt;= price): spend first, THEN apply the rescue —
        /// never the other way around, per this task's own explicit "do not
        /// apply Recovery Line first and then attempt payment" ordering
        /// requirement. ContinueCurrentLevel (see this class's own remarks
        /// on why that method IS "Recovery Line" here) is a synchronous,
        /// null-guarded void call with no observable failure mode of its
        /// own — the same trust OnContinuePressed already places in it —
        /// so there is no realistic way to reach "charged but not rescued"
        /// once TrySpendCoins has returned true.
        ///
        /// _isSaveMeProcessing + disabling the button's own Interactable
        /// together guard against a double-click firing this twice before
        /// either outcome above takes effect (panel closing in the success
        /// case; Unity simply won't route a second click to a
        /// non-interactable Button, so a same-frame duplicate press can
        /// never reach a second TrySpendCoins call).
        /// </summary>
        private void OnSaveMePressed()
        {
            if (_isSaveMeProcessing)
                return;

            _isSaveMeProcessing = true;
            if (saveMeButton != null)
                saveMeButton.interactable = false;

            bool spent = coinWalletService != null
                && coinWalletService.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            if (!spent)
            {
                _isSaveMeProcessing = false;
                if (saveMeButton != null)
                    saveMeButton.interactable = true;

                if (coinStoreUI != null)
                    coinStoreUI.Open();

                return;
            }

            if (failureRecoveryController != null)
                failureRecoveryController.ContinueCurrentLevel();

            if (panel != null)
                panel.SetActive(false);
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
