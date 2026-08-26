using Project001.Gameplay.Levels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project001.Gameplay
{
    /// <summary>
    /// Owns which level is "current" for this Play Mode session and how to
    /// resolve the next one. LevelBootstrapper asks this controller for the
    /// LevelId to build instead of deciding itself. No UI knowledge, no save
    /// system, no unlock/replay/map logic — the simplest deterministic
    /// level_001 -&gt; level_002 progression only.
    ///
    /// Uses one static field to remember the current LevelId across the
    /// scene reload LoadNextLevel performs — a MonoBehaviour instance cannot
    /// survive SceneManager.LoadScene(&lt;same scene&gt;) on its own, and the usual
    /// alternative (DontDestroyOnLoad plus a duplicate-instance guard in
    /// Awake) is a singleton in every way that matters, which needs either a
    /// static reference or FindObjectOfType to detect the duplicate — both
    /// explicitly avoided here. A bare static field is the narrowest option
    /// left: it holds one piece of session-scoped data, not a registry or
    /// locator, and it only ever changes through this class's own methods.
    ///
    /// Correctness here does not depend on Unity reloading the C# domain
    /// between Play Mode sessions — with Enter Play Mode Options set to skip
    /// Domain Reload, the fields' initializers would only run once, ever,
    /// and a session that ended on level_002 would incorrectly resume
    /// there. ResetProgressionForNewSession, tagged
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)],
    /// explicitly resets both _currentLevelId and _isSessionInitialized once
    /// at the start of every session regardless of that setting, matching
    /// "no save system yet" unconditionally.
    ///
    /// _isSessionInitialized is what lets this class distinguish the first
    /// Bootstrap load of a session from a later scene reload
    /// (LoadNextLevel, Retry): see GetOrInitializeCurrentLevel.
    /// </summary>
    public class LevelProgressionController : MonoBehaviour
    {
        private static readonly LevelId[] LevelOrder =
        {
            LevelCatalog.PrototypeLevelId,
            LevelCatalog.SecondTestLevelId,
        };

        private static LevelId _currentLevelId = LevelOrder[0];
        private static bool _isSessionInitialized;

        /// <summary>
        /// Explicitly rearms session progression to LevelOrder[0] and clears
        /// _isSessionInitialized. Runs once at the start of every Play
        /// Mode/application session — before any scene loads — regardless
        /// of whether Domain Reload is enabled, so a session is never able
        /// to inherit state left over from a previous session. Scene
        /// reloads within a session (e.g. LoadNextLevel, Retry) do not
        /// re-trigger this method.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProgressionForNewSession()
        {
            _currentLevelId = LevelOrder[0];
            _isSessionInitialized = false;
        }

        /// <summary>
        /// The level this Play Mode session is currently on.
        /// </summary>
        public LevelId GetCurrentLevelId() => _currentLevelId;

        /// <summary>
        /// Called by LevelBootstrapper on every Bootstrap Awake to resolve
        /// which level this Awake should build. On the first call of a
        /// session (_isSessionInitialized still false, set by
        /// ResetProgressionForNewSession), adopts startingLevelId as
        /// _currentLevelId and marks the session initialized. On every
        /// later call within the same session — Bootstrap reloads caused by
        /// LoadNextLevel or RetryCurrentLevel — startingLevelId is ignored
        /// and the existing _currentLevelId is returned unchanged, so
        /// progression already made during the session is never clobbered
        /// by the Inspector's Starting Level Id.
        /// </summary>
        public LevelId GetOrInitializeCurrentLevel(LevelId startingLevelId)
        {
            if (!_isSessionInitialized)
            {
                _currentLevelId = startingLevelId;
                _isSessionInitialized = true;
            }

            return _currentLevelId;
        }

        /// <summary>
        /// The current level's 1-based position in LevelOrder (level_001 -&gt;
        /// 1, level_002 -&gt; 2, ...) — the single source of truth for the
        /// number a "LEVEL {number}" HUD label displays, so that label never
        /// parses or duplicates LevelId's own string content. Falls back to
        /// 1 if _currentLevelId is somehow not present in LevelOrder (should
        /// not happen given GetOrInitializeCurrentLevel only ever assigns
        /// values from LevelOrder), rather than returning 0 or a negative
        /// number to a caller that will display it directly.
        /// </summary>
        public int GetCurrentLevelNumber()
        {
            for (int i = 0; i < LevelOrder.Length; i++)
            {
                if (LevelOrder[i] == _currentLevelId)
                    return i + 1;
            }

            return 1;
        }

        /// <summary>
        /// The level after the current one in LevelOrder. Returns
        /// GetCurrentLevelId() unchanged when the current level is last —
        /// callers detect "no next level" by comparing the result against
        /// GetCurrentLevelId(), keeping this a plain LevelId-returning query
        /// rather than a Try-pattern or nullable value.
        /// </summary>
        public LevelId GetNextLevelId()
        {
            for (int i = 0; i < LevelOrder.Length - 1; i++)
            {
                if (LevelOrder[i] == _currentLevelId)
                    return LevelOrder[i + 1];
            }

            return _currentLevelId;
        }

        /// <summary>
        /// Advances to the next level and reloads the active scene so
        /// LevelBootstrapper rebuilds it fresh with the new current level.
        /// Never throws or crashes when there is no next level — logs and
        /// leaves the current level unchanged instead.
        /// </summary>
        public void LoadNextLevel()
        {
            LevelId nextLevelId = GetNextLevelId();

            if (nextLevelId == _currentLevelId)
            {
                Debug.Log("No next level available.");
                return;
            }

            _currentLevelId = nextLevelId;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
