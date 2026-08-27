using UnityEngine;

namespace Project001.UI.Hud
{
    /// <summary>
    /// Keeps a modal artwork panel's rendered width at
    /// min(availableWidth * widthFraction, maxWidth) — the one piece of
    /// "responsive width" that RectTransform anchors alone cannot express,
    /// since anchor stretch is purely proportional to its parent with no
    /// absolute ceiling. Without this, a modal sized as a pure width
    /// fraction keeps growing without bound on wider/tablet screens.
    ///
    /// Sits on the same RectTransform as the modal artwork's
    /// Image/AspectRatioFitter. This component owns sizeDelta.x only —
    /// AspectRatioFitter still independently derives sizeDelta.y from that
    /// width and the artwork's own aspect ratio exactly as before, so the
    /// panel is never stretched on X/Y independently.
    ///
    /// Also uniformly scales contentRoot (the title/description/buttons
    /// container) by resolvedWidth / contentReferenceWidth. contentRoot's
    /// children are authored once as fixed pixel positions/sizes against
    /// contentReferenceWidth; this scale keeps every one of those fixed
    /// values in the same visual proportion to the modal at whatever width
    /// the modal actually resolves to on a given device, rather than the
    /// artwork box resizing while its contents stay a fixed pixel size.
    ///
    /// Recomputed on every enable and every frame while enabled (cheap —
    /// a couple of float ops) rather than once, so it stays correct through
    /// a resolution/orientation change while the modal is open. The
    /// GameObject this lives on is only ever active while its modal is
    /// actually shown (see ExitConfirmationUI.Open/OnStayPressed), so this
    /// never runs while the dialog is closed.
    /// </summary>
    public class ResponsiveModalBox : MonoBehaviour
    {
        [SerializeField, Tooltip("Full-screen area this modal's width is a fraction of — its backdrop panel, not the artwork itself.")]
        private RectTransform availableAreaSource;

        [SerializeField, Tooltip("Title/description/buttons container, uniformly scaled to match this panel's resolved width.")]
        private RectTransform contentRoot;

        [SerializeField, Tooltip("Fraction of availableAreaSource's width this panel targets before the maxWidth ceiling is applied.")]
        private float widthFraction = 0.9f;

        [SerializeField, Tooltip("Absolute ceiling on this panel's width, in the same Canvas units as availableAreaSource — what keeps the modal from growing indefinitely on wide/tablet screens.")]
        private float maxWidth = 1400f;

        [SerializeField, Tooltip("The contentRoot width its children's fixed pixel positions/sizes were authored against — the divisor for the uniform content scale.")]
        private float contentReferenceWidth = 972f;

        private RectTransform _self;

        private void Awake()
        {
            _self = (RectTransform)transform;
        }

        private void OnEnable()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (availableAreaSource == null || _self == null)
                return;

            float availableWidth = availableAreaSource.rect.width;
            if (availableWidth <= 0f)
                return;

            float width = Mathf.Min(availableWidth * widthFraction, maxWidth);

            Vector2 size = _self.sizeDelta;
            size.x = width;
            _self.sizeDelta = size;

            if (contentRoot != null && contentReferenceWidth > 0f)
            {
                float scale = width / contentReferenceWidth;
                contentRoot.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
