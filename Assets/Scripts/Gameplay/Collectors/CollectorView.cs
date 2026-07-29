using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns a Visual child GameObject that
    /// holds the actual SpriteRenderer, and applies whatever sprite
    /// CollectorPresentation requests via ApplySprite() — this view never
    /// decides which pose to show, only applies it. The SpriteRenderer's
    /// color is always Color.white and is never tinted: MonsterColor selects
    /// which sprites are shown (via MonsterSkin), not a tint applied on top
    /// of them.
    ///
    /// The SpriteRenderer deliberately lives on a child (Visual), not on
    /// this root: this root's transform is gameplay-owned (queue placement
    /// by CollectorQueueBoard, movement by ConveyorSystem, reparenting by
    /// WaitingLine/RecoveryRowView), while Visual's local transform is the
    /// only thing CollectorAnimation is ever allowed to animate — see
    /// CollectorAnimation. Root and Visual share the same local origin, so
    /// the rendered result is identical to before this split; only the
    /// ownership of "who may move what" changed.
    ///
    /// Also owns a RemainingHunger text indicator kept in sync via
    /// ConveyorRider.RemainingHungerChanged — this view never reads or
    /// stores hunger state itself, only displays whatever value the event
    /// last reported. The label is a direct child of this root (a sibling of
    /// Visual, never a child of it), which is scaled by
    /// GameplayLayout.CollectorSpriteScale (see
    /// CollectorQueueBoard.GenerateBoard) — without correction the label
    /// would inherit that scale and grow right along with Mofu. Instead the
    /// label's own local scale is set to the inverse of
    /// CollectorSpriteScale, so its rendered size always equals
    /// GameplayLayout.HungerLabelWorldSize regardless of how big the
    /// collector's sprite is, and being a sibling of Visual rather than its
    /// child, the label is also completely unaffected by any presentation
    /// animation played on Visual. Holds no movement, input, or queue logic,
    /// and has no knowledge of the conveyor beyond that — a Collider2D on
    /// this root only makes it detectable via Physics2D point queries for
    /// selection.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(ConveyorRider))]
    [RequireComponent(typeof(CollectorPresentation))]
    public class CollectorView : MonoBehaviour
    {
        // Positioned slightly below the sprite's own center — the local
        // offset is expressed in this collector's local space, so it scales
        // together with the sprite (staying anchored on the body) even
        // though the label's own rendered size does not.
        private static readonly Vector3 HungerTextLocalOffset = new Vector3(0f, -0.15f, 0f);

        [SerializeField, Tooltip("Font size, in font-import units, of the RemainingHunger text.")]
        [Min(1)]
        private int hungerTextFontSize = 48;

        private static Font _sharedFont;

        private static Font SharedFont =>
            _sharedFont != null ? _sharedFont : _sharedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private SpriteRenderer _spriteRenderer;
        private Transform _visual;
        private ConveyorRider _conveyorRider;
        private CollectorPresentation _presentation;
        private TextMesh _hungerText;

        /// <summary>
        /// This collector's CollectorPresentation, cached once in Awake.
        /// Exposed so callers (e.g. CollectorSelectionController, the moment
        /// a collector boards the Conveyor) can switch its pose without a
        /// runtime GetComponent call.
        /// </summary>
        public CollectorPresentation Presentation => _presentation;

        /// <summary>
        /// This collector's Visual child transform — the SpriteRenderer's
        /// own GameObject, created here in Awake. Gameplay never reads or
        /// writes this transform; it exists purely so CollectorAnimation has
        /// a local scale/position/rotation to animate that is never also
        /// being driven by ConveyorSystem, CollectorQueueBoard, WaitingLine,
        /// or RecoveryRowView, all of which only ever touch this collector's
        /// root transform (this GameObject's own transform), never Visual's.
        /// </summary>
        public Transform Visual => _visual;

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

            _visual = CreateVisual();

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
        /// Applies the given sprite to the Visual child's SpriteRenderer.
        /// Called only by this collector's own CollectorPresentation
        /// (ShowWaitingBackIdle, ShowConveyorFrontEating, and its bite/
        /// satisfied/heart sequences) — this view makes no pose decision
        /// itself. Never touches the SpriteRenderer's color: that stays
        /// Color.white, fixed in Awake.
        /// </summary>
        public void ApplySprite(Sprite sprite)
        {
            _spriteRenderer.sprite = sprite;
        }

        /// <summary>
        /// Hides the RemainingHunger label for good. Called once, by
        /// CollectorPresentation right as the terminal final-bite sequence
        /// begins — RemainingHunger already reads 0 at that point (set by
        /// ConveyorRider.RegisterConsumedPixel just before), and the player
        /// must never see that value on screen. There is no matching "show"
        /// method: this collector is destroyed shortly after the sequence
        /// completes, so the label never needs to reappear.
        /// </summary>
        public void HideHungerText()
        {
            _hungerText.gameObject.SetActive(false);
        }

        private void OnRemainingHungerChanged(int remainingHunger)
        {
            _hungerText.text = remainingHunger.ToString();
        }

        /// <summary>
        /// Creates the Visual child that owns the SpriteRenderer and is the
        /// only transform CollectorAnimation is ever allowed to animate.
        /// Local position/scale/rotation are left at identity, so — combined
        /// with this root's own CollectorSpriteScale localScale, assigned by
        /// CollectorQueueBoard — the rendered result is pixel-identical to
        /// when the SpriteRenderer lived directly on this root.
        /// </summary>
        private Transform CreateVisual()
        {
            var visualObject = new GameObject("Visual");
            visualObject.transform.SetParent(transform, false);

            _spriteRenderer = visualObject.AddComponent<SpriteRenderer>();
            // MonsterColor selects sprites, never a tint on top of them.
            _spriteRenderer.color = Color.white;

            return visualObject.transform;
        }

        private TextMesh CreateHungerText()
        {
            var textObject = new GameObject("HungerText");
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = HungerTextLocalOffset;

            // Cancels out this collector root's own CollectorSpriteScale
            // (read directly from GameplayLayout, not this transform's live
            // localScale, since CollectorQueueBoard assigns that scale after
            // this Awake already ran) so the label's rendered size is driven
            // by HungerLabelWorldSize alone.
            textObject.transform.localScale =
                Vector3.one / GameplayLayout.CollectorSpriteScale;

            var textMesh = textObject.AddComponent<TextMesh>();
            textMesh.font = SharedFont;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.white;
            textMesh.characterSize = GameplayLayout.HungerLabelWorldSize;
            textMesh.fontSize = hungerTextFontSize;

            var meshRenderer = textObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = SharedFont.material;
            meshRenderer.sortingLayerID = _spriteRenderer.sortingLayerID;
            meshRenderer.sortingOrder = _spriteRenderer.sortingOrder + 1;

            return textMesh;
        }
    }
}
