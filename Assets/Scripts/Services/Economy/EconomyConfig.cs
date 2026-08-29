namespace Project001.Services.Economy
{
    /// <summary>
    /// Centralized, tunable coin-economy values — the single place gameplay/
    /// UI code reads reward and (eventually) price numbers from, mirroring
    /// how GameplayConstants centralizes global gameplay rules (see its own
    /// remarks). Nothing outside this class should hardcode a literal
    /// economy value.
    ///
    /// Booster prices (expected to land somewhere around 1000-1500 coins)
    /// are not added yet — no booster system exists to consume them (see
    /// CoinTransactionReason.BoosterPurchase's own remarks). When that
    /// system is built, its price(s) belong here as additional consts (or,
    /// if per-booster-type pricing is needed, a small
    /// booster-id-to-price lookup here) rather than inline in booster code —
    /// a plain const class scales to that without needing a different shape
    /// now.
    /// </summary>
    public static class EconomyConfig
    {
        /// <summary>Coins awarded for successfully completing a level, before any rewarded-ad bonus (see CoinTransactionReason.LevelReward). Doubled by exactly one successful rewarded ad — see CoinTransactionReason.RewardedAd.</summary>
        public const int BaseLevelCoinReward = 100;
    }
}
