using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Pixels;
using UnityEngine;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Runtime entry point for one gameplay scene. Builds PixelGrid,
    /// ConveyorSystem, WaitingLine, and CollectorQueueBoard from a
    /// LevelDefinition on Awake, so the scene works after entering Play Mode
    /// or being loaded fresh — none of those systems' generated state is
    /// expected to survive Unity scene serialization, only this component's
    /// own object references do.
    ///
    /// Resolves its LevelDefinition from a LevelCatalog by LevelId. Pixel
    /// colour presentation is resolved entirely inside PixelGrid now, from
    /// each cell's own Match ID (see PixelLayoutDefinition/ColorPalette) —
    /// this class injects no separate colour mapping any more. Does not own
    /// progression — every Awake it
    /// asks levelProgressionController.GetOrInitializeCurrentLevel(startingLevelId)
    /// for the LevelId to build, and does not itself decide whether this is
    /// the first Bootstrap load of the session or a later reload
    /// (LoadNextLevel, Retry): LevelProgressionController owns that
    /// distinction and applies startingLevelId only once per session,
    /// returning its existing current level unchanged on every later call.
    ///
    /// startingLevelId lets a developer launch any approved level directly
    /// from the Inspector (default "level_001") without a separate
    /// enable/disable checkbox — changing it changes where a new session
    /// starts; it has no effect on a session already in progress.
    ///
    /// levelProgressionController is required. A later Victory's Continue
    /// still goes through the normal VictoryFlowController -&gt;
    /// LevelProgressionController.LoadNextLevel() path unchanged — only the
    /// initial level source is affected here.
    ///
    /// Optionally derives a Bootstrap-only, deterministic Failure test level
    /// from the resolved approved level via FailureTestLevelFactory — see
    /// enableFailureTestSetup. The approved LevelDefinition and LevelCatalog
    /// are never mutated by this.
    ///
    /// Optionally replaces the resolved approved level entirely with a
    /// Bootstrap-only, deterministic Feeding Flow test level (all 20 Match
    /// IDs, real feeding pipeline) via FeedingFlowTestLevelFactory — see
    /// enableFeedingFlowTestSetup. Mutually exclusive with
    /// enableFailureTestSetup; if both are enabled, Feeding Flow wins and an
    /// error is logged.
    ///
    /// Conveyor capacity, conveyor move speed, and Waiting Line capacity all
    /// come from GameplayConstants, never from the resolved LevelDefinition —
    /// they are global gameplay rules identical for every normal level, not
    /// level-authored data.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelBootstrapper : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid built from the level's pixel layout.")]
        private PixelGrid pixelGrid;

        [SerializeField, Tooltip("Conveyor configured entirely from GameplayConstants (BaseConveyorCapacity, BaseConveyorMoveSpeed) — the selected level never influences conveyor capacity or move speed.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Waiting line built with GameplayConstants.WaitingLineCapacity — same for every level.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("Board built from the level's collector queues.")]
        private CollectorQueueBoard collectorQueueBoard;

        [SerializeField, Tooltip("Notified via NotifyLevelBuilt(), as the last step of BuildLevel, once every system above is populated — the only deterministic point at which Endgame Cleanup's initial RemainingCollectors check can safely run.")]
        private EndgameCleanupController endgameCleanupController;

        [SerializeField, Tooltip("Required. Asked every Awake for the LevelId to build via GetOrInitializeCurrentLevel(startingLevelId) — LevelProgressionController decides whether Starting Level Id applies (first load of the session) or is ignored (later reload).")]
        private LevelProgressionController levelProgressionController;

        [SerializeField, Tooltip("LevelId a new Play Mode/application session starts on. Applied once, at the first Bootstrap load of the session; ignored on later reloads (Victory's Continue, Retry), which keep whatever level the session already progressed to. Change this directly to launch any approved level from the Inspector. Known ids: level_001, level_002.")]
        private string startingLevelId = "level_001";

        [SerializeField, Tooltip("Bootstrap-only deterministic Failure test setup. When enabled, prepends GameplayConstants.WaitingLineCapacity + 1 debug collectors (a dedicated MatchTypeId that never appears on any approved pixel layout) to the resolved level's queues, so the first GameplayConstants.WaitingLineCapacity fill the Waiting Line and the next one triggers Failure on demand. Never mutates LevelCatalog or approved level data; the derived level is never validated by LevelDefinitionValidator, since it is intentionally invalid test data. Mutually exclusive with Enable Feeding Flow Test Setup below — do not enable both at once.")]
        private bool enableFailureTestSetup = false;

        [SerializeField, Tooltip("Bootstrap-only deterministic Feeding Flow test setup. When enabled, replaces the resolved level entirely with FeedingFlowTestLevelFactory's own level: all 20 Match IDs, one collector each across 4 queues, a dedicated 20x6 pixel layout giving every Match ID 6 real, matching pixels, and a debug-only large hunger capacity (FeedingFlowTestLevelFactory.FeedingFlowTestHungerCapacity) so collectors normally survive long enough to reach WaitingLine/RecoveryRow instead of being satisfied instantly - except the one Match ID FeedingFlowTestLevelFactory deliberately exempts, whose hunger exactly equals its pixel supply so it CAN be satisfied deliberately. Every collector still goes through the real PixelGrid -> PixelConsumer -> FoodFlightController -> FoodPacket -> FeedTarget chain, unlike Enable Failure Test Setup's permanently-unmatched debug collectors. Never mutates LevelCatalog or startingLevelId's approved level; the derived level is never validated by LevelDefinitionValidator, since its hunger surplus is intentionally unbalanced test data. Mutually exclusive with Enable Failure Test Setup above - do not enable both at once.")]
        private bool enableFeedingFlowTestSetup = false;

        private readonly LevelCatalog _levelCatalog = new LevelCatalog();

        private void Awake()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(startingLevelId))
            {
                Debug.LogError($"LevelBootstrapper: '{name}' has no Starting Level Id configured; aborting bootstrap.", this);
                enabled = false;
                return;
            }

            var configuredStartingLevelId = new LevelId(startingLevelId);
            LevelId currentLevelId = levelProgressionController.GetOrInitializeCurrentLevel(configuredStartingLevelId);

            if (!_levelCatalog.TryGetLevel(currentLevelId, out LevelDefinition approvedLevel))
            {
                Debug.LogError($"LevelBootstrapper: '{name}' has no approved level for LevelId '{currentLevelId}'; aborting bootstrap.", this);
                enabled = false;
                return;
            }

            LevelDefinition levelToBuild = approvedLevel;

            if (enableFailureTestSetup && enableFeedingFlowTestSetup)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' has both Enable Failure Test Setup and Enable Feeding Flow Test Setup checked; they are mutually exclusive. Using Enable Feeding Flow Test Setup and ignoring Enable Failure Test Setup this session — uncheck one in the Inspector.", this);
                enableFailureTestSetup = false;
            }

            if (enableFeedingFlowTestSetup)
            {
                levelToBuild = FeedingFlowTestLevelFactory.BuildFeedingFlowTestLevel();
                Debug.Log($"(DEBUG) LevelBootstrapper: '{name}' Enable Feeding Flow Test Setup is active — building the dedicated all-20-Match-ID feeding test level instead of '{currentLevelId}'.", this);
            }
            else if (enableFailureTestSetup)
            {
                int debugCollectorCount = GameplayConstants.WaitingLineCapacity + 1;
                levelToBuild = FailureTestLevelFactory.BuildFailureTestLevel(approvedLevel, debugCollectorCount);
                Debug.Log($"(DEBUG) LevelBootstrapper: '{name}' Enable Failure Test Setup is active — prepending {debugCollectorCount} debug collectors to trigger Failure on demand.", this);
            }

            BuildLevel(levelToBuild);
        }

        /// <summary>
        /// Confirms every system this bootstrapper builds is actually wired
        /// up. Logs one specific error per missing reference rather than
        /// stopping at the first, so a misconfigured scene reports every
        /// problem at once instead of one Inspector fix at a time.
        /// </summary>
        private bool ValidateReferences()
        {
            bool isValid = true;

            if (pixelGrid == null)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' is missing its PixelGrid reference; level will not be built.", this);
                isValid = false;
            }

            if (conveyorSystem == null)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' is missing its ConveyorSystem reference; level will not be built.", this);
                isValid = false;
            }

            if (waitingLine == null)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' is missing its WaitingLine reference; level will not be built.", this);
                isValid = false;
            }

            if (collectorQueueBoard == null)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' is missing its CollectorQueueBoard reference; level will not be built.", this);
                isValid = false;
            }

            if (endgameCleanupController == null)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' is missing its EndgameCleanupController reference; level will not be built.", this);
                isValid = false;
            }

            if (levelProgressionController == null)
            {
                Debug.LogError($"LevelBootstrapper: '{name}' is missing its LevelProgressionController reference; level will not be built.", this);
                isValid = false;
            }

            return isValid;
        }

        private void BuildLevel(LevelDefinition levelDefinition)
        {
            pixelGrid.Initialize(levelDefinition.PixelLayout);
            conveyorSystem.Configure(GameplayConstants.BaseConveyorCapacity, GameplayConstants.BaseConveyorMoveSpeed);
            waitingLine.Initialize(GameplayConstants.WaitingLineCapacity);
            collectorQueueBoard.Initialize(levelDefinition.CollectorQueues);

            // Last, and only after every system above is fully populated —
            // this is the sole deterministic point at which Endgame
            // Cleanup's initial RemainingCollectors check can safely run.
            endgameCleanupController.NotifyLevelBuilt();
        }
    }
}
