using System.Collections.Generic;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Feeding;
using Project001.Gameplay.Presentation;
using UnityEditor;
using UnityEngine;

namespace Project001.EditorTools
{
    /// <summary>
    /// Builds the 20 project-owned Character_XX prefabs (and their baked
    /// Character_XX.mat materials) from the fixed Match ID -&gt; Character
    /// table below and the vendor Cube Animals prefabs, replacing the old
    /// HSV recoloring pipeline (OctopusColorVariants) and single-species
    /// wrapper builder (OctopusSetup) entirely. Run once via
    /// Tools/Characters/Build All Character Prefabs (or -executeMethod in
    /// batch mode); safe to re-run any number of times, since it only ever
    /// reads the vendor source and the already-authored Character_XX.png
    /// files, and only ever writes under
    /// Assets/Art/Themes/Classic/Character/Character_XX/ - never under
    /// Assets/Cube Animals 02/.
    ///
    /// Unlike the pipeline this replaces, no pixel of any texture is ever
    /// generated or recolored here: each Character_XX.png is used exactly
    /// as authored, as the body material's _BaseMap/_MainTex. The only
    /// generated assets are the material that points at it and the prefab
    /// that assembles vendor body + vendor face around it - both baked once
    /// at build time, never swapped or replaced at runtime.
    ///
    /// Two calibration passes, both baked once here and never touched again
    /// at runtime (see CharacterVerification.RunDiagnostics for how the
    /// numbers below were actually measured, under the real Bootstrap.unity
    /// Main Camera/Directional Light/ambient rig, not guessed from raw
    /// texture RGB):
    ///
    /// 1. Emission lift (EmissionIntensityBySpecies) - URP's Directional
    /// Light + flat ambient rig (see BootstrapSceneCreator.CreateKeyLight/
    /// ConfigureEnvironmentLighting) noticeably darkens a fully matte
    /// (Smoothness 0, Metallic 0, white _BaseColor) body relative to its
    /// flat authored texture color - confirmed by real rendered-pixel
    /// measurement: hue is preserved almost exactly, but measured Value
    /// dropped ~0.21-0.42 below each Match ID's canonical
    /// Assets/Art/ColorPalette.md Value, varying by species (worse on
    /// Turtle's more strongly-curved shell than on Fish's flatter body).
    /// Each material's _EmissionMap is set to that same Character_XX.png
    /// (so the added light follows the texture's own per-pixel shading
    /// instead of flattening it) with a per-species _EmissionColor
    /// intensity closing that measured gap, while the Directional Light's
    /// own Lambertian shading still visibly shapes the mesh underneath it -
    /// this is additive lift, not a flat unlit override.
    ///
    /// 2. Shared-height root scale (see BuildCharacter) - every species is
    /// scaled to the SAME target body height (GameplayLayout.
    /// CollectorVisibleHeightRatio), independent of its own width. This
    /// replaced an earlier "contain-fit" design (the more restrictive of a
    /// width-fit and a height-fit) that scaled each species down
    /// independently whenever it was wider than a fixed width envelope -
    /// which kept the queue from overlapping, but produced up to a ~69%
    /// perceived-height spread between species (Crab in particular read as
    /// roughly half the height of Octopus), a visually inconsistent "size
    /// category" difference the queue-overlap fix should never have been
    /// solved by. The design now inverts that: every species reads as the
    /// same height by construction (a single shared multiplier of that
    /// species' own measured height, never touching width at all), and
    /// GameplayLayout's queue column spacing is instead sized to the
    /// widest species that results (see GameplayLayout.
    /// CollectorVisibleWidthRatio's own remarks for the measured value and
    /// how it was derived) - the queue adapts to the roster, not the other
    /// way around.
    ///
    /// Every assembled model also gets a FeedTarget child (see
    /// CreateFeedTarget) - the world position a FoodPacket flies to under
    /// the Pixel Feed Flow pipeline (Project001.Gameplay.Feeding) - and a
    /// GroundAnchor child (see CreateGroundAnchor) - this species' own true
    /// visual ground/contact point, consumed by CollectorView to align
    /// riding presentation to the Conveyor belt without ever assuming every
    /// species shares the same contact point relative to its own bounds
    /// center. Added for all four species here, not per-species by hand.
    /// </summary>
    public static class CharacterAssetBuilder
    {
        private enum Species
        {
            Crab,
            Turtle,
            Fish,
            Octopus,
        }

