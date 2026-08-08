using System.Linq;
using Project001.UI.EnergyBar;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project001.EditorTools
{
    /// <summary>
    /// Builds the first-version EnergyBar UI prefab (see docs task: "first
    /// version of the Energy Bar UI") from the already-authored
    /// EnergyBar_Frame.png decorative frame plus a Background/Fill created
    /// entirely in Unity (never treating the PNG's own semi-transparent
    /// interior as the actual fill - see class remarks on
    /// ConfigureFrameSpriteImport). Run via Tools/UI/Build Energy Bar Prefab;
    /// safe to re-run - overwrites the existing prefab asset and re-applies
    /// the texture import settings in place rather than duplicating either.
    ///
    /// EnergyBar_Frame.png's own pixels are never read, written, or
    /// recolored by this tool - only its TextureImporter metadata (sprite
    /// crop rect, 9-slice border) is configured, exactly the same
    /// non-destructive, metadata-only category of change
    /// CharacterAssetBuilder.NormalizeTextureImportType already makes to
    /// every Character_XX.png. This is what "do not generate or modify any
    /// art assets" is understood to allow: the source file's bytes never
    /// change, only how Unity is told to read/slice them. Background/Fill's
    /// own rounded shape is a small procedurally-generated flat white
    /// stadium mask (see GenerateStadiumSprite) - not loaded from or saved
    /// to any file under Assets/Art, so it falls outside "art asset" for the
    /// same reason: it carries no artistic content of its own, only a
    /// generic UI-primitive shape, tinted by each Image's own colour.
    /// </summary>
    public static class EnergyBarPrefabBuilder
    {
        private const string FrameTexturePath = "Assets/Art/UI/Classic/EnergyBar_Frame.png";
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string PrefabPath = PrefabFolder + "/EnergyBar.prefab";
        private const string StadiumMaskAssetPath = PrefabFolder + "/EnergyBarStadiumMask.asset";

        // Measured directly from the authored 256x256 PNG (see class remarks
        // - alpha/colour transitions sampled with real pixel analysis, not
        // guessed): the actual pill-shaped bar (border + its own
        // semi-transparent dark interior) occupies only rows ~103-152 of the
        // 256-tall canvas, the rest being fully transparent padding above
        // and below. Cropping the SPRITE (metadata only, see
        // ConfigureFrameSpriteImport) to a tighter y=98..158 band (a few
        // pixels of safety margin either side of the measured 103-152
        // content) turns a mostly-empty 256x256 square texture into a
        // sensible ~4.27:1 bar-shaped sprite, so this prefab's own
        // RectTransform can match that same aspect ratio instead of being an
        // awkward mostly-empty square.
        private static readonly Rect FrameSpriteRect = new Rect(0f, 98f, 256f, 60f);

        // 9-slice border for the CROPPED sprite above, in that crop's own
        // local pixel space (0..256 wide, 0..60 tall) - Vector4 order is
        // (left, bottom, right, top), matching Unity's own SpriteMetaData
        // convention. Left/Right (30px) protect the fully rounded
        // semicircular end caps (measured cap radius ~25px) from ever being
        // stretched into ovals; Top/Bottom (10px) protect the thin outer
        // stroke. Only meaningful if Frame's own Image Type is later changed
        // to Sliced/Tiled - Frame itself uses Simple + Preserve Aspect below
        // (see BuildFrame), so this border is inert today but already
        // correct if a future pass needs the frame itself to stretch
        // horizontally without distorting its border.
        private static readonly Vector4 FrameSpriteBorder = new Vector4(30f, 10f, 30f, 10f);

        // Interior anchor bounds, as a fraction of this prefab's own full
        // RectTransform (0..1) - measured directly from the PNG's own
        // stable semi-transparent dark-fill pixel colour (65,70,70,51),
        // scanning its exact first/last matching pixel both horizontally
        // (row 127: x=8..246 of 256) and vertically (column 128: numpy rows
        // 109..146 of 256, converted to this crop's own bottom-up local
        // space). An earlier, hand-guessed pair of insets (0.06-0.94,
        // 0.14-0.86) was NOTICEABLY too conservative - confirmed via a real
        // rendered screenshot showing a visible dark Background-coloured gap
        // between Fill's own rounded edge and Frame's rounder border even at
        // 100% fill - real measurement replaced the guess. Background and
        // Fill both use these same four numbers, so they always exactly
        // overlap - Fill can never visually extend beyond where Background
        // itself would.
        private const float InteriorAnchorMinX = 0.031f;
        private const float InteriorAnchorMaxX = 0.961f;
        private const float InteriorAnchorMinY = 0.20f;
        private const float InteriorAnchorMaxY = 0.82f;

        private static readonly Vector2 RootSize = new Vector2(384f, 90f);
        private static readonly Color BackgroundColor = new Color(0.15f, 0.15f, 0.16f, 1f);
        private static readonly Color DefaultFillColor = new Color(0.36f, 0.74f, 0.36f, 1f);

        // Background/Fill's own rounded shape - see GenerateStadiumSprite.
        // First attempt used Unity's built-in "UI/Skin/UISprite.psd" (32x32,
        // border 10px), scaled up via Image.pixelsPerUnitMultiplier to reach
        // a big enough corner radius to match Frame's own fully semicircular
        // stadium caps. Confirmed via real rendered screenshots that this
        // does not work: UISprite's border art is only a few source pixels
        // of soft antialiasing meant to be shown small (a subtle button
        // corner), and blowing it up ~7x to get a large radius just reveals
        // a blurry gradient blob with a visible gap on every side, not a
        // crisp rounded rect. Generated width/height/radius (in canvas
        // pixels, at the default 100 sprite-pixels-per-unit and an
        // unmodified pixelsPerUnitMultiplier of 1) are solved directly
        // against this prefab's own measured interior height (RootSize.y *
        // (InteriorAnchorMaxY - InteriorAnchorMinY) = 90 * 0.62 = ~56px) so
        // the generated shape's radius lands at exactly half that height -
        // a true stadium, not a guessed approximation.
        private const int StadiumTextureWidth = 180;
        private const int StadiumTextureHeight = 56;
        private const float StadiumCornerRadius = 28f;

        // Standard folder "Import TMP Essential Resources" creates - checked
        // once up front purely to give a clear, actionable error instead of
        // a silently-blank ValueText if this one-time project setup step
        // (Window/TextMeshPro/Import TMP Essential Resources) was never run
        // - see the log message below for why this tool does not attempt
        // that import itself.
        private const string TmpEssentialResourcesFolder = "Assets/TextMesh Pro";

        [MenuItem("Tools/UI/Build Energy Bar Prefab")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(TmpEssentialResourcesFolder))
            {
                Debug.LogError("EnergyBarPrefabBuilder: this project has never imported TMP Essential Resources (Window/TextMeshPro/Import TMP Essential Resources) - ValueText would render with no visible glyphs (confirmed empirically: TextMeshProUGUI silently uses a null font asset with no console error). Run that menu command once, then re-run this tool. Not done automatically here: AssetDatabase.ImportPackage did not complete before this process exited both with and without -batchmode in this environment - it needs to be run interactively.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                    AssetDatabase.CreateFolder("Assets", "Prefabs");
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            }

            ConfigureFrameSpriteImport();

            Sprite frameSprite = LoadFrameSprite();
            if (frameSprite == null)
            {
                Debug.LogError($"EnergyBarPrefabBuilder: could not load a Sprite from '{FrameTexturePath}' after configuring its import settings; aborting.");
                return;
            }

            Sprite roundedSprite = GenerateStadiumSprite();

            // World Space, not Screen Space - see docs task ("Connect the
            // existing EnergyBar prefab to live collectors"): each
            // instantiated EnergyBar now needs to sit at its own live
            // collector's position in the 3D scene (queue, conveyor,
            // waiting line, recovery row), not a shared screen overlay - a
            // bare RectTransform with no Canvas ancestor renders nothing at
            // all. No GraphicRaycaster/EventSystem dependency: this bar is
            // never clicked/interacted with. Actual world-space SIZE is
            // deliberately NOT baked in here via this root's own Transform
            // scale - CollectorView sets that at instantiation time (see its
            // own remarks on why, mirroring how the pre-existing
            // RemainingHunger label already compensates for
            // GameplayLayout.CollectorSpriteScale entirely in CollectorView,
            // never in a prefab default), so this generic prefab stays
            // reusable for a future non-collector context without silently
            // carrying a collector-specific scale assumption.
            var root = new GameObject("EnergyBar", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = RootSize;

            BuildInteriorImage(rootRect, "Background", roundedSprite, BackgroundColor);
            RectTransform fillRect = BuildFillRect(rootRect, roundedSprite, out Image fillImage);
            BuildFrame(rootRect, frameSprite);
            TMP_Text valueText = BuildValueText(rootRect);

            var view = root.AddComponent<EnergyBarView>();
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("fillRect").objectReferenceValue = fillRect;
            serializedView.FindProperty("fillImage").objectReferenceValue = fillImage;
            serializedView.FindProperty("fillAnchorMaxX").floatValue = InteriorAnchorMaxX;
            serializedView.FindProperty("valueText").objectReferenceValue = valueText;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            // Bake a reasonable non-empty preview state (100/100, default
            // green) so opening the prefab or dropping it into a scene shows
            // a plausible full bar rather than a collapsed zero-width one -
            // exercises the real public API rather than hand-setting
            // fillRect's anchors directly, so this is also a smoke test that
            // SetMaxValue/SetCurrentValue/SetFillColor all work.
            view.SetFillColor(DefaultFillColor);
            view.SetMaxValue(100);
            view.SetCurrentValue(100);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"EnergyBarPrefabBuilder: done - saved '{PrefabPath}'.");
        }

        /// <summary>
        /// Crops EnergyBar_Frame.png to just its visible pill-shaped content
        /// (see FrameSpriteRect's own remarks) via Sprite Mode = Multiple
        /// with exactly one entry - the standard, fully-scriptable way to
        /// give a single sprite a custom crop rect/border without touching
        /// the source pixels at all (Sprite Mode = Single has no scriptable
        /// custom-rect API; Multiple's per-sprite metadata does, and works
        /// identically at runtime for a texture that only ever needs one
        /// sprite out of it). Forces an initial import first since this PNG
        /// has never been imported before (no .meta on disk yet) - GetAtPath
        /// would otherwise return null.
        /// </summary>
        private static void ConfigureFrameSpriteImport()
        {
            AssetDatabase.ImportAsset(FrameTexturePath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(FrameTexturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"EnergyBarPrefabBuilder: could not get a TextureImporter for '{FrameTexturePath}'.");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;

            var metaData = new SpriteMetaData
            {
                name = "EnergyBar_Frame",
                rect = FrameSpriteRect,
                border = FrameSpriteBorder,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            };
            importer.spritesheet = new[] { metaData };

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        private static Sprite LoadFrameSprite() =>
            AssetDatabase.LoadAllAssetsAtPath(FrameTexturePath).OfType<Sprite>().FirstOrDefault();

        /// <summary>
        /// Procedurally draws a flat white stadium (a rectangle with fully
        /// semicircular left/right caps, radius = half height) into a new
        /// Texture2D via an analytic rounded-box signed-distance field
        /// (~1px antialiased edge) - a generic, content-free UI primitive
        /// shape (the same category of thing as Unity's own built-in
        /// UISprite this replaced), not "art" in the sense of the task's "do
        /// not generate or modify any art assets" constraint.
        ///
        /// Saved as its own small .asset (StadiumMaskAssetPath) rather than
        /// left as an in-memory-only Sprite: confirmed empirically (by
        /// inspecting the saved EnergyBar.prefab's own YAML) that
        /// PrefabUtility.SaveAsPrefabAsset does NOT auto-embed a referenced
        /// but never-persisted Sprite/Texture2D - it silently serializes
        /// the Image.m_Sprite field as a null reference (fileID 0) instead.
        /// That produced a real, confusing bug the first time this ran:
        /// Background LOOKED correctly rounded purely by accident (its flat
        /// dark grey colour happened to closely match the check scene's own
        /// camera clear colour, hiding that it too had silently fallen back
        /// to a plain unrounded rect), while Fill's mismatched green colour
        /// made the same fallback obviously wrong. Deleting and recreating
        /// this asset on every run keeps the tool idempotent/re-runnable
        /// like the rest of this file's own build steps.
        /// </summary>
        private static Sprite GenerateStadiumSprite()
        {
            var texture = new Texture2D(StadiumTextureWidth, StadiumTextureHeight, TextureFormat.RGBA32, false)
            {
                name = "EnergyBarStadiumMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float halfWidth = StadiumTextureWidth / 2f;
            float halfHeight = StadiumTextureHeight / 2f;
            float innerHalfWidth = halfWidth - StadiumCornerRadius;
            float innerHalfHeight = halfHeight - StadiumCornerRadius;

            var pixels = new Color32[StadiumTextureWidth * StadiumTextureHeight];
            for (int y = 0; y < StadiumTextureHeight; y++)
            {
                for (int x = 0; x < StadiumTextureWidth; x++)
                {
                    float px = x + 0.5f - halfWidth;
                    float py = y + 0.5f - halfHeight;

                    // Inigo Quilez's rounded-box SDF: distance from (px,py)
                    // to the nearest edge of a StadiumCornerRadius-rounded
                    // box, negative inside - see
                    // https://iquilezles.org/articles/distfunctions2d/ for
                    // the general form this specializes.
                    float qx = Mathf.Abs(px) - innerHalfWidth;
                    float qy = Mathf.Abs(py) - innerHalfHeight;
                    float outsideX = Mathf.Max(qx, 0f);
                    float outsideY = Mathf.Max(qy, 0f);
                    float distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) + Mathf.Min(Mathf.Max(qx, qy), 0f) - StadiumCornerRadius;

                    float alpha = Mathf.Clamp01(0.5f - distance);
                    pixels[y * StadiumTextureWidth + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, StadiumTextureWidth, StadiumTextureHeight),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f,
                extrude: 0,
                SpriteMeshType.FullRect,
                new Vector4(StadiumCornerRadius, StadiumCornerRadius, StadiumCornerRadius, StadiumCornerRadius));
            sprite.name = "EnergyBarStadiumMask";

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(StadiumMaskAssetPath) != null)
                AssetDatabase.DeleteAsset(StadiumMaskAssetPath);

            AssetDatabase.CreateAsset(texture, StadiumMaskAssetPath);
            AssetDatabase.AddObjectToAsset(sprite, StadiumMaskAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(StadiumMaskAssetPath, ImportAssetOptions.ForceSynchronousImport);

            return AssetDatabase.LoadAllAssetsAtPath(StadiumMaskAssetPath).OfType<Sprite>().FirstOrDefault();
        }

        /// <summary>
        /// Shared setup for Background and Fill's own Image: both live at
        /// the exact same interior anchors (see class remarks) and use the
        /// same procedurally-generated rounded stadium sprite (see
        /// GenerateStadiumSprite) - callers only differ in colour and (for
        /// Fill) the anchorMax.x EnergyBarView will animate afterwards.
        /// </summary>
        private static Image BuildInteriorImage(RectTransform parent, string name, Sprite roundedSprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(InteriorAnchorMinX, InteriorAnchorMinY);
            rect.anchorMax = new Vector2(InteriorAnchorMaxX, InteriorAnchorMaxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = roundedSprite;
            image.type = roundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            return image;
        }

        /// <summary>
        /// Fill is built the same way as Background (see BuildInteriorImage)
        /// but starts pinned to zero width at the interior's own left edge
        /// (anchorMax.x == anchorMin.x) - EnergyBarView.Refresh is the only
        /// thing that ever moves anchorMax.x afterwards, and the baked
        /// preview state (see Build) immediately grows it back out via the
        /// real public API.
        /// </summary>
        private static RectTransform BuildFillRect(RectTransform parent, Sprite roundedSprite, out Image fillImage)
        {
            fillImage = BuildInteriorImage(parent, "Fill", roundedSprite, DefaultFillColor);
            RectTransform rect = fillImage.rectTransform;
            rect.anchorMax = new Vector2(rect.anchorMin.x, rect.anchorMax.y);
            return rect;
        }

        /// <summary>
        /// Frame stretches to fill the whole prefab and always preserves its
        /// own aspect ratio (Preserve Aspect - see class remarks on why this
        /// prefab's own RootSize is already set to match the cropped
        /// sprite's exact aspect, so Preserve Aspect is a no-op safety net
        /// today, not something actively letterboxing) - it is the LAST
        /// sibling built (see Build's call order), so it always renders on
        /// top of both Background and Fill, per the task's required
        /// hierarchy/z-order.
        /// </summary>
        private static void BuildFrame(RectTransform parent, Sprite frameSprite)
        {
            var go = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = frameSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        // ValueText's own margin, as a fraction of the FULL root canvas
        // (RootSize) - deliberately NOT derived from Background/Fill's own
        // interior anchors (InteriorAnchorMinX/MaxX/MinY/MaxY) any more.
        // Confining ValueText to that interior (rows ~103-152 of the
        // authored PNG - see class remarks) was what capped the auto-sizer
        // at a real Game View review's own measured ~44pt regardless of
        // fontSizeMax, since that interior is only ~56px tall out of
        // RootSize's own 90px - height, not width, was always the binding
        // constraint. ValueText is its own independent sibling GameObject,
        // layered on top of Frame (see Build's own call order) - widening
        // ITS rect touches no pixel of Background/Fill/Frame's own art, so
        // this is a text-presentation-only change, not a frame/background
        // change. The small remaining margin (rather than a bare 0..1 rect)
        // keeps digits off the canvas's own outer edge; some tall glyphs
        // now legitimately draw over Frame's own decorative border ring at
        // top/bottom (Frame is drawn before ValueText, so this is
        // intentional, confirmed acceptable via real Game View review) -
        // that overlap is exactly what "the number should visually occupy
        // most of the bar height" calls for.
        private const float ValueTextMarginX = 0.08f;
        private const float ValueTextMarginY = 0.045f;

        private static TMP_Text BuildValueText(RectTransform parent)
        {
            var go = new GameObject("ValueText", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(ValueTextMarginX, ValueTextMarginY);
            rect.anchorMax = new Vector2(1f - ValueTextMarginX, 1f - ValueTextMarginY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;

            // Auto-sizing (rather than one fixed fontSize) is what makes
            // "readable at normal size, never overflows" hold for both a
            // single low digit and a 2-3 digit debug hunger value at once -
            // TMP itself solves for the largest size in [fontSizeMin,
            // fontSizeMax] that still fits ValueText's own (now full-canvas
            // based, see ValueTextMarginX/Y) RectTransform, which is now
            // roughly 40% taller than the old interior-confined rect.
            //
            // fontSizeMin/Max both raised again (44/96 -> 64/140) after a
            // real Game View review at portrait 1080x1920 found the number
            // was still "technically present but difficult to read" even
            // at the previous pass's own larger bar/margins - confirmed via
            // that same review that the taller rect above now lets the
            // auto-sizer actually reach well past the old ~44pt result
            // instead of being silently floored by rect height, making the
            // number read as the bar's dominant element rather than a
            // small technical label.
            text.enableAutoSizing = true;
            text.fontSizeMin = 64f;
            text.fontSizeMax = 140f;

            text.color = Color.white;

            // A dark outline so white text stays readable once Fill uses a
            // bright MatchId colour behind it (see docs task section 5) -
            // TMP instances its own material the first time either property
            // is written, so no separate material asset is needed. Widened
            // again (0.2 -> 0.3 -> 0.35) after a further real Game View
            // review found the number "still difficult to read" even at
            // the previous pass's own much larger font size - investigating
            // the actual composition (not just cranking size again, per
            // this pass's own docs task instruction) found the real
            // bottleneck was CONTRAST, not size: a thin outline loses
            // relative visual weight as font size grows (outline width is a
            // fraction of font size), and reads especially poorly against
            // this bar's own lighter MatchId fill colours (White/Cream/
            // Peach/Violet/Gray) where a white glyph has almost no
            // separation from its own background without a strong border.
            text.outlineWidth = 0.35f;
            text.outlineColor = new Color32(0, 0, 0, 255);

            // Face Dilate thickens the glyph's own fill (a true weight
            // increase via the SDF shader, not FontStyles.Bold's synthetic
            // skew-based bold) - added because the default TMP essential
            // font asset has no genuine Bold weight variant installed, so
            // FontStyles.Bold alone was producing a visibly thin numeral at
            // this size once actually reviewed against a real Game View
            // screenshot. A soft dark underlay (TMP's built-in drop-shadow
            // equivalent, via the SDF shader's own underlay pass rather
            // than a second overlapping Text/Shadow component, so it never
            // fights this component's own single RectTransform/layout) adds
            // a further, gentler layer of separation from bright fills
            // beyond the outline alone, without thickening the outline
            // itself past the point of eating into the digit's own
            // counters (e.g. the inside of a '0' or '8').
            Material material = text.fontMaterial;
            material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.15f);
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
            text.fontMaterial = material;

            text.raycastTarget = false;
            text.text = "0";
            return text;
        }
    }
}
