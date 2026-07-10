using UnityEngine;

namespace Project001.Gameplay.Pixels
{
    /// <summary>
    /// Generates a centred 6x6 grid of PixelCell objects on Awake, using a single
    /// shared runtime-generated 1x1 sprite and a deterministic 3-colour pattern.
    /// </summary>
    public class PixelGrid : MonoBehaviour
    {
        private const int GridSize = 6;
        private const float CellSize = 1f;

        [SerializeField, Tooltip("Extra spacing added between neighbouring cells, in world units.")]
        [Min(0f)]
        private float cellGap = 0.05f;

        private static readonly Color[] Palette =
        {
            Color.red,
            Color.green,
            Color.blue
        };

        private Texture2D _sharedTexture;
        private Sprite _sharedSprite;

        private void Awake()
        {
            CreateSharedSprite();
            GenerateGrid();
        }

        private void CreateSharedSprite()
        {
            _sharedTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _sharedTexture.SetPixel(0, 0, Color.white);
            _sharedTexture.Apply();

            _sharedSprite = Sprite.Create(
                _sharedTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f);
        }

        private void GenerateGrid()
        {
            float spacing = CellSize + cellGap;
            float offset = (GridSize - 1) * spacing * 0.5f;

            for (int x = 0; x < GridSize; x++)
            {
                for (int y = 0; y < GridSize; y++)
                {
                    var cellObject = new GameObject($"Pixel_{x}_{y}", typeof(PixelCell));
                    cellObject.transform.SetParent(transform, false);
                    cellObject.transform.localPosition = new Vector3(
                        x * spacing - offset,
                        y * spacing - offset,
                        0f);
                    cellObject.transform.localScale = new Vector3(CellSize, CellSize, 1f);

                    var spriteRenderer = cellObject.GetComponent<SpriteRenderer>();
                    spriteRenderer.sprite = _sharedSprite;

                    var cell = cellObject.GetComponent<PixelCell>();
                    cell.SetColor(Palette[(x + y) % Palette.Length]);
                }
            }
        }

        private void OnDestroy()
        {
            if (_sharedSprite != null)
                Destroy(_sharedSprite);

            if (_sharedTexture != null)
                Destroy(_sharedTexture);
        }
    }
}
