using Project001.Gameplay.Presentation;
using UnityEditor;
using UnityEngine;

namespace Project001.EditorTools
{
    /// <summary>
    /// One-off spike tooling: configures the imported Mofu.fbx, generates the
    /// three temporary MonsterColor materials, and wraps the mesh in a reusable
    /// prefab. Not part of the shipped presentation pipeline — run once via the
    /// Tools/Mofu3D menu (or -executeMethod in batch mode) while setting up the
    /// 3D presentation spike on branch experiment/3d-mofu.
    ///
    /// Limitation of the current test model, confirmed by inspecting
    /// Mofu.fbx's own binary structure: it has exactly one Geometry, one
    /// Material, and its LayerElementMaterial is MappingInformationType
    /// "AllSame" (every polygon references material index 0 — no per-region
    /// material assignment, no separate submesh, and no vertex-colour layer
    /// either). Body, face, eyes, mouth, and teeth are therefore all one
    /// inseparable mesh with one material slot — there is no data in this
    /// FBX a mask or a second material could target. Every colour variant
    /// below is necessarily a single flat tint of the whole model, including
    /// the face, until a production export provides separate material slots
    /// (e.g. "Fur", "Eyes", "Mouth", "Teeth") or a per-region vertex/UV mask
    /// the fur tint could be confined to. See CreateMaterials.
    /// </summary>
    public static class Mofu3DSetup
    {
        private const string FbxPath = "Assets/Art/Themes/Classic/Character/Mofu.fbx";
        private const string CharacterFolder = "Assets/Art/Themes/Classic/Character";
        private const string MaterialsFolder = "Assets/Art/Themes/Classic/Character/Materials";
        private const string PrefabPath = "Assets/Art/Themes/Classic/Character/Mofu.prefab";

        // Scale applied to the prefab's root, on top of the FBX's own import
        // scale, so the rendered model's footprint approximates the sprite
        // system's CollectorVisibleWidth/Height envelope at
        // GameplayLayout.CollectorSpriteScale. The imported mesh measures
        // ~1 world unit tall (see LogMeshInfo); GameplayLayout's own sprite
        // visible-height ratio (632/800 = 0.79) applied here reproduces the
        // exact visible height the sprite system rendered at, since both are
        // multiplied by the same CollectorSpriteScale at runtime.
        public const float PrefabRootScale = 632f / 800f;

        // Colours read from ColorPalette (the single source of truth for
        // approved 3D Mofu fur colours) rather than hardcoded here — see
        // ColorPalette's own remarks for why these differ from
        // ColorPalette.md's brighter, flat-sprite-tuned Orange/Green/Purple.
        private static readonly (string name, Color color)[] Colors =
        {
            ("Purple", ColorPalette.MofuPurple),
            ("Green", ColorPalette.MofuGreen),
            ("Orange", ColorPalette.MofuOrange),
        };

        [MenuItem("Tools/Mofu3D/Run Full Setup")]
        public static void RunAll()
        {
            ConfigureImport();
            CreateMaterials();
            CreatePrefab();
            LogMeshInfo();
        }

        [MenuItem("Tools/Mofu3D/1 Configure Import")]
        public static void ConfigureImport()
        {
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"Mofu3DSetup: no ModelImporter found at '{FbxPath}'.");
                return;
            }

            // No skeletal animation exists for this spike - transforms only.
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importConstraints = false;

            // Materials are resolved at runtime from MonsterColor, never
            // imported/hardcoded from the source file.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importBlendShapes = false;
            importer.keepQuads = false;

            importer.SaveAndReimport();
            Debug.Log("Mofu3DSetup: FBX import configured (no rig/animation, no imported materials).");
        }

        [MenuItem("Tools/Mofu3D/2 Create Materials")]
        public static void CreateMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder(CharacterFolder, "Materials");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("Mofu3DSetup: 'Universal Render Pipeline/Lit' shader not found - is URP installed?");
                return;
            }

            foreach ((string name, Color color) in Colors)
            {
                string path = $"{MaterialsFolder}/Mofu_{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = material == null;
                if (isNew)
                    material = new Material(shader);

                material.shader = shader;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", 0.25f);
                material.SetFloat("_Metallic", 0f);

                if (isNew)
                    AssetDatabase.CreateAsset(material, path);
                else
                    EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Mofu3DSetup: materials created/updated (Purple, Green, Orange).");
        }

        [MenuItem("Tools/Mofu3D/3 Create Prefab")]
        public static void CreatePrefab()
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"Mofu3DSetup: could not load FBX asset at '{FbxPath}'.");
                return;
            }

            var defaultMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mofu_Purple.mat");
            if (defaultMaterial == null)
                Debug.LogWarning("Mofu3DSetup: default (Purple) material not found - run 'Create Materials' first.");

            var root = new GameObject("Mofu");
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            modelInstance.transform.SetParent(root.transform, false);
            root.transform.localScale = Vector3.one * PrefabRootScale;

            if (defaultMaterial != null)
            {
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = defaultMaterial;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            Debug.Log($"Mofu3DSetup: prefab created at '{PrefabPath}' with root scale {PrefabRootScale}.");
        }

        [MenuItem("Tools/Mofu3D/4 Log Mesh Info")]
        public static void LogMeshInfo()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Mofu3DSetup: prefab not found at '{PrefabPath}' - run 'Create Prefab' first.");
                return;
            }

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;

                Mesh mesh = filter.sharedMesh;
                Bounds localBounds = mesh.bounds;
                Debug.Log(
                    $"Mofu3DSetup: mesh '{filter.name}' local bounds size={localBounds.size} " +
                    $"center={localBounds.center} verts={mesh.vertexCount} tris={mesh.triangles.Length / 3} " +
                    $"lossyScale={filter.transform.lossyScale}");
            }

            Bounds? combined = null;
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                if (combined == null)
                {
                    combined = renderer.bounds;
                }
                else
                {
                    Bounds b = combined.Value;
                    b.Encapsulate(renderer.bounds);
                    combined = b;
                }
            }

            if (combined != null)
                Debug.Log($"Mofu3DSetup: combined renderer bounds at prefab's authored transform, size={combined.Value.size}, center={combined.Value.center}");
            else
                Debug.LogWarning("Mofu3DSetup: no MeshRenderer found under prefab.");
        }
    }
}
