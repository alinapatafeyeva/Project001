using Project001.Gameplay.Collectors;
using Project001.Gameplay.Victory;
using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Explicitly controls whether gameplay interaction is active. Reacts to
    /// VictoryController.OnVictory by pausing; exposes PauseGameplay and
    /// ResumeGameplay as a small reusable API a future Failure UI can call
    /// too — neither method is Victory-specific. Holds no Canvas, Button,
    /// Text, or other presentation reference of its own; the level always
    /// stays visible, only time-based movement and selection stop.
    ///
    /// Time.timeScale alone does not stop CollectorSelectionController — its
    /// Update() reads the New Input System's Pointer directly and keeps
    /// running regardless of timescale — so selection/launching is disabled
    /// explicitly via its enabled flag, the same way Unity stops any
    /// MonoBehaviour's per-frame logic.
    /// </summary>
    public class GameplayFlowController : MonoBehaviour
    {
        [SerializeField, Tooltip("Victory detector this controller reacts to by pausing gameplay. Optional — if missing, only manual PauseGameplay/ResumeGameplay calls have any effect.")]
        private VictoryController victoryController;

        [SerializeField, Tooltip("Disabled while gameplay is paused so neither selection nor conveyor launching can happen, regardless of Time.timeScale. Optional — if missing, selection is simply left as-is.")]
        private CollectorSelectionController collectorSelectionController;

        private bool _isGameplayPaused;
        private float _savedTimeScale;
        private bool _savedCollectorSelectionEnabled;

        private void Awake()
        {
            if (victoryController != null)
                victoryController.OnVictory += PauseGameplay;
        }

        private void OnDestroy()
        {
            if (victoryController != null)
                victoryController.OnVictory -= PauseGameplay;

            // Never leave the game frozen because this controller went away
            // while gameplay was paused.
            if (_isGameplayPaused)
                ResumeGameplay();
        }

        /// <summary>
        /// Remembers the current Time.timeScale and CollectorSelectionController
        /// enabled state, then stops time-based gameplay (conveyor movement,
        /// pixel-consumption cooldowns, via Time.timeScale) and explicitly
        /// disables collector selection and launching. Never hides, destroys,
        /// or otherwise touches the level's visibility. A no-op while already
        /// paused — it never overwrites the already-saved pre-pause state —
        /// and safe if collectorSelectionController is missing or destroyed.
        /// </summary>
        public void PauseGameplay()
        {
            if (_isGameplayPaused)
                return;

            _savedTimeScale = Time.timeScale;
            _savedCollectorSelectionEnabled = collectorSelectionController != null && collectorSelectionController.enabled;

            _isGameplayPaused = true;
            Time.timeScale = 0f;

            if (collectorSelectionController != null)
                collectorSelectionController.enabled = false;
        }

        /// <summary>
        /// Restores the Time.timeScale and CollectorSelectionController
        /// enabled state saved by PauseGameplay. A no-op if gameplay is not
        /// currently paused — including if PauseGameplay was never called.
        /// </summary>
        public void ResumeGameplay()
        {
            if (!_isGameplayPaused)
                return;

            _isGameplayPaused = false;
            Time.timeScale = _savedTimeScale;

            if (collectorSelectionController != null)
                collectorSelectionController.enabled = _savedCollectorSelectionEnabled;
        }
    }
}
