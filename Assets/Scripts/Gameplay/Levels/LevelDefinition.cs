using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Normalized, immutable pixel layout: exactly one MatchTypeId per cell of
    /// a width x height grid. Exposes no mutable array — cells are read one at
    /// a time by coordinate.
    /// </summary>
    public sealed class PixelLayoutDefinition
    {
        private readonly MatchTypeId[] _cells;

        public int Width { get; }

        public int Height { get; }

        public PixelLayoutDefinition(int width, int height, IReadOnlyList<MatchTypeId> cells)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            if (cells.Count != width * height)
            {
                throw new ArgumentException(
                    $"Expected {width * height} cells for a {width}x{height} layout, got {cells.Count}.",
                    nameof(cells));
            }

            Width = width;
            Height = height;

            _cells = new MatchTypeId[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                _cells[i] = cells[i];
        }

        /// <summary>
        /// MatchTypeId of the cell at the given grid coordinate.
        /// </summary>
        public MatchTypeId GetMatchTypeId(int x, int y)
        {
            if (x < 0 || x >= Width)
                throw new ArgumentOutOfRangeException(nameof(x));

            if (y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException(nameof(y));

            return _cells[y * Width + x];
        }
    }

    /// <summary>
    /// One collector's immutable gameplay identity: which MatchTypeId it
    /// accepts and how many matching pixels satisfy it.
    /// </summary>
    public sealed class CollectorDefinition
    {
        public MatchTypeId MatchTypeId { get; }

        public int HungerCapacity { get; }

        public CollectorDefinition(MatchTypeId matchTypeId, int hungerCapacity)
        {
            if (hungerCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(hungerCapacity), "Hunger capacity must be positive.");

            MatchTypeId = matchTypeId;
            HungerCapacity = hungerCapacity;
        }
    }

    /// <summary>
    /// An ordered, immutable queue of CollectorDefinitions.
    /// </summary>
    public sealed class CollectorQueueDefinition
    {
        private readonly ReadOnlyCollection<CollectorDefinition> _collectors;

        public IReadOnlyList<CollectorDefinition> Collectors => _collectors;

        public CollectorQueueDefinition(IReadOnlyList<CollectorDefinition> collectors)
        {
            if (collectors == null)
                throw new ArgumentNullException(nameof(collectors));

            if (collectors.Count == 0)
                throw new ArgumentException("A collector queue must contain at least one collector.", nameof(collectors));

            var copy = new List<CollectorDefinition>(collectors.Count);
            foreach (CollectorDefinition collector in collectors)
            {
                if (collector == null)
                    throw new ArgumentException("Collector queue must not contain null collectors.", nameof(collectors));

                copy.Add(collector);
            }

            _collectors = new ReadOnlyCollection<CollectorDefinition>(copy);
        }
    }

    /// <summary>
    /// One complete, normalized, deterministic level: pixel layout and
    /// collector queues — only what actually varies between levels. Global
    /// gameplay rules that apply identically to every level (conveyor
    /// capacity, conveyor speed, Waiting Line capacity) live in
    /// GameplayConstants instead. Immutable after construction, with no
    /// runtime state (no consumed/remaining counts, no MonoBehaviour
    /// references) and no player-progress or catalog concerns (no map
    /// position, unlock/completed state, rewards, stars, display title, or
    /// replay rules — those belong to future level-catalog and
    /// player-progress systems).
    /// </summary>
    public sealed class LevelDefinition
    {
        private readonly ReadOnlyCollection<CollectorQueueDefinition> _collectorQueues;

        public LevelId Id { get; }

        public PixelLayoutDefinition PixelLayout { get; }

        public IReadOnlyList<CollectorQueueDefinition> CollectorQueues => _collectorQueues;

        public LevelDefinition(
            LevelId id,
            PixelLayoutDefinition pixelLayout,
            IReadOnlyList<CollectorQueueDefinition> collectorQueues)
        {
            if (pixelLayout == null)
                throw new ArgumentNullException(nameof(pixelLayout));

            if (collectorQueues == null)
                throw new ArgumentNullException(nameof(collectorQueues));

            if (collectorQueues.Count == 0)
                throw new ArgumentException("A level must contain at least one collector queue.", nameof(collectorQueues));

            var copy = new List<CollectorQueueDefinition>(collectorQueues.Count);
            foreach (CollectorQueueDefinition queue in collectorQueues)
            {
                if (queue == null)
                    throw new ArgumentException("Collector queues must not contain null entries.", nameof(collectorQueues));

                copy.Add(queue);
            }

            Id = id;
            PixelLayout = pixelLayout;
            _collectorQueues = new ReadOnlyCollection<CollectorQueueDefinition>(copy);
        }
    }
}
