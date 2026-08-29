using UnityEngine;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Enforces "Coin.png, then a fixed gap, then the number" as a single
    /// sequential horizontal relationship, computed from CoinIcon's own
    /// ACTUAL resolved world-space bounds — never by combining raw
    /// anchoredPosition.x values from coinRectTransform and
    /// numberRectTransform directly.
    ///
    /// That second mistake is exactly what made the "structurally
    /// guaranteed" v1 of this class still overlap in the actual Game View:
    /// CoinIcon is anchored/pivoted at its parent's CENTRE (0.5, 0.5), so
    /// its anchoredPosition.x is a distance measured from the parent's
    /// centre. The digit row is anchored/pivoted at its parent's LEFT EDGE
    /// (0, 0.5), so ITS anchoredPosition.x is a distance measured from the
    /// parent's left edge instead. Those are two different reference
    /// points on the very same parent rect, offset from each other by
    /// exactly parent.rect.width / 2 — but v1 took "coin right edge,
    /// measured from parent centre" and assigned it straight into
    /// numberRectTransform.anchoredPosition.x as if it were already
    /// "measured from parent's left edge". For this HUD group (pivoted at
    /// its OWN parent's top-right corner, width 256), that silently shifted
    /// the whole digit row 128px too far left — squarely on top of the
    /// coin. The bug was invisible to RectTransform-math review because
    /// each individual formula "looked" locally correct; it only breaks
    /// once you compare two anchoredPosition.x values that don't share a
    /// reference point.
    ///
    /// This version never combines anchoredPosition values directly. It
    /// reads CoinIcon's actual resolved WORLD corners (GetWorldCorners,
    /// the same bounds Unity itself uses to render/hit-test), converts the
    /// coin's right edge into the shared parent's own local space via
    /// InverseTransformPoint, and only then derives numberRectTransform's
    /// anchoredPosition.x from ITS OWN anchor reference point on that same
    /// parent rect. This is correct regardless of either object's own
    /// pivot/anchor convention or the parent's own pivot — it does not
    /// assume the parent is centre-pivoted, or that coin and number share
    /// the same anchor reference point, the way the previous version
    /// implicitly (and wrongly) did.
    /// </summary>
    public class CoinBalanceLayout : MonoBehaviour
    {
        [SerializeField, Tooltip("Coin.png's own RectTransform — its actual current world-space right edge (not a guessed/nominal size, and not its raw anchoredPosition.x) is what the number is positioned relative to.")]
        private RectTransform coinRectTransform;

        [SerializeField, Tooltip("The digit row's own RectTransform (SpriteDigitNumberDisplay) — repositioned to start immediately after the coin. Must be left-pivoted (anchorMin=anchorMax=pivot=0,0.5) since Reposition treats its pivot point as its left edge.")]
        private RectTransform numberRectTransform;

        [SerializeField, Tooltip("Gap between Coin.png's actual right edge and the first digit's left edge — a named, positive, always-enforced separation, not an incidental side effect of two independently-computed positions.")]
        private float coinToNumberGap = 12f;

        private readonly Vector3[] _coinWorldCorners = new Vector3[4];

        private void Awake()
        {
            Reposition();
        }

        /// <summary>
        /// Positions numberRectTransform so its own left edge sits exactly
        /// coinToNumberGap to the right of coinRectTransform's own current
        /// actual right edge, with both edges first converted into the
        /// same coordinate space (the shared parent's local space) before
        /// being compared or combined. Coin.png's size never changes at
        /// runtime today, so calling this once in Awake is sufficient —
        /// but nothing here assumes that will always be true, so this
        /// stays safe to call again if it ever does (see
        /// BootstrapSceneCreator, which also calls this once at edit time
        /// so the baked scene position and the runtime position can never
        /// diverge into two different formulas again).
        /// </summary>
        public void Reposition()
        {
            if (coinRectTransform == null || numberRectTransform == null)
                return;

            var parent = numberRectTransform.parent as RectTransform;
            if (parent == null)
                return;

            // GetWorldCorners is Unity's own authoritative resolved bounds
            // (order: bottom-left, top-left, top-right, bottom-right) — not
            // a re-derivation from anchoredPosition/pivot/sizeDelta that
            // could repeat the same mistake in a new form.
            coinRectTransform.GetWorldCorners(_coinWorldCorners);
            float coinRightEdgeWorldX = _coinWorldCorners[2].x;

            // Convert into the shared parent's own local space — the one
            // coordinate system both coinRectTransform's and
            // numberRectTransform's anchoredPosition are ultimately
            // expressed against, regardless of their own individual
            // anchors/pivots.
            float coinRightEdgeLocalX = parent.InverseTransformPoint(new Vector3(coinRightEdgeWorldX, 0f, 0f)).x;
            float desiredLeftEdgeLocalX = coinRightEdgeLocalX + coinToNumberGap;

            // numberRectTransform's own anchor reference point on that same
            // parent rect (never assumed to be the parent's centre, or the
            // parent's rect.xMin under an assumed pivot).
            float numberAnchorRefX = Mathf.Lerp(parent.rect.xMin, parent.rect.xMax, numberRectTransform.anchorMin.x);

            // numberRectTransform is left-pivoted, so its pivot point (the
            // point anchoredPosition actually places) IS its own left edge
            // — no further pivot-width correction needed here.
            Vector2 anchoredPosition = numberRectTransform.anchoredPosition;
            anchoredPosition.x = desiredLeftEdgeLocalX - numberAnchorRefX;
            numberRectTransform.anchoredPosition = anchoredPosition;
        }
    }
}
