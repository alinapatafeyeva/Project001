#if UNITY_EDITOR
using Project001.Services.Economy;
using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Manual Play Mode verification for the coin economy. The entire file
    /// is wrapped in #if UNITY_EDITOR: a MonoBehaviour living under
    /// Assets/Scripts/Editor/ cannot be attached to a scene GameObject at
    /// all ("Can't add script behaviour '...' because it is an editor
    /// script" — confirmed directly, not assumed), so this is the other
    /// option the task allows — compiled only under UNITY_EDITOR — and it
    /// strips this class out of a Player build entirely, the same
    /// guarantee an Editor-only folder would have given.
    ///
    /// Exists only because the gameplay HUD does not display coins yet (a
    /// separate future task) and no real rewarded-ad SDK is integrated yet,
    /// so there is currently no other way to inspect or exercise the wallet
    /// from Play Mode.
    ///
    /// "Simulate Rewarded Ad Success" deliberately calls
    /// LevelRewardController.OnRewardedAdCompleted() — the exact same
    /// integration point a real rewarded-ad provider's success callback
    /// would call — rather than adding coins to CoinWalletService directly,
    /// so this genuinely exercises the production x2 idempotency path
    /// instead of bypassing it. "Reset Coin Balance To Zero" still goes
    /// through CoinWalletService.TrySpendCoins (never a direct field/
    /// PlayerPrefs write) tagged with CoinTransactionReason.DebugAdjustment,
    /// so the wallet's own invariants (never negative, always persisted)
    /// stay enforced even for this debug action.
    ///
    /// Wired up by BootstrapSceneCreator.CreateCoinEconomyDebugTools onto
    /// its own GameObject in the generated Bootstrap.unity, referencing the
    /// same CoinWalletService/LevelRewardController every other gameplay/UI
    /// script uses; it never creates a second wallet or reward controller
    /// of its own.
    /// </summary>
    public class CoinEconomyDebugTools : MonoBehaviour
    {
        [SerializeField, Tooltip("The player's persistent coin wallet — the same instance every other gameplay/UI script uses.")]
        private CoinWalletService coinWalletService;

        [SerializeField, Tooltip("Owns the currently-completed level's reward. \"Simulate Rewarded Ad Success\" calls its OnRewardedAdCompleted() — never CoinWalletService directly.")]
        private LevelRewardController levelRewardController;

        [SerializeField, Tooltip("Read-only. Play Mode only: the wallet's current balance, kept up to date via CoinWalletService.BalanceChanged. Not editable, not used by any gameplay code — see CoinEconomyDebugToolsEditor for the disabled/read-only Inspector display of this field.")]
        private int currentCoinBalance;

        [SerializeField, Tooltip("Play Mode only: the balance ApplyCoinBalance() will set the wallet to. Editable target, separate from the read-only Current Coin Balance display above — see CoinEconomyDebugToolsEditor.")]
        [Min(0)]
        private int targetCoinBalance;

        /// <summary>The value CoinEconomyDebugToolsEditor displays as "Current Coin Balance" — kept in sync via CoinWalletService.BalanceChanged, never written to any wallet itself.</summary>
        public int CurrentCoinBalance => currentCoinBalance;

        private void OnEnable()
        {
            if (coinWalletService != null)
            {
                coinWalletService.BalanceChanged += RefreshDisplayedBalance;
                currentCoinBalance = coinWalletService.Balance;
            }
        }

        private void OnDisable()
        {
            if (coinWalletService != null)
                coinWalletService.BalanceChanged -= RefreshDisplayedBalance;
        }

        private void RefreshDisplayedBalance(int balance)
        {
            currentCoinBalance = balance;
        }

        [ContextMenu("1) Log Current Coin Balance")]
        public void LogCurrentCoinBalance()
        {
            if (!ValidateWalletAssigned())
                return;

            Debug.Log($"CoinEconomyDebugTools: current wallet balance = {coinWalletService.Balance}");
        }

        [ContextMenu("2) Reset Coin Balance To Zero")]
        public void ResetCoinBalanceToZero()
        {
            if (!ValidateWalletAssigned())
                return;

            int balanceBefore = coinWalletService.Balance;
            if (balanceBefore > 0)
                coinWalletService.TrySpendCoins(balanceBefore, CoinTransactionReason.DebugAdjustment);

            Debug.Log($"CoinEconomyDebugTools: wallet balance reset from {balanceBefore} to {coinWalletService.Balance}.");
        }

        [ContextMenu("3) Simulate Rewarded Ad SUCCESS (current level)")]
        public void SimulateRewardedAdSuccess()
        {
            if (levelRewardController == null)
            {
                Debug.LogWarning("CoinEconomyDebugTools: levelRewardController is not assigned.");
                return;
            }

            levelRewardController.OnRewardedAdCompleted();

            int? balance = coinWalletService != null ? coinWalletService.Balance : (int?)null;
            Debug.Log($"CoinEconomyDebugTools: OnRewardedAdCompleted() invoked. CurrentLevelReward = {levelRewardController.CurrentLevelReward}, wallet balance = {(balance.HasValue ? balance.Value.ToString() : "unknown (coinWalletService not assigned)")}.");
        }

        /// <summary>
        /// Sets the wallet's real balance (and, through it, PlayerPrefs-backed
        /// persistence — see CoinWalletService.Wallet) to targetCoinBalance,
        /// clamped to never below 0. Deliberately goes through only the
        /// wallet's own existing public API — AddCoins for the difference if
        /// the target is higher, TrySpendCoins for the difference if lower —
        /// exactly the same two calls ResetCoinBalanceToZero already uses for
        /// its own single case (target 0), rather than adding any new
        /// exact-set method to CoinWallet itself: CoinWallet's own class
        /// remarks are explicit that "there is no public setter, so callers
        /// cannot mutate the stored integer directly", and this preserves
        /// that invariant completely — every one of CoinWallet's own
        /// guarantees (never negative, always persisted, BalanceChanged
        /// raised) still comes from CoinWallet itself, not bypassed here.
        /// </summary>
        [ContextMenu("4) Apply Coin Balance")]
        public void ApplyCoinBalance()
        {
            if (!ValidateWalletAssigned())
                return;

            int target = Mathf.Max(0, targetCoinBalance);
            int current = coinWalletService.Balance;

            if (target > current)
                coinWalletService.AddCoins(target - current, CoinTransactionReason.DebugAdjustment);
            else if (target < current)
                coinWalletService.TrySpendCoins(current - target, CoinTransactionReason.DebugAdjustment);

            Debug.Log($"CoinEconomyDebugTools: balance set to {coinWalletService.Balance} (requested {target}).");
        }

        private bool ValidateWalletAssigned()
        {
            if (coinWalletService != null)
                return true;

            Debug.LogWarning("CoinEconomyDebugTools: coinWalletService is not assigned.");
            return false;
        }
    }
}
#endif
