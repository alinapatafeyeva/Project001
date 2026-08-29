using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project001.EditorTools
{
    /// <summary>
    /// Generates one small TMP SDF Font Asset from Inter-Regular.ttf (SIL
    /// Open Font License — see Inter-Regular.ttf.LICENSE.txt alongside it)
    /// and registers it as a fallback on Fredoka SemiBold's own
    /// fallbackFontAssetTable — nothing else changes about Fredoka SemiBold,
    /// and this fallback only ever engages for glyphs Fredoka SemiBold
    /// itself doesn't contain.
    ///
    /// Exists solely because neither Fredoka-SemiBold.ttf nor
    /// Fredoka-Medium.ttf contains a glyph for ★ (U+2605) — confirmed via a
    /// direct cmap check of both source .ttf files, not assumed — and this
    /// project's TMP Settings has no fallback font configured at all
    /// (m_fallbackFontAssets: []). Without this, a TextMeshProUGUI showing
    /// the literal "★" character with a Fredoka font asset renders a blank/
    /// missing-glyph box instead of a star. Inter was chosen because it is
    /// already sitting on this machine (bundled with the Unity Editor itself
    /// at Contents/Resources/Fonts/Inter-Regular.ttf), confirmed to contain
    /// U+2605, and is safely redistributable (OFL) — unlike copying a macOS
    /// system font such as Apple Symbols.ttf, which would neither be
    /// license-clean to embed nor present on non-Mac machines.
    ///
    /// Drives Unity's own built-in "Assets/Create/TextMeshPro/Font
    /// Asset/SDF" command exactly like FredokaFontAssetBuilder does, for the
    /// same reason: byte-for-byte what a user would get right-clicking the
    /// .ttf in the Project window, a standard Dynamic-population-mode SDF
    /// font asset that populates its atlas with ★ on demand the first time
    /// TMP actually needs it via the fallback lookup.
    ///
    /// Run via Tools/UI/Generate Star Fallback Font Asset; safe to re-run —
    /// deletes and recreates the target .asset rather than duplicating it,
    /// and only adds itself to Fredoka SemiBold's fallback table once (a
    /// Contains check guards against duplicate entries on repeat runs).
    /// </summary>
    public static class StarFallbackFontAssetBuilder
    {
        private const string SourceTtfPath = "Assets/Art/UI/Fonts/StarFallback/Inter-Regular.ttf";
        private const string FallbackAssetPath = "Assets/Art/UI/Fonts/StarFallback/Inter-Regular SDF.asset";
        private const string FredokaSemiBoldAssetPath = "Assets/Art/UI/Fonts/Fredoka/Fredoka-SemiBold SDF.asset";

        private const string CreateSdfFontAssetMenuPath = "Assets/Create/TextMeshPro/Font Asset/SDF";

        [MenuItem("Tools/UI/Generate Star Fallback Font Asset")]
        public static void GenerateStarFallbackFontAsset()
        {
            if (TMP_Settings.instance == null)
            {
                Debug.LogError("StarFallbackFontAssetBuilder: TMP Essential Resources are not imported in this project; cannot create the fallback Font Asset.");
                return;
            }

            Font sourceFont = LoadFontWithEmbeddedData(SourceTtfPath);
            if (sourceFont == null)
            {
                Debug.LogError($"StarFallbackFontAssetBuilder: could not load '{SourceTtfPath}' as a Font asset.");
                return;
            }

            DeleteIfExists(FallbackAssetPath);

            Selection.objects = new Object[] { sourceFont };
            EditorApplication.ExecuteMenuItem(CreateSdfFontAssetMenuPath);
            Selection.objects = new Object[0];

            var fallbackFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackAssetPath);
            if (fallbackFontAsset == null)
            {
                Debug.LogError($"StarFallbackFontAssetBuilder: expected '{FallbackAssetPath}' to be created but it was not found.");
                return;
            }

            Debug.Log($"StarFallbackFontAssetBuilder: generated '{FallbackAssetPath}' from '{SourceTtfPath}'.");

            var fredokaSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaSemiBoldAssetPath);
            if (fredokaSemiBold == null)
            {
                Debug.LogError($"StarFallbackFontAssetBuilder: could not load '{FredokaSemiBoldAssetPath}' to register the fallback on it. Run Tools/UI/Generate Fredoka Font Assets first.");
                return;
            }

            if (fredokaSemiBold.fallbackFontAssetTable == null)
                fredokaSemiBold.fallbackFontAssetTable = new List<TMP_FontAsset>();

            if (!fredokaSemiBold.fallbackFontAssetTable.Contains(fallbackFontAsset))
                fredokaSemiBold.fallbackFontAssetTable.Add(fallbackFontAsset);

            EditorUtility.SetDirty(fredokaSemiBold);
            AssetDatabase.SaveAssets();

            Debug.Log($"StarFallbackFontAssetBuilder: registered '{FallbackAssetPath}' as a fallback on '{FredokaSemiBoldAssetPath}'.");
        }

        /// <summary>Same technique as FredokaFontAssetBuilder.LoadFontWithEmbeddedData — the built-in Font Asset SDF command's own FontEngine.LoadFontFace(Font, ...) call requires "Include Font Data" on.</summary>
        private static Font LoadFontWithEmbeddedData(string ttfPath)
        {
            if (AssetImporter.GetAtPath(ttfPath) is TrueTypeFontImporter importer && !importer.includeFontData)
            {
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        }

        private static void DeleteIfExists(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
        }
    }
}
