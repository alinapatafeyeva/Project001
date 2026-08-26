using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns the player-selected gameplay speed (1x/2x) via Time.timeScale —
    /// the same mechanism GameplayFlowController already uses for pause
    /// (Time.timeScale = 0), since every gameplay-relevant per-frame system
    /// in this project (ConveyorSystem, CollectorAnimation, FoodPacket,
    /// PixelConsumer, ConveyorBeltAnimation, CharacterPosePresentation)
    /// already reads Time.deltaTime rather than Time.unscaledDeltaTime —
    /// see GameplayConstants' own remarks foreshadowing exactly this: "a
    /// multiplier shared by conveyor movement, animations, effects, and
    /// timers rather than implemented only inside ConveyorSystem." A single
    /// global Time.timeScale write here reaches all of them uniformly,
    /// without a second per-system speed implementation.
    ///
    /// This class never touches pause state directly: GameplayFlowController
    /// remains the sole owner of PauseGameplay/ResumeGameplay. Because
    /// PauseGameplay saves whatever Time.timeScale is current (1 or 2, the
    /// selection this class last applied) and ResumeGameplay restores that
    /// exact value, the selected speed survives pause/resume automatically
    /// with no coordination code needed on either side — see
    /// GameplayFlowController's own remarks. Correctness here therefore
    /// depends on ToggleSpeed only ever being reachable while gameplay is
    /// NOT paused (the top HUD's speed button is unreachable behind the
    /// Pause/Exit modal's own full-screen backdrop while either is open —
    /// see Project001.UI.Hud), so this class deliberately does not guard
    /// against being called during a pause; that scenario is prevented by
    /// construction, not defended against here.
    ///
    /// Resets to 1x on every Awake — a fresh MonoBehaviour instance every
    /// scene load (Retry, LoadNextLevel, or a session's first Bootstrap
    /// load) — rather than only on first-ever session start: Time.timeScale
    /// is a global engine setting that survives a scene reload on its own
    /// (see VictoryFlowController/FailureRecoveryController's own remarks on
    /// exactly this), so without this reset a level reloaded while 2x was
    /// selected would silently start at 2x instead of the deterministic
    /// default every fresh attempt should have.
    /// </summary>
    public class GameplaySpeedController : MonoBehaviour
    {
        private const float NormalSpeedMultiplier = 1f;
        private const float FastSpeedMultiplier = 2f;

        private bool _isFastSpeedSelected;

        /// <summary>Whether 2x is currently selected. False (1x) is always the default on a fresh level.</summary>
        public bool IsFastSpeedSelected => _isFastSpeedSelected;

        /// <summary>The Time.timeScale value the current selection applies: 1 or 2.</summary>
        public float CurrentSpeedMultiplier => _isFastSpeedSelected ? FastSpeedMultiplier : NormalSpeedMultiplier;

        private void Awake()
        {
            _isFastSpeedSelected = false;
            Time.timeScale = NormalSpeedMultiplier;
        }

        /// <summary>
        /// Flips the selection between 1x and 2x and applies it immediately
        /// via Time.timeScale. See class remarks for why this must only ever
        /// be called while gameplay is not paused.
        /// </summary>
        public void ToggleSpeed()
        {
            _isFastSpeedSelected = !_isFastSpeedSelected;
            Time.timeScale = CurrentSpeedMultiplier;
        }
    }
}
