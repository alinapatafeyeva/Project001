using NUnit.Framework;
using Project001.Services.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace Project001.Tests.EditMode
{
    /// <summary>Pure domain tests for CoinWallet — no scene, no MonoBehaviour, no real PlayerPrefs I/O (see FakeCoinStorage).</summary>
    public class CoinWalletTests
    {
        [Test]
        public void NewWallet_WithEmptyStorage_StartsAtZero()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());

            Assert.AreEqual(0, wallet.Balance);
        }

        [Test]
        public void AddCoins_LevelReward_IncreasesBalanceByAmount()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());

            wallet.AddCoins(100, CoinTransactionReason.LevelReward);

            Assert.AreEqual(100, wallet.Balance);
        }

        [Test]
        public void Balance_PersistsAndReloads_ThroughSharedStorage()
        {
            var storage = new FakeCoinStorage();
            var firstSession = new CoinWallet(storage);
            firstSession.AddCoins(250, CoinTransactionReason.LevelReward);

            // A second CoinWallet over the same storage simulates the app
            // restarting and reloading the persisted balance.
            var secondSession = new CoinWallet(storage);

            Assert.AreEqual(250, secondSession.Balance);
        }

        [Test]
        public void PlayerPrefsCoinStorage_SavesAndReloadsBalance()
        {
            var storage = new PlayerPrefsCoinStorage();
            int originalValue = storage.LoadBalance();

            try
            {
                storage.SaveBalance(777);
                var reloaded = new PlayerPrefsCoinStorage();

                Assert.AreEqual(777, reloaded.LoadBalance());
            }
            finally
            {
                storage.SaveBalance(originalValue);
            }
        }

        [Test]
        public void TrySpendCoins_SucceedsWhenBalanceIsSufficient()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(500, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(300, CoinTransactionReason.BoosterPurchase);

            Assert.IsTrue(result);
        }

        [Test]
        public void TrySpendCoins_SubtractsExactAmount()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(500, CoinTransactionReason.LevelReward);

            wallet.TrySpendCoins(300, CoinTransactionReason.BoosterPurchase);

            Assert.AreEqual(200, wallet.Balance);
        }

        [Test]
        public void TrySpendCoins_FailsWhenBalanceIsInsufficient()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(100, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(101, CoinTransactionReason.BoosterPurchase);

            Assert.IsFalse(result);
        }

        [Test]
        public void FailedSpend_DoesNotModifyBalance()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(100, CoinTransactionReason.LevelReward);

            wallet.TrySpendCoins(101, CoinTransactionReason.BoosterPurchase);

            Assert.AreEqual(100, wallet.Balance);
        }

        [Test]
        public void Balance_NeverBecomesNegative()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(50, CoinTransactionReason.LevelReward);

            wallet.TrySpendCoins(1000, CoinTransactionReason.BoosterPurchase);
            wallet.TrySpendCoins(int.MaxValue, CoinTransactionReason.BoosterPurchase);

            Assert.GreaterOrEqual(wallet.Balance, 0);
        }

        [Test]
        public void AddCoins_NonPositiveAmount_IsRejectedSafely()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*"));
            wallet.AddCoins(0, CoinTransactionReason.LevelReward);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*"));
            wallet.AddCoins(-10, CoinTransactionReason.LevelReward);

            Assert.AreEqual(0, wallet.Balance);
        }

        [Test]
        public void AddCoins_SupportsIapPurchase_AsAValidReason()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());

            wallet.AddCoins(500, CoinTransactionReason.IapPurchase);

            Assert.AreEqual(500, wallet.Balance);
        }

        [Test]
        public void TrySpendCoins_SupportsBoosterPurchase_AsAValidReason()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            wallet.AddCoins(1500, CoinTransactionReason.LevelReward);

            bool result = wallet.TrySpendCoins(1200, CoinTransactionReason.BoosterPurchase);

            Assert.IsTrue(result);
            Assert.AreEqual(300, wallet.Balance);
        }
    }
}
