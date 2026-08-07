using System.Collections.Generic;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Builds a runtime-only, Bootstrap-test-only LevelDefinition exercising
    /// every Character_01-20 through the real Pixel Feed Flow — a sibling to
    /// FailureTestLevelFactory (same seam: a LevelBootstrapper Inspector
    /// toggle building a derived LevelDefinition, never touching LevelCatalog
    /// or any approved level), but with the opposite intent: where
    /// FailureTestLevelFactory's debug collectors are deliberately
    /// unmatched so they can NEVER be fed, every collector built here carries
    /// a real, pixel-layout-matching MatchTypeId so it CAN be fed through the
    /// exact production chain (PixelGrid -&gt; PixelConsumer -&gt;
    /// FoodFlightController -&gt; FoodPacket -&gt; FeedTarget -&gt;
    /// ConveyorRider.RegisterConsumedPixel). Nothing here calls
    /// RegisterConsumedPixel or decrements hunger directly, and nothing spawns
    /// a presentation-only FoodPacket — every packet this level's collectors
    /// ever receive is a genuine one launched by FoodFlightController in
    /// response to a genuine PixelGrid consumption.
    ///
    /// Replaces the whole level (own pixel layout, own queues) rather than
    /// augmenting an approved one — unlike FailureTestLevelFactory, which
    /// only ever prepends debug collectors onto whatever level
    /// LevelProgressionController already resolved. Its result is
    /// intentionally never passed through LevelDefinitionValidator, for the
    /// same reason FailureTestLevelFactory's is not: FeedingFlowTestHungerCapacity
    /// deliberately exceeds each Match ID's own pixel supply (see its own
    /// remarks) so debug collectors normally survive a full lap instead of
    /// being satisfied instantly — a level with that deliberate surplus is
    /// not balanced, approved data.
    /// </summary>
    public static class FeedingFlowTestLevelFactory
    {
        public static readonly LevelId FeedingFlowTestLevelId = new LevelId("debug_feeding_flow");

        /// <summary>
        /// CharacterDatabase (see BootstrapSceneCreator.CreateCharacterDatabase)
        /// always configures exactly Match ID 1-20 -&gt; Character_01-20; this
        /// is the single place that count is authored for this factory, so
        /// every loop below (queue layout, pixel layout, per-species
        /// MatchTypeId) stays in lockstep with it without repeating "20"
        /// as a separate magic number per loop.
        /// </summary>
        private const int CharacterCount = 20;

        private const int QueueCount = 4;
        private const int CollectorsPerQueue = CharacterCount / QueueCount;

        /// <summary>
        /// Exposed pixels generated per Match ID in this level's own pixel
        /// layout (see BuildPixelLayout) — not a production balance value,
        /// just "enough to repeatedly feed a debug collector several times
        /// and observe several distinct packet arrivals" without the grid
        /// becoming unreasonably large (20 species x 6 = 120 cells total).
        /// </summary>
        private const int PixelsPerMatchType = 6;

        /// <summary>
        /// Debug-only hunger capacity, deliberately larger than
        /// PixelsPerMatchType so a debug collector built with it can never be
        /// fully satisfied purely from its own Match ID's pixel supply — it
        /// stays hungry after consuming everything reachable, completes its
        /// conveyor lap unsatisfied, and is handed to WaitingLine/RecoveryRow
        /// exactly like a real undersupplied collector would be, instead of
        /// disappearing the moment a developer starts testing it. Comfortably
        /// large for manual testing (room for several distinct FoodPacket
        /// arrivals before hunger would even matter), not an extreme/edge-case
        /// value — plain arithmetic against PixelsPerMatchType, no overflow or
        /// rounding concern at this scale. Applied to most, but deliberately
        /// not every, collector — see IsSatisfiableRow.
        /// </summary>
        public const int FeedingFlowTestHungerCapacity = PixelsPerMatchType * 3;

        /// <summary>
        /// Match ID always satisfiable regardless of row (see
        /// IsSatisfiableRow) — its collector gets exactly PixelsPerMatchType
        /// hunger, equal to its own Match ID's full pixel supply, so
        /// consuming every reachable pixel of that colour satisfies it
        /// exactly, with no leftover and no shortfall, whichever queue/row it
        /// happens to land on. Match ID 1 (Crab) is chosen because it is also
        /// this project's one species with runtime claw-pose switching (see
        /// CharacterPosePresentation), so deliberately
        /// satisfying it doubles as an end-to-end check that pose switching
        /// still works right up through the satisfied/disappear sequence.
        /// </summary>
        private const int SatisfactionDemoMatchId = 1;

        /// <summary>
        /// Builds the full debug feeding-flow LevelDefinition: a fresh pixel
        /// layout covering all 20 Match IDs plus a fresh set of queues,
        /// entirely independent of whatever level LevelProgressionController
        /// resolved (contrast FailureTestLevelFactory, which layers onto an
        /// approved level's own data). Deterministic — calling this twice
        /// produces two LevelDefinition instances with identical data.
        /// </summary>
        public static LevelDefinition BuildFeedingFlowTestLevel()
        {
            return new LevelDefinition(
                FeedingFlowTestLevelId,
                BuildPixelLayout(),
                BuildCollectorQueues());
        }

        /// <summary>
        /// One MatchTypeId per Match ID (1-20), used identically by both the
        /// pixel layout and the collector queues below so every collector's
        /// MatchTypeId genuinely appears on the grid — the one property that
        /// actually routes packets through PixelConsumer -&gt;
        /// FoodFlightController for real, unlike FailureTestLevelFactory's
        /// DebugUnmatchedMatchTypeId.
        /// </summary>
        private static MatchTypeId MatchTypeIdForCharacter(int matchId) => new MatchTypeId($"debug_feed_{matchId:D2}");

        /// <summary>
        /// A CharacterCount-wide, PixelsPerMatchType-tall grid (20x6 = 120
        /// cells) using the same diagonal cycling
        /// (index = (x + y) % matchTypeCount) LevelCatalog's own approved
        /// levels use, just generalized from 2-3 match types to all 20 — a
        /// pattern already proven to expose a mix of types from every grid
        /// side as outer cells are consumed, rather than trapping one Match
        /// ID's pixels entirely behind another's. Because width equals
        /// exactly the number of match types, every row is itself one full
        /// cyclic permutation of all 20 Match IDs, so this distribution is
        /// perfectly even: every Match ID gets exactly PixelsPerMatchType (6)
        /// pixels, never more, never fewer — no Match ID can be accidentally
        /// starved during testing.
        /// </summary>
        private static PixelLayoutDefinition BuildPixelLayout()
        {
            const int width = CharacterCount;
            const int height = PixelsPerMatchType;

            var cells = new List<MatchTypeId>(width * height);
            var cellMatchIds = new List<int>(width * height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int matchId = (x + y) % CharacterCount + 1;
                    cells.Add(MatchTypeIdForCharacter(matchId));
                    cellMatchIds.Add(matchId);
                }
            }

            return new PixelLayoutDefinition(width, height, cells, cellMatchIds);
        }

        /// <summary>
        /// A collector on this row is a satisfiable "release valve" (hunger
        /// == PixelsPerMatchType, same as SatisfactionDemoMatchId) rather
        /// than a permanently-hungry FeedingFlowTestHungerCapacity one. Every
        /// row except one (currently row 2) is satisfiable — deliberately
        /// keeping the permanently-hungry group small and safely under the
        /// combined Conveyor + WaitingLine capacity
        /// (GameplayConstants.BaseConveyorCapacity + WaitingLineCapacity, 5 +
        /// 5 = 10 total slots), rather than making it the numeric majority.
        /// A collector only ever leaves this system by becoming satisfied
        /// (destroyed) — WaitingLine has no eject/timeout — so every
        /// permanently-hungry collector occupies one slot forever once it
        /// gets one. If the permanently-hungry group's total size reaches or
        /// exceeds those combined 10 slots, they alone can fill both the
        /// Conveyor and WaitingLine completely and never free another slot
        /// again, permanently stranding every Match ID still waiting in
        /// queue as unreachable for the rest of that Play session — this was
        /// confirmed empirically (11 permanently-hungry collectors reliably
        /// deadlocked the queue partway through during verification; keeping
        /// the group at 4 - one row's worth per queue, comfortably under the
        /// combined 10-slot ceiling with room to spare - reliably drains all
        /// 20). The remaining rows (0, 1, 3, 4) being satisfiable is still a
        /// meaningful, clearly visible demonstration of "large hunger stays
        /// hungry, reaches WaitingLine, and stays there" (task requirement)
        /// for every Match ID that lands on row 2 - it does not need to be
        /// the numeric majority to prove the behaviour, and here it also
        /// keeps the whole debug board provably reachable end to end in one
        /// continuous session, deterministically, rather than only most of
        /// the time depending on real-time scheduling luck.
        /// </summary>
        private static bool IsSatisfiableRow(int rowIndex) => rowIndex != 2;

        /// <summary>
        /// QueueCount queues of CollectorsPerQueue collectors each (4x5 = 20),
        /// one collector per Match ID, assigned deterministically as
        /// matchId = queueIndex + rowIndex * QueueCount + 1 — e.g. queue 0
        /// holds Match IDs 1/5/9/13/17, queue 1 holds 2/6/10/14/18, and so on,
        /// so all 20 species are reachable in the same board without ever
        /// editing code between runs, and every collector's MatchId and
        /// MatchTypeId agree (MatchTypeIdForCharacter is the single source
        /// both this method and BuildPixelLayout call). Resolves no prefab
        /// path itself — CollectorQueueBoard.ResolvePrefab still does that,
        /// purely from CollectorDefinition.MatchId via the same
        /// CharacterDatabase production uses, exactly as for any approved
        /// level.
        /// </summary>
        private static IReadOnlyList<CollectorQueueDefinition> BuildCollectorQueues()
        {
            var queues = new List<CollectorQueueDefinition>(QueueCount);

            for (int queueIndex = 0; queueIndex < QueueCount; queueIndex++)
            {
                var collectors = new List<CollectorDefinition>(CollectorsPerQueue);

                for (int rowIndex = 0; rowIndex < CollectorsPerQueue; rowIndex++)
                {
                    int matchId = queueIndex + rowIndex * QueueCount + 1;
                    bool satisfiable = matchId == SatisfactionDemoMatchId || IsSatisfiableRow(rowIndex);
                    int hungerCapacity = satisfiable ? PixelsPerMatchType : FeedingFlowTestHungerCapacity;

                    collectors.Add(new CollectorDefinition(MatchTypeIdForCharacter(matchId), hungerCapacity, matchId));
                }

                queues.Add(new CollectorQueueDefinition(collectors));
            }

            return queues;
        }
    }
}
