using System;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Stable, immutable identifier for one LevelDefinition. Carries no
    /// UnityEngine dependency and no meaning beyond "which level" — map
    /// position, unlock state, and display data belong to future
    /// level-catalog and player-progress systems.
    /// </summary>
    public readonly struct LevelId : IEquatable<LevelId>
    {
        public string Value { get; }

        public LevelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("LevelId value must not be null or empty.", nameof(value));

            Value = value;
        }

        public bool Equals(LevelId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LevelId other && Equals(other);

        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public override string ToString() => Value;

        public static bool operator ==(LevelId left, LevelId right) => left.Equals(right);

        public static bool operator !=(LevelId left, LevelId right) => !left.Equals(right);
    }
}
