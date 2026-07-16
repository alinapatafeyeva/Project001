using System;
using System.Collections.Generic;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Builds a runtime-only, Bootstrap-test-only LevelDefinition derived from
    /// an approved one, with deterministic debug collectors prepended to its
    /// queues so Failure can be reproduced on demand. Never reads from or
    /// writes to LevelCatalog, never mutates the approved LevelDefinition or
    /// any of its collections (LevelDefinition and CollectorQueueDefinition
    /// both defensively copy whatever they are given, so building a derived
    /// definition from an approved one's data is always read-only), and its
    /// result is intentionally never passed through LevelDefinitionValidator
    /// — a level with unmatched debug collectors is not approved data.
    /// </summary>
    public static class FailureTestLevelFactory
    {
        // Debug collectors stay permanently unsatisfied because their
        // MatchTypeId never appears on any pixel grid, not because of this
        // value. Deliberately large as an explicit debug signal that they
        // are meant to stay hungry even against future, larger test layouts.
        private const int DebugHungerCapacity = 50;

        /// <summary>
        /// Returns a new LevelDefinition with the same Id and PixelLayout as
        /// approvedLevel, but with
        /// debugCollectorCount debug collectors (MatchTypeId
        /// MatchTypePresentation.DebugUnmatchedMatchTypeId, HungerCapacity
        /// DebugHungerCapacity) distributed round-robin across
        /// approvedLevel's existing queues and prepended to each queue's
        /// front — so debug collectors are selectable before any normal
        /// collector, and every normal collector keeps its original relative
        /// order afterward. Works for any approved queue count.
        /// </summary>
        public static LevelDefinition BuildFailureTestLevel(LevelDefinition approvedLevel, int debugCollectorCount)
        {
            if (approvedLevel == null)
                throw new ArgumentNullException(nameof(approvedLevel));

            if (debugCollectorCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(debugCollectorCount), "Debug collector count must be positive.");

            IReadOnlyList<CollectorQueueDefinition> sourceQueues = approvedLevel.CollectorQueues;
            int queueCount = sourceQueues.Count;

            var debugCollectorsPerQueue = new List<CollectorDefinition>[queueCount];
            for (int queueIndex = 0; queueIndex < queueCount; queueIndex++)
                debugCollectorsPerQueue[queueIndex] = new List<CollectorDefinition>();

            for (int debugIndex = 0; debugIndex < debugCollectorCount; debugIndex++)
            {
                int queueIndex = debugIndex % queueCount;
                debugCollectorsPerQueue[queueIndex].Add(
                    new CollectorDefinition(MatchTypePresentation.DebugUnmatchedMatchTypeId, DebugHungerCapacity));
            }

            var mergedQueues = new List<CollectorQueueDefinition>(queueCount);
            for (int queueIndex = 0; queueIndex < queueCount; queueIndex++)
            {
                IReadOnlyList<CollectorDefinition> normalCollectors = sourceQueues[queueIndex].Collectors;
                var mergedCollectors = new List<CollectorDefinition>(debugCollectorsPerQueue[queueIndex].Count + normalCollectors.Count);
                mergedCollectors.AddRange(debugCollectorsPerQueue[queueIndex]);
                mergedCollectors.AddRange(normalCollectors);

                mergedQueues.Add(new CollectorQueueDefinition(mergedCollectors));
            }

            return new LevelDefinition(
                approvedLevel.Id,
                approvedLevel.PixelLayout,
                mergedQueues);
        }
    }
}
