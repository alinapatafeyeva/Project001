using UnityEngine;

namespace Project001.Services.Economy
{
    /// <summary>
    /// MVP local persistence for the player's coin balance, via
    /// PlayerPrefs — the simplest reliable option available for a
    /// single-device, no-accounts-yet MVP (no login, no cloud/backend exists
    /// yet, see ICoinStorage's own remarks). One fixed key: this project has
    /// exactly one player and one wallet per device, never multiple
    /// profiles.
    ///
    /// Deliberately not the wallet itself — CoinWallet depends on
    /// ICoinStorage, not on this class, so this can be replaced by an
    /// account/cloud-backed ICoinStorage implementation later without
    /// touching CoinWallet.
    /// </summary>
    public sealed class PlayerPrefsCoinStorage : ICoinStorage
    {
        private const string BalanceKey = "Project001.CoinWallet.Balance";

        public int LoadBalance()
        {
            return Mathf.Max(0, PlayerPrefs.GetInt(BalanceKey, 0));
        }

        public void SaveBalance(int balance)
        {
            PlayerPrefs.SetInt(BalanceKey, Mathf.Max(0, balance));
            PlayerPrefs.Save();
        }
    }
}
