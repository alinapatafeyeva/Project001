using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Temporary source of the prototype LevelDefinition and its
    /// MatchTypeId-to-Color presentation mapping. A future LevelCatalog
    /// replaces CreateLevel as the source of the LevelDefinition to load
    /// (looked up by CurrentProgressLevelId), without changing how
    /// LevelBootstrapper builds gameplay systems from it.
    /// </summary>
    public class PrototypeLevelProvider
    {
        private static readonly LevelId PrototypeLevelId = new LevelId("level_prototype_001");

        private static readonly MatchTypeId MatchType1 = new MatchTypeId("m001");
        private static readonly MatchTypeId MatchType2 = new MatchTypeId("m002");
        private static readonly MatchTypeId MatchType3 = new MatchTypeId("m003");
        private static readonly MatchTypeId MatchType4 = new MatchTypeId("m004");

        private static readonly Dictionary<MatchTypeId, Color> PrototypePresentation = new Dictionary<MatchTypeId, Color>
        {
            { MatchType1, Color.red },
            { MatchType2, Color.green },
            { MatchType3, Color.blue },
            { MatchType4, Color.yellow },
        };

        /// <summary>
        /// The prototype's one fixed, deterministic LevelDefinition. Reproduces
        /// the pre-migration prototype's effective values exactly: a 6x6 pixel
        /// grid cycling through 3 match types, 4 queues of 5 collectors each
        /// cycling through 4 match types (the 4th never appears on the grid,
        /// intentionally preserving the always-hungry collector the prototype
        /// uses to exercise Waiting Line and failure behaviour), hunger
        /// capacity 3, conveyor capacity 5, move speed 2, and a 5-slot
        /// Waiting Line.
        /// </summary>
        public LevelDefinition CreateLevel()
        {
            return new LevelDefinition(
                PrototypeLevelId,
                BuildPrototypePixelLayout(),
                BuildPrototypeCollectorQueues(),
                conveyorCapacity: 5,
                conveyorMoveSpeed: 2f,
                waitingLineCapacity: 5);
        }

        /// <summary>
        /// The prototype's only presentation mapping — the single source both
        /// pixel and collector visuals draw from. Gameplay never infers
        /// MatchTypeId from colour; this is the one place colour is derived
        /// from MatchTypeId, and only for the prototype's temporary
        /// presentation.
        /// </summary>
        public Color GetPresentationColor(MatchTypeId matchTypeId)
        {
            return PrototypePresentation.TryGetValue(matchTypeId, out Color color) ? color : Color.magenta;
        }

        private static PixelLayoutDefinition BuildPrototypePixelLayout()
        {
            const int width = 6;
            const int height = 6;

            var matchTypes = new[] { MatchType1, MatchType2, MatchType3 };
            var cells = new List<MatchTypeId>(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    cells.Add(matchTypes[(x + y) % matchTypes.Length]);
            }

            return new PixelLayoutDefinition(width, height, cells);
        }

        private static IReadOnlyList<CollectorQueueDefinition> BuildPrototypeCollectorQueues()
        {
            const int queueCount = 4;
            const int collectorsPerQueue = 5;
            const int hungerCapacity = 3;

            var matchTypes = new[] { MatchType1, MatchType2, MatchType3, MatchType4 };
            var queues = new List<CollectorQueueDefinition>(queueCount);

            for (int queueIndex = 0; queueIndex < queueCount; queueIndex++)
            {
                var collectors = new List<CollectorDefinition>(collectorsPerQueue);

                for (int rowIndex = 0; rowIndex < collectorsPerQueue; rowIndex++)
                {
                    MatchTypeId matchTypeId = matchTypes[(queueIndex + rowIndex) % matchTypes.Length];
                    collectors.Add(new CollectorDefinition(matchTypeId, hungerCapacity));
                }

                queues.Add(new CollectorQueueDefinition(collectors));
            }

            return queues;
        }
    }
}
