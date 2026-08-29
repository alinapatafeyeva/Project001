using System;
using UnityEngine;

namespace Project001.Services.Economy
{
    /// <summary>
    /// Scene-owned host that gives CoinWallet a lifetime, matching this
    /// project's existing composition pattern (BootstrapSceneCreator creates
    /// one plain MonoBehaviour per service/controller, wired by reference —
    /// see VictoryController, FailureController, GameplayFlowController).
    ///
    /// Deliberately not DontDestroyOnLoad and not a singleton: the wallet
    /// represents player state, but the state of record lives in
    /// PlayerPrefs (via PlayerPrefsCoinStorage), not in this instance. A
    /// scene reload (e.g. LevelProgressionController.LoadNextLevel)
    /// recreates this object fresh, and Wallet's lazy getter reloads the
    /// persisted balance the first time anything asks for it — the same way
    /// LevelProgressionController survives scene reloads through a
    /// mechanism other than DontDestroyOnLoad (see its own remarks), just
    /// backed by real persistence here instead of a static field, since a
    /// coin balance must also survive an application restart.
    ///
    /// Wallet is lazily created (not in Awake) so any caller's execution
    /// order — this component's own Awake, or another component's Awake
    /// that runs first — reaches the same, already-loaded instance rather
    /// than risking a null reference if Unity happens to call the other
    /// component's Awake first.
    /// </summary>
    public class CoinWalletService : MonoBehaviour
    {
        private CoinWallet _wallet;

        /// <summary>The single authoritative CoinWallet instance for this play session, backed by PlayerPrefsCoinStorage. Created on first access.</summary>
        public CoinWallet Wallet => _wallet ??= new CoinWallet(new PlayerPrefsCoinStorage());

        public int Balance => Wallet.Balance;

        public event Action<int> BalanceChanged
        {
            add => Wallet.BalanceChanged += value;
            remove => Wallet.BalanceChanged -= value;
        }

        public void AddCoins(int amount, CoinTransactionReason reason) => Wallet.AddCoins(amount, reason);

        public bool TrySpendCoins(int amount, CoinTransactionReason reason) => Wallet.TrySpendCoins(amount, reason);
    }
}
