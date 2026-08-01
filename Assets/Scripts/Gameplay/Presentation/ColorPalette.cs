using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Single source of truth for approved 3D Mofu fur colours — the code
    /// counterpart to Assets/Art/ColorPalette.md's 2D sprite/pixel palette,
    /// not a duplicate of it. ColorPalette.md's own Orange/Green/Purple
    /// swatches (#fe920b/#00c02d/#8e1dfe) are tuned for flat, unlit 2D art;
    /// once shaded by a Directional Light on a 3D URP/Lit surface, those
    /// same hues read as oversaturated. These three entries are the exact,
    /// separately approved values for the 3D Mofu spike's fur materials —
    /// Mofu3DSetup reads them from here rather than hardcoding its own
    /// approximation, so there is exactly one place to update if the
    /// approved values ever change.
    /// </summary>
    public static class ColorPalette
    {
        public static readonly Color MofuOrange = (Color)new Color32(0xD8, 0x6B, 0x25, 0xFF);
        public static readonly Color MofuGreen = (Color)new Color32(0x2D, 0x9A, 0x34, 0xFF);
        public static readonly Color MofuPurple = (Color)new Color32(0x72, 0x2B, 0xA9, 0xFF);
    }
}
