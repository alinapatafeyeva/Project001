using Project001.Services.Economy;
using UnityEngine;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Top gameplay HUD's Coin+Amount group: a pure, presentation-only
    /// display of CoinWalletService.Balance — the same authoritative wallet
    /// every other economy script reads/writes, never a second stored
    /// balance and never a direct PlayerPrefs read. Shows the TOTAL wallet
    /// balance, not any single level's reward — that distinction belongs to
    /// LevelRewardController/VictoryUI's "You earned" display, which this
    /// class has no knowledge of.
    ///
    /// Reads coinWalletService.Balance once immediately in Awake, so a
    /// balance already restored from local persistence (see
    /// PlayerPrefsCoinStorage) displays correctly the instant gameplay
    /// starts, without waiting for the first transaction. After that it
    /// stays in sync purely by subscribing to CoinWalletService.
    /// BalanceChanged — no per-frame polling. Rendering itself (digit
    /// decomposition, thousands-group spacing, natural per-digit aspect
    /// ratios) is delegated to SpriteDigitNumberDisplay — this view only
    /// ever passes it the raw integer balance.
    /// </summary>
    public class CoinBalanceHudView : MonoBehaviour
    {
        [SerializeField, Tooltip("The player's persistent coin wallet — read-only from here; this view never calls AddCoins/TrySpendCoins.")]
        private CoinWalletService coinWalletService;

        [SerializeField, Tooltip("Renders the balance as a row of digit sprites. Knows nothing about the wallet/economy — this view is the only thing that calls SetValue on it.")]
        private SpriteDigitNumberDisplay spriteDigitNumberDisplay;

        private void Awake()
        {
            if (coinWalletService != null)
            {
                coinWalletService.BalanceChanged += DisplayBalance;
                DisplayBalance(coinWalletService.Balance);
            }
        }

        private void OnDestroy()
        {
            if (coinWalletService != null)
                coinWalletService.BalanceChanged -= DisplayBalance;
        }

        private void DisplayBalance(int balance)
        {
            if (spriteDigitNumberDisplay != null)
                spriteDigitNumberDisplay.SetValue(balance);
        }
    }
}
