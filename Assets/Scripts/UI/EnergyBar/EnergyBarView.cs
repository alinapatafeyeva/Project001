using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.UI.EnergyBar
{
    /// <summary>
    /// Reusable energy/hunger-style bar widget: a dark rounded-rectangle
    /// Background, a coloured Fill that grows from the left, a decorative
    /// Frame (EnergyBar_Frame.png) drawn on top of both, and a centered
    /// ValueText showing the current value as a whole number - see
    /// EnergyBarPrefabBuilder for how the prefab itself is assembled and
    /// EnergyBar.prefab's own baked field references.
    ///
    /// This is a pure presentation component: it holds no gameplay state of
    /// its own beyond the current/max values needed to compute the fill
    /// fraction, and nothing here reads from or writes to any gameplay
    /// system - callers (a future hunger/energy controller) push values in
    /// via SetCurrentValue/SetMaxValue/SetFillColor. Not wired to gameplay
    /// yet (first-version task scope).
    ///
    /// Fill is driven purely through fillRect's own anchorMax.x, interpolated
    /// between fillRect's own anchorMin.x (0% - flush with the Background's
    /// left interior edge) and fillAnchorMaxX (100% - flush with the
    /// Background's right interior edge, still short of Frame's own outer
    /// border - see EnergyBarPrefabBuilder's own remarks on the measured
    /// interior bounds). Anchors, not a pixel sizeDelta, so Fill
    /// keeps tracking the interior correctly if this bar is ever resized -
    /// no pixel math here needs to know this instance's actual current
    /// width. Every update is an immediate, non-animated snap to the new
    /// fraction - smooth animation is an explicit non-goal for this first
    /// version (see class task remarks); the anchor-based approach is what
    /// makes that follow-up trivial later (just lerp anchorMax.x over time
    /// instead of setting it directly), without changing this class's public
    /// API.
    /// </summary>
    public class EnergyBarView : MonoBehaviour
    {
        [SerializeField, Tooltip("Grows from the left as the fill fraction increases. Its anchorMin.x is the 0% position (Background's left interior edge); its anchorMax.x is animated between anchorMin.x and fillAnchorMaxX below.")]
        private RectTransform fillRect;

        [SerializeField, Tooltip("Fill's own Image - SetFillColor tints this. Kept separate from fillRect so this component never assumes fillRect.GetComponent<Image>() is safe to cache before Awake.")]
        private Image fillImage;

        [SerializeField, Tooltip("fillRect.anchorMax.x at 100% fill - Background's right interior edge, still inside Frame's own border (see EnergyBarPrefabBuilder's measured interior anchors). Matches fillRect's baked anchorMin.x at 0% automatically; only the 100% end needs to be configured here.")]
        private float fillAnchorMaxX = 0.95f;

        [SerializeField, Tooltip("Centered label showing CurrentValue as a whole number.")]
        private TMP_Text valueText;

        private int _currentValue;
        private int _maxValue = 1;

        /// <summary>Current value last set via SetCurrentValue, clamped to [0, MaxValue].</summary>
        public int CurrentValue => _currentValue;

        /// <summary>Current max value last set via SetMaxValue, never below 1.</summary>
        public int MaxValue => _maxValue;

        /// <summary>
        /// The number currently shown in ValueText, verbatim - whichever of
        /// SetCurrentValue/SetMaxValue/SetDisplayValue last wrote it. Exposed
        /// so callers (e.g. CollectorView.HungerDisplayText, verification
        /// tooling) can read the actual displayed text without reaching into
        /// the TMP_Text component directly. Empty string if valueText itself
        /// is unassigned.
        /// </summary>
        public string DisplayText => valueText != null ? valueText.text : string.Empty;

        /// <summary>
        /// Sets the current value, clamped to [0, MaxValue], and immediately
        /// updates both the fill fraction and the displayed number. No-op on
        /// the fill/text update if the clamped value did not actually change
        /// - safe to call every frame from a future gameplay binding without
        /// re-laying-out anchors when nothing moved.
        /// </summary>
        public void SetCurrentValue(int value)
        {
            int clamped = Mathf.Clamp(value, 0, _maxValue);
            if (clamped == _currentValue)
                return;

            _currentValue = clamped;
            Refresh();
        }

        /// <summary>
        /// Sets the max value (minimum 1, so the fill fraction is always
        /// well-defined), re-clamps CurrentValue against it, and immediately
        /// refreshes. Changing MaxValue alone (current value unchanged in
        /// absolute terms) still moves the fill, since the fraction is
        /// CurrentValue / MaxValue.
        /// </summary>
        public void SetMaxValue(int value)
        {
            _maxValue = Mathf.Max(1, value);
            _currentValue = Mathf.Clamp(_currentValue, 0, _maxValue);
            Refresh();
        }

        /// <summary>Tints Fill's own Image - callers decide the colour (e.g. per-MatchId), this component never hardcodes one.</summary>
        public void SetFillColor(Color color)
        {
            if (fillImage != null)
                fillImage.color = color;
        }

        /// <summary>
        /// Overrides the displayed number directly, independent of the fill
        /// fraction - added for "countdown" style bars (e.g. a collector's
        /// RemainingHunger) where the visible number and the fill fraction
        /// are deliberately DIFFERENT values (text counts down from
        /// MaxHunger to 0; fill instead grows from empty to full, driven by
        /// the inverse - see the caller that computes that inversion). Call
        /// this AFTER SetCurrentValue/SetMaxValue, since either of those
        /// would otherwise overwrite valueText with ITS OWN current value on
        /// the very next Refresh. This component still never computes that
        /// inversion itself - it stays gameplay-agnostic, just displaying
        /// whatever int a caller hands it.
        /// </summary>
        public void SetDisplayValue(int value)
        {
            if (valueText != null)
                valueText.text = value.ToString();
        }

        private void Refresh()
        {
            float fraction = _maxValue > 0 ? Mathf.Clamp01((float)_currentValue / _maxValue) : 0f;

            if (fillRect != null)
            {
                Vector2 anchorMax = fillRect.anchorMax;
                anchorMax.x = Mathf.Lerp(fillRect.anchorMin.x, fillAnchorMaxX, fraction);
                fillRect.anchorMax = anchorMax;
            }

            if (valueText != null)
                valueText.text = _currentValue.ToString();
        }
    }
}
