using Project001.Gameplay.Levels;
using UnityEngine;

namespace Project001.Gameplay.Pixels
{
    /// <summary>
    /// A single cell of a pixel grid. Owns its grid coordinates, gameplay
    /// MatchTypeId, and a separate visual colour, and exposes whether it can
    /// still be consumed. Holds no rider, conveyor, or consumption-search
    /// logic - PixelGrid decides what and when to consume.
    ///
    /// Rendered as a child "Visual" instance of the shared PixelBlock mesh
    /// (see CreateVisual) rather than a SpriteRenderer on this GameObject
    /// itself - this transform's own position/scale stay exactly what
    /// PixelGrid placed them at (its position is read directly by
    /// PixelGrid.TryConsumeNearestExposed as the cell's world position), so
    /// consumption/matching logic is entirely unaffected by how the cell
    /// renders.
    /// </summary>
    public class PixelCell : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Visual-only overscale (on top of CellSize) applied to the Visual
        /// mesh's local X axis (world columns - horizontal spacing between
        /// cells) and local Y axis (world Z - camera-depth "thickness",
        /// kept the same as X rather than a separate arbitrary value, so
        /// the block doesn't get flattened). PixelBlock.fbx's rounded
        /// silhouette leaves a small visible gap between adjacent blocks at
        /// exactly CellSize, so the rendered footprint is nudged up a
        /// little past CellSize to close it. Purely presentational -
        /// PixelCell's own transform (position PixelGrid placed it at, read
        /// directly by PixelGrid.TryConsumeNearestExposed) and PixelGrid's
        /// spacing/gap are completely unaffected.
        /// </summary>
        private const float VisualScaleFactor = 1.05f;

        /// <summary>
        /// Visual-only overscale applied to the Visual mesh's LOCAL Z axis
        /// only. PixelBlock.fbx's imported root carries a baked (270, 0, 0)
        /// local rotation (verified during import/orientation checks): its
        /// raw, pre-rotation mesh bounds run local X ±0.499 (footprint),
        /// local Y ±0.499 (footprint), local Z 0..~1 (the "tall" axis as
        /// authored). A -90 degree rotation about X maps local Z to world Y
        /// and local Y to world Z, while local X is unaffected - confirmed
        /// against the actually-rendered instance, whose world bounds come
        /// out centred at world Y ~0.5 (matching the local Z 0..~1 range)
        /// with local Y instead landing on world Z. PixelGrid places cells
        /// with grid-row Y directly as world Y (GenerateGrid's localPosition
        /// y), so the row-to-row visual footprint - the vertical gap this
        /// factor closes - is controlled by local Z, not local Y, and must
        /// not be folded into VisualScaleFactor above.
        /// </summary>
        private const float VisualRowScaleFactor = 1.20f;

        public int GridX { get; private set; }

        public int GridY { get; private set; }

        public Vector2 LocalPosition { get; private set; }

        public MatchTypeId MatchTypeId { get; private set; }

        /// <summary>
        /// The Match ID (1-20) this cell's colour was resolved from (see
        /// PixelGrid.GenerateGrid/ColorPalette.TryGetByMatchId) — presentation
        /// only, exposed purely so tooling/logging can report it; consumption
        /// matching (PixelGrid.TryConsumeNearestExposed) still compares
        /// MatchTypeId exclusively, never this.
        /// </summary>
        public int MatchId { get; private set; }

        public Color PixelColor { get; private set; }

        public bool IsActive { get; private set; } = true;

        public void Initialize(int gridX, int gridY, Vector2 localPosition, MatchTypeId matchTypeId, int matchId, Color color)
        {
            GridX = gridX;
            GridY = gridY;
            LocalPosition = localPosition;
            MatchTypeId = matchTypeId;
            MatchId = matchId;
            PixelColor = color;
        }

        /// <summary>
        /// Spawns this cell's visual: one instance of the shared
        /// pixelBlockSource mesh, as a child of this transform, coloured via
        /// MaterialPropertyBlock (never a material instance) so every cell
        /// across the whole grid still shares the exact same Mesh and
        /// Material assets. Must be called after Initialize (needs
        /// PixelColor).
        ///
        /// A child, not this GameObject directly - pixelBlockSource's own
        /// mesh has a base-centre pivot (its Blender-authored bounds run
        /// local Y 0..~1, not -0.5..0.5), so it needs its own local Y offset
        /// to visually centre on the cell; this transform's position must
        /// stay exactly what PixelGrid placed it at, since
        /// PixelGrid.TryConsumeNearestExposed reads it directly as the
        /// cell's world/spawn position.
        /// </summary>
        public void CreateVisual(GameObject pixelBlockSource, Material pixelBlockMaterial, float cellSize)
        {
            GameObject visual = Instantiate(pixelBlockSource, transform);
            visual.name = "Visual";
            // x -> world columns, y -> world depth (Z), z -> world rows (Y)
            // - see VisualScaleFactor/VisualRowScaleFactor's own remarks for
            // why local Z, not local Y, is the row-to-row axis here.
            visual.transform.localScale = new Vector3(
                cellSize * VisualScaleFactor,
                cellSize * VisualScaleFactor,
                cellSize * VisualRowScaleFactor);

            var renderer = visual.GetComponentInChildren<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogError($"PixelCell: pixelBlockSource has no MeshRenderer; cell ({GridX},{GridY}) will render nothing.", this);
                return;
            }

            // See CreateVisual's own remarks - shift down by half the
            // rendered height so the mesh's visual centre, not its base,
            // lands on this cell's position.
            visual.transform.localPosition = new Vector3(0f, -renderer.bounds.extents.y, 0f);

            renderer.sharedMaterial = pixelBlockMaterial;

            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, PixelColor);
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Deactivates the cell's visual representation. Safe to call more
        /// than once; only the first call has any effect. Deactivating this
        /// GameObject also deactivates its Visual child, so no separate
        /// renderer reference is needed here.
        /// </summary>
        public void Consume()
        {
            if (!IsActive)
                return;

            IsActive = false;
            gameObject.SetActive(false);
        }
    }
}
