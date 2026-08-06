using System;
using System.Collections;
using Project001.Gameplay.Collectors;
using UnityEngine;

namespace Project001.Gameplay.Feeding
{
    /// <summary>
    /// One consumed gameplay pixel, travelling from where it was consumed to
    /// a collector's FeedTarget. One PixelCell consumption always produces
    /// exactly one FoodPacket (see PixelConsumer) - never batched, never
    /// split. A temporary runtime object: created by FoodFlightController.
    /// Create, destroys itself the instant its own flight ends. No pooling
    /// (see class-level project note in FoodFlightController).
    ///
    /// Deliberately knows nothing about hunger, collectors' internal state,
    /// or reactions beyond exposing which CollectorView it is bound for -
    /// FoodFlightController supplies the one mandatory onArrived callback
    /// (hunger decrement) this class always invokes on arrival; the static
    /// OnFoodPacketLaunched/OnFoodPacketArrived events below are a
    /// completely separate, purely-additive extension point other systems
    /// subscribe to without this file, or FoodFlightController, ever
    /// changing - CollectorPresentation's bounce/highlight reaction is the
    /// first such subscriber (Phase 2), sound/particles/mouth-animation
    /// remain free to become later ones the same way.
    ///
    /// Visual is intentionally a flat placeholder square (a SpriteRenderer
    /// on the same runtime-generated 1x1 sprite PixelGrid/PixelCell already
    /// use, tinted to the consumed pixel's own colour) - not final art. On
    /// arrival it shrinks and fades over DissolveDuration (see
    /// FlightRoutine/DissolveRoutine) rather than disappearing instantly,
    /// then destroys itself.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FoodPacket : MonoBehaviour
    {
        /// <summary>
        /// Raised the instant a packet's flight starts (inside Launch,
        /// before the first frame of movement). Static: any future system
        /// wants one subscription point regardless of how many packets
        /// exist at once, the same reasoning CollectorLifecycle.
        /// CollectorSatisfied already uses in this codebase.
        /// </summary>
        public static event Action<FoodPacket> OnFoodPacketLaunched;

        /// <summary>
        /// Raised the instant a packet reaches its target, immediately
        /// after the mandatory onArrived callback (see Launch) - this is
        /// the single synchronization point every arrival effect (hunger
        /// decrement, collector bounce/highlight) shares. The packet itself
        /// still exists briefly afterward, playing its own dissolve before
        /// Destroy (see DissolveRoutine); a subscriber that needs the
        /// packet's position/Collector should still read them synchronously
        /// during this event rather than caching the reference for later.
        /// </summary>
        public static event Action<FoodPacket> OnFoodPacketArrived;

        private const float VisualSize = 0.3f;

        // Phase 2 (Pixel Feed Flow): the packet no longer disappears the
        // instant it arrives - it shrinks and fades over this short window
        // first, then destroys itself. Deliberately AFTER onArrived/
        // OnFoodPacketArrived, never before: arrival - hunger decrement,
        // collector bounce/highlight - happens at the single synchronous
        // instant the packet reaches FeedTarget, exactly as before; the
        // dissolve is purely this packet's own trailing visual, invisible
        // to every other system.
        private const float DissolveDuration = 0.1f;

        private static Sprite _sharedSprite;

        private SpriteRenderer _spriteRenderer;
        private Transform _target;
        private float _duration;
        private Action<FoodPacket> _onArrived;

        /// <summary>
        /// Which collector this packet is bound for - exposed so a future
        /// OnFoodPacketArrived subscriber (e.g. a bounce reaction) knows
        /// which collector to react on without re-deriving it from
        /// position. Never used by this class or FoodFlightController for
        /// anything beyond exposing it.
        /// </summary>
        public CollectorView Collector { get; private set; }

        /// <summary>
        /// Builds one new packet GameObject at startWorldPosition, coloured
        /// to match the consumed pixel. Does not start its flight - call
        /// Launch separately (kept as two steps so a future caller could
        /// inspect/adjust the freshly created packet before it starts
        /// moving, without this factory growing an ever-longer parameter
        /// list).
        /// </summary>
        public static FoodPacket Create(Vector3 startWorldPosition, Color color)
        {
            var packetObject = new GameObject("FoodPacket");
            packetObject.transform.position = startWorldPosition;
            packetObject.transform.localScale = new Vector3(VisualSize, VisualSize, 1f);

            var packet = packetObject.AddComponent<FoodPacket>();
            packet.SpriteRendererComponent.sprite = SharedSprite;
            packet.SpriteRendererComponent.color = color;

            return packet;
        }

        /// <summary>
        /// Starts this packet's flight from its current position (set by
        /// Create) toward target's own LIVE world position, sampled fresh
        /// every frame for the whole flight - never a position snapshot -
        /// so a moving target (e.g. a FeedTarget riding the Conveyor) is
        /// still reached exactly, regardless of how it moved meanwhile.
        /// onArrived is invoked exactly once, synchronously, the moment the
        /// flight ends, before OnFoodPacketArrived and before this packet
        /// destroys itself.
        /// </summary>
        public void Launch(Transform target, float duration, CollectorView collector, Action<FoodPacket> onArrived)
        {
            _target = target;
            _duration = Mathf.Max(0f, duration);
            Collector = collector;
            _onArrived = onArrived;

            OnFoodPacketLaunched?.Invoke(this);
            StartCoroutine(FlightRoutine());
        }

        private IEnumerator FlightRoutine()
        {
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < _duration)
            {
                elapsed += Time.deltaTime;
                float t = _duration > 0f ? Mathf.Clamp01(elapsed / _duration) : 1f;
                Vector3 targetPosition = _target != null ? _target.position : start;
                transform.position = Vector3.Lerp(start, targetPosition, t);
                yield return null;
            }

            if (_target != null)
                transform.position = _target.position;

            // Arrival is the single synchronization point: both callbacks
            // fire here, synchronously, on the exact frame the packet
            // reaches FeedTarget - never earlier (launch), never later
            // (mid-dissolve). onArrived (hunger decrement, mandatory) always
            // runs first; OnFoodPacketArrived (collector bounce/highlight,
            // and any future optional reaction) second, on the same frame.
            _onArrived?.Invoke(this);
            OnFoodPacketArrived?.Invoke(this);

            yield return DissolveRoutine();

            Destroy(gameObject);
        }

