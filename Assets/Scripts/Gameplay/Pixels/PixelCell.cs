using UnityEngine;

namespace Project001.Gameplay.Pixels
{
    /// <summary>
    /// A single visual cell of a pixel grid. Owns a SpriteRenderer and exposes
    /// a way to set its colour. Holds no other logic.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PixelCell : MonoBehaviour
    {
        private SpriteRenderer _spriteRenderer;

        private SpriteRenderer SpriteRenderer =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

        public void SetColor(Color color)
        {
            SpriteRenderer.color = color;
        }
    }
}
