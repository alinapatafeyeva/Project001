using System.Collections.Generic;
using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.Levels
{
    /// <summary>
    /// Temporary MatchTypeId-to-Color presentation mapping, shared by every
    /// catalog level. Deliberately independent of LevelCatalog and
    /// LevelDefinition: gameplay identity (MatchTypeId) never carries colour,
    /// and colour is never read back to infer gameplay identity — this is the
    /// one place colour is derived from MatchTypeId, and only for temporary
    /// presentation. A future theme/skin system replaces this mapping without
    /// changing LevelDefinition, LevelCatalog, or gameplay logic.
    /// </summary>
    public sealed class MatchTypePresentation
    {
        /// <summary>
        /// Dedicated MatchTypeId for Bootstrap-only Failure test debug
        /// collectors (see LevelBootstrapper.enableFailureTestSetup). Never
        /// appears in any approved pixel layout, so a collector built from
        /// it can never be satisfied by real gameplay. Mapped here, like
        /// every other MatchTypeId, purely so no missing-colour diagnostic
        /// is logged for it.
        /// </summary>
        public static readonly MatchTypeId DebugUnmatchedMatchTypeId = new MatchTypeId("debug_unmatched");

        private static readonly Dictionary<MatchTypeId, Color> Colors = new Dictionary<MatchTypeId, Color>
        {
            // m001/m002/m003 match the collectors' finished MonsterColor
            // skins (Purple/Orange/Green respectively), read from
            // ColorPalette — the runtime mirror of ColorPalette.md — rather
            // than a separate approximation. Pixel presentation only;
            // MatchTypeId, matching logic, and level data are untouched.
            { new MatchTypeId("m001"), ColorPalette.Purple },
            { new MatchTypeId("m002"), ColorPalette.Orange },
            { new MatchTypeId("m003"), ColorPalette.Green },
            { new MatchTypeId("m004"), Color.yellow },
            { DebugUnmatchedMatchTypeId, Color.black },
        };

        public Color GetColor(MatchTypeId matchTypeId)
        {
            if (Colors.TryGetValue(matchTypeId, out Color color))
                return color;

            Debug.LogError($"MatchTypePresentation: no colour mapped for MatchTypeId '{matchTypeId}'; falling back to magenta.");
            return Color.magenta;
        }
    }
}
