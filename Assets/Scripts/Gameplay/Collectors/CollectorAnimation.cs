using System;
using System.Collections;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Presentation-only micro-animation of a collector's Visual child
    /// transform — local scale/position/rotation only. Never touches the
    /// collector root, which gameplay owns (CollectorQueueBoard placement,
    /// ConveyorSystem movement, WaitingLine/RecoveryRow reparenting) — see
    /// CollectorView, which creates Visual as a child of that root
    /// specifically so presentation animation never fights gameplay for the
    /// same transform.
    ///
    /// Exactly one tween coroutine is ever active: every Play* method stops
    /// whatever is currently running and snaps Visual back to its authored
    /// baseline (scale one, position zero, rotation identity) before
    /// starting the next one — no animation ever treats a mid-motion
    /// transform as its own new baseline, which is what prevents cumulative
    /// drift across repeated reactions. Once PlayHeartPulseAndCollapse
    /// begins, every other Play* call becomes a no-op: Heart is terminal,
    /// nothing may resume idle/boarding/eating motion after it starts.
    ///
    /// Called only by CollectorPresentation, the sole authority on which
    /// reaction should play when — this component makes no decisions about
    /// hunger, selection, or lifecycle, only how the Visual child moves once
    /// told to.
    /// </summary>
    public class CollectorAnimation : MonoBehaviour
    {
        // Live-tunable in the Inspector, including on a live-selected
        // Collector in Play Mode, specifically so the waiting-idle feel can
        // be calibrated against the real portrait Game view instead of by
        // editing hardcoded constants and restarting. idleBreathingSpeed,
        // idleWidthExpansion, and idleHeightExpansion are all read fresh
        // every frame inside IdleBreathingRoutine, so editing any of them on
        // a running collector changes its very next frame.
        // idleSpeedVariance/idleAmplitudeVariance are only consulted once,
        // in Awake, to bake this collector's own persistent random factor —
        // by design, since that factor is this collector's individuality,
        // not something a live edit should retroactively rewrite; a changed
        // variance only affects collectors spawned after the change (e.g.
        // the next Play Mode run).
        //
        // Deliberately no rotation and no independent vertical-float field:
        // a waiting Mofu must read as planted and breathing, not floating or
        // rocking — see IdleBreathingRoutine for the grounded-expansion
        // approach that replaced them.
        //
        // Calibrated final defaults: quick enough to read clearly at the
        // real portrait Game view scale, but well below the exaggerated
        // diagnostic values (0.8 / 0.10 / 0.12) used to confirm the motion
        // was actually reaching Visual — those made Mofus look like slowly
        // inflating balloons, which these are tuned to avoid.
        [Header("Waiting Idle (Back Idle only: Queue / WaitingLine / RecoveryRow)")]
        [SerializeField, Tooltip("Cycles-per-second speed of the shared waiting-idle breathing wave, before this collector's own random speed variance below is applied. Lower = calmer, slower breathing.")]
        [Min(0f)]
        private float idleBreathingSpeed = 1.05f;

        [SerializeField, Tooltip("Random per-collector speed variance around Idle Breathing Speed, as a fraction (0.1 = ±10%). Drawn once per collector in Awake so collectors do not breathe in lockstep.")]
        [Range(0f, 1f)]
        private float idleSpeedVariance = 0.1f;

        [SerializeField, Tooltip("How much this collector's width expands at the peak of each waiting-idle breath, as a fraction of scale 1 (0.05 = 5%). The body only ever expands from baseline and returns — it never compresses below Vector3.one.")]
        [Min(0f)]
        private float idleWidthExpansion = 0.05f;

        [SerializeField, Tooltip("How much this collector's height expands at the peak of each waiting-idle breath, as a fraction of scale 1 (0.06 = 6%). Never compresses below baseline; the bottom edge is compensated (see IdleBreathingRoutine) so only the top visibly rises, keeping the collector planted.")]
        [Min(0f)]
        private float idleHeightExpansion = 0.06f;

        [SerializeField, Tooltip("Random per-collector amplitude variance applied to both expansion amounts above together, as a fraction (0.1 = ±10%). Drawn once per collector in Awake.")]
        [Range(0f, 1f)]
        private float idleAmplitudeVariance = 0.1f;

        private const float BoardingBounceDuration = 0.22f;
        private const float BoardingBounceAmount = 0.12f;

        private const float EatingPunchDuration = 0.16f;
        private const float EatingPunchAmount = 0.14f;

        private const float SatisfiedPunchDuration = 0.3f;
        private const float SatisfiedPunchAmount = 0.18f;
        private const float SatisfiedHopHeight = 0.05f;

        private const float HeartPulseDuration = 0.35f;
        private const float HeartPulseAmount = 0.25f;
        private const float HeartCollapseDuration = 0.22f;

        private static readonly Vector3 BaselineScale = Vector3.one;
        private static readonly Vector3 BaselinePosition = Vector3.zero;

        private CollectorView _view;
        private Transform _visual;
        private Coroutine _activeRoutine;
        private bool _terminal;

        // Per-collector breathing individuality, all three drawn once in
        // Awake so collectors created together neither start from the same
        // phase, nor breathe at exactly the same speed, nor with exactly
        // the same amplitude. Purely a presentation seed — none of it ever
        // feeds into gameplay state, so this randomness cannot affect
        // deterministic gameplay. _idleSpeedFactor/_idleAmplitudeFactor are
        // multipliers baked once from idleSpeedVariance/idleAmplitudeVariance
        // at spawn time — this collector's individuality, which a later
        // Inspector edit to those two variance fields deliberately does not
        // retroactively rewrite (see the field comments above).
        private float _idlePhase;
        private float _idleSpeedFactor;
        private float _idleAmplitudeFactor;

        // Visual's sprite height in its own local space (Sprite.bounds — a
        // fixed asset-level value, unlike SpriteRenderer.bounds, which is a
        // world-space AABB that changes with the transform's current scale
        // and would need re-baselining every frame). Cached once, the first
        // time Visual is resolved, and never recomputed from a live bounds
        // query afterwards — see EnsureVisual/CacheBaselineLocalHeight and
        // IdleBreathingRoutine's grounding compensation, which depends on
        // this staying a fixed baseline throughout.
        private float _baselineLocalHeight = 1f;

        private CollectorView View => _view != null ? _view : _view = GetComponent<CollectorView>();

        /// <summary>
        /// True once this component can actually resolve the Visual child to
        /// animate. CollectorPresentation checks this before committing to
        /// the terminal Satisfied/Heart sequence, so a missing Visual falls
        /// back to immediate destruction instead of starting a reaction that
        /// could never complete or fire its completion callback.
        /// </summary>
        public bool HasVisual => EnsureVisual() != null;

        private void Awake()
        {
            _idlePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _idleSpeedFactor = UnityEngine.Random.Range(1f - idleSpeedVariance, 1f + idleSpeedVariance);
            _idleAmplitudeFactor = UnityEngine.Random.Range(1f - idleAmplitudeVariance, 1f + idleAmplitudeVariance);
        }

        /// <summary>
        /// Starts (or resumes) the subtle waiting-idle breathing loop, used
        /// only while a collector shows Back Idle — a Front Eating rider's
        /// own bite reactions already provide its active motion, so nothing
        /// calls this while riding. Every call re-enters the sine wave at
        /// this collector's own _idlePhase rather than phase zero, so a
        /// collector that stops and later restarts breathing (e.g. queue →
        /// board → rollback back to queue) always resumes from the same
        /// individual point other collectors don't share, instead of
        /// resyncing with them. Also runs at this collector's own
        /// _idleSpeedFactor times idleBreathingSpeed, not the shared speed
        /// alone, for the same reason — see IdleBreathingRoutine for the
        /// grounded breathing motion this actually plays.
        /// </summary>
        public void PlayIdleBreathing() =>
            StartRoutine(IdleBreathingRoutine());

        public void PlayBoardingBounce(Action onComplete = null) =>
            StartRoutine(SquashPunchRoutine(BoardingBounceDuration, BoardingBounceAmount, onComplete));

        public void PlayEatingPunch(Action onComplete = null) =>
            StartRoutine(SquashPunchRoutine(EatingPunchDuration, EatingPunchAmount, onComplete));

        public void PlaySatisfiedPunch(Action onComplete = null) =>
            StartRoutine(SatisfiedPunchRoutine(onComplete));

        /// <summary>
        /// Terminal: one heart pulse, then a collapse to nothing. No
        /// animation may play after this — every later Play* call becomes a
        /// no-op for the rest of this collector's lifetime.
        /// </summary>
        public void PlayHeartPulseAndCollapse(Action onComplete) =>
            StartRoutine(HeartPulseAndCollapseRoutine(onComplete), forceTerminal: true);

        private Transform EnsureVisual()
        {
            if (_visual != null)
                return _visual;

            CollectorView view = View;
            if (view == null)
                return null;

            _visual = view.Visual;
            CacheBaselineLocalHeight();
            return _visual;
        }

        /// <summary>
        /// Reads Visual's currently-applied sprite's local-space bounds
        /// height exactly once and keeps it for the rest of this collector's
        /// lifetime. Called right after Visual is first resolved — by then
        /// CollectorPresentation has always already applied a sprite (e.g.
        /// ShowWaitingBackIdle applies Back Idle before ever calling
        /// PlayIdleBreathing), so there is always a real sprite to measure.
        /// Falls back to leaving the default (1) in place if a sprite
        /// genuinely is not resolvable, since a wrong-but-finite baseline
        /// is safer than dividing by/multiplying against zero.
        /// </summary>
        private void CacheBaselineLocalHeight()
        {
            if (_visual == null)
                return;

            var spriteRenderer = _visual.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.sprite != null)
                _baselineLocalHeight = spriteRenderer.sprite.bounds.size.y;
        }

        private void StartRoutine(IEnumerator routine, bool forceTerminal = false)
        {
            if (_terminal)
                return;

            if (EnsureVisual() == null)
                return;

            if (_activeRoutine != null)
                StopCoroutine(_activeRoutine);

            ResetToBaseline();

            if (forceTerminal)
                _terminal = true;

            _activeRoutine = StartCoroutine(routine);
        }

        private void ResetToBaseline()
        {
            _visual.localScale = BaselineScale;
            _visual.localPosition = BaselinePosition;
            _visual.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Planted "breathing", not floating or rocking: a normalized
        /// breath in [0, 1] — (sin(t) + 1) * 0.5, never the raw -1..1 wave —
        /// drives width and height expansion together from the same cycle,
        /// so the body only ever grows from its authored baseline and
        /// returns to it, never compressing below Vector3.one and never
        /// looking like jelly. No rotation and no independent vertical
        /// float: Visual's own local Y position is touched for exactly one
        /// reason — to compensate for the centered sprite pivot, which
        /// would otherwise raise and lower the feet by the same amount as
        /// the head every time height scale changes. Shifting Visual up by
        /// half of the height just gained (using _baselineLocalHeight, this
        /// collector's fixed local sprite height, never a live bounds query)
        /// keeps the bottom visible edge exactly where it was at baseline,
        /// so only the top of the fur visibly rises — reading as standing
        /// and breathing, not bouncing. Resolves to the exact authored
        /// baseline (ResetToBaseline) the moment anything else starts.
        ///
        /// idleBreathingSpeed/idleWidthExpansion/idleHeightExpansion are
        /// read directly from the serialized fields every iteration (never
        /// cached into a local), so editing any of them in the Inspector —
        /// including on a live-selected collector in Play Mode — changes
        /// this loop's very next frame.
        /// </summary>
        private IEnumerator IdleBreathingRoutine()
        {
            float t = _idlePhase;
            while (true)
            {
                t += Time.deltaTime * idleBreathingSpeed * _idleSpeedFactor;
                float breath = (Mathf.Sin(t) + 1f) * 0.5f;

                float widthScale = 1f + breath * idleWidthExpansion * _idleAmplitudeFactor;
                float heightScale = 1f + breath * idleHeightExpansion * _idleAmplitudeFactor;

                _visual.localScale = new Vector3(widthScale, heightScale, 1f);
                _visual.localPosition = new Vector3(0f, _baselineLocalHeight * 0.5f * (heightScale - 1f), 0f);

                yield return null;
            }
        }

        private IEnumerator SquashPunchRoutine(float duration, float amount, Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float wave = Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI);
                _visual.localScale = new Vector3(1f + wave * amount * 0.5f, 1f - wave * amount, 1f);
                yield return null;
            }

            _visual.localScale = BaselineScale;
            onComplete?.Invoke();
        }

        private IEnumerator SatisfiedPunchRoutine(Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < SatisfiedPunchDuration)
            {
                elapsed += Time.deltaTime;
                float wave = Mathf.Sin(Mathf.Clamp01(elapsed / SatisfiedPunchDuration) * Mathf.PI);
                _visual.localScale = new Vector3(1f - wave * SatisfiedPunchAmount * 0.5f, 1f + wave * SatisfiedPunchAmount, 1f);
                _visual.localPosition = new Vector3(0f, wave * SatisfiedHopHeight, 0f);
                yield return null;
            }

            _visual.localScale = BaselineScale;
            _visual.localPosition = BaselinePosition;
            onComplete?.Invoke();
        }

        private IEnumerator HeartPulseAndCollapseRoutine(Action onComplete)
        {
            float elapsed = 0f;
            while (elapsed < HeartPulseDuration)
            {
                elapsed += Time.deltaTime;
                float wave = Mathf.Sin(Mathf.Clamp01(elapsed / HeartPulseDuration) * Mathf.PI);
                float scale = 1f + wave * HeartPulseAmount;
                _visual.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            _visual.localScale = BaselineScale;

            elapsed = 0f;
            while (elapsed < HeartCollapseDuration)
            {
                elapsed += Time.deltaTime;
                float scale = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / HeartCollapseDuration));
                _visual.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            _visual.localScale = Vector3.zero;
            onComplete?.Invoke();
        }
    }
}
