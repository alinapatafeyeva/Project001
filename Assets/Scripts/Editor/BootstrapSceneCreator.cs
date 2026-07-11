using System.IO;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Pixels;
using Project001.Gameplay.Victory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BootstrapSceneCreator
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";

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

        Camera mainCamera = CreateCamera();
        PixelGrid pixelGrid = CreatePixelGrid();
        ConveyorSystem conveyorSystem = CreateConveyor();
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine = CreateWaitingLine();
        FailureController failureController = CreateFailureController(pixelGrid);
        CollectorQueueBoard collectorQueueBoard = CreateCollectorQueueBoard(pixelGrid, conveyorSystem, waitingLine, failureController);
        CreateCollectorSelectionController(mainCamera, collectorQueueBoard, waitingLine, conveyorSystem);
        CreateVictoryController(pixelGrid);

        string directory = Path.GetDirectoryName(ScenePath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
    }

    private static Camera CreateCamera()
    {
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 10f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);

        return camera;
    }

    private static PixelGrid CreatePixelGrid()
    {
        var pixelGridObject = new GameObject("PixelGrid", typeof(PixelGrid));
        pixelGridObject.transform.position = Vector3.zero;

        return pixelGridObject.GetComponent<PixelGrid>();
    }

    private static ConveyorSystem CreateConveyor()
    {
        var conveyorObject = new GameObject(
            "Conveyor",
            typeof(ConveyorPath),
            typeof(ConveyorPathRenderer),
            typeof(ConveyorSystem));
        conveyorObject.transform.position = Vector3.zero;

        var conveyorPath = conveyorObject.GetComponent<ConveyorPath>();
        var serializedPath = new SerializedObject(conveyorPath);
        serializedPath.FindProperty("width").floatValue = 8f;
        serializedPath.FindProperty("height").floatValue = 8f;
        serializedPath.FindProperty("cornerRadius").floatValue = 1f;
        serializedPath.ApplyModifiedPropertiesWithoutUndo();

        var conveyorSystem = conveyorObject.GetComponent<ConveyorSystem>();
        var serializedSystem = new SerializedObject(conveyorSystem);
        serializedSystem.FindProperty("conveyorPath").objectReferenceValue = conveyorPath;
        serializedSystem.FindProperty("boardingProgress").floatValue = 0.55f;
        serializedSystem.FindProperty("boardingClearance").floatValue = 1f;
        serializedSystem.ApplyModifiedPropertiesWithoutUndo();

        return conveyorSystem;
    }

    private static Project001.Gameplay.WaitingLine.WaitingLine CreateWaitingLine()
    {
        var waitingLineObject = new GameObject(
            "WaitingLine",
            typeof(Project001.Gameplay.WaitingLine.WaitingLine));
        waitingLineObject.transform.position = new Vector3(0f, -5.0f, 0f);

        return waitingLineObject.GetComponent<Project001.Gameplay.WaitingLine.WaitingLine>();
    }

    private static CollectorQueueBoard CreateCollectorQueueBoard(
        PixelGrid pixelGrid,
        ConveyorSystem conveyorSystem,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        FailureController failureController)
    {
        var boardObject = new GameObject("CollectorQueueBoard", typeof(CollectorQueueBoard));
        boardObject.transform.position = new Vector3(0f, -6.6f, 0f);

        var collectorQueueBoard = boardObject.GetComponent<CollectorQueueBoard>();
        var serializedBoard = new SerializedObject(collectorQueueBoard);
        serializedBoard.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedBoard.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedBoard.FindProperty("waitingLine").objectReferenceValue = waitingLine;
        serializedBoard.FindProperty("failureController").objectReferenceValue = failureController;
        serializedBoard.ApplyModifiedPropertiesWithoutUndo();

        return collectorQueueBoard;
    }

    private static void CreateCollectorSelectionController(
        Camera selectionCamera,
        CollectorQueueBoard collectorQueueBoard,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        ConveyorSystem conveyorSystem)
    {
        var controllerObject = new GameObject(
            "CollectorSelectionController",
            typeof(CollectorSelectionController));

        var controller = controllerObject.GetComponent<CollectorSelectionController>();
        var serializedController = new SerializedObject(controller);
        serializedController.FindProperty("selectionCamera").objectReferenceValue = selectionCamera;
        serializedController.FindProperty("collectorQueueBoard").objectReferenceValue = collectorQueueBoard;
        serializedController.FindProperty("waitingLine").objectReferenceValue = waitingLine;
        serializedController.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
    }

    private static FailureController CreateFailureController(PixelGrid pixelGrid)
    {
        var failureControllerObject = new GameObject("FailureController", typeof(FailureController));

        var failureController = failureControllerObject.GetComponent<FailureController>();
        var serializedFailureController = new SerializedObject(failureController);
        serializedFailureController.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedFailureController.ApplyModifiedPropertiesWithoutUndo();

        return failureController;
    }

    private static void CreateVictoryController(PixelGrid pixelGrid)
    {
        var victoryControllerObject = new GameObject("VictoryController", typeof(VictoryController));

        var victoryController = victoryControllerObject.GetComponent<VictoryController>();
        var serializedVictoryController = new SerializedObject(victoryController);
        serializedVictoryController.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedVictoryController.ApplyModifiedPropertiesWithoutUndo();
    }
}
