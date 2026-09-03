namespace Project001.Services.Economy
{
    /// <summary>
    /// Why a CoinWallet balance changed — semantic information passed
    /// through CoinWallet.AddCoins/TrySpendCoins for future analytics, not a
    /// transaction history/database of its own (none exists yet).
    ///
    /// IapPurchase and BoosterPurchase are not used by anything yet — they
    /// exist now so CoinWallet's public API does not need to change when a
    /// future IAP service or booster system starts calling it.
    /// </summary>
    public enum CoinTransactionReason
    {
        /// <summary>Base reward for successfully completing a level (see EconomyConfig.BaseLevelCoinReward).</summary>
        LevelReward,

        /// <summary>Bonus reward granted after a rewarded ad successfully completes (e.g. the Level Complete modal's "x2 Coins" action).</summary>
        RewardedAd,

        /// <summary>Coins credited after a verified real-money purchase. No IAP integration exists yet — this is reserved for a future IAP service to use through CoinWallet's existing API.</summary>
        IapPurchase,

        /// <summary>Coins spent purchasing a booster. No booster system exists yet — this is reserved for a future booster system to use through CoinWallet's existing API.</summary>
        BoosterPurchase,

        /// <summary>Coins spent on the Level Failed modal's "Save me!" action — the paid alternative to the free rewarded-ad Continue, both ultimately invoking the same FailureRecoveryController.ContinueCurrentLevel rescue (see EconomyConfig.RecoveryLinePrice).</summary>
        RecoveryLinePurchase,

        /// <summary>Balance adjusted by Editor-only manual-verification tooling (see CoinEconomyDebugTools) — never a production gameplay/IAP/ad path. Exists so a debug reset can still go through CoinWallet's normal AddCoins/TrySpendCoins API (never bypassing it) without mislabelling the change as one of the real economic reasons above.</summary>
        DebugAdjustment
    }
}
