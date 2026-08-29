using System;

namespace Project001.Services.Economy
{
    /// <summary>
    /// Tracks the coin reward for exactly ONE level completion — created
    /// fresh per completion by LevelRewardController, never reused across
    /// two different victories. Owns the "has the base reward already been
    /// granted for this completion" / "has the double-reward already been
    /// granted for this completion" guards, so the reward is tied to the
    /// completion event itself rather than to how many times something
    /// downstream (a repeated callback, a rebuilt Victory UI, a
    /// double-clicked button) asks for it.
    ///
    /// Pure domain logic — no MonoBehaviour, no VictoryController
    /// knowledge, no ad SDK knowledge. LevelRewardController is the thin
    /// MonoBehaviour that owns one of these per completion and connects it
    /// to VictoryController.OnVictory and to a future rewarded-ad
    /// provider's success callback.
    /// </summary>
    public sealed class LevelCompletionReward
    {
        private readonly CoinWallet _wallet;
        private bool _baseRewardGranted;
        private bool _doubleRewardGranted;

        public LevelCompletionReward(CoinWallet wallet)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        /// <summary>This completion's reward so far — EconomyConfig.BaseLevelCoinReward once the base reward has been granted, doubled again once the rewarded-ad bonus has also been granted. This level's reward only, never the wallet's total balance.</summary>
        public int TotalReward { get; private set; }

        /// <summary>True only between a granted base reward and a granted double reward — the one window in which GrantDoubleReward can still succeed.</summary>
        public bool CanGrantDoubleReward => _baseRewardGranted && !_doubleRewardGranted;

        /// <summary>Raised after TotalReward actually changes (base grant or double grant) — never for a rejected repeat call.</summary>
        public event Action<int> RewardChanged;

        /// <summary>
        /// Grants EconomyConfig.BaseLevelCoinReward exactly once for this
        /// completion. A second call (a repeated OnVictory callback, a
        /// rebuilt Victory UI re-invoking this, etc.) is a safe no-op — the
        /// reward belongs to the completion this instance represents, not
        /// to how many times this method gets called for it.
        /// </summary>
        public void GrantBaseReward()
        {
            if (_baseRewardGranted)
                return;

            _baseRewardGranted = true;
            TotalReward += EconomyConfig.BaseLevelCoinReward;
            _wallet.AddCoins(EconomyConfig.BaseLevelCoinReward, CoinTransactionReason.LevelReward);
            RewardChanged?.Invoke(TotalReward);
        }

        /// <summary>
        /// Grants exactly one additional EconomyConfig.BaseLevelCoinReward
        /// for this completion's rewarded-ad bonus. The integration point a
        /// future rewarded-ad provider calls after the ad has actually
        /// finished successfully — never on "ad requested" or "ad started".
        /// A no-op if the base reward has not been granted yet, or if the
        /// double reward has already been granted for this completion —
        /// idempotent regardless of how many times a duplicate success
        /// callback fires.
        /// </summary>
        public void GrantDoubleReward()
        {
            if (!CanGrantDoubleReward)
                return;

            _doubleRewardGranted = true;
            TotalReward += EconomyConfig.BaseLevelCoinReward;
            _wallet.AddCoins(EconomyConfig.BaseLevelCoinReward, CoinTransactionReason.RewardedAd);
            RewardChanged?.Invoke(TotalReward);
        }
    }
}
