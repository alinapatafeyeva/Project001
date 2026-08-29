using System;
using UnityEngine;

namespace Project001.Services.Economy
{
    /// <summary>
    /// The single authoritative owner of the player's coin balance. Plain
    /// C# (no MonoBehaviour dependency) so it is directly constructible and
    /// testable in EditMode without a scene — see CoinWalletService for the
    /// scene-owned host that gives this a lifetime and a concrete
    /// ICoinStorage.
    ///
    /// Represents PLAYER state, not level state: the same instance's
    /// balance persists across level changes, scene reloads, and (via
    /// ICoinStorage) application restarts. Never instantiate a second one
    /// per modal/level — CoinWalletService is the one place that owns this.
    ///
    /// Balance can only change through AddCoins/TrySpendCoins — there is no
    /// public setter, so callers cannot mutate the stored integer directly.
    /// Every successful change is persisted via the injected ICoinStorage
    /// immediately, and raises BalanceChanged for future HUD binding.
    /// </summary>
    public sealed class CoinWallet
    {
        private readonly ICoinStorage _storage;
        private int _balance;

        public CoinWallet(ICoinStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _balance = Mathf.Max(0, _storage.LoadBalance());
        }

        public int Balance => _balance;

        /// <summary>Raised after every successful AddCoins/TrySpendCoins, with the new balance. Not raised for a rejected (invalid-amount or insufficient-funds) operation.</summary>
        public event Action<int> BalanceChanged;

        /// <summary>
        /// Credits the wallet. amount must be positive — zero/negative
        /// amounts are rejected (logged, balance unchanged) rather than
        /// silently accepted or thrown, since callers here are gameplay/ad/
        /// IAP callbacks that must never be able to corrupt wallet state by
        /// passing a bad value.
        /// </summary>
        public void AddCoins(int amount, CoinTransactionReason reason)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"CoinWallet.AddCoins: ignoring non-positive amount ({amount}) for reason {reason}.");
                return;
            }

            _balance += amount;
            Persist();
            BalanceChanged?.Invoke(_balance);
        }

        /// <summary>
        /// Attempts to debit the wallet. Fails cleanly (returns false,
        /// balance unchanged) for a non-positive amount or when the balance
        /// is insufficient — the balance can never become negative through
        /// this method.
        /// </summary>
        public bool TrySpendCoins(int amount, CoinTransactionReason reason)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"CoinWallet.TrySpendCoins: ignoring non-positive amount ({amount}) for reason {reason}.");
                return false;
            }

            if (amount > _balance)
                return false;

            _balance -= amount;
            Persist();
            BalanceChanged?.Invoke(_balance);
            return true;
        }

        private void Persist()
        {
            _storage.SaveBalance(_balance);
        }
    }
}
