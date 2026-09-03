using Project001.Services.Economy;
using UnityEngine;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Anchors the whole Coin+Balance assembly (CoinIcon, then a fixed gap,
    /// then the digit row) as ONE right-anchored unit, structurally derived
    /// from the RIGHT HUD group's own other members rather than a hand-tuned
    /// offset from the canvas corner:
    ///
    ///   digitRow.right = SpeedButton.left - coinToSpeedGap
    ///   coin.left       &gt;= LevelDisplay.right + minLevelToCoinGap
    ///
    /// so a wider balance (more digits) grows the assembly LEFTWARD from a
    /// stable right-side boundary near Speed/Pause, and can never encroach
    /// past a guaranteed-visible gap to Level — replacing an earlier version
    /// that instead anchored this whole group at a fixed, hand-picked offset
    /// from the canvas's own top-right corner (HudCoinGroupOffsetX) sized to
    /// assume a worst-case content width: every time that assumption was
    /// wrong (an unaccounted-for digit-sprite trim margin, a wider worst-case
    /// value than the one actually checked), the only lever was to shift the
    /// fixed offset further left — which, by construction, moves the whole
    /// group TOWARD Level, eventually crowding it. Deriving both edges from
    /// the ACTUAL resolved bounds of SpeedButton and LevelDisplay (never
    /// their assumed/nominal positions) makes that entire class of bug
    /// impossible: this can only ever be as wrong as SpeedButton's or
    /// LevelDisplay's own real positions are.
    ///
    /// Recomputed on two triggers, never just once in Awake:
    /// coinWalletService.BalanceChanged (the digit row's own rendered width
    /// changes with the balance) and topHudUI.LevelDisplayBuilt (Level's own
    /// digits are built at runtime in TopHudUI.Start, so its real right edge
    /// does not exist yet in Awake). Subscribing to both in THIS component's
    /// own Awake is safe regardless of these components' relative Awake/Start
    /// order: Unity runs every object's Awake before any object's Start, so
    /// by the time LevelDisplayBuilt can possibly fire (from a Start), this
    /// subscription and CoinBalanceHudView's own initial SetValue call (also
    /// unconditionally inside an Awake) have both already happened.
    /// </summary>
    public class CoinBalanceLayout : MonoBehaviour
    {
        [SerializeField, Tooltip("Coin.png's own RectTransform — its actual current world-space bounds (not a guessed/nominal size) anchor the digit row's own left edge.")]
        private RectTransform coinRectTransform;

        [SerializeField, Tooltip("The digit row's own RectTransform (SpriteDigitNumberDisplay) — its actual rendered width (via GetWorldCorners, so any last-resort shrink is already accounted for) determines how far left this assembly must grow. Must be left-pivoted (anchorMin=anchorMax=pivot=0,0.5).")]
        private RectTransform numberRectTransform;

        [SerializeField, Tooltip("SpeedButton's own RectTransform — this assembly's right edge is pinned coinToSpeedGap to its left, never a fixed offset from the canvas corner.")]
        private RectTransform speedButtonRectTransform;

        [SerializeField, Tooltip("Built the LEFT HUD group's Level display and exposes its resolved right edge (LevelDisplayRightEdgeWorldX) + LevelDisplayBuilt event — the other boundary this assembly's left edge is clamped against.")]
        private TopHudUI topHudUI;

        [SerializeField, Tooltip("Read-only here — only used to know exactly when the digit row's own rendered width has changed (its BalanceChanged event), never to read the balance itself.")]
        private CoinWalletService coinWalletService;

        [SerializeField, Tooltip("Gap between Coin.png's actual right edge and the first digit's left edge — a named, positive, always-enforced internal separation.")]
        private float coinToNumberGap = 6f;

        [SerializeField, Tooltip("Gap enforced between this assembly's own rightmost extent (the digit row's right edge) and SpeedButton's own actual left edge — this assembly's right-side anchor.")]
        private float coinToSpeedGap = 16f;

        [SerializeField, Tooltip("Minimum guaranteed gap between LevelDisplay's own actual right edge and this assembly's own leftmost extent (CoinIcon's left edge) — a hard floor Reposition never lets the assembly cross, however wide the current balance's digit row is. Chosen to read as a clearly visible breathing gap at 1080 width, not a near-zero algebraic positive.")]
        private float minLevelToCoinGap = 40f;

        private readonly Vector3[] _worldCorners = new Vector3[4];

        private void Awake()
        {
            if (topHudUI != null)
                topHudUI.LevelDisplayBuilt += Reposition;

            if (coinWalletService != null)
                coinWalletService.BalanceChanged += OnBalanceChanged;

            Reposition();
        }

        private void OnDestroy()
        {
            if (topHudUI != null)
                topHudUI.LevelDisplayBuilt -= Reposition;

            if (coinWalletService != null)
                coinWalletService.BalanceChanged -= OnBalanceChanged;
        }

        private void OnBalanceChanged(int _) => Reposition();

        /// <summary>
        /// Recomputes the whole assembly's position from scratch, in world
        /// space throughout (GetWorldCorners / Transform.position — never
        /// combining two RectTransforms' own anchoredPosition values
        /// directly, which is exactly what made an earlier version of this
        /// class silently overlap despite "looking" correct locally — see
        /// class remarks) so it is correct regardless of any object's own
        /// anchor/pivot convention:
        ///
        ///   1. digitRow.right = SpeedButton.left - coinToSpeedGap
        ///   2. digitRow.left  = digitRow.right - digitRow's own actual rendered width
        ///   3. coin.right     = digitRow.left - coinToNumberGap
        ///   4. coin.left      = coin.right - coin's own actual rendered width
        ///   5. if coin.left &lt; LevelDisplay.right + minLevelToCoinGap: shift
        ///      the WHOLE assembly (both coin and digitRow) right by the
        ///      shortfall, preserving the internal coinToNumberGap — i.e.
        ///      the Level floor wins over the nominal Speed gap, never the
        ///      other way round, exactly per this task's own priority order.
        ///
        /// Safe to call before topHudUI has built LevelDisplay yet
        /// (LevelDisplayRightEdgeWorldX simply reads 0 until then, which
        /// only makes the Level clamp permissive for that one intermediate
        /// call — always superseded once LevelDisplayBuilt actually fires).
        /// </summary>
        public void Reposition()
        {
            if (coinRectTransform == null || numberRectTransform == null || speedButtonRectTransform == null)
                return;

            speedButtonRectTransform.GetWorldCorners(_worldCorners);
            float speedLeftWorldX = _worldCorners[0].x;

            numberRectTransform.GetWorldCorners(_worldCorners);
            float digitRowWidth = _worldCorners[2].x - _worldCorners[1].x;

            coinRectTransform.GetWorldCorners(_worldCorners);
            float coinWidth = _worldCorners[2].x - _worldCorners[1].x;

            float digitRowRightX = speedLeftWorldX - coinToSpeedGap;
            float digitRowLeftX = digitRowRightX - digitRowWidth;
            float coinRightX = digitRowLeftX - coinToNumberGap;
            float coinLeftX = coinRightX - coinWidth;

            float levelRightX = topHudUI != null ? topHudUI.LevelDisplayRightEdgeWorldX : 0f;
            float minCoinLeftX = levelRightX + minLevelToCoinGap;
            if (coinLeftX < minCoinLeftX)
            {
                float shortfall = minCoinLeftX - coinLeftX;
                coinLeftX += shortfall;
                digitRowLeftX += shortfall;
            }

            Vector3 coinPos = coinRectTransform.position;
            coinPos.x = coinLeftX + coinWidth * 0.5f;
            coinRectTransform.position = coinPos;

            Vector3 numberPos = numberRectTransform.position;
            numberPos.x = digitRowLeftX;
            numberRectTransform.position = numberPos;
        }
    }
}
