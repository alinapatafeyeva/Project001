using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns a SpriteRenderer and exposes a way
    /// to initialise its colour. Holds no movement, input, or queue logic.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CollectorView : MonoBehaviour
    {
        private SpriteRenderer _spriteRenderer;

        private SpriteRenderer SpriteRenderer =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

        public void Initialize(Color color)
        {
            SpriteRenderer.color = color;
        }
    }
}
