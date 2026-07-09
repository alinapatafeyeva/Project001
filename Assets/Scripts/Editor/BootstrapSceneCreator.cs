using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BootstrapSceneCreator
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string SquareShaderName = "Universal Render Pipeline/Unlit";

    [MenuItem("Tools/Bootstrap/Create Bootstrap Scene")]
    public static void CreateBootstrapScene()
    {
        if (File.Exists(ScenePath))
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Bootstrap Scene Exists",
                $"{ScenePath} already exists. Overwrite it?",
                "Overwrite",
                "Cancel");

            if (!overwrite)
                return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateCamera();
        CreateWhiteSquare();

        string directory = Path.GetDirectoryName(ScenePath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
    }

    private static void CreateCamera()
    {
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);
    }

    private static void CreateWhiteSquare()
    {
        GameObject square = GameObject.CreatePrimitive(PrimitiveType.Quad);
        square.name = "White Square";
        square.transform.position = Vector3.zero;
        square.transform.localScale = new Vector3(2f, 2f, 1f);

        Object.DestroyImmediate(square.GetComponent<MeshCollider>());

        Shader shader = Shader.Find(SquareShaderName);
        if (shader == null)
        {
            Debug.LogError($"BootstrapSceneCreator: shader '{SquareShaderName}' not found.");
            return;
        }

        var material = new Material(shader) { name = "BootstrapWhite" };
        material.SetColor("_BaseColor", Color.white);

        square.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
