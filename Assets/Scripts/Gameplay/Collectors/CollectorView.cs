using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns a SpriteRenderer and exposes a way
    /// to initialise its colour, plus a RemainingHunger text indicator kept in
    /// sync via ConveyorRider.RemainingHungerChanged — this view never reads
    /// or stores hunger state itself, only displays whatever value the event
    /// last reported. Holds no movement, input, or queue logic, and has no
    /// knowledge of the conveyor beyond that — a Collider2D only makes it
    /// detectable via Physics2D point queries for selection.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(ConveyorRider))]
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
        private TextMesh _hungerText;

        private SpriteRenderer SpriteRenderer =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

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

            _hungerText = CreateHungerText();

            _conveyorRider = GetComponent<ConveyorRider>();
            _conveyorRider.RemainingHungerChanged += OnRemainingHungerChanged;
        }

        private void OnDestroy()
        {
            if (_conveyorRider != null)
                _conveyorRider.RemainingHungerChanged -= OnRemainingHungerChanged;
        }

        public void Initialize(Color color)
        {
            SpriteRenderer.color = color;
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
