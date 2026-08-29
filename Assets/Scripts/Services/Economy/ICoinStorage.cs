namespace Project001.Services.Economy
{
    /// <summary>
    /// Persistence abstraction for a single player's coin balance.
    /// CoinWallet depends on this interface, never on a concrete storage
    /// mechanism, so swapping local storage (see PlayerPrefsCoinStorage) for
    /// account/cloud-backed storage later is a new ICoinStorage
    /// implementation wired in where CoinWallet is constructed — not a
    /// CoinWallet or UI change.
    /// </summary>
    public interface ICoinStorage
    {
        /// <summary>The last saved balance, or 0 if none has ever been saved.</summary>
        int LoadBalance();

        /// <summary>Persists the given balance as the new saved value.</summary>
        void SaveBalance(int balance);
    }
}
