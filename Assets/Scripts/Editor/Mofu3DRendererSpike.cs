using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Project001.EditorTools
{
    /// <summary>
    /// One-off spike tooling: adds a Universal Renderer Data asset alongside
    /// the project's existing 2D Renderer (without removing or replacing it)
    /// and points the gameplay camera at it, so 3D URP/Lit materials (Mofu)
    /// receive normal Directional Light shading while gameplay stays on the
    /// existing 2D camera/composition. Not part of the shipped presentation
    /// pipeline - run once via the Tools/Mofu3D menu while investigating
    /// renderer compatibility on branch experiment/3d-mofu.
    /// </summary>
    public static class Mofu3DRendererSpike
    {
        private const string PipelineAssetPath = "Assets/Settings/UniversalRP.asset";
        private const string UniversalRendererDataPath = "Assets/Settings/UniversalRenderer.asset";
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string MainCameraName = "Main Camera";

        [MenuItem("Tools/Mofu3D/Renderer Spike/Run All")]
        public static void RunAll()
        {
            CreateUniversalRendererData();
            RegisterRendererOnPipelineAsset();
            AssignRendererToMainCamera();
        }

        [MenuItem("Tools/Mofu3D/Renderer Spike/1 Create Universal Renderer Data")]
        public static void CreateUniversalRendererData()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UniversalRendererDataPath);
            if (existing != null)
            {
                Debug.Log($"Mofu3DRendererSpike: '{UniversalRendererDataPath}' already exists, skipping creation.");
                return;
            }

            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, UniversalRendererDataPath);
            ResourceReloader.ReloadAllNullIn(data, "Packages/com.unity.render-pipelines.universal");
            AssetDatabase.SaveAssets();
            Debug.Log($"Mofu3DRendererSpike: created Universal Renderer Data at '{UniversalRendererDataPath}' (default settings, no renderer features, no post-processing).");
        }

        [MenuItem("Tools/Mofu3D/Renderer Spike/2 Register Renderer On Pipeline Asset")]
        public static void RegisterRendererOnPipelineAsset()
        {
            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipelineAsset == null)
            {
                Debug.LogError($"Mofu3DRendererSpike: no UniversalRenderPipelineAsset found at '{PipelineAssetPath}'.");
                return;
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UniversalRendererDataPath);
            if (rendererData == null)
            {
                Debug.LogError($"Mofu3DRendererSpike: no Universal Renderer Data found at '{UniversalRendererDataPath}' - run step 1 first.");
                return;
            }

            var serializedAsset = new SerializedObject(pipelineAsset);
            SerializedProperty listProp = serializedAsset.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndexProp = serializedAsset.FindProperty("m_DefaultRendererIndex");

            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue == rendererData)
                {
                    Debug.Log($"Mofu3DRendererSpike: '{rendererData.name}' already registered on '{pipelineAsset.name}' at index {i}. Default renderer index stays {defaultIndexProp.intValue} (unchanged).");
                    return;
                }
            }

            int newIndex = listProp.arraySize;
            listProp.arraySize++;
            listProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = rendererData;
            // m_DefaultRendererIndex is intentionally left untouched: the
            // existing 2D Renderer stays the pipeline default for any
            // camera/preview that doesn't explicitly opt into the Universal
            // Renderer.
            serializedAsset.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Debug.Log($"Mofu3DRendererSpike: registered '{rendererData.name}' on '{pipelineAsset.name}' at index {newIndex}. Default renderer index stays {defaultIndexProp.intValue} (Renderer2D, unchanged).");
        }

        [MenuItem("Tools/Mofu3D/Renderer Spike/3 Assign Renderer To Main Camera")]
        public static void AssignRendererToMainCamera()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UniversalRendererDataPath);
            if (pipelineAsset == null || rendererData == null)
            {
                Debug.LogError("Mofu3DRendererSpike: pipeline asset or renderer data missing - run steps 1 and 2 first.");
                return;
            }

            int rendererIndex = IndexOfRenderer(pipelineAsset, rendererData);
            if (rendererIndex < 0)
            {
                Debug.LogError($"Mofu3DRendererSpike: '{rendererData.name}' is not registered on '{pipelineAsset.name}' - run step 2 first.");
                return;
            }

            GameObject cameraObject = GameObject.Find(MainCameraName);
            if (cameraObject == null)
            {
                Debug.LogError($"Mofu3DRendererSpike: no GameObject named '{MainCameraName}' found in '{ScenePath}'.");
                return;
            }

            var camera = cameraObject.GetComponent<Camera>();
            if (camera == null)
            {
                Debug.LogError($"Mofu3DRendererSpike: '{MainCameraName}' has no Camera component.");
                return;
            }

            var cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
                cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();

            cameraData.SetRenderer(rendererIndex);
            EditorUtility.SetDirty(cameraObject);

            EditorSceneManager.MarkSceneDirty(cameraObject.scene);
            EditorSceneManager.SaveScene(cameraObject.scene);

            Debug.Log($"Mofu3DRendererSpike: '{MainCameraName}' now renders through '{rendererData.name}' (renderer index {rendererIndex}). Orthographic camera settings and 2D composition untouched.");
        }

        private static int IndexOfRenderer(UniversalRenderPipelineAsset pipelineAsset, UniversalRendererData rendererData)
        {
            var serializedAsset = new SerializedObject(pipelineAsset);
            SerializedProperty listProp = serializedAsset.FindProperty("m_RendererDataList");
            for (int i = 0; i < listProp.arraySize; i++)
            {
                if (listProp.GetArrayElementAtIndex(i).objectReferenceValue == rendererData)
                    return i;
            }

            return -1;
        }
    }
}
