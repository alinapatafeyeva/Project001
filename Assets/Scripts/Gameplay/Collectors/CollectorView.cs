using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns the SpriteRenderer and applies
    /// whatever sprite CollectorPresentation requests via ApplySprite() —
    /// this view never decides which pose to show, only applies it. The
    /// SpriteRenderer's color is always Color.white and is never tinted:
    /// MonsterColor selects which sprites are shown (via MonsterSkin), not a
    /// tint applied on top of them.
    ///
    /// Also owns a RemainingHunger text indicator kept in sync via
    /// ConveyorRider.RemainingHungerChanged — this view never reads or stores
    /// hunger state itself, only displays whatever value the event last
    /// reported. Holds no movement, input, or queue logic, and has no
    /// knowledge of the conveyor beyond that — a Collider2D only makes it
    /// detectable via Physics2D point queries for selection.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(ConveyorRider))]
    [RequireComponent(typeof(CollectorPresentation))]
    public class CollectorView : MonoBehaviour
    {
        [SerializeField, Tooltip("World-space character size of the RemainingHunger text, scaled by this collector's own transform.")]
        [Min(0.01f)]
        private float hungerTextCharacterSize = 0.12f;

        [SerializeField, Tooltip("Font size, in font-import units, of the RemainingHunger text.")]
        [Min(1)]
        private int hungerTextFontSize = 48;

        private static Font _sharedFont;

        private static Font SharedFont =>
            _sharedFont != null ? _sharedFont : _sharedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private SpriteRenderer _spriteRenderer;
        private ConveyorRider _conveyorRider;
        private CollectorPresentation _presentation;
        private TextMesh _hungerText;

        private SpriteRenderer SpriteRenderer =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

        /// <summary>
        /// This collector's CollectorPresentation, cached once in Awake.
        /// Exposed so callers (e.g. CollectorSelectionController, the moment
        /// a collector boards the Conveyor) can switch its pose without a
        /// runtime GetComponent call.
        /// </summary>
        public CollectorPresentation Presentation => _presentation;

        /// <summary>
        /// Currently displayed RemainingHunger text. Read-only — this view
        /// never sets hunger state, only mirrors whatever
        /// ConveyorRider.RemainingHungerChanged last reported. Exposed so
        /// tests and tooling can verify the indicator without reaching into
        /// the TextMesh directly.
        /// </summary>
        public string HungerDisplayText => _hungerText.text;

        private void Awake()
        {
            // Selection detection only; must not participate in physical collision.
            GetComponent<Collider2D>().isTrigger = true;

            // MonsterColor selects sprites, never a tint on top of them.
            SpriteRenderer.color = Color.white;

            _presentation = GetComponent<CollectorPresentation>();
            _hungerText = CreateHungerText();

            _conveyorRider = GetComponent<ConveyorRider>();
            _conveyorRider.RemainingHungerChanged += OnRemainingHungerChanged;
        }

        private void OnDestroy()
        {
            if (_conveyorRider != null)
                _conveyorRider.RemainingHungerChanged -= OnRemainingHungerChanged;
        }

        /// <summary>
        /// Applies the given sprite to the SpriteRenderer. Called only by
        /// this collector's own CollectorPresentation (ShowQueueBack,
        /// ShowGameplayFront) — this view makes no pose decision itself.
        /// Never touches SpriteRenderer.color: that stays Color.white, fixed
        /// in Awake.
        /// </summary>
        public void ApplySprite(Sprite sprite)
        {
            SpriteRenderer.sprite = sprite;
        }

        private void OnRemainingHungerChanged(int remainingHunger)
        {
            _hungerText.text = remainingHunger.ToString();
        }

        private TextMesh CreateHungerText()
        {
            var textObject = new GameObject("HungerText");
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = Vector3.zero;

            var textMesh = textObject.AddComponent<TextMesh>();
            textMesh.font = SharedFont;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.white;
            textMesh.characterSize = hungerTextCharacterSize;
            textMesh.fontSize = hungerTextFontSize;

            var meshRenderer = textObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = SharedFont.material;
            meshRenderer.sortingLayerID = SpriteRenderer.sortingLayerID;
            meshRenderer.sortingOrder = SpriteRenderer.sortingOrder + 1;

            return textMesh;
        }
    }
}
