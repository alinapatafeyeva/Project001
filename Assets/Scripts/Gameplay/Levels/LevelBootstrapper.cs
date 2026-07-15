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
    /// Resolves its LevelDefinition from a LevelCatalog by LevelId, and its
    /// MatchTypeId-to-Color presentation mapping from a separate
    /// MatchTypePresentation. A future CurrentProgressLevelId source replaces
    /// only where the LevelId passed to the catalog comes from, without
    /// changing the catalog itself or how the systems below are built from
    /// its result.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelBootstrapper : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid built from the level's pixel layout.")]
        private PixelGrid pixelGrid;

        [SerializeField, Tooltip("Conveyor configured with the level's capacity and move speed.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Waiting line built with the level's capacity.")]
        private Project001.Gameplay.WaitingLine.WaitingLine waitingLine;

        [SerializeField, Tooltip("Board built from the level's collector queues.")]
        private CollectorQueueBoard collectorQueueBoard;

        [SerializeField, Tooltip("Test-only LevelId this scene loads from the LevelCatalog. A future CurrentProgressLevelId source replaces this field without changing anything below it. Known test ids: level_001, level_002.")]
        private string testLevelId = "level_001";

        private readonly LevelCatalog _levelCatalog = new LevelCatalog();
        private readonly MatchTypePresentation _presentation = new MatchTypePresentation();

        private void Awake()
        {
            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(testLevelId))
            {
                Debug.LogError($"LevelBootstrapper: '{name}' has no LevelId configured; aborting bootstrap.", this);
                enabled = false;
                return;
            }

            if (!_levelCatalog.TryGetLevel(new LevelId(testLevelId), out LevelDefinition levelDefinition))
            {
                Debug.LogError($"LevelBootstrapper: '{name}' has no approved level for LevelId '{testLevelId}'; aborting bootstrap.", this);
                enabled = false;
                return;
            }

            BuildLevel(levelDefinition);
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

            return isValid;
        }

        private void BuildLevel(LevelDefinition levelDefinition)
        {
            pixelGrid.Initialize(levelDefinition.PixelLayout, _presentation.GetColor);
            conveyorSystem.Configure(levelDefinition.ConveyorCapacity, levelDefinition.ConveyorMoveSpeed);
            waitingLine.Initialize(levelDefinition.WaitingLineCapacity);
            collectorQueueBoard.Initialize(levelDefinition.CollectorQueues, _presentation.GetColor);
        }
    }
}
