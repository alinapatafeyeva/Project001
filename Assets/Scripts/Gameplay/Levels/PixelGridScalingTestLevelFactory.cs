using System;
using System.Collections.Generic;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Representative grid size this factory can build — exactly the set the
    /// Pixel Grid scaling investigation used to evaluate PixelGrid's
    /// preferred-size/proportional-gap formula (see PixelGrid.
    /// TryComputeCellMetrics): two sizes small enough to sit under
    /// preferredCellSize's natural fit (no shrink expected) and three dense
    /// enough to force a uniform scale-down.
    /// </summary>
    public enum PixelGridScalingTestSize
    {
        Grid6x6,
        Grid10x10,
        Grid16x16,
        Grid20x20,
        Grid30x40
    }

    /// <summary>
    /// Builds runtime-only, Bootstrap-test-only LevelDefinitions at
    /// arbitrary width x height — used solely to visually and technically
    /// verify PixelGrid's preferred-size/proportional-gap scaling across
    /// representative densities (PixelGridScalingTestSize), never to author
    /// or replace an approved level. Never touches LevelCatalog.
    ///
    /// Unlike FailureTestLevelFactory/FeedingFlowTestLevelFactory - whose
    /// debug data is deliberately unbalanced (permanently-hungry collectors,
    /// on purpose) - this factory's output is fully balanced and passes
    /// LevelDefinitionValidator.Validate exactly like an approved level
    /// would: every pixel's MatchTypeId has exactly matching collector
    /// HungerCapacity. That lets a real Play Mode session actually exercise
    /// the production feeding chain (PixelGrid -&gt; PixelConsumer -&gt;
    /// FoodFlightController -&gt; FoodPacket) at each density, not just
    /// render cells statically - required for the 30x40 performance
    /// verification (5 collectors genuinely consuming), not only the visual
    /// checks at smaller sizes.
    /// </summary>
    public static class PixelGridScalingTestLevelFactory
    {
        public static readonly LevelId TestLevelId = new LevelId("debug_pixel_grid_scaling");

        // Reuses the exact same 3 Match IDs LevelCatalog.BuildPrototypeLevel
        // already assigns to MatchType1/2/3 (Crab/Octopus/Turtle) - see its
        // own PrototypeMatchType1/2/3CharacterId - so every rendered pixel
        // and collector here resolves a real, already-verified
        // CharacterDatabase entry instead of a placeholder colour/prefab.
        // MatchTypeId values are still this factory's own dedicated debug
        // ids (never level_001's), since MatchTypeId meaning is level-scoped
        // (see PixelLayoutDefinition's own remarks) and this is a distinct,
        // non-approved level.
        private static readonly (MatchTypeId matchTypeId, int matchId)[] MatchTypes =
        {
            (new MatchTypeId("scaling_test_1"), 1),  // Crab
            (new MatchTypeId("scaling_test_2"), 7),  // Octopus
            (new MatchTypeId("scaling_test_3"), 9),  // Turtle
        };

        // Up to this many collectors per Match Type's queue, fewer if the
        // grid is too small/sparse to give each at least 1 HungerCapacity
        // (see BuildCollectorQueues) - purely so the debug queue reads as a
        // real multi-collector queue rather than a single giant-hunger
        // placeholder, at every representative size.
        private const int MaxCollectorsPerType = 4;

        private static readonly Dictionary<PixelGridScalingTestSize, (int width, int height)> Sizes = new Dictionary<PixelGridScalingTestSize, (int width, int height)>
        {
            { PixelGridScalingTestSize.Grid6x6, (6, 6) },
            { PixelGridScalingTestSize.Grid10x10, (10, 10) },
            { PixelGridScalingTestSize.Grid16x16, (16, 16) },
            { PixelGridScalingTestSize.Grid20x20, (20, 20) },
            { PixelGridScalingTestSize.Grid30x40, (30, 40) },
        };

        /// <summary>
        /// Convenience overload resolving one of the fixed representative
        /// sizes above via BuildTestLevel(int, int).
        /// </summary>
        public static LevelDefinition BuildTestLevel(PixelGridScalingTestSize size)
        {
            (int width, int height) = Sizes[size];
            return BuildTestLevel(width, height);
        }

        /// <summary>
        /// Builds a width x height pixel layout, diagonally cycling through
        /// MatchTypes exactly like LevelCatalog's own approved levels (index
        /// = (x + y) % MatchTypes.Length - the same pattern proven to expose
        /// a mix of types from every grid side as outer cells are consumed),
        /// and a balanced set of collector queues - one queue per Match
        /// Type, up to MaxCollectorsPerType collectors whose HungerCapacity
        /// sums exactly to that type's real pixel count - so the result
        /// passes LevelDefinitionValidator.Validate exactly like an approved
        /// level would.
        /// </summary>
        public static LevelDefinition BuildTestLevel(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            PixelLayoutDefinition layout = BuildPixelLayout(width, height, out int[] countsByType);
            IReadOnlyList<CollectorQueueDefinition> queues = BuildCollectorQueues(countsByType);

            var level = new LevelDefinition(TestLevelId, layout, queues);
            LevelDefinitionValidator.Validate(level);
            return level;
        }

        private static PixelLayoutDefinition BuildPixelLayout(int width, int height, out int[] countsByType)
        {
            var cells = new List<MatchTypeId>(width * height);
            var cellMatchIds = new List<int>(width * height);
            countsByType = new int[MatchTypes.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (x + y) % MatchTypes.Length;
                    (MatchTypeId matchTypeId, int matchId) = MatchTypes[index];
                    cells.Add(matchTypeId);
                    cellMatchIds.Add(matchId);
                    countsByType[index]++;
                }
            }

            return new PixelLayoutDefinition(width, height, cells, cellMatchIds);
        }

        private static IReadOnlyList<CollectorQueueDefinition> BuildCollectorQueues(int[] countsByType)
        {
            var queues = new List<CollectorQueueDefinition>(MatchTypes.Length);

            for (int typeIndex = 0; typeIndex < MatchTypes.Length; typeIndex++)
            {
                (MatchTypeId matchTypeId, int matchId) = MatchTypes[typeIndex];
                int totalCount = countsByType[typeIndex];
                int collectorCount = Math.Min(MaxCollectorsPerType, Math.Max(1, totalCount));

                queues.Add(new CollectorQueueDefinition(BuildCollectorsForCount(matchTypeId, matchId, totalCount, collectorCount)));
            }

            return queues;
        }

        /// <summary>
        /// Splits totalCount into collectorCount collectors as evenly as
        /// possible (base = totalCount / collectorCount, the first
        /// totalCount % collectorCount collectors get one extra) so the sum
        /// is always exactly totalCount and every collector's HungerCapacity
        /// is at least 1 - collectorCount is never greater than totalCount
        /// (see BuildCollectorQueues), so base is always at least... the
        /// remainder-distribution below still guarantees every entry &gt;= 1
        /// even when base is 0 for a small totalCount close to
        /// collectorCount.
        /// </summary>
        private static List<CollectorDefinition> BuildCollectorsForCount(MatchTypeId matchTypeId, int matchId, int totalCount, int collectorCount)
        {
            var collectors = new List<CollectorDefinition>(collectorCount);
            int baseCapacity = totalCount / collectorCount;
            int remainder = totalCount % collectorCount;

            for (int i = 0; i < collectorCount; i++)
            {
                int capacity = baseCapacity + (i < remainder ? 1 : 0);
                collectors.Add(new CollectorDefinition(matchTypeId, capacity, matchId));
            }

            return collectors;
        }
    }
}
