using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// A single visual collector slot. Owns a SpriteRenderer and exposes a way
    /// to initialise its colour. Holds no movement, input, or queue logic, and
    /// has no knowledge of the conveyor — a Collider2D only makes it detectable
    /// via Physics2D point queries for selection.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class CollectorView : MonoBehaviour
    {
        private SpriteRenderer _spriteRenderer;

        private SpriteRenderer SpriteRenderer =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

        private void Awake()
        {
            // Selection detection only; must not participate in physical collision.
            GetComponent<Collider2D>().isTrigger = true;
        }

        public void Initialize(Color color)
        {
            SpriteRenderer.color = color;
        }
    }
}
