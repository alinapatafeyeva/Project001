using Project001.Services.Economy;

namespace Project001.Tests.EditMode
{
    /// <summary>
    /// In-memory ICoinStorage for tests — never touches real PlayerPrefs, so
    /// tests stay isolated from each other and from a developer's actual
    /// saved balance. Sharing one instance between two CoinWallet instances
    /// simulates "the app restarted and reloaded from storage" without any
    /// real device I/O.
    /// </summary>
    internal sealed class FakeCoinStorage : ICoinStorage
    {
        private int _savedBalance;

        public int LoadBalance() => _savedBalance;

        public void SaveBalance(int balance) => _savedBalance = balance;
    }
}
