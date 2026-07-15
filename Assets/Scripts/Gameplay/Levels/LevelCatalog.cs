using System.Collections.Generic;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Resolves approved, deterministic LevelDefinition instances by LevelId.
    /// Owns construction of every approved level's gameplay data — the single
    /// place LevelBootstrapper loads a LevelDefinition from. A future
    /// CurrentProgressLevelId source only changes which LevelId is looked up
    /// here; it does not change this catalog or how LevelBootstrapper
    /// consumes the result.
    /// </summary>
    public sealed class LevelCatalog
    {
        public static readonly LevelId PrototypeLevelId = new LevelId("level_001");
        public static readonly LevelId SecondTestLevelId = new LevelId("level_002");

        private static readonly MatchTypeId MatchType1 = new MatchTypeId("m001");
        private static readonly MatchTypeId MatchType2 = new MatchTypeId("m002");
        private static readonly MatchTypeId MatchType3 = new MatchTypeId("m003");
        private static readonly MatchTypeId MatchType4 = new MatchTypeId("m004");

        private readonly Dictionary<LevelId, LevelDefinition> _levels;

        public LevelCatalog()
        {
            _levels = new Dictionary<LevelId, LevelDefinition>
            {
                { PrototypeLevelId, BuildPrototypeLevel() },
                { SecondTestLevelId, BuildSecondTestLevel() },
            };

            foreach (LevelDefinition level in _levels.Values)
                LevelDefinitionValidator.Validate(level);
        }

        /// <summary>
        /// Resolves the approved LevelDefinition for the given id. Returns
        /// false (definition left at its default/null) for any id not
        /// present in the catalog rather than throwing or returning a
        /// partially-built level — the caller decides how to handle an
        /// unknown id.
        /// </summary>
        public bool TryGetLevel(LevelId id, out LevelDefinition definition)
        {
            return _levels.TryGetValue(id, out definition);
        }

        /// <summary>
        /// A 6x6 pixel grid cycling through 3 match types (12 pixels each of
        /// m001/m002/m003, 0 of m004), and 4 queues of 3 collectors each
        /// cycling through only those 3 match types so every collector
        /// MatchTypeId appears on the grid and every grid MatchTypeId is
        /// exactly covered: hunger capacity 3, 4 collectors of each of
        /// m001/m002/m003 (4 x 3 = 12, matching the pixel count exactly, no
        /// surplus and no non-matching filler). Conveyor capacity 5, move
        /// speed 2. Waiting Line capacity is not level data — every level
        /// gets the same fixed capacity from LevelBootstrapper.
        /// </summary>
        private static LevelDefinition BuildPrototypeLevel()
        {
            return new LevelDefinition(
                PrototypeLevelId,
                BuildPrototypePixelLayout(),
                BuildPrototypeCollectorQueues(),
                conveyorCapacity: 5,
                conveyorMoveSpeed: 2f);
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
            const int collectorsPerQueue = 3;
            const int hungerCapacity = 3;

            var matchTypes = new[] { MatchType1, MatchType2, MatchType3 };
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

        /// <summary>
        /// A second approved level, deliberately different from the
        /// prototype in every dimension a catalog switch should visibly
        /// prove: a narrower 4x8 grid (32 cells, taller than wide) in a
        /// 2-match-type checkerboard (16 m002 pixels, 16 m004 pixels), 4
        /// queues of 4 collectors each dedicated to a single MatchTypeId per
        /// queue — alternating m002/m004/m002/m004, a structurally different
        /// arrangement than the prototype's mixed per-queue cycling —
        /// hunger capacity 2 (lower — more bites needed per collector), and
        /// conveyor capacity 8 at move speed 3.5 (higher throughput,
        /// faster). Waiting Line capacity is not level data — every level
        /// gets the same fixed capacity from LevelBootstrapper.
        ///
        /// Only m002 and m004 collectors exist here, matching the grid's
        /// only two MatchTypeIds exactly: 2 queues of m002 (2 x 4 x 2 = 16)
        /// and 2 queues of m004 (2 x 4 x 2 = 16), each equal to that type's
        /// pixel count with no surplus and no non-matching filler.
        /// </summary>
        private static LevelDefinition BuildSecondTestLevel()
        {
            return new LevelDefinition(
                SecondTestLevelId,
                BuildSecondTestPixelLayout(),
                BuildSecondTestCollectorQueues(),
                conveyorCapacity: 8,
                conveyorMoveSpeed: 3.5f);
        }

        private static PixelLayoutDefinition BuildSecondTestPixelLayout()
        {
            const int width = 4;
            const int height = 8;

            var matchTypes = new[] { MatchType2, MatchType4 };
            var cells = new List<MatchTypeId>(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    cells.Add(matchTypes[(x + y) % matchTypes.Length]);
            }

            return new PixelLayoutDefinition(width, height, cells);
        }

        /// <summary>
        /// Explicit per-queue composition (not modulo-cycled) so the
        /// capacity dedicated to each MatchTypeId is directly readable and
        /// verifiable here: each queue is dedicated entirely to one of the
        /// grid's two MatchTypeIds, alternating by queue index.
        /// </summary>
        private static IReadOnlyList<CollectorQueueDefinition> BuildSecondTestCollectorQueues()
        {
            const int collectorsPerQueue = 4;
            const int hungerCapacity = 2;

            var queueMatchTypes = new[] { MatchType2, MatchType4, MatchType2, MatchType4 };
            var queues = new List<CollectorQueueDefinition>(queueMatchTypes.Length);

            foreach (MatchTypeId queueMatchType in queueMatchTypes)
            {
                var collectors = new List<CollectorDefinition>(collectorsPerQueue);

                for (int rowIndex = 0; rowIndex < collectorsPerQueue; rowIndex++)
                    collectors.Add(new CollectorDefinition(queueMatchType, hungerCapacity));

                queues.Add(new CollectorQueueDefinition(collectors));
            }

            return queues;
        }
    }
}