        /// <summary>
        /// Shrinks to nothing and fades to transparent together, over
        /// DissolveDuration - simple on purpose (task explicitly allows
        /// fade, shrink, or both). Runs after every other arrival effect
        /// has already been triggered, so it never delays hunger, the
        /// collector's reaction, or OnFoodPacketArrived; it only delays
        /// this packet's own Destroy.
        /// </summary>
        private IEnumerator DissolveRoutine()
        {
            Vector3 startScale = transform.localScale;
            Color startColor = SpriteRendererComponent.color;
            float elapsed = 0f;

            while (elapsed < DissolveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / DissolveDuration);

                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                SpriteRendererComponent.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, t));

                yield return null;
            }
        }

        private SpriteRenderer SpriteRendererComponent =>
            _spriteRenderer != null ? _spriteRenderer : _spriteRenderer = GetComponent<SpriteRenderer>();

        /// <summary>
        /// One runtime-generated 1x1 white sprite shared by every packet
        /// ever created (tinted per-instance via SpriteRenderer.color, same
        /// approach PixelGrid uses for every PixelCell) - never per-packet,
        /// so packets never allocate a texture of their own.
        /// </summary>
        private static Sprite SharedSprite
        {
            get
            {
                if (_sharedSprite != null)
                    return _sharedSprite;

                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();

                _sharedSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 1f);

                return _sharedSprite;
            }
        }
    }
}
