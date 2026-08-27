using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project001.EditorTools
{
    /// <summary>
    /// Generates the two TMP SDF Font Assets the Exit confirmation modal
    /// (BootstrapSceneCreator.CreateExitConfirmPanel) loads by path and
    /// assigns to its TextMeshProUGUI elements — never a global TMP
    /// Settings default font, so this stays scoped to whichever UI
    /// explicitly references these two assets.
    ///
    /// Drives Unity's own built-in "Assets/Create/TextMeshPro/Font
    /// Asset/SDF" command (by selecting each source Font and invoking that
    /// menu item) rather than hand-rolling a re-implementation, so the
    /// output is byte-for-byte what a user would get right-clicking each
    /// .ttf in the Project window — a standard Dynamic-population-mode SDF
    /// font asset, atlas populated on demand the first time each glyph is
    /// actually needed at runtime/edit time (this is normal TMP behaviour:
    /// the saved .asset legitimately starts with a 1x1 placeholder atlas
    /// texture, not a bug — TryAddCharacters-ing it up front here would
    /// only be undone by TMP's own reimport pipeline, which resets Dynamic
    /// atlases on save by design).
    ///
    /// Run via Tools/UI/Generate Fredoka Font Assets; safe to re-run —
    /// deletes and recreates the two target .asset files rather than
    /// duplicating them (Unity's own command would otherwise suffix a new
    /// copy, e.g. "... SDF 1.asset"). The source .ttf files themselves are
    /// only ever read, never moved, renamed, or modified — only their
    /// TextureImporter-equivalent (TrueTypeFontImporter) import settings
    /// may be adjusted, the same non-destructive, metadata-only category of
    /// change already used elsewhere in this project's Editor tools.
    /// </summary>
    public static class FredokaFontAssetBuilder
    {
        private const string FredokaFolder = "Assets/Art/UI/Fonts/Fredoka";
        private const string SemiBoldTtfPath = FredokaFolder + "/Fredoka-SemiBold.ttf";
        private const string MediumTtfPath = FredokaFolder + "/Fredoka-Medium.ttf";

        private const string SemiBoldAssetPath = FredokaFolder + "/Fredoka-SemiBold SDF.asset";
        private const string MediumAssetPath = FredokaFolder + "/Fredoka-Medium SDF.asset";

        private const string CreateSdfFontAssetMenuPath = "Assets/Create/TextMeshPro/Font Asset/SDF";

        [MenuItem("Tools/UI/Generate Fredoka Font Assets")]
        public static void GenerateFredokaFontAssets()
        {
            if (TMP_Settings.instance == null)
            {
                Debug.LogError("FredokaFontAssetBuilder: TMP Essential Resources are not imported in this project; cannot create Font Assets.");
                return;
            }

            Font semiBoldFont = LoadFontWithEmbeddedData(SemiBoldTtfPath);
            Font mediumFont = LoadFontWithEmbeddedData(MediumTtfPath);

            if (semiBoldFont == null || mediumFont == null)
            {
                Debug.LogError("FredokaFontAssetBuilder: could not load one or both source .ttf files as Font assets.");
                return;
            }

            DeleteIfExists(SemiBoldAssetPath);
            DeleteIfExists(MediumAssetPath);

            Selection.objects = new Object[] { semiBoldFont, mediumFont };
            EditorApplication.ExecuteMenuItem(CreateSdfFontAssetMenuPath);
            Selection.objects = new Object[0];

            bool semiBoldCreated = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiBoldAssetPath) != null;
            bool mediumCreated = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumAssetPath) != null;

            if (semiBoldCreated)
                Debug.Log($"FredokaFontAssetBuilder: generated '{SemiBoldAssetPath}' from '{SemiBoldTtfPath}'.");
            else
                Debug.LogError($"FredokaFontAssetBuilder: expected '{SemiBoldAssetPath}' to be created but it was not found.");

            if (mediumCreated)
                Debug.Log($"FredokaFontAssetBuilder: generated '{MediumAssetPath}' from '{MediumTtfPath}'.");
            else
                Debug.LogError($"FredokaFontAssetBuilder: expected '{MediumAssetPath}' to be created but it was not found.");
        }

        private static void DeleteIfExists(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
        }

        /// <summary>
        /// Loads the Font at ttfPath, forcing "Include Font Data" on in its
        /// TrueTypeFontImporter first if needed — the built-in Font Asset
        /// SDF command's own FontEngine.LoadFontFace(Font, ...) call
        /// requires it, and the importer default is not guaranteed on
        /// across Unity versions. Never touches any other import setting,
        /// and never writes to the source .ttf file itself, only its .meta.
        /// </summary>
        private static Font LoadFontWithEmbeddedData(string ttfPath)
        {
            if (AssetImporter.GetAtPath(ttfPath) is TrueTypeFontImporter importer && !importer.includeFontData)
            {
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        }
    }
}