        // Assets/Art/ColorPalette.md's Match ID -> Character table, fixed
        // forever. This is the only place a Match ID is ever associated
        // with a species - gameplay code never sees "Crab"/"Fish"/etc,
        // only the Match ID itself (see CharacterDatabase).
        private static readonly Dictionary<int, Species> MatchIdToSpecies = new Dictionary<int, Species>
        {
            { 1, Species.Crab },
            { 2, Species.Crab },
            { 3, Species.Turtle },
            { 4, Species.Turtle },
            { 5, Species.Turtle },
            { 6, Species.Fish },
            { 7, Species.Octopus },
            { 8, Species.Fish },
            { 9, Species.Turtle },
            { 10, Species.Fish },
            { 11, Species.Octopus },
            { 12, Species.Octopus },
            { 13, Species.Octopus },
            { 14, Species.Crab },
            { 15, Species.Crab },
            { 16, Species.Turtle },
            { 17, Species.Fish },
            { 18, Species.Octopus },
            { 19, Species.Fish },
            { 20, Species.Crab },
        };

        private const string VendorBodyRoot = "Assets/Cube Animals 02/Customize Here/Body WO Root";
        private const string VendorFaceRoot = "Assets/Cube Animals 02/Customize Here/Faces";

        // One vendor colour variant per species, used purely as a
        // structural/rig template - its own texture is replaced by this
        // Match ID's Character_XX.png, so which vendor colour is picked
        // here is otherwise irrelevant.
        private static readonly Dictionary<Species, string> BodyPrefabPath = new Dictionary<Species, string>
        {
            { Species.Crab, $"{VendorBodyRoot}/Crab Blue.prefab" },
            { Species.Turtle, $"{VendorBodyRoot}/Turtle Blue.prefab" },
            { Species.Fish, $"{VendorBodyRoot}/Fish Blue.prefab" },
            { Species.Octopus, $"{VendorBodyRoot}/Octopus Blue.prefab" },
        };

        // Which socket(s) on the body each species' face attaches to, and
        // which vendor face prefab to use - confirmed by inspecting each
        // body prefab's own child hierarchy. Crab is the only species with
        // two independent eye sockets rather than one combined face socket.
        private static readonly Dictionary<Species, (string socketName, string facePrefabPath)[]> FaceAttachments = new Dictionary<Species, (string, string)[]>
        {
            { Species.Crab, new[]
                {
                    ("+ L Eye", $"{VendorFaceRoot}/Crab L Eye 01.prefab"),
                    ("+ R Eye", $"{VendorFaceRoot}/Crab R Eyes 01.prefab"),
                }
            },
            { Species.Turtle, new[] { ("+ Head", $"{VendorFaceRoot}/Turtle Face 01.prefab") } },
            { Species.Fish, new[] { ("+ Head", $"{VendorFaceRoot}/Fish Face 01.prefab") } },
            { Species.Octopus, new[] { ("+ Face", $"{VendorFaceRoot}/Octopus Face 01.prefab") } },
        };

        private const string CharacterRoot = "Assets/Art/Themes/Classic/Character";

        // The single shared body-height target every species' root scale is
        // derived from (see BuildCharacter) - read directly from
        // GameplayLayout rather than re-derived here so the two can never
        // drift apart. No corresponding "target width": width is never part
        // of the scale decision any more (see class remarks) - each
        // species' resulting width is instead measured AFTER scaling and
        // fed into GameplayLayout.CollectorVisibleWidthRatio, the reverse
        // dependency direction contain-fit used to have.
        private static readonly float TargetVisibleHeight = GameplayLayout.CollectorVisibleHeightRatio;

