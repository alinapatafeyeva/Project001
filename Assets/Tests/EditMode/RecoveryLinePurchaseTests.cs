using NUnit.Framework;
using Project001.Services.Economy;

namespace Project001.Tests.EditMode
{
    /// <summary>
    /// Pure domain tests for the Level Failed modal's "Save me!" purchase —
    /// no scene, no MonoBehaviour, no FailureUI (see CoinWalletTests' own
    /// remarks for why this project's economy tests stay at this level).
    /// FailureUI.OnSaveMePressed is a thin orchestration layer over exactly
    /// the CoinWallet.TrySpendCoins call these tests exercise directly, at
    /// EconomyConfig.RecoveryLinePrice — the real coin-safety guarantees
    /// (never double-spend, never go negative, persist correctly) live in
    /// CoinWallet itself, which is what these tests actually verify.
    /// </summary>
    public class RecoveryLinePurchaseTests
    {
        [Test]
        public void RecoveryLinePrice_Is1000()
        {
            Assert.AreEqual(1000, EconomyConfig.RecoveryLinePrice);
        }

        [Test]
        public void TrySpendCoins_SupportsRecoveryLinePurchase_AsAValidReason()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.IsTrue(result);
        }

        // ----- Test case A: balance = 999 -----

        [Test]
        public void BalanceBelowPrice_SaveMeSpend_Fails()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(999, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.IsFalse(result);
        }

        [Test]
        public void BalanceBelowPrice_SaveMeSpend_LeavesBalanceUnchanged()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(999, CoinTransactionReason.LevelReward);

            wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.AreEqual(999, wallet.Balance);
        }

        // ----- Test case B: balance = 1000 -----

        [Test]
        public void BalanceExactlyPrice_SaveMeSpend_Succeeds()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(1000, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.IsTrue(result);
        }

        [Test]
        public void BalanceExactlyPrice_SaveMeSpend_LeavesBalanceAtZero()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(1000, CoinTransactionReason.LevelReward);

            wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.AreEqual(0, wallet.Balance);
        }

        // ----- Test case C: balance = 1500 -----

        [Test]
        public void BalanceAbovePrice_SaveMeSpend_LeavesExpectedRemainder()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(1500, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.IsTrue(result);
            Assert.AreEqual(500, wallet.Balance);
        }

        // ----- Test case D: double-click / repeated callback -----

        [Test]
        public void RepeatedSpendCallAfterSuccess_CannotChargeTwice()
        {
            // Simulates two SaveMe-press callbacks reaching CoinWallet back
            // to back (the scenario FailureUI's own _isSaveMeProcessing
            // guard is meant to prevent from ever happening at the UI layer)
            // — even if both calls somehow reached the wallet, the SECOND
            // TrySpendCoins call must fail on its own, since the first
            // already consumed the exact balance needed.
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(1000, CoinTransactionReason.LevelReward);

            bool first = wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);
            bool second = wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(0, wallet.Balance);
        }

        // ----- Test case E: insufficient funds, repeated press -----

        [Test]
        public void RepeatedInsufficientSpend_NeverGoesNegative()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(500, CoinTransactionReason.LevelReward);

            wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);
            wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);
            wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.AreEqual(500, wallet.Balance);
            Assert.GreaterOrEqual(wallet.Balance, 0);
        }

        // ----- Test case F: successful rescue persists through the existing storage -----

        [Test]
        public void SuccessfulSaveMeSpend_PersistsThroughSharedStorage()
        {
            var storage = new FakeCoinStorage();
            var firstSession = new CoinWallet(storage);
            firstSession.AddCoins(1500, CoinTransactionReason.LevelReward);
            firstSession.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            // A second CoinWallet over the same storage simulates the app
            // restarting and reloading the persisted balance (see
            // CoinWalletTests.Balance_PersistsAndReloads_ThroughSharedStorage).
            var secondSession = new CoinWallet(storage);

            Assert.AreEqual(500, secondSession.Balance);
        }

        [Test]
        public void SuccessfulSaveMeSpend_RaisesBalanceChanged_WithNewBalance()
        {
            // The HUD's own CoinBalanceHudView updates purely by subscribing
            // to this event (see its own remarks) — this confirms the event
            // fires with the correct post-spend value, the same mechanism
            // the HUD relies on, without needing a HUD/scene of our own.
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(1500, CoinTransactionReason.LevelReward);
            int? lastBalance = null;
            wallet.BalanceChanged += value => lastBalance = value;

            wallet.TrySpendCoins(EconomyConfig.RecoveryLinePrice, CoinTransactionReason.RecoveryLinePurchase);

            Assert.AreEqual(500, lastBalance);
        }
    }
}
