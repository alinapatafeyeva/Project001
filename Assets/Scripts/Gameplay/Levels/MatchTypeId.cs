using System;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Stable, abstract gameplay-matching identity shared by an edible pixel
    /// and the collectors that can consume it (e.g. "m001"). Carries no
    /// colour, sprite, food name, or theme meaning — a visual theme maps
    /// MatchTypeId to presentation separately, without changing level data
    /// or gameplay logic.
    /// </summary>
    public readonly struct MatchTypeId : IEquatable<MatchTypeId>
    {
        public string Value { get; }

        public MatchTypeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("MatchTypeId value must not be null or empty.", nameof(value));

            Value = value;
        }

        public bool Equals(MatchTypeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is MatchTypeId other && Equals(other);

        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

        public override string ToString() => Value;

        public static bool operator ==(MatchTypeId left, MatchTypeId right) => left.Equals(right);

        public static bool operator !=(MatchTypeId left, MatchTypeId right) => !left.Equals(right);
    }
}
