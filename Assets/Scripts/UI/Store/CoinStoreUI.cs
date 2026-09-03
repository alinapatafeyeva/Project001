using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Store
{
    /// <summary>
    /// Placeholder shell for the future coin store: open, a dimmed
    /// full-screen backdrop consistent with every other modal in this
    /// project (see CreateModalBackdrop), "Not enough coins" placeholder
    /// content, and a Close button. Deliberately as small as
    /// ExitConfirmationUI — owns only panel visibility, nothing else. No
    /// purchase cards, no prices, no IAP SDK — this exists only so the
    /// Level Failed modal's insufficient-coins path has somewhere real to
    /// send the player, per this task's own explicit "shell/navigation
    /// state only" scope. Safe/easy to extend or replace entirely once the
    /// real store is designed — nothing else in the project depends on this
    /// class beyond calling Open()/knowing it exists.
    /// </summary>
    public class CoinStoreUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Root panel (full-screen backdrop) shown while this modal is open and hidden again on Close. Starts inactive.")]
        private GameObject panel;

        [SerializeField, Tooltip("Closes the modal. The only action this placeholder shell has.")]
        private Button closeButton;

        private void Awake()
        {
            if (panel != null)
                panel.SetActive(false);

            if (closeButton != null)
                closeButton.onClick.AddListener(OnClosePressed);
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(OnClosePressed);
        }

        /// <summary>Shows the modal.</summary>
        public void Open()
        {
            if (panel != null)
                panel.SetActive(true);
        }

        private void OnClosePressed()
        {
            if (panel != null)
                panel.SetActive(false);
        }
    }
}
