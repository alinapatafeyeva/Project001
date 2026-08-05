using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns a three-level presentation
    /// hierarchy under this root:
    ///
    ///   Collector root (gameplay-owned; queue placement, Conveyor movement,
    ///   WaitingLine/RecoveryRowView reparenting)
    ///   └── Visual — the yaw pivot; CollectorAnimation's Update() is the
    ///       only thing that ever touches its localRotation, and never its
    ///       localPosition/localScale (always identity/one)
    ///       └── VisualMotion — the scale/position pivot; breathing, boarding
    ///           bounce, eating/satisfied punches, and the heart pulse/
    ///           collapse are the only things that ever touch its
    ///           localScale/localPosition, and never its localRotation
    ///           (always identity)
    ///           └── MofuModel — the resolved Character_XX prefab instance
    ///               (injected via Initialize(), pivot-corrected against its
    ///               own mesh bounds), already carrying its own baked
    ///               Character_XX material on its renderer(s)
    ///
    /// Splitting yaw (Visual) from scale/position (VisualMotion) is what
    /// makes it structurally impossible for a reaction animation to
    /// overwrite or fight the facing rotation: they are different nodes with
    /// different owners, not different fields on the same node a routine
    /// could accidentally touch — see CollectorAnimation for why the old
    /// single-node design was the actual root cause of the rotation bugs
    /// this replaced.
    ///
    /// This view never decides pose or facing, only which prefab to
    /// instantiate; CollectorPresentation and CollectorAnimation own the
    /// rest. The prefab it is given (resolved by CollectorQueueBoard via
    /// CharacterDatabase from the collector's MatchId) already carries its
    /// own baked Character_XX material on every renderer — there is no
    /// runtime material selection or swap here, unlike the shared-model/
    /// resolved-color scheme this replaced.
    ///
    /// Also owns a RemainingHunger text indicator kept in sync via
    /// ConveyorRider.RemainingHungerChanged — this view never reads or
    /// stores hunger state itself, only displays whatever value the event
    /// last reported. The label is a direct child of this root (a sibling of
    /// Visual, never a descendant of it), which is scaled by
    /// GameplayLayout.CollectorSpriteScale (see
    /// CollectorQueueBoard.GenerateBoard) — without correction the label
    /// would inherit that scale and grow right along with Mofu. Instead the
    /// label's own local scale is set to the inverse of
    /// CollectorSpriteScale, so its rendered size always equals
    /// GameplayLayout.HungerLabelWorldSize regardless of how big the
    /// collector's sprite is, and being a sibling of Visual rather than its
    /// descendant, the label is also completely unaffected by any
    /// presentation animation played on Visual/VisualMotion. Holds no
    /// movement, input, or queue logic, and has no knowledge of the conveyor
    /// beyond that — a Collider2D on this root only makes it detectable via
    /// Physics2D point queries for selection.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(ConveyorRider))]
    [RequireComponent(typeof(CollectorPresentation))]
    public class CollectorView : MonoBehaviour
    {
        // Positioned slightly below the model's own center and toward the
        // camera (negative Z, in this root's own local space, before
        // CollectorSpriteScale is applied) so it renders in front of the
        // model regardless of which way Visual is currently rotated to face
        // (see CollectorAnimation's facing rotation) — Visual can sit at any
        // Y-axis angle while riding (facing the pixel grid from wherever the
        // conveyor loop has carried it, not just a fixed toward/away flip
        // any more), so an offset that only cleared the model's front face
        // at one angle would clip through it at another. The mesh's own
        // roughly circular footprint (local X/Z extents both ~0.89, from
        // Mofu.fbx) keeps its camera-facing half-depth about the same at
        // every rotation angle, ~0.35 at MofuModel's authored scale (0.88
        // mesh depth x 0.79 PrefabRootScale x 0.5), plus a small margin for
        // breathing's scale wobble — -0.5 clears that with room to spare
        // regardless of facing angle. A physical Z offset is a
        // spike-appropriate fix, not a production one; a camera-facing
        // billboard/canvas label would be more robust against rotation and
        // is called out as a follow-up in the spike's evaluation notes.
        private static readonly Vector3 HungerTextLocalOffset = new Vector3(0f, -0.15f, -0.5f);

        [SerializeField, Tooltip("Font size, in font-import units, of the RemainingHunger text.")]
        [Min(1)]
        private int hungerTextFontSize = 48;

        private static Font _sharedFont;

        private static Font SharedFont =>
            _sharedFont != null ? _sharedFont : _sharedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private Transform _visual;
        private Transform _visualMotion;
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
        /// This collector's Visual child transform — the yaw pivot created
        /// by Initialize(). Gameplay never reads or writes this transform;
        /// it exists purely so CollectorAnimation has a facing rotation to
        /// own that is never also being driven by ConveyorSystem,
        /// CollectorQueueBoard, WaitingLine, or RecoveryRowView, all of which
        /// only ever touch this collector's root transform (this
        /// GameObject's own transform), never Visual's. Ordinarily only
        /// localRotation changes here — see VisualMotion for scale/position
        /// — with one deliberate exception: CollectorAnimation.
        /// EnterTerminalForeground offsets localPosition toward the camera,
        /// once, when the terminal Satisfied/Heart sequence begins, so it
        /// cannot visually merge with an ordinary rider sharing its screen
        /// position; never reset, since this collector is destroyed shortly
        /// after. Null until Initialize() has run.
        /// </summary>
        public Transform Visual => _visual;

        /// <summary>
        /// Visual's child transform — the scale/position pivot every
        /// breathing/bounce/punch/heart reaction animates, created by
        /// Initialize(). Deliberately a separate node from Visual: a
        /// reaction that sets localScale/localPosition here can never also
        /// touch Visual's localRotation, since it is a different Transform
        /// entirely. Null until Initialize() has run.
        /// </summary>
        public Transform VisualMotion => _visualMotion;

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
        /// Builds the Visual/VisualMotion/MofuModel hierarchy (see the class
        /// remarks). Must be called exactly once, by CollectorQueueBoard
        /// right after this collector is constructed and before
        /// CollectorPresentation.ShowWaitingBackIdle() — there is no
        /// Inspector session for this component to source a prefab
        /// reference from (collectors are still built at runtime, never from
        /// a prefab themselves), so it is injected here the same way
        /// ConveyorRider, PixelConsumer, and CollectorLifecycle are each
        /// initialized.
        ///
        /// Visual and VisualMotion are both plain, empty pivot GameObjects —
        /// never the prefab instance itself — left at local position zero,
        /// rotation identity, scale one. Neither carries the model's own
        /// authored scale or any pivot correction; both live one level
        /// deeper, on MofuModel, so they are never disturbed by whichever of
        /// the two pivots above them is currently being animated.
        ///
        /// MofuModel (VisualMotion's child) is the actual visualPrefab
        /// instance, carrying its own authored localScale (derived by
        /// CharacterAssetBuilder, preserved by leaving it untouched after
        /// Instantiate) plus a derived localPosition correction: an imported
        /// model is typically pivoted at its feet (local Y running ~0 to ~1,
        /// not centered on Y 0), while every queue/conveyor slot in
        /// GameplayLayout places a collector's root at the visual CENTER of
        /// the old, center-pivoted sprite (see GameplayLayout's symmetric
        /// CollectorVisibleHeight*0.5 usage). Left uncorrected, every queued
        /// or riding character would render shifted upward by roughly half
        /// its own visible height. The correction below re-centers MofuModel
        /// by reading its own combined mesh bounds (CollectorAnimation.
        /// ComputeLocalRendererBounds, shared rather than duplicated) and
        /// offsetting by the negative of that bounds center — derived from
        /// the model's actual geometry, not a hand-measured constant.
        ///
        /// No material is applied here: visualPrefab already carries its own
        /// baked Character_XX material on every renderer (built once, at
        /// editor time, by CharacterAssetBuilder), so Instantiate() alone
        /// produces the correct, fully-textured result — there is nothing
        /// left for this method to swap or tint.
        /// </summary>
        public void Initialize(GameObject visualPrefab)
        {
            var visualWrapper = new GameObject("Visual");
            visualWrapper.transform.SetParent(transform, false);

            var visualMotion = new GameObject("VisualMotion");
            visualMotion.transform.SetParent(visualWrapper.transform, false);

            var modelInstance = Instantiate(visualPrefab, visualMotion.transform, false);
            modelInstance.name = "MofuModel";
            modelInstance.transform.localRotation = Quaternion.identity;

            Bounds? localBounds = CollectorAnimation.ComputeLocalRendererBounds(modelInstance.transform);
            modelInstance.transform.localPosition = localBounds.HasValue
                ? -Vector3.Scale(modelInstance.transform.localScale, localBounds.Value.center)
                : Vector3.zero;

            _visual = visualWrapper.transform;
            _visualMotion = visualMotion.transform;
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

            // No SpriteRenderer to inherit a sorting layer/order from now —
            // HungerTextLocalOffset's own negative Z already guarantees this
            // renders in front of the model, so the default sorting layer is
            // sufficient; sortingOrder 1 is kept only as a harmless safety
            // margin over the model's own renderers.
            var meshRenderer = textObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = SharedFont.material;
            meshRenderer.sortingOrder = 1;

            return textMesh;
        }
    }
}
