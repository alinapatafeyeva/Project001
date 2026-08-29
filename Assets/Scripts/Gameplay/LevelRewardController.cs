using System;
using Project001.Gameplay.Victory;
using Project001.Services.Economy;
using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns the coin reward for the level currently being played: awards
    /// EconomyConfig.BaseLevelCoinReward exactly once when VictoryController
    /// raises OnVictory, and exposes OnRewardedAdCompleted as the one
    /// integration point a future rewarded-ad provider calls after an ad
    /// actually finishes successfully. All idempotency/duplicate-prevention
    /// logic lives in the LevelCompletionReward instance this class owns —
    /// this class is only the thin bridge between VictoryController's event
    /// and that domain object, the same "flow controller reached only
    /// through its own API" convention VictoryFlowController/
    /// LevelExitFlowController already use for their own gameplay systems.
    ///
    /// The reward belongs to the completion event, not to opening the
    /// Victory UI — VictoryUI only ever reads CurrentLevelReward/subscribes
    /// to LevelCompleted/RewardChanged to display a number; it never grants
    /// anything itself, so reopening or rebuilding that UI can never
    /// duplicate a reward.
    ///
    /// Ordering fix (was the cause of the first-completion "You earned: 0"
    /// bug): VictoryUI used to subscribe to victoryController.OnVictory
    /// directly, in parallel with this class's own HandleVictory subscriber
    /// to the very same event. Unity does not guarantee the relative Awake/
    /// subscription order of two independent components, so on some loads
    /// VictoryUI's Show() ran (and read CurrentLevelReward) BEFORE
    /// HandleVictory had granted the reward — reading a stale 0. That was
    /// compounded by RewardChanged's old add/remove accessors proxying
    /// through a private _reward field that was only assigned in Awake:
    /// subscribing to it before this component's own Awake had run silently
    /// dropped the subscription for the rest of the scene's lifetime, so
    /// nothing ever corrected the stale display afterward either.
    ///
    /// Both are fixed the same way: Reward is now a lazily-initialized
    /// property (mirroring CoinWalletService.Wallet's own ??= pattern)
    /// rather than an Awake-assigned field, so it is never null the moment
    /// anything touches it — subscribe-before-Awake can no longer drop a
    /// subscription. And VictoryUI no longer listens to
    /// victoryController.OnVictory at all; it listens to this class's own
    /// LevelCompleted event instead, which HandleVictory only raises AFTER
    /// GrantBaseReward() has already returned — the correct order is now
    /// guaranteed by the call stack itself (LevelCompleted physically cannot
    /// fire before the grant happens, regardless of which component's Awake
    /// ran first), not by hoping Unity picks a favourable Awake/subscription
    /// order.
    /// </summary>
    public class LevelRewardController : MonoBehaviour
    {
        [SerializeField, Tooltip("Raises OnVictory exactly once per completion — this controller's sole trigger for granting the base reward.")]
        private VictoryController victoryController;

        [SerializeField, Tooltip("Sole owner of the player's persistent coin balance — reached only through its own API, never bypassed.")]
        private CoinWalletService coinWalletService;

        private LevelCompletionReward _reward;

        /// <summary>
        /// Lazily creates the LevelCompletionReward for this completion on
        /// first access rather than in Awake, so any caller — regardless of
        /// whether its own Awake ran before or after this component's —
        /// always reaches a valid instance instead of racing Awake order.
        /// coinWalletService itself is a SerializeField already populated
        /// from scene data before any Awake runs, so accessing
        /// coinWalletService.Wallet here is always safe.
        /// </summary>
        private LevelCompletionReward Reward
        {
            get
            {
                if (_reward == null && coinWalletService != null)
                    _reward = new LevelCompletionReward(coinWalletService.Wallet);

                return _reward;
            }
        }

        /// <summary>This level's current reward total (base, or base+double after a successful rewarded ad) — never the wallet's total balance. 0 before OnVictory has fired.</summary>
        public int CurrentLevelReward => Reward?.TotalReward ?? 0;

        /// <summary>True only after the base reward has been granted and before the double reward has been granted for this completion.</summary>
        public bool CanGrantDoubleReward => Reward != null && Reward.CanGrantDoubleReward;

        /// <summary>Raised after CurrentLevelReward actually changes — suitable for VictoryUI to update its displayed amount live (e.g. after a successful rewarded ad). Safe to subscribe at any time, including before this component's own Awake has run.</summary>
        public event Action<int> RewardChanged
        {
            add { if (Reward != null) Reward.RewardChanged += value; }
            remove { if (Reward != null) Reward.RewardChanged -= value; }
        }

        /// <summary>
        /// Raised once per completion, strictly after the base reward has
        /// already been granted (see HandleVictory) — the signal VictoryUI
        /// should show itself on, instead of victoryController.OnVictory
        /// directly, so CurrentLevelReward is always already correct by the
        /// time VictoryUI reads it.
        /// </summary>
        public event Action LevelCompleted;

        private void Awake()
        {
            if (victoryController != null)
                victoryController.OnVictory += HandleVictory;
        }

        private void OnDestroy()
        {
            if (victoryController != null)
                victoryController.OnVictory -= HandleVictory;
        }

        private void HandleVictory()
        {
            Reward?.GrantBaseReward();
            LevelCompleted?.Invoke();
        }

        /// <summary>
        /// Call only from a rewarded-ad provider's own "ad successfully
        /// completed" callback — never from "ad requested"/"ad started", and
        /// never directly from a button's onClick (see VictoryUI.
        /// OnDoubleCoinsPressed's own remarks: that handler represents the
        /// player's request only). Safe to call more than once for the same
        /// completion — only the first call after the base reward grants
        /// anything; see LevelCompletionReward.GrantDoubleReward.
        /// </summary>
        public void OnRewardedAdCompleted()
        {
            Reward?.GrantDoubleReward();
        }
    }
}