        // Per-species Emission intensity (see class remarks) - the scalar
        // _EmissionColor multiplier applied to each material's own
        // Character_XX.png used again as _EmissionMap. Measured via
        // CharacterVerification.RunDiagnostics against the real Bootstrap
        // lighting rig: each value closes that species' own measured
        // rendered-Value shortfall versus its canonical ColorPalette Value.
        // Deliberately per-species, not one shared constant - the shortfall
        // itself varies by how strongly curved/self-shadowed each species'
        // geometry is under the Directional Light, not by texture hue, so a
        // single blanket multiplier over- or under-corrects some species.
        private static readonly Dictionary<Species, float> EmissionIntensityBySpecies = new Dictionary<Species, float>
        {
            { Species.Crab, 0.55f },
            { Species.Turtle, 1.35f },
            { Species.Fish, 0.64f },
            { Species.Octopus, 0.85f },
        };

        // Escape hatch for a single Match ID whose measured result still
        // doesn't land close enough to canonical at its species' shared
        // intensity - empty until real measurement shows one is needed.
        // Overrides EmissionIntensityBySpecies for that id only when present.
        private static readonly Dictionary<int, float> EmissionIntensityByMatchIdOverride = new Dictionary<int, float>();

        [MenuItem("Tools/Characters/Build All Character Prefabs")]
        public static void BuildAll()
        {
            int succeeded = 0;
            int failed = 0;

            for (int matchId = 1; matchId <= 20; matchId++)
            {
                if (BuildCharacter(matchId))
                    succeeded++;
                else
                    failed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"CharacterAssetBuilder: done. {succeeded} succeeded, {failed} failed (see errors above for any failures).");
        }

        private static bool BuildCharacter(int matchId)
        {
            string idLabel = matchId.ToString("D2");

            if (!MatchIdToSpecies.TryGetValue(matchId, out Species species))
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} has no species mapping; skipping.");
                return false;
            }

            string characterFolder = $"{CharacterRoot}/Character_{idLabel}";
            string texturePath = $"{characterFolder}/Character_{idLabel}.png";
            string materialPath = $"{characterFolder}/Character_{idLabel}.mat";
            string prefabPath = $"{characterFolder}/Character_{idLabel}.prefab";

