using UnityEngine;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns what the Exit confirmation dialog actually does, regardless of
    /// whether it was opened directly from the top HUD's Exit button or from
    /// the Pause modal's Exit Level button — one implementation for both
    /// entry points, never a second one duplicated per caller. Presentation
    /// (Project001.UI.Hud.TopHudUI, PauseUI, ExitConfirmationUI) only ever
    /// calls OpenExitConfirmation/CancelExit/ConfirmExit; it never touches
    /// GameplayFlowController or Time.timeScale directly, mirroring
    /// VictoryFlowController/FailureRecoveryController's own convention.
    ///
    /// OpenExitConfirmation/CancelExit only run when the dialog transitions
    /// gameplay's own paused/running state — opened directly from the top
    /// HUD (gameplay was running) or cancelled back to direct gameplay
    /// (Stay). When the dialog instead opens from within the already-paused
    /// Pause modal, or Stay returns to that already-open Pause modal,
    /// neither method is called at all — presentation swaps which panel is
    /// visible without this controller pausing/resuming anything, since
    /// gameplay's paused state does not actually change in that case. See
    /// PauseUI.OnExitLevelPressed.
    ///
    /// ConfirmExit is intentionally a stub. This project has no destination
    /// a confirmed Exit could navigate to yet: only Bootstrap.unity and the
    /// unrelated default SampleScene.unity are registered in Build Settings
    /// (see EditorBuildSettings.asset), there is no MainMenu/LevelSelect
    /// scene, and no navigation controller exists anywhere in the codebase.
    /// Inventing a scene load here would mean guessing at a destination and
    /// a navigation architecture that do not exist — explicitly out of
    /// scope (see the top gameplay HUD task's own stop conditions). Once a
    /// real destination/navigation path exists, replace this method's body
    /// with the actual navigation call; no other change should be needed,
    /// since every caller already funnels through this one method.
    /// </summary>
    public class LevelExitFlowController : MonoBehaviour
    {
        [SerializeField, Tooltip("Sole owner of pause/resume state — reached only through its own API, never bypassed with a direct Time.timeScale assignment here.")]
        private GameplayFlowController gameplayFlowController;

        /// <summary>Suspends gameplay while the Exit confirmation dialog opened directly from the top HUD (gameplay was running beforehand).</summary>
        public void OpenExitConfirmation()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.PauseGameplay();
        }

        /// <summary>Stay: resumes the exact gameplay state (including selected speed) that existed before the dialog opened directly from the top HUD.</summary>
        public void CancelExit()
        {
            if (gameplayFlowController != null)
                gameplayFlowController.ResumeGameplay();
        }

        /// <summary>
        /// Exit, confirmed. No valid destination exists yet — see class
        /// remarks. Leaves gameplay exactly as paused as it already was;
        /// never resumes or reloads anything on its own.
        /// </summary>
        public void ConfirmExit()
        {
            Debug.LogWarning("LevelExitFlowController.ConfirmExit(): no level-exit destination exists yet (no MainMenu/LevelSelect scene is registered in Build Settings and no navigation controller exists). Gameplay stays paused instead of navigating anywhere — implement this once a real destination is available.");
        }
    }
}
