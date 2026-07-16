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
        /// exactly covered. Each MatchTypeId's 4 collectors deliberately carry
        /// non-uniform HungerCapacity values (never all equal) that still sum
        /// to exactly 12 per type — m001: 5+3+2+2, m002: 4+4+3+1,
        /// m003: 6+2+3+1 — proving HungerCapacity is a per-collector value,
        /// not a per-MatchTypeId constant. Conveyor capacity, conveyor move
        /// speed, and Waiting Line capacity are not level data — every level
        /// shares the same GameplayConstants values from LevelBootstrapper.
        /// </summary>
        private static LevelDefinition BuildPrototypeLevel()
        {
            return new LevelDefinition(
                PrototypeLevelId,
                BuildPrototypePixelLayout(),
                BuildPrototypeCollectorQueues());
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

        /// <summary>
        /// Explicit per-collector composition (not modulo-cycled capacity),
        /// preserving the same per-queue MatchTypeId order the original
        /// modulo cycling produced, so each MatchTypeId's total HungerCapacity
        /// (12, matching its pixel count) is directly readable here while
        /// deliberately varying across that type's four collectors.
        /// </summary>
        private static IReadOnlyList<CollectorQueueDefinition> BuildPrototypeCollectorQueues()
        {
            var queueSpecs = new (MatchTypeId matchTypeId, int hungerCapacity)[][]
            {
                new[] { (MatchType1, 5), (MatchType2, 4), (MatchType3, 6) },
                new[] { (MatchType2, 4), (MatchType3, 2), (MatchType1, 3) },
                new[] { (MatchType3, 3), (MatchType1, 2), (MatchType2, 3) },
                new[] { (MatchType1, 2), (MatchType2, 1), (MatchType3, 1) },
            };

            var queues = new List<CollectorQueueDefinition>(queueSpecs.Length);

            foreach ((MatchTypeId matchTypeId, int hungerCapacity)[] spec in queueSpecs)
            {
                var collectors = new List<CollectorDefinition>(spec.Length);

                foreach ((MatchTypeId matchTypeId, int hungerCapacity) in spec)
                    collectors.Add(new CollectorDefinition(matchTypeId, hungerCapacity));

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
        /// arrangement than the prototype's mixed per-queue cycling. Conveyor
        /// capacity, conveyor move speed, and Waiting Line capacity are not
        /// level data — every level shares the same GameplayConstants values
        /// from LevelBootstrapper.
        ///
        /// Only m002 and m004 collectors exist here, matching the grid's
        /// only two MatchTypeIds exactly: 2 queues of m002 and 2 queues of
        /// m004, each type's 8 collectors carrying deliberately non-uniform
        /// HungerCapacity values that still total exactly 16 — m002:
        /// 3+2+2+1 / 1+2+3+2, m004: 3+2+1+2 / 2+3+2+1 — a balanced spread
        /// (values 1-3 only, no large outlier) rather than a single large
        /// value next to many small ones, and no surplus or non-matching
        /// filler.
        /// </summary>
        private static LevelDefinition BuildSecondTestLevel()
        {
            return new LevelDefinition(
                SecondTestLevelId,
                BuildSecondTestPixelLayout(),
                BuildSecondTestCollectorQueues());
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
        /// grid's two MatchTypeIds, alternating by queue index, with each
        /// queue's own 4 HungerCapacity values deliberately non-uniform.
        /// </summary>
        private static IReadOnlyList<CollectorQueueDefinition> BuildSecondTestCollectorQueues()
        {
            var queueSpecs = new (MatchTypeId matchTypeId, int[] hungerCapacities)[]
            {
                (MatchType2, new[] { 3, 2, 2, 1 }),
                (MatchType4, new[] { 3, 2, 1, 2 }),
                (MatchType2, new[] { 1, 2, 3, 2 }),
                (MatchType4, new[] { 2, 3, 2, 1 }),
            };

            var queues = new List<CollectorQueueDefinition>(queueSpecs.Length);

            foreach ((MatchTypeId matchTypeId, int[] hungerCapacities) in queueSpecs)
            {
                var collectors = new List<CollectorDefinition>(hungerCapacities.Length);

                foreach (int hungerCapacity in hungerCapacities)
                    collectors.Add(new CollectorDefinition(matchTypeId, hungerCapacity));

                queues.Add(new CollectorQueueDefinition(collectors));
            }

            return queues;
        }
    }
}