            if (!NormalizeTextureImportType(texturePath, idLabel))
                return false;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not load texture at '{texturePath}'; skipping.");
                return false;
            }

            var bodyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath[species]);
            if (bodyAsset == null)
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not load vendor body prefab at '{BodyPrefabPath[species]}'; skipping.");
                return false;
            }

            var root = new GameObject($"Character_{idLabel}");

            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyAsset, root.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;

            foreach ((string socketName, string facePrefabPath) in FaceAttachments[species])
            {
                var faceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(facePrefabPath);
                if (faceAsset == null)
                {
                    Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not load vendor face prefab at '{facePrefabPath}'; skipping.");
                    Object.DestroyImmediate(root);
                    return false;
                }

                Transform socket = FindDeep(body.transform, socketName);
                if (socket == null)
                {
                    Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not find socket '{socketName}' under vendor body '{BodyPrefabPath[species]}'; skipping.");
                    Object.DestroyImmediate(root);
                    return false;
                }

                var face = (GameObject)PrefabUtility.InstantiatePrefab(faceAsset, socket);
                face.transform.localPosition = Vector3.zero;
                face.transform.localRotation = Quaternion.identity;
                face.transform.localScale = Vector3.one;
            }

            var bodySkinnedMeshRenderer = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySkinnedMeshRenderer == null)
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not find the vendor body's SkinnedMeshRenderer; skipping.");
                Object.DestroyImmediate(root);
                return false;
            }

            Material bakedMaterial = BuildBakedMaterial(bodySkinnedMeshRenderer.sharedMaterial, texture, matchId, species, materialPath);
            bodySkinnedMeshRenderer.sharedMaterial = bakedMaterial;

            // Vendor rig ships with a multi-state Animator Controller that
            // auto-cycles through unrelated actions (Move/Spawn/Cast Spell/
            // Take Damage/Die/...) - every character here is a static queue/
            // conveyor visual with no animation of its own, so the Animator
            // is disabled outright rather than left to cycle through those.
            // Whether this runs before or after the Crab pose adapter is
            // configured below has no bearing on either step - the Animator
            // was never enabled/driving anything during this build pass to
            // begin with (PrefabUtility.InstantiatePrefab does not run
            // animation), so every arm bone is still sitting at its raw
            // imported (bind) rotation when ApplyCrabPosePresentation reads
            // it below, regardless of this ordering. Unlike the one-off
            // build-time bone edit this replaced, bones ARE re-posed again
            // at runtime now, on request - see CharacterPosePresentation -
            // but only ever by that component's own SetPose, driven only by
            // CollectorPresentation; the Animator being permanently disabled
            // just means nothing else (no clip, no other system) ever also
            // drives these bones.
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.enabled = false;
            }

            if (species == Species.Crab)
                ApplyCrabPosePresentation(body, idLabel);

            // The same rootBone-space-aware bounds computation CollectorView
            // uses at runtime to pivot-center this same prefab instance (see
            // CollectorAnimation.ComputeLocalRendererBounds) - measured here,
            // at root's own local space (root has no scale/rotation applied
            // yet), so build-time scale and runtime pivot-centering can never
            // disagree about this character's actual assembled size.
            Bounds? combined = CollectorAnimation.ComputeLocalRendererBounds(root.transform);
            if (!combined.HasValue || combined.Value.size.y <= 0f)
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - no renderer bounds found under the assembled model; cannot derive root scale; skipping.");
                Object.DestroyImmediate(root);
                return false;
            }

            // Every species gets a FeedTarget (not just Crab - see
            // CreateFeedTarget), placed from this same bounds measurement
            // before root's own scale is applied below, so it ends up as an
            // ordinary child transform that scales with the rest of the
            // model automatically.
            CreateFeedTarget(root, combined.Value);

            // Every species also gets a GroundAnchor (see CreateGroundAnchor)
            // - this species' own true visual ground/contact point, from the
            // exact same pre-scale bounds measurement, for the exact same
            // "ordinary child transform, scales automatically" reason.
            CreateGroundAnchor(root, combined.Value, species);

            Vector3 size = combined.Value.size;

            // Shared-height-only: every species is scaled so its own height
            // (size.y) matches the SAME TargetVisibleHeight, full stop - no
            // width term in this formula at all any more (see class
            // remarks for why the earlier contain-fit design, which capped
            // scale whenever width would have exceeded a fixed envelope,
            // was replaced). A uniform scalar (never non-uniform per-axis
            // scaling), so aspect ratio/proportions are preserved exactly;
            // width is left to fall out naturally from that scale and each
            // species' own raw proportions, then measured (not assumed) via
            // CharacterVerification.RunDiagnostics to size
            // GameplayLayout.CollectorVisibleWidthRatio - see its remarks.
            float rootScale = TargetVisibleHeight / size.y;

            root.transform.localScale = Vector3.one * rootScale;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            float scaledHeight = size.y * rootScale;
            float scaledBodyWidth = size.x * rootScale;
            float scaledMaxVisualWidth = Mathf.Max(size.x, size.z) * rootScale;
            Debug.Log($"CharacterAssetBuilder: Match ID {idLabel} ({species}) -> '{prefabPath}' (root scale {rootScale:F3}, unscaled bounds size=({size.x:F3},{size.y:F3},{size.z:F3}), scaled height={scaledHeight:F3}, scaled body width(x)={scaledBodyWidth:F3}, scaled max visual width(any facing)={scaledMaxVisualWidth:F3}, material '{materialPath}').");
            return true;
        }

        // How far up from bounds' vertical center a FeedTarget sits,
        // expressed as a fraction of the model's own half-height
        // (localBounds.extents.y) - 1.0 would sit exactly at the model's
        // topmost point (the very top of the head/antennae/shell), 0.0 at
        // its vertical center. 0.7 was picked as a reasonable first pass at
        // "approximately the mouth/upper face" (see class remarks on
        // Phase 1 of the Pixel Feed Flow pipeline) without measuring each
        // species' actual face geometry - a single shared fraction applied
        // to each species' own measured bounds already produces a
        // species-appropriate absolute offset (a taller model's FeedTarget
        // sits proportionally higher too), and this one constant is the
        // only thing a future pass needs to retune if it reads wrong for a
        // particular species once actually seen in Play Mode.
        private const float FeedTargetHeightFraction = 0.7f;

        /// <summary>
        /// Adds a FeedTarget child to every species' assembled model (never
        /// gated on species, unlike the Crab-only pose adapter above) at a
        /// position derived purely from this model's own measured bounds -
        /// never a hand-placed or screen-space position. Added as a child of
        /// root itself (not body, unlike CharacterPosePresentation) so its
        /// position is unaffected by which vendor body sub-hierarchy a
        /// future species swap might restructure; root's own later
        /// localScale assignment (see BuildCharacter, right after this call)
        /// scales this child along with everything else automatically, so
        /// localBounds - measured here in root's own PRE-scale local space -
        /// is exactly the right space to compute this position in.
        /// </summary>
        private static void CreateFeedTarget(GameObject root, Bounds localBounds)
        {
            var feedTargetObject = new GameObject("FeedTarget", typeof(FeedTarget));
            feedTargetObject.transform.SetParent(root.transform, false);
            feedTargetObject.transform.localPosition =
                localBounds.center + Vector3.up * (localBounds.extents.y * FeedTargetHeightFraction);
        }

        // How far DOWN from bounds' vertical center a GroundAnchor sits by
        // default, expressed as a fraction of the model's own half-height
        // (localBounds.extents.y) - mirrors FeedTargetHeightFraction's own
        // convention exactly (a single shared fraction applied to each
        // species' own measured bounds already produces a species-
        // appropriate absolute offset), just measured downward instead of
        // upward. 1.0 sits exactly at the model's own lowest rendered point
        // (bounds.min.y) - the sensible default for "where does this model
        // actually touch the ground", per the investigation's own request
        // to "prefer deriving an initial position from renderer bounds".
        private const float GroundAnchorHeightFraction = 1.0f;

        // Escape hatch for a single species whose bounds-bottom measurement
        // does not land on that species' own true visual contact point
        // (e.g. a stray low-hanging polygon, or a limb that extends below
        // where the model actually reads as "standing") - empty until real
        // Play Mode screenshot review shows one is needed for a specific
        // species. Overrides GroundAnchorHeightFraction for that species
        // only when present. Deliberately keyed by Species, not MatchId:
        // GroundAnchor is a per-MODEL-FAMILY concept (see GroundAnchor's own
        // class remarks) - every Match ID sharing a species shares its
        // model, and therefore its correct ground contact point.
        private static readonly Dictionary<Species, float> GroundAnchorHeightFractionOverride = new Dictionary<Species, float>();

        /// <summary>
        /// Adds a GroundAnchor child to every species' assembled model,
        /// mirroring CreateFeedTarget immediately above almost exactly (see
        /// its own remarks for why root, not body, and why pre-scale
        /// localBounds is the right space) - measuring DOWN from bounds'
        /// vertical center instead of up. This is this species' own true
        /// visual ground/contact point (see GroundAnchor's own class
        /// remarks), consumed at runtime by CollectorView to align the
        /// character's PRESENTATION to wherever it is actually standing
        /// (Conveyor riding today - see CollectorView.
        /// ApplyConveyorPresentationOffset) - never derived from the
        /// collector root, and never a per-MatchId runtime lookup: this is
        /// the only place any species-specific ground-contact knowledge
        /// exists at all, baked once per model family, at build time.
        /// </summary>
        private static void CreateGroundAnchor(GameObject root, Bounds localBounds, Species species)
        {
            float fraction = GroundAnchorHeightFractionOverride.TryGetValue(species, out float overrideFraction)
                ? overrideFraction
                : GroundAnchorHeightFraction;

            var groundAnchorObject = new GameObject("GroundAnchor", typeof(GroundAnchor));
            groundAnchorObject.transform.SetParent(root.transform, false);
            groundAnchorObject.transform.localPosition =
                localBounds.center - Vector3.up * (localBounds.extents.y * fraction);
        }

        /// <summary>
        /// Clones the vendor body material (Object.Instantiate, not a
        /// hand-typed property list) so every shading property besides the
        /// texture matches the vendor original exactly; only
        /// _BaseMap/_MainTex is repointed at this Match ID's own
        /// Character_XX.png, plus this species' Emission lift baked on top
        /// (see class remarks) - _EmissionMap is the same Character_XX.png,
        /// _EmissionColor is a flat white scaled by the calibrated
        /// intensity, so the added light is proportional to the texture's
        /// own per-pixel brightness rather than a flat wash. Re-running this
        /// tool updates the existing material asset in place (via
        /// EditorUtility.CopySerialized) rather than creating a duplicate.
        /// </summary>
        private static Material BuildBakedMaterial(Material vendorMaterial, Texture2D texture, int matchId, Species species, string materialPath)
        {
            var material = new Material(vendorMaterial);
            material.name = $"Character_{matchId:D2}";
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);

            float emissionIntensity = EmissionIntensityByMatchIdOverride.TryGetValue(matchId, out float overrideIntensity)
                ? overrideIntensity
                : EmissionIntensityBySpecies[species];

            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", Color.white * emissionIntensity);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(material, existing);
                Object.DestroyImmediate(material);
                return existing;
            }

            AssetDatabase.CreateAsset(material, materialPath);
            return material;
        }

        /// <summary>
        /// Character_XX.png files auto-imported as Sprite (Multiple) -
        /// never intentionally so, since these are only ever read as a 3D
        /// material's _BaseMap/_EmissionMap, likely just this project's
        /// default new-texture import preset (tuned for its other, genuinely
        /// 2D sprite assets) applying to any PNG dropped under Assets/.
        /// Normalizes the import TYPE to Default - metadata only, in the
        /// .meta file; the authored PNG's own pixel bytes are never touched.
        /// A no-op (returns true immediately) if already Default.
        /// </summary>
        private static bool NormalizeTextureImportType(string texturePath, string idLabel)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not get TextureImporter at '{texturePath}'; skipping.");
                return false;
            }

            if (importer.textureType == TextureImporterType.Default)
                return true;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
            return true;
        }

        // Explicit bone names, discovered from the vendor Crab rig itself
        // (readable strings embedded in Assets/Cube Animals 02/FBX/No Root/
        // Crab/Crab.fbx - RigHub is the root, RigLArm1/RigRArm1 the
        // shoulder joint of each claw's arm chain, RigLArm2/RigRArm2 the
        // elbow joint, continuing on to RigLArm3/RigRArm3 the wrist and then
        // the palm/fingers, neither of which this pose adapter touches) -
        // never a fragile approximate name/renderer match.
        private const string CrabLeftShoulderBone = "RigLArm1";
        private const string CrabRightShoulderBone = "RigRArm1";
        private const string CrabLeftElbowBone = "RigLArm2";
        private const string CrabRightElbowBone = "RigRArm2";

        // Blend duration for CharacterPosePresentation's Waiting<->Conveyor
        // transition - inside the project's requested 0.12-0.2s window, and
        // deliberately the same order of magnitude as
        // CollectorAnimation.BoardingBounceDuration (0.22s), so the claw
        // fold/unfold reads as part of the same boarding/return beat rather
        // than a separately-timed effect. An elbow-folded Waiting-pose
        // candidate was tried and rejected after real Play Mode viewing -
        // it looked unnaturally rotated outward/upward, not compact - so
        // this now restores the exact shoulder-only delta the project's
        // pre-existing (now-deleted) ApplyCrabClawPoseAdjustment used,
        // recovered verbatim from git history (commit 256a0b2, the only
        // commit that ever touched this file, i.e. the state immediately
        // before this pose-switching feature existed) rather than
        // re-approximated: same 90-degree AngleAxis around local Z,
        // post-multiplied onto the bone's own current rotation, applied
        // IDENTICALLY to both shoulders with no left/right sign flip (the
        // rig's own local axis conventions for the two arms already mirror
        // each other, confirmed empirically by that original
        // implementation - see its own historical remarks, reproduced
        // below). Elbows are left at their authored rotation in both
        // states - this pose only ever touched the shoulder.
        private const float CrabClawBlendSeconds = 0.16f;

        // Recovered verbatim from git history (256a0b2) - the exact delta
        // ApplyCrabClawPoseAdjustment used to apply directly to the live
        // bone before this feature replaced that permanent build-time bake
        // with runtime pose switching. Its own original remarks: "A
        // 90-degree rotation around each shoulder bone's own local Z axis,
        // applied identically to both (confirmed empirically to already
        // produce a correctly left/right-mirrored world-space result - the
        // rig's own local axis conventions for the two arms are themselves
        // mirror images of each other, so no sign-flip is needed between L
        // and R here). Swings the rigid claw chain from 'extending
        // sideways' to 'tucked back toward the body,' which is what
        // actually narrows the silhouette." Applied here the same way that
        // implementation applied it (bone.localRotation * delta, i.e. in
        // the bone's own local space, post-multiplied) - the only
        // difference is WHERE: that version wrote it directly onto the live
        // bone once, permanently, at build time; this version stores it as
        // CharacterPosePresentation's Waiting target, applied and reverted
        // at runtime, on request, arbitrarily many times, always from the
        // same authored Conveyor rotation - never cumulative.
        private static readonly Quaternion CrabShoulderWaitingDelta = Quaternion.AngleAxis(90f, Vector3.forward);

        /// <summary>
        /// Configures Crab's CharacterPosePresentation: finds the four
        /// controlled bones once, captures each one's CURRENT local
        /// rotation (the raw imported/bind rotation - see the Animator
        /// remarks above for why this is guaranteed unmodified at this
        /// point) as that bone's Conveyor pose, pairs it with an explicit
        /// Waiting-pose rotation, and adds the component to body carrying
        /// that configuration. This is the only place any Crab bone
        /// rotation is ever read or decided - CollectorPresentation and
        /// CollectorView never see a bone name, only the
        /// CharacterPresentationPose enum (see CharacterPosePresentation).
        /// A missing bone leaves Crab without a pose adapter entirely
        /// (logged, not thrown) rather than configuring a partial/asymmetric
        /// one - CollectorView.PosePresentation is then simply null and
        /// every SetPose request against it is already a safe no-op.
        /// </summary>
        private static void ApplyCrabPosePresentation(GameObject body, string idLabel)
        {
            Transform leftShoulder = FindDeep(body.transform, CrabLeftShoulderBone);
            Transform rightShoulder = FindDeep(body.transform, CrabRightShoulderBone);
            Transform leftElbow = FindDeep(body.transform, CrabLeftElbowBone);
            Transform rightElbow = FindDeep(body.transform, CrabRightElbowBone);

            if (leftShoulder == null || rightShoulder == null || leftElbow == null || rightElbow == null)
            {
                Debug.LogError($"CharacterAssetBuilder: Match ID {idLabel} - could not find every Crab claw bone ('{CrabLeftShoulderBone}'/'{CrabRightShoulderBone}'/'{CrabLeftElbowBone}'/'{CrabRightElbowBone}'); no pose adapter added, claws left in their wide authored pose everywhere.");
                return;
            }

            var bonePoses = new[]
            {
                BuildCrabBonePose(leftShoulder, CrabShoulderWaitingDelta),
                BuildCrabBonePose(rightShoulder, CrabShoulderWaitingDelta),
                BuildCrabBonePose(leftElbow, Quaternion.identity),
                BuildCrabBonePose(rightElbow, Quaternion.identity),
            };

            var posePresentation = body.AddComponent<CharacterPosePresentation>();
            posePresentation.Configure(bonePoses, CrabClawBlendSeconds);
        }

        /// <summary>
        /// One bone's Conveyor pose is simply its own current rotation,
        /// read fresh (never a cached/assumed identity) so this stays
        /// correct even if the vendor rig's own authored bind pose ever
        /// changes. Its Waiting pose is that same rotation with waitingDelta
        /// applied on top, in the bone's own local space (conveyorRotation *
        /// waitingDelta) - the exact multiplication order/convention the
        /// original ApplyCrabClawPoseAdjustment used on the live bone (see
        /// CrabShoulderWaitingDelta's own remarks), just never written onto
        /// the live bone itself here (see CharacterPosePresentation's own
        /// remarks on why applying a pose is deliberately absolute at apply
        /// time, not cumulative). Callers pass CrabShoulderWaitingDelta for
        /// both shoulders (identical, no left/right sign flip - see
        /// CrabShoulderWaitingDelta's remarks) and Quaternion.identity for
        /// both elbows, so an elbow's Waiting and Conveyor rotations end up
        /// exactly equal - i.e. elbows never move between states at all.
        /// </summary>
        private static CharacterPosePresentation.BonePose BuildCrabBonePose(Transform bone, Quaternion waitingDelta)
        {
            Quaternion conveyorRotation = bone.localRotation;
            return new CharacterPosePresentation.BonePose
            {
                bone = bone,
                conveyorLocalRotation = conveyorRotation,
                waitingLocalRotation = conveyorRotation * waitingDelta,
            };
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;

            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
