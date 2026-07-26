using System.Collections.Generic;
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

        // Approximate sRGB reads of Assets/Art/ColorPalette.png's Purple (#11),
        // Orange (#2), and Green (#5) swatches — picked by eye against that
        // reference image, not sampled pixel-exact. Re-tune here if the
        // finished Mofu art establishes exact hex values later; nothing else
        // needs to change.
        private static readonly Color Purple = new Color(0.557f, 0.267f, 0.678f); // ~#8E44AD
        private static readonly Color Orange = new Color(0.953f, 0.612f, 0.071f); // ~#F39C12
        private static readonly Color Green = new Color(0.153f, 0.682f, 0.376f); // ~#27AE60

        private static readonly Dictionary<MatchTypeId, Color> Colors = new Dictionary<MatchTypeId, Color>
        {
            // m001/m002/m003 now match the collectors' finished MonsterColor
            // skins (Purple/Orange/Green respectively) instead of technical
            // red/green/blue — pixel presentation only; MatchTypeId, matching
            // logic, and level data are untouched.
            { new MatchTypeId("m001"), Purple },
            { new MatchTypeId("m002"), Orange },
            { new MatchTypeId("m003"), Green },
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
