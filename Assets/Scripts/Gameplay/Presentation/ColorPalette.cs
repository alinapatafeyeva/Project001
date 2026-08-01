using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Runtime representation of Assets/Art/ColorPalette.md's Match ID -&gt;
    /// Color table — the single design source of truth for every gameplay
    /// colour. Every entry below is the exact hex value from that table's
    /// "Stable Match IDs" section, parsed as-is via ColorUtility (no manual
    /// re-derivation, so there is no room for transcription drift). Match ID
    /// is the only thing gameplay ever keys on; the named accessors here
    /// exist purely for presentation code to read a colour without spelling
    /// out its Match ID.
    /// </summary>
    public static class ColorPalette
    {
        public readonly struct Entry
        {
            public readonly int MatchId;
            public readonly string Name;
            public readonly Color Color;

            public Entry(int matchId, string name, string hex)
            {
                MatchId = matchId;
                Name = name;

                if (!ColorUtility.TryParseHtmlString(hex, out Color))
                    Debug.LogError($"ColorPalette: could not parse hex '{hex}' for Match ID {matchId:D2} ({name}).");
            }
        }

        // Exact copy of Assets/Art/ColorPalette.md's "Stable Match IDs"
        // table. Do not hand-edit a value here without updating that file
        // first — ColorPalette.md is the approved source, this is its
        // runtime mirror.
        private static readonly Entry[] Entries =
        {
            new Entry(1, "Red", "#f81718"),
            new Entry(2, "Orange", "#fe920b"),
            new Entry(3, "Yellow", "#ffe924"),
            new Entry(4, "Lime", "#a6f108"),
            new Entry(5, "Green", "#00c02d"),
            new Entry(6, "Mint", "#3af8cb"),
            new Entry(7, "Cyan", "#00d1ee"),
            new Entry(8, "Azure", "#014de0"),
            new Entry(9, "Peach", "#ffb98f"),
            new Entry(10, "Navy", "#0c3264"),
            new Entry(11, "Purple", "#8e1dfe"),
            new Entry(12, "Violet", "#e0b4e7"),
            new Entry(13, "Pink", "#fc4982"),
            new Entry(14, "Coral", "#fc4b2c"),
            new Entry(15, "Brown", "#7f300a"),
            new Entry(16, "Cream", "#ffe7c2"),
            new Entry(17, "Teal", "#189b99"),
            new Entry(18, "White", "#fbf4f0"),
            new Entry(19, "Gray", "#908781"),
            new Entry(20, "Black", "#212121"),
        };

        public static IReadOnlyList<Entry> All => Entries;

        public static Color Red => Entries[0].Color;
        public static Color Orange => Entries[1].Color;
        public static Color Yellow => Entries[2].Color;
        public static Color Lime => Entries[3].Color;
        public static Color Green => Entries[4].Color;
        public static Color Mint => Entries[5].Color;
        public static Color Cyan => Entries[6].Color;
        public static Color Azure => Entries[7].Color;
        public static Color Peach => Entries[8].Color;
        public static Color Navy => Entries[9].Color;
        public static Color Purple => Entries[10].Color;
        public static Color Violet => Entries[11].Color;
        public static Color Pink => Entries[12].Color;
        public static Color Coral => Entries[13].Color;
        public static Color Brown => Entries[14].Color;
        public static Color Cream => Entries[15].Color;
        public static Color Teal => Entries[16].Color;
        public static Color White => Entries[17].Color;
        public static Color Gray => Entries[18].Color;
        public static Color Black => Entries[19].Color;

        /// <summary>
        /// Resolves a colour by its permanent Match ID (1-20), never by
        /// name — mirrors how gameplay itself matches. Returns false for any
        /// id outside the approved table.
        /// </summary>
        public static bool TryGetByMatchId(int matchId, out Color color)
        {
            foreach (Entry entry in Entries)
            {
                if (entry.MatchId == matchId)
                {
                    color = entry.Color;
                    return true;
                }
            }

            color = default;
            return false;
        }
    }
}
