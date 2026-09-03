using Project001.UI.Digits;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Renders a non-negative integer as a row of individually-sized digit
    /// sprites (Digit0.png-Digit9.png) — purely a number renderer, with no
    /// knowledge of CoinWallet, rewards, ads, purchases, or boosters (see
    /// CoinBalanceHudView for the economy-facing wiring that calls
    /// SetValue). All decomposition/positioning math is delegated to
    /// DigitLayout, a plain class with no Unity dependency (see its own
    /// remarks) — this component only turns that math into actual Image
    /// children.
    ///
    /// Each digit keeps its own native aspect ratio at the shared
    /// digitHeight (never stretched to a common width — see DigitLayout).
    ///
    /// Fitting a long balance (e.g. a 5-digit value) into maxWidth follows a
    /// strict progressive order — consume spacing before ever touching size,
    /// so an ordinary balance never looks "shrunk to fit":
    ///   1. Coin.png's own size is never touched by this component at all
    ///      (a separate sibling — see CoinBalanceLayout).
    ///   2. Try the row at full digitHeight/digitGap/groupGap first. If it
    ///      already fits within maxWidth (every balance up to 4 digits
    ///      today), that is exactly what renders — zero shrink, unchanged
    ///      from before this fitting order existed.
    ///   3. Otherwise retry at digitGapFloor/groupGapFloor instead — a
    ///      real, meaningful width saving with digitHeight still completely
    ///      untouched.
    ///   4. Still not enough, AND the value has 5+ digits (10000+)? Retry
    ///      once more at digitGapFloor2/groupGapFloor2 — a second, tighter
    ///      spacing floor, digitHeight STILL untouched. This is what keeps a
    ///      5-digit balance's digits close to full size: without this step,
    ///      the entire 5-digit shortfall had to be absorbed by step 6's
    ///      scale-down alone, which is what previously made 10000/99999
    ///      read as noticeably miniature next to 900/1000. Gated on digit
    ///      count specifically (not just "still doesn't fit") so this never
    ///      changes a 3- or 4-digit balance's own rendering, which already
    ///      looked correct and is deliberately left exactly as it was.
    ///   5. The gap between Coin.png and this row is reduced separately, by
    ///      a small fixed amount — see CoinBalanceLayout's own remarks.
    ///   6. Only if the row still exceeds maxWidth even at digitGapFloor2/
    ///      groupGapFloor2 is the whole row scaled down uniformly via this
    ///      component's own RectTransform.localScale, and only down to
    ///      MinScale at most. Anchored at this object's own left edge, so a
    ///      long balance shrinks away from Level/Speed-Pause rather than
    ///      growing into them.
    /// </summary>
    public class SpriteDigitNumberDisplay : MonoBehaviour
    {
        [SerializeField, Tooltip("Digit0.png-Digit9.png, indexed by digit value (digitSprites[3] is the '3' sprite).")]
        private Sprite[] digitSprites;

        [SerializeField, Tooltip("Shared displayed height for every digit at 1:1 scale (before any maxWidth shrink) — each digit's own displayed width follows from its native aspect ratio at this height.")]
        private float digitHeight = 66f;

        [SerializeField, Tooltip("Gap between adjacent digits within the same thousands group, used whenever the row fits maxWidth at this spacing.")]
        private float digitGap = 4f;

        [SerializeField, Tooltip("Additional gap (on top of digitGap) at a thousands-group boundary — represents the grouping separator as spacing, since no comma/space sprite exists.")]
        private float groupGap = 10f;

        [SerializeField, Tooltip("Tighter digitGap tried, still at full digitHeight, only once the row does not already fit maxWidth at the normal digitGap above — see the class remarks' fitting order.")]
        private float digitGapFloor = 4f;

        [SerializeField, Tooltip("Tighter groupGap tried alongside digitGapFloor — see the class remarks' fitting order.")]
        private float groupGapFloor = 6f;

        [SerializeField, Tooltip("Second, tighter digitGap tried — still at full digitHeight — only once digitGapFloor/groupGapFloor still does not fit maxWidth AND the value has 5+ digits. See the class remarks' fitting order.")]
        private float digitGapFloor2 = 2f;

        [SerializeField, Tooltip("Second, tighter groupGap tried alongside digitGapFloor2 — kept slightly bigger than digitGapFloor2 so a thousands-group boundary still reads as separated even at this tightest spacing.")]
        private float groupGapFloor2 = 3f;

        [SerializeField, Tooltip("Maximum allowed row width before this component uniformly shrinks itself to fit — only reached after digitGapFloor/groupGapFloor and digitGapFloor2/groupGapFloor2 have already been tried and the row still does not fit (see the class remarks' fitting order). 0 or less means no limit.")]
        private float maxWidth = 170f;

        /// <summary>Never shrink further than this fraction of digitHeight, even for a pathologically long value — a sane floor, not a product requirement (ordinary long balances — up to 5 digits — land well above it; see the class remarks).</summary>
        private const float MinScale = 0.65f;

        private RectTransform _rectTransform;

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
        }

        /// <summary>Rebuilds the digit row for value. Cheap enough to call on every CoinWalletService.BalanceChanged — coin balance changes are infrequent (level completions, rewarded ads), never per-frame.</summary>
        public void SetValue(int value)
        {
            if (_rectTransform == null)
                _rectTransform = (RectTransform)transform;

            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            if (digitSprites == null || digitSprites.Length != 10)
            {
                Debug.LogError("SpriteDigitNumberDisplay: digitSprites must contain exactly 10 entries (index 0-9); cannot render a value.");
                return;
            }

            if (value < 0)
                value = 0;

            var aspectRatios = new float[10];
            for (int digit = 0; digit < 10; digit++)
            {
                Sprite sprite = digitSprites[digit];
                aspectRatios[digit] = sprite != null && sprite.rect.height > 0f
                    ? sprite.rect.width / sprite.rect.height
                    : 1f;
            }

            // Step 2 (see class remarks): try full spacing first — this is
            // what every balance up to 4 digits already fits within
            // maxWidth at, so it renders completely unchanged.
            var placements = DigitLayout.Compute(value, aspectRatios, digitHeight, digitGap, groupGap);
            float widthAtFullGaps = DigitLayout.TotalWidth(placements);

            // Step 3: only retried at the tighter floor gaps — digitHeight
            // still fully untouched — when full spacing does not already
            // fit. A long balance therefore always prefers "same size, less
            // air between digits" over any shrink at all.
            if (maxWidth > 0f && widthAtFullGaps > maxWidth)
            {
                placements = DigitLayout.Compute(value, aspectRatios, digitHeight, digitGapFloor, groupGapFloor);

                // Step 4: only for a 5+ digit balance that still doesn't fit
                // even at the first floor — retry once more at the tighter
                // digitGapFloor2/groupGapFloor2, still without touching
                // digitHeight, so scale-down (step 6) only ever has to make
                // up whatever shortfall remains after BOTH spacing floors.
                // The digit-count gate keeps a 4-digit balance (e.g. 9999)
                // on exactly the same digitGapFloor/groupGapFloor/scale path
                // it already used — this step exists purely to soften
                // 5-digit's own additional shrink, never to touch 4-digit's.
                if (placements.Count >= 5 && DigitLayout.TotalWidth(placements) > maxWidth)
                    placements = DigitLayout.Compute(value, aspectRatios, digitHeight, digitGapFloor2, groupGapFloor2);
            }

            foreach (DigitLayout.DigitPlacement placement in placements)
            {
                var digitObject = new GameObject($"Digit_{placement.Digit}", typeof(Image));
                digitObject.transform.SetParent(transform, false);

                var rect = digitObject.GetComponent<RectTransform>();
                // Anchor at this row's own LEFT edge (0, 0.5) — NOT centre
                // (0.5, 0.5) — while keeping pivot at the digit's own centre
                // (0.5, 0.5) for correct sizing/rendering. DigitLayout's
                // CenterX is defined with its origin at the row's left edge
                // (see DigitLayoutTests: a lone digit of width 10 gets
                // CenterX=5), so it must be applied against a LEFT-edge
                // anchor reference. A centre anchor (0.5, 0.5) resolves its
                // reference point from this row's OWN current rect width —
                // which is this container's sizeDelta, only assigned AFTER
                // this loop runs (see below) — so every digit silently
                // rendered shifted right by half the row's own total width
                // (its centre-reference drifting per SetValue call instead
                // of staying fixed), even though each digit's anchoredPosition
                // itself held the mathematically-correct CenterX. A left
                // edge anchor has a fixed reference (this row's rect.xMin,
                // which is always 0 since this row is itself left-pivoted)
                // independent of the row's own width, matching DigitLayout's
                // own coordinate convention exactly.
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(placement.Width, digitHeight);
                rect.anchoredPosition = new Vector2(placement.CenterX, 0f);

                var image = digitObject.GetComponent<Image>();
                image.sprite = digitSprites[placement.Digit];
                image.preserveAspect = true;
            }

            float totalWidth = DigitLayout.TotalWidth(placements);

            // This container's own RectTransform reports its real content
            // width — never a fixed/reserved box — so anything inspecting
            // this component's own bounds sees the actual laid-out number,
            // matching every digit's own natural width rather than an
            // assumed equal-width cell. Does not affect child positioning
            // (children use anchorMin==anchorMax, so their anchoredPosition
            // is an absolute offset independent of this box's own size).
            _rectTransform.sizeDelta = new Vector2(totalWidth, digitHeight);

            // Step 6 (see class remarks): the last-resort uniform shrink,
            // now evaluated against totalWidth AFTER both floor-gap retries
            // above already did what they could — so this only ever needs
            // to make up whatever shortfall remains, never the whole
            // deficit from full spacing.
            float scale = 1f;
            if (maxWidth > 0f && totalWidth > maxWidth)
                scale = Mathf.Max(MinScale, maxWidth / totalWidth);

            _rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
