using System;
using System.Collections.Generic;
using System.Globalization;

namespace Project001.UI.Digits
{
    /// <summary>
    /// Pure digit-decomposition and horizontal layout math for rendering a
    /// non-negative integer as a row of individually-sized digit sprites —
    /// no UnityEngine dependency, no knowledge of what a "digit sprite"
    /// actually looks like (aspect ratios are supplied by the caller), so
    /// it is directly unit-testable and reusable anywhere a number needs to
    /// be laid out as separate glyph images each keeping its own natural
    /// width (see SpriteDigitNumberDisplay for the MonoBehaviour that
    /// actually instantiates Image components from this).
    ///
    /// Thousands grouping mirrors the same grouping CoinBalanceFormatter's
    /// own thousands separator uses (groups of 3 counting from the least
    /// significant digit) — represented here as extra horizontal spacing
    /// rather than a printable character, since no separator sprite exists.
    /// </summary>
    public static class DigitLayout
    {
        /// <summary>One digit's resolved horizontal placement, in the same units as displayedHeight/digitGap/groupGap.</summary>
        public readonly struct DigitPlacement
        {
            public DigitPlacement(int digit, float centerX, float width)
            {
                Digit = digit;
                CenterX = centerX;
                Width = width;
            }

            /// <summary>0-9.</summary>
            public int Digit { get; }

            /// <summary>This digit's horizontal center, measured from the left edge of the whole laid-out row (the row's own left edge is X=0).</summary>
            public float CenterX { get; }

            /// <summary>This digit's own displayed width at the shared displayedHeight (never a fixed/equal width across digits).</summary>
            public float Width { get; }
        }

        /// <summary>
        /// Lays out every decimal digit of value (most significant first)
        /// left-to-right. Each digit's width is computed from its own
        /// native aspect ratio (digitAspectRatios[d] = that digit's sprite
        /// width/height) at the shared displayedHeight, so narrow digits
        /// (e.g. "1") stay narrow and wide digits (e.g. "8") stay wide —
        /// never stretched/squashed to a common width. Adds digitGap
        /// between adjacent digits within the same thousands group, and
        /// digitGap+groupGap when crossing a group-of-three boundary.
        /// </summary>
        public static IReadOnlyList<DigitPlacement> Compute(
            int value,
            IReadOnlyList<float> digitAspectRatios,
            float displayedHeight,
            float digitGap,
            float groupGap)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "value must be non-negative.");

            if (digitAspectRatios == null || digitAspectRatios.Count != 10)
                throw new ArgumentException("digitAspectRatios must contain exactly 10 entries (index 0-9).", nameof(digitAspectRatios));

            string digitsString = value.ToString(CultureInfo.InvariantCulture);
            int digitCount = digitsString.Length;

            var placements = new List<DigitPlacement>(digitCount);
            float x = 0f;

            for (int i = 0; i < digitCount; i++)
            {
                int digit = digitsString[i] - '0';
                float width = displayedHeight * digitAspectRatios[digit];

                if (i > 0)
                {
                    int digitsFromEndBeforeThis = digitCount - i;
                    bool crossesGroupBoundary = digitsFromEndBeforeThis % 3 == 0;
                    x += digitGap + (crossesGroupBoundary ? groupGap : 0f);
                }

                float centerX = x + width / 2f;
                placements.Add(new DigitPlacement(digit, centerX, width));
                x += width;
            }

            return placements;
        }

        /// <summary>Total width of the laid-out row — the last placement's own right edge — or 0 for an empty list.</summary>
        public static float TotalWidth(IReadOnlyList<DigitPlacement> placements)
        {
            if (placements == null || placements.Count == 0)
                return 0f;

            DigitPlacement last = placements[placements.Count - 1];
            return last.CenterX + last.Width / 2f;
        }
    }
}
