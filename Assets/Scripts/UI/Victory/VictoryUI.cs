using Project001.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Victory
{
    /// <summary>
    /// Level Complete screen. Listens to LevelRewardController.LevelCompleted
    /// (not VictoryController.OnVictory directly — see that event's own
    /// remarks for why) and owns only panel visibility and button
    /// presentation (Double Coins, Exit, Next Level — see
    /// reference/UI/LevelCompleteModalTarget.png). VictoryController has no
    /// reference to or knowledge of this class; this is the only direction
    /// the dependency runs.
    ///
    /// Next Level forwards to VictoryFlowController.LoadNextLevel (resume
    /// gameplay, then load the next level via LevelProgressionController) —
    /// this class never touches Time.timeScale, LevelProgressionController,
    /// or scene loading directly. Exit instead forwards to
    /// LevelExitFlowController.ConfirmExit — the same single Exit
    /// implementation the top HUD, Pause modal, and Level Failed modal
    /// already share, never a duplicate one. Double Coins (the rewarded-ad
    /// CTA) represents the player's request in a production build — see
    /// OnDoubleCoinsPressed's own remarks; no ad SDK is integrated yet. In
    /// the Unity Editor only, that same handler also simulates the ad
    /// succeeding by calling levelRewardController.OnRewardedAdCompleted()
    /// — the real production integration point, not a separate bypass —
    /// so this class still never grants coins itself in either case.
    ///
    /// The displayed reward amount and its granting both belong to
    /// LevelRewardController, reached only through CurrentLevelReward/
    /// LevelCompleted/RewardChanged — this class never calls
    /// CoinWalletService or EconomyConfig directly, and never grants the
    /// base or double reward itself, so reopening/rebuilding this UI can
    /// never duplicate a reward.
    /// </summary>
    public class VictoryUI : MonoBehaviour
    {
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

        [SerializeField, Tooltip("Rewarded-ad CTA (DoubleCoinsButton.png). Currently a safe no-op placeholder — see OnDoubleCoinsPressed's own remarks; no ad SDK integrated yet.")]
        private Button doubleCoinsButton;

        [SerializeField, Tooltip("Owns this completion's coin reward (grants it exactly once, exposes the rewarded-ad double-reward integration point) — this UI only ever reads/displays it, never grants anything itself.")]
        private LevelRewardController levelRewardController;

        [SerializeField, Tooltip("EarnedCoinsValue — displays this completion's current reward (see LevelRewardController.CurrentLevelReward), never the wallet's total balance. Content only; layout/artwork untouched.")]
        private TextMeshProUGUI earnedCoinsValueText;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (levelRewardController != null)
            {
                levelRewardController.LevelCompleted += Show;
                levelRewardController.RewardChanged += UpdateEarnedCoinsDisplay;
            }

            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(OnNextLevelPressed);

            if (exitButton != null)
                exitButton.onClick.AddListener(OnExitPressed);

            if (doubleCoinsButton != null)
                doubleCoinsButton.onClick.AddListener(OnDoubleCoinsPressed);
        }

        private void OnDestroy()
        {
            if (levelRewardController != null)
            {
                levelRewardController.LevelCompleted -= Show;
                levelRewardController.RewardChanged -= UpdateEarnedCoinsDisplay;
            }

            if (nextLevelButton != null)
                nextLevelButton.onClick.RemoveListener(OnNextLevelPressed);

            if (exitButton != null)
                exitButton.onClick.RemoveListener(OnExitPressed);

            if (doubleCoinsButton != null)
                doubleCoinsButton.onClick.RemoveListener(OnDoubleCoinsPressed);
        }

        /// <summary>
        /// Subscribed to LevelRewardController.LevelCompleted, never to
        /// VictoryController.OnVictory directly — LevelCompleted is only
        /// raised after GrantBaseReward() has already returned (see
        /// LevelRewardController.HandleVictory), so CurrentLevelReward is
        /// guaranteed correct here regardless of Awake/subscription order.
        /// </summary>
        private void Show()
        {
            if (panel != null)
                panel.SetActive(true);

            if (levelRewardController != null)
                UpdateEarnedCoinsDisplay(levelRewardController.CurrentLevelReward);
        }

        private void UpdateEarnedCoinsDisplay(int amount)
        {
            if (earnedCoinsValueText != null)
                earnedCoinsValueText.text = amount.ToString();
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
        /// Represents "the player requested the rewarded ad" — in a
        /// production build, nothing more than that: no ad SDK is
        /// integrated yet, so this does nothing beyond logging, exactly
        /// like before. It never hides the panel or touches
        /// VictoryFlowController/LevelExitFlowController either way.
        ///
        /// Inside the Unity Editor only (#if UNITY_EDITOR), this also
        /// simulates the ad having succeeded, so the full x2 flow can be
        /// manually verified without a real ad SDK — by calling
        /// levelRewardController.OnRewardedAdCompleted(), the exact same
        /// integration point a future real rewarded-ad provider's own
        /// "ad succeeded" callback will call. This method still never calls
        /// CoinWalletService/CoinWallet/EconomyConfig directly — the grant
        /// and its idempotency guard remain entirely LevelRewardController/
        /// LevelCompletionReward's responsibility. The #if is compiled out
        /// of any non-Editor build, so a production build can never
        /// auto-grant the double reward just from this button press —
        /// pressing it there still only logs a request, same as before.
        /// </summary>
        private void OnDoubleCoinsPressed()
        {
#if UNITY_EDITOR
            Debug.Log("Double Coins (Watch Ad) pressed — UNITY_EDITOR: simulating a successful rewarded ad for manual verification (no real ad SDK integrated yet).");

            if (levelRewardController != null)
                levelRewardController.OnRewardedAdCompleted();
#else
            Debug.Log("Double Coins (Watch Ad) pressed — no rewarded-ad SDK integrated yet; nothing is granted from this click.");
#endif
        }
    }
}
