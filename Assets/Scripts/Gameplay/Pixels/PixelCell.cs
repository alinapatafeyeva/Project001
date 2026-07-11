using UnityEngine;

namespace Project001.Gameplay.Pixels
{
    /// <summary>
    /// A single visual cell of a pixel grid. Owns a SpriteRenderer plus its
    /// grid coordinates and colour/type, and exposes whether it can still be
    /// consumed. Holds no rider, conveyor, or consumption-search logic —
    /// PixelGrid decides what and when to consume.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PixelCell : MonoBehaviour
    {
        private SpriteRenderer _spriteRenderer;

        private SpriteRenderer SpriteRenderer =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

        public int GridX { get; private set; }

        public int GridY { get; private set; }

        public Vector2 LocalPosition { get; private set; }

        public Color PixelColor { get; private set; }

        public bool IsActive { get; private set; } = true;

        public void Initialize(int gridX, int gridY, Vector2 localPosition, Color color)
        {
            GridX = gridX;
            GridY = gridY;
            LocalPosition = localPosition;
            PixelColor = color;
            SpriteRenderer.color = color;
        }

        /// <summary>
        /// Deactivates the cell's visual representation. Safe to call more
        /// than once; only the first call has any effect.
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
