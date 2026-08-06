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

        // Which permanent Match ID (Assets/Art/ColorPalette.md) each
        // MatchTypeId resolves to *within this level* - the single place
        // that association is authored, reused by both this level's pixel
        // layout and its collector queues below, so a MatchTypeId's pixels
        // and the collectors that consume them can never drift onto
        // different Match IDs (see LevelDefinitionValidator.
        // ValidateMatchIdConsistency, which enforces this at construction
        // time). A MatchTypeId's meaning - and therefore which Match ID it
        // maps to - is level-scoped, not global: level_001 and level_002
        // deliberately assign "m002" to different Match IDs below, which is
        // legitimate precisely because MatchTypeId never carries colour or
        // identity outside its own level.
        private const int PrototypeMatchType1CharacterId = 1;  // Crab
        private const int PrototypeMatchType2CharacterId = 7;  // Octopus
        private const int PrototypeMatchType3CharacterId = 9;  // Turtle

        private const int SecondTestMatchType2CharacterId = 6;  // Fish
        private const int SecondTestMatchType4CharacterId = 20; // Crab

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
            var matchIds = new[] { PrototypeMatchType1CharacterId, PrototypeMatchType2CharacterId, PrototypeMatchType3CharacterId };
            var cells = new List<MatchTypeId>(width * height);
            var cellMatchIds = new List<int>(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (x + y) % matchTypes.Length;
                    cells.Add(matchTypes[index]);
                    cellMatchIds.Add(matchIds[index]);
                }
            }

            return new PixelLayoutDefinition(width, height, cells, cellMatchIds);
        }

        /// <summary>
        /// Explicit per-collector composition (not modulo-cycled capacity),
        /// preserving the same per-queue MatchTypeId order the original
        /// modulo cycling produced, so each MatchTypeId's total HungerCapacity
        /// (12, matching its pixel count) is directly readable here while
        /// deliberately varying across that type's four collectors.
        ///
        /// Each collector also carries the same Match ID this level's pixel
        /// layout assigns its MatchTypeId (PrototypeMatchType1CharacterId
        /// etc. above: MatchType1 -&gt; 1/Crab, MatchType2 -&gt; 7/Octopus,
        /// MatchType3 -&gt; 9/Turtle) — a level-authoring choice, not a
        /// global MatchTypeId-to-MatchId rule (a different level is free to
        /// assign a MatchTypeId to a different Match ID; see
        /// SecondTestMatchType2CharacterId below for exactly that), but
        /// every pixel and collector *within this level* sharing a
        /// MatchTypeId must agree on one Match ID -
        /// LevelDefinitionValidator.ValidateMatchIdConsistency enforces
        /// this at construction time.
        /// </summary>
        private static IReadOnlyList<CollectorQueueDefinition> BuildPrototypeCollectorQueues()
        {
            const int crab = PrototypeMatchType1CharacterId;
            const int octopus = PrototypeMatchType2CharacterId;
            const int turtle = PrototypeMatchType3CharacterId;

            var queueSpecs = new (MatchTypeId matchTypeId, int hungerCapacity, int matchId)[][]
            {
                new[] { (MatchType1, 5, crab), (MatchType2, 4, octopus), (MatchType3, 6, turtle) },
                new[] { (MatchType2, 4, octopus), (MatchType3, 2, turtle), (MatchType1, 3, crab) },
                new[] { (MatchType3, 3, turtle), (MatchType1, 2, crab), (MatchType2, 3, octopus) },
                new[] { (MatchType1, 2, crab), (MatchType2, 1, octopus), (MatchType3, 1, turtle) },
            };

            var queues = new List<CollectorQueueDefinition>(queueSpecs.Length);

            foreach ((MatchTypeId matchTypeId, int hungerCapacity, int matchId)[] spec in queueSpecs)
            {
                var collectors = new List<CollectorDefinition>(spec.Length);

                foreach ((MatchTypeId matchTypeId, int hungerCapacity, int matchId) in spec)
                    collectors.Add(new CollectorDefinition(matchTypeId, hungerCapacity, matchId));

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
            var matchIds = new[] { SecondTestMatchType2CharacterId, SecondTestMatchType4CharacterId };
            var cells = new List<MatchTypeId>(width * height);
            var cellMatchIds = new List<int>(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (x + y) % matchTypes.Length;
                    cells.Add(matchTypes[index]);
                    cellMatchIds.Add(matchIds[index]);
                }
            }

            return new PixelLayoutDefinition(width, height, cells, cellMatchIds);
        }

        /// <summary>
        /// Explicit per-queue composition (not modulo-cycled) so the
        /// capacity dedicated to each MatchTypeId is directly readable and
        /// verifiable here: each queue is dedicated entirely to one of the
        /// grid's two MatchTypeIds, alternating by queue index, with each
        /// queue's own 4 HungerCapacity values deliberately non-uniform.
        /// Each queue also carries the same Match ID this level's pixel
        /// layout assigns its MatchTypeId (SecondTestMatchType2CharacterId
        /// etc. above: MatchType2 -&gt; 6/Fish, MatchType4 -&gt; 20/Crab) — a
        /// level-authoring choice (note this level deliberately maps "m002"
        /// to a different Match ID than the prototype level does, which is
        /// legitimate since MatchTypeId is level-scoped), enforced
        /// consistent within this level by
        /// LevelDefinitionValidator.ValidateMatchIdConsistency.
        /// </summary>
        private static IReadOnlyList<CollectorQueueDefinition> BuildSecondTestCollectorQueues()
        {
            const int fish = SecondTestMatchType2CharacterId;
            const int crab = SecondTestMatchType4CharacterId;

            var queueSpecs = new (MatchTypeId matchTypeId, int[] hungerCapacities, int matchId)[]
            {
                (MatchType2, new[] { 3, 2, 2, 1 }, fish),
                (MatchType4, new[] { 3, 2, 1, 2 }, crab),
                (MatchType2, new[] { 1, 2, 3, 2 }, fish),
                (MatchType4, new[] { 2, 3, 2, 1 }, crab),
            };

            var queues = new List<CollectorQueueDefinition>(queueSpecs.Length);

            foreach ((MatchTypeId matchTypeId, int[] hungerCapacities, int matchId) in queueSpecs)
            {
                var collectors = new List<CollectorDefinition>(hungerCapacities.Length);

                foreach (int hungerCapacity in hungerCapacities)
                    collectors.Add(new CollectorDefinition(matchTypeId, hungerCapacity, matchId));

                queues.Add(new CollectorQueueDefinition(collectors));
            }

            return queues;
        }
    }
}
