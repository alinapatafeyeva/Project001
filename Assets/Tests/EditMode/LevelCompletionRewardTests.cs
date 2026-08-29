using NUnit.Framework;
using Project001.Services.Economy;

namespace Project001.Tests.EditMode
{
    /// <summary>Pure domain tests for LevelCompletionReward — no scene, no MonoBehaviour, no VictoryController.</summary>
    public class LevelCompletionRewardTests
    {
        [Test]
        public void GrantBaseReward_AwardsBaseLevelCoinReward_Once()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);

            reward.GrantBaseReward();

            Assert.AreEqual(EconomyConfig.BaseLevelCoinReward, wallet.Balance);
        }

        [Test]
        public void BaseLevelCoinReward_ComesFromEconomyConfig()
        {
            Assert.AreEqual(100, EconomyConfig.BaseLevelCoinReward);
        }

        [Test]
        public void GrantBaseReward_SetsTotalRewardTo100_ForNormalCompletion()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);

            reward.GrantBaseReward();

            Assert.AreEqual(100, reward.TotalReward);
        }

        [Test]
        public void GrantDoubleReward_AfterBaseReward_AddsExactlyAnother100()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);
            reward.GrantBaseReward();

            reward.GrantDoubleReward();

            Assert.AreEqual(200, wallet.Balance);
        }

        [Test]
        public void GrantDoubleReward_AfterBaseReward_SetsTotalRewardTo200()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);
            reward.GrantBaseReward();

            reward.GrantDoubleReward();

            Assert.AreEqual(200, reward.TotalReward);
        }

        [Test]
        public void RepeatedGrantDoubleReward_CannotAwardAdditionalCoins()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);
            reward.GrantBaseReward();

            reward.GrantDoubleReward();
            reward.GrantDoubleReward();
            reward.GrantDoubleReward();

            Assert.AreEqual(200, wallet.Balance);
            Assert.AreEqual(200, reward.TotalReward);
        }

        [Test]
        public void RepeatedGrantBaseReward_CannotAwardTheBaseRewardTwice()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);

            reward.GrantBaseReward();
            reward.GrantBaseReward();
            reward.GrantBaseReward();

            Assert.AreEqual(EconomyConfig.BaseLevelCoinReward, wallet.Balance);
            Assert.AreEqual(EconomyConfig.BaseLevelCoinReward, reward.TotalReward);
        }

        [Test]
        public void GrantDoubleReward_BeforeBaseReward_IsRejectedSafely()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);

            reward.GrantDoubleReward();

            Assert.AreEqual(0, wallet.Balance);
            Assert.AreEqual(0, reward.TotalReward);
        }

        [Test]
        public void CanGrantDoubleReward_IsFalse_UntilBaseRewardGranted()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);

            Assert.IsFalse(reward.CanGrantDoubleReward);

            reward.GrantBaseReward();

            Assert.IsTrue(reward.CanGrantDoubleReward);

            reward.GrantDoubleReward();

            Assert.IsFalse(reward.CanGrantDoubleReward);
        }

        [Test]
        public void RewardChanged_FiresWithTotalReward_OnBaseAndDoubleGrant()
        {
            var wallet = new CoinWallet(new FakeCoinStorage());
            var reward = new LevelCompletionReward(wallet);
            int? lastValue = null;
            reward.RewardChanged += value => lastValue = value;

            reward.GrantBaseReward();
            Assert.AreEqual(100, lastValue);

            reward.GrantDoubleReward();
            Assert.AreEqual(200, lastValue);
        }
    }
}
