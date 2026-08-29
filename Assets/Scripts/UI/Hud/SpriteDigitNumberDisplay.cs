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
    /// If the laid-out row would exceed maxWidth (0 = no limit), the whole
    /// row is scaled down uniformly via this component's own
    /// RectTransform.localScale — never by distorting individual digits or
    /// their relative spacing — anchored at this object's own left edge, so
    /// a long balance shrinks away from Level/Speed-Pause rather than
    /// growing into them.
    /// </summary>
    public class SpriteDigitNumberDisplay : MonoBehaviour
    {
        [SerializeField, Tooltip("Digit0.png-Digit9.png, indexed by digit value (digitSprites[3] is the '3' sprite).")]
        private Sprite[] digitSprites;

        [SerializeField, Tooltip("Shared displayed height for every digit at 1:1 scale (before any maxWidth shrink) — each digit's own displayed width follows from its native aspect ratio at this height.")]
        private float digitHeight = 66f;

        [SerializeField, Tooltip("Gap between adjacent digits within the same thousands group.")]
        private float digitGap = 4f;

        [SerializeField, Tooltip("Additional gap (on top of digitGap) at a thousands-group boundary — represents the grouping separator as spacing, since no comma/space sprite exists.")]
        private float groupGap = 10f;

        [SerializeField, Tooltip("Maximum allowed row width at 1:1 scale before this component uniformly shrinks itself to fit. 0 or less means no limit.")]
        private float maxWidth = 170f;

        /// <summary>Never shrink further than this fraction of digitHeight, even for a pathologically long value — a sane floor, not a product requirement (ordinary long balances land well above it).</summary>
        private const float MinScale = 0.4f;

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

            var placements = DigitLayout.Compute(value, aspectRatios, digitHeight, digitGap, groupGap);

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

            float scale = 1f;
            if (maxWidth > 0f && totalWidth > maxWidth)
                scale = Mathf.Max(MinScale, maxWidth / totalWidth);

            _rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
