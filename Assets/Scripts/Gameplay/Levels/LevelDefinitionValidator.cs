using System;
using System.Collections.Generic;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Validates an approved LevelDefinition's pixel-to-collector balance at
    /// the data boundary, before it ever reaches a runtime gameplay system.
    /// Deliberately not part of PixelGrid, CollectorQueueBoard, or any other
    /// runtime component — those trust the LevelDefinition they are given;
    /// this is what earns that trust for every level LevelCatalog approves.
    /// </summary>
    public static class LevelDefinitionValidator
    {
        /// <summary>
        /// For every MatchTypeId appearing anywhere in the level (pixel
        /// layout or collector queues), the total collector HungerCapacity
        /// for that MatchTypeId must equal the pixel count for that
        /// MatchTypeId exactly — no MatchTypeId may appear on one side
        /// without the other, and no surplus or shortfall is allowed. Throws
        /// InvalidOperationException identifying the offending LevelId and
        /// MatchTypeId on the first violation found.
        /// </summary>
        public static void Validate(LevelDefinition level)
        {
            Dictionary<MatchTypeId, int> pixelCounts = CountPixelsByMatchType(level.PixelLayout);
            Dictionary<MatchTypeId, int> collectorCapacities = CountCollectorCapacityByMatchType(level.CollectorQueues);

            foreach (KeyValuePair<MatchTypeId, int> pixelEntry in pixelCounts)
            {
                if (!collectorCapacities.TryGetValue(pixelEntry.Key, out int capacity))
                {
                    throw new InvalidOperationException(
                        $"LevelDefinitionValidator: level '{level.Id}' has {pixelEntry.Value} pixel(s) of MatchTypeId '{pixelEntry.Key}' but no collectors of that MatchTypeId.");
                }

                if (capacity != pixelEntry.Value)
                {
                    throw new InvalidOperationException(
                        $"LevelDefinitionValidator: level '{level.Id}' has {pixelEntry.Value} pixel(s) of MatchTypeId '{pixelEntry.Key}' but total collector HungerCapacity for that MatchTypeId is {capacity}; they must match exactly.");
                }
            }

            foreach (KeyValuePair<MatchTypeId, int> collectorEntry in collectorCapacities)
            {
                if (!pixelCounts.ContainsKey(collectorEntry.Key))
                {
                    throw new InvalidOperationException(
                        $"LevelDefinitionValidator: level '{level.Id}' has collector(s) of MatchTypeId '{collectorEntry.Key}' (total HungerCapacity {collectorEntry.Value}) but the pixel layout has no pixels of that MatchTypeId.");
                }
            }
        }

        private static Dictionary<MatchTypeId, int> CountPixelsByMatchType(PixelLayoutDefinition layout)
        {
            var counts = new Dictionary<MatchTypeId, int>();

            for (int y = 0; y < layout.Height; y++)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    MatchTypeId matchTypeId = layout.GetMatchTypeId(x, y);
                    counts[matchTypeId] = counts.TryGetValue(matchTypeId, out int count) ? count + 1 : 1;
                }
            }

            return counts;
        }

        private static Dictionary<MatchTypeId, int> CountCollectorCapacityByMatchType(IReadOnlyList<CollectorQueueDefinition> collectorQueues)
        {
            var capacities = new Dictionary<MatchTypeId, int>();

            foreach (CollectorQueueDefinition queue in collectorQueues)
            {
                foreach (CollectorDefinition collector in queue.Collectors)
                {
                    capacities[collector.MatchTypeId] = capacities.TryGetValue(collector.MatchTypeId, out int capacity)
                        ? capacity + collector.HungerCapacity
                        : collector.HungerCapacity;
                }
            }

            return capacities;
        }
    }
}
