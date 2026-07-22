using System.Collections.Generic;
using System.IO;
using Project001.Gameplay;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Levels;
using Project001.Gameplay.Pixels;
using Project001.Gameplay.Presentation;
using Project001.Gameplay.Recovery;
using Project001.Gameplay.Victory;
using Project001.UI.Failure;
using Project001.UI.Victory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creates the empty scene skeleton and wires cross-references between its
/// gameplay systems. Does not generate any level content itself — PixelGrid,
/// WaitingLine, CollectorQueueBoard, and ConveyorSystem stay empty/unconfigured
/// until LevelBootstrapper builds them from a LevelDefinition at runtime
/// (Awake), so the saved scene works correctly after entering Play Mode or
/// being loaded fresh, not just immediately after this menu command runs.
/// </summary>
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
        RecoveryRowController recoveryRowController = CreateRecoveryRow();
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine = CreateWaitingLine();
        FailureController failureController = CreateFailureController(pixelGrid);
        MonsterSkinDatabase monsterSkinDatabase = CreateMonsterSkinDatabase();
        CollectorQueueBoard collectorQueueBoard = CreateCollectorQueueBoard(pixelGrid, conveyorSystem, waitingLine, failureController, monsterSkinDatabase);
        CollectorSelectionController collectorSelectionController = CreateCollectorSelectionController(mainCamera, collectorQueueBoard, waitingLine, recoveryRowController, conveyorSystem);
        VictoryController victoryController = CreateVictoryController(pixelGrid);
        GameplayFlowController gameplayFlowController = CreateGameplayFlowController(victoryController, failureController, collectorSelectionController);
        FailureRecoveryController failureRecoveryController = CreateFailureRecoveryController(failureController, gameplayFlowController, conveyorSystem, recoveryRowController);
        EndgameCleanupController endgameCleanupController = CreateEndgameCleanupController(collectorQueueBoard, conveyorSystem, waitingLine, recoveryRowController, failureController);
        LevelProgressionController levelProgressionController = CreateLevelProgressionController();
        VictoryFlowController victoryFlowController = CreateVictoryFlowController(gameplayFlowController, levelProgressionController);
        CreateLevelBootstrapper(pixelGrid, conveyorSystem, waitingLine, collectorQueueBoard, endgameCleanupController, levelProgressionController);
        CreateEventSystem();
        CreateVictoryUI(victoryController, victoryFlowController);
        CreateFailureUI(failureController, failureRecoveryController);

        string directory = Path.GetDirectoryName(ScenePath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        EnsureSceneRegisteredInBuildSettings();
    }

    /// <summary>
    /// Required for SceneManager.LoadScene (used by FailureRecoveryController's
    /// Retry) to work at all — Unity refuses to load any scene not present in
    /// Build Settings, even in Play Mode. Additive only: leaves every other
    /// already-registered scene untouched, and does nothing if Bootstrap is
    /// already present.
    /// </summary>
    private static void EnsureSceneRegisteredInBuildSettings()
    {
        foreach (EditorBuildSettingsScene existingScene in EditorBuildSettings.scenes)
        {
            if (existingScene.path == ScenePath)
                return;
        }

        var updatedScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        EditorBuildSettings.scenes = updatedScenes.ToArray();
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

        var pixelGrid = pixelGridObject.GetComponent<PixelGrid>();
        var serializedPixelGrid = new SerializedObject(pixelGrid);
        serializedPixelGrid.FindProperty("availableWidth").floatValue = 6.5f;
        serializedPixelGrid.FindProperty("availableHeight").floatValue = 6.5f;
        serializedPixelGrid.ApplyModifiedPropertiesWithoutUndo();

        return pixelGrid;
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

    /// <summary>
    /// Sits directly beneath the Conveyor, in the world-space slot the
    /// Waiting Line previously occupied. RecoveryRowController owns whatever
    /// collectors ReceiveCollectors is later given — no capacity to
    /// configure here, since the row sizes itself from however many
    /// collectors it receives. RecoveryRowView lives on the same GameObject
    /// and handles reparenting/layout, wired to the controller so it
    /// refreshes on CollectorsChanged rather than being driven directly.
    /// </summary>
    private static RecoveryRowController CreateRecoveryRow()
    {
        var recoveryRowObject = new GameObject(
            "RecoveryRow",
            typeof(RecoveryRowController),
            typeof(RecoveryRowView));
        recoveryRowObject.transform.position = new Vector3(0f, -5.0f, 0f);

        var recoveryRowController = recoveryRowObject.GetComponent<RecoveryRowController>();

        var recoveryRowView = recoveryRowObject.GetComponent<RecoveryRowView>();
        var serializedView = new SerializedObject(recoveryRowView);
        serializedView.FindProperty("recoveryRowController").objectReferenceValue = recoveryRowController;
        serializedView.ApplyModifiedPropertiesWithoutUndo();

        return recoveryRowController;
    }

    private static Project001.Gameplay.WaitingLine.WaitingLine CreateWaitingLine()
    {
        var waitingLineObject = new GameObject(
            "WaitingLine",
            typeof(Project001.Gameplay.WaitingLine.WaitingLine));
        // Shifted below the Recovery Row's Y (-5.0) by the same 1.6-unit gap
        // that already separated WaitingLine from CollectorQueueBoard, so
        // the two rows never overlap.
        waitingLineObject.transform.position = new Vector3(0f, -6.6f, 0f);

        return waitingLineObject.GetComponent<Project001.Gameplay.WaitingLine.WaitingLine>();
    }

    private static CollectorQueueBoard CreateCollectorQueueBoard(
        PixelGrid pixelGrid,
        ConveyorSystem conveyorSystem,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        FailureController failureController,
        MonsterSkinDatabase monsterSkinDatabase)
    {
        var boardObject = new GameObject("CollectorQueueBoard", typeof(CollectorQueueBoard));
        boardObject.transform.position = new Vector3(0f, -8.2f, 0f);

        var collectorQueueBoard = boardObject.GetComponent<CollectorQueueBoard>();
        var serializedBoard = new SerializedObject(collectorQueueBoard);
        serializedBoard.FindProperty("monsterSkinDatabase").objectReferenceValue = monsterSkinDatabase;
        serializedBoard.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedBoard.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedBoard.FindProperty("waitingLine").objectReferenceValue = waitingLine;
        serializedBoard.FindProperty("failureController").objectReferenceValue = failureController;
        serializedBoard.ApplyModifiedPropertiesWithoutUndo();

        return collectorQueueBoard;
    }

    private const string MofuSetsRoot = "Assets/Art/Sprites/Characters/Mofu/Sets";

    /// <summary>
    /// Builds the MonsterSkinDatabase scene object and populates one entry
    /// per MonsterColor (Purple, Green, Orange) by loading each color's five
    /// sprites from its Sets/{Color} folder via AssetDatabase — editor-only,
    /// one-time wiring, exactly like every other cross-reference this class
    /// sets up. Runtime code never loads sprites this way: MonsterSkinDatabase
    /// only ever reads the serialized entries this produces.
    /// </summary>
    private static MonsterSkinDatabase CreateMonsterSkinDatabase()
    {
        var databaseObject = new GameObject("MonsterSkinDatabase", typeof(MonsterSkinDatabase));
        var database = databaseObject.GetComponent<MonsterSkinDatabase>();

        var monsterColors = new[] { MonsterColor.Purple, MonsterColor.Green, MonsterColor.Orange };

        var serializedDatabase = new SerializedObject(database);
        SerializedProperty skinsProperty = serializedDatabase.FindProperty("skins");
        skinsProperty.arraySize = monsterColors.Length;

        for (int i = 0; i < monsterColors.Length; i++)
        {
            MonsterColor monsterColor = monsterColors[i];
            SerializedProperty entryProperty = skinsProperty.GetArrayElementAtIndex(i);
            entryProperty.FindPropertyRelative("color").enumValueIndex = (int)monsterColor;

            SerializedProperty skinProperty = entryProperty.FindPropertyRelative("skin");
            string folder = $"{MofuSetsRoot}/{monsterColor}";
            AssignSkinSprite(skinProperty, "backIdle", $"{folder}/Mofu_Back_Idle.png");
            AssignSkinSprite(skinProperty, "frontIdle", $"{folder}/Mofu_Front_Idle.png");
            AssignSkinSprite(skinProperty, "frontEating", $"{folder}/Mofu_Front_Eating.png");
            AssignSkinSprite(skinProperty, "frontSatisfied", $"{folder}/Mofu_Front_Satisfied.png");
            AssignSkinSprite(skinProperty, "heart", $"{folder}/Mofu_Heart.png");
        }

        serializedDatabase.ApplyModifiedPropertiesWithoutUndo();

        return database;
    }

    private static void AssignSkinSprite(SerializedProperty skinProperty, string fieldName, string assetPath)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load sprite at '{assetPath}'; MonsterSkinDatabase will have no {fieldName} sprite there.");

        skinProperty.FindPropertyRelative(fieldName).objectReferenceValue = sprite;
    }

    private static CollectorSelectionController CreateCollectorSelectionController(
        Camera selectionCamera,
        CollectorQueueBoard collectorQueueBoard,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        RecoveryRowController recoveryRowController,
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
        serializedController.FindProperty("recoveryRowController").objectReferenceValue = recoveryRowController;
        serializedController.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        return controller;
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

    private static VictoryController CreateVictoryController(PixelGrid pixelGrid)
    {
        var victoryControllerObject = new GameObject("VictoryController", typeof(VictoryController));

        var victoryController = victoryControllerObject.GetComponent<VictoryController>();
        var serializedVictoryController = new SerializedObject(victoryController);
        serializedVictoryController.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedVictoryController.ApplyModifiedPropertiesWithoutUndo();

        return victoryController;
    }

    /// <summary>
    /// Explicitly controls whether gameplay interaction is active, reacting
    /// to both victoryController.OnVictory and failureController.OnFailure by
    /// pausing — the same flow for either terminal outcome, one controller.
    /// Presentation (VictoryUI, FailureUI) only ever calls its
    /// ResumeGameplay/PauseGameplay API — it never manipulates
    /// Time.timeScale or collectorSelectionController itself.
    /// </summary>
    private static GameplayFlowController CreateGameplayFlowController(
        VictoryController victoryController,
        FailureController failureController,
        CollectorSelectionController collectorSelectionController)
    {
        var gameplayFlowControllerObject = new GameObject("GameplayFlowController", typeof(GameplayFlowController));

        var gameplayFlowController = gameplayFlowControllerObject.GetComponent<GameplayFlowController>();
        var serializedGameplayFlowController = new SerializedObject(gameplayFlowController);
        serializedGameplayFlowController.FindProperty("victoryController").objectReferenceValue = victoryController;
        serializedGameplayFlowController.FindProperty("failureController").objectReferenceValue = failureController;
        serializedGameplayFlowController.FindProperty("collectorSelectionController").objectReferenceValue = collectorSelectionController;
        serializedGameplayFlowController.ApplyModifiedPropertiesWithoutUndo();

        return gameplayFlowController;
    }

    /// <summary>
    /// Owns what Retry/Continue actually do after a Failure (full level
    /// restart vs. resuming the same level state, including transferring
    /// every Conveyor rider into the Recovery Row). Presentation (FailureUI)
    /// only ever calls its RetryCurrentLevel/ContinueCurrentLevel API — it
    /// never resets failureController, reloads scenes, or touches
    /// gameplayFlowController, conveyorSystem, or recoveryRowController
    /// itself.
    /// </summary>
    private static FailureRecoveryController CreateFailureRecoveryController(
        FailureController failureController,
        GameplayFlowController gameplayFlowController,
        ConveyorSystem conveyorSystem,
        RecoveryRowController recoveryRowController)
    {
        var failureRecoveryControllerObject = new GameObject("FailureRecoveryController", typeof(FailureRecoveryController));

        var failureRecoveryController = failureRecoveryControllerObject.GetComponent<FailureRecoveryController>();
        var serializedFailureRecoveryController = new SerializedObject(failureRecoveryController);
        serializedFailureRecoveryController.FindProperty("failureController").objectReferenceValue = failureController;
        serializedFailureRecoveryController.FindProperty("gameplayFlowController").objectReferenceValue = gameplayFlowController;
        serializedFailureRecoveryController.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedFailureRecoveryController.FindProperty("recoveryRowController").objectReferenceValue = recoveryRowController;
        serializedFailureRecoveryController.ApplyModifiedPropertiesWithoutUndo();

        return failureRecoveryController;
    }

    /// <summary>
    /// Owns entering the Endgame Cleanup phase once RemainingCollectors drops
    /// to WaitingLineCapacity or below: directs FailureController to
    /// permanently disable, WaitingLine to stop accepting new collectors, and
    /// ConveyorSystem to speed up — each system still owns its own affected
    /// behaviour, this controller only signals the transition once.
    /// </summary>
    private static EndgameCleanupController CreateEndgameCleanupController(
        CollectorQueueBoard collectorQueueBoard,
        ConveyorSystem conveyorSystem,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        RecoveryRowController recoveryRowController,
        FailureController failureController)
    {
        var endgameCleanupControllerObject = new GameObject("EndgameCleanupController", typeof(EndgameCleanupController));

        var endgameCleanupController = endgameCleanupControllerObject.GetComponent<EndgameCleanupController>();
        var serializedEndgameCleanupController = new SerializedObject(endgameCleanupController);
        serializedEndgameCleanupController.FindProperty("collectorQueueBoard").objectReferenceValue = collectorQueueBoard;
        serializedEndgameCleanupController.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedEndgameCleanupController.FindProperty("waitingLine").objectReferenceValue = waitingLine;
        serializedEndgameCleanupController.FindProperty("recoveryRowController").objectReferenceValue = recoveryRowController;
        serializedEndgameCleanupController.FindProperty("failureController").objectReferenceValue = failureController;
        serializedEndgameCleanupController.ApplyModifiedPropertiesWithoutUndo();

        return endgameCleanupController;
    }

    /// <summary>
    /// Owns which level is current for this session and how to resolve the
    /// next one. No dependencies of its own to wire — LevelBootstrapper reads
    /// from it, and VictoryFlowController advances through it, but this
    /// controller depends on neither.
    /// </summary>
    private static LevelProgressionController CreateLevelProgressionController()
    {
        var levelProgressionControllerObject = new GameObject("LevelProgressionController", typeof(LevelProgressionController));
        return levelProgressionControllerObject.GetComponent<LevelProgressionController>();
    }

    /// <summary>
    /// Owns what Continue actually does after a Victory: resume gameplay via
    /// gameplayFlowController, then load the next level via
    /// levelProgressionController. Presentation (VictoryUI) only ever calls
    /// its LoadNextLevel API.
    /// </summary>
    private static VictoryFlowController CreateVictoryFlowController(
        GameplayFlowController gameplayFlowController,
        LevelProgressionController levelProgressionController)
    {
        var victoryFlowControllerObject = new GameObject("VictoryFlowController", typeof(VictoryFlowController));

        var victoryFlowController = victoryFlowControllerObject.GetComponent<VictoryFlowController>();
        var serializedVictoryFlowController = new SerializedObject(victoryFlowController);
        serializedVictoryFlowController.FindProperty("gameplayFlowController").objectReferenceValue = gameplayFlowController;
        serializedVictoryFlowController.FindProperty("levelProgressionController").objectReferenceValue = levelProgressionController;
        serializedVictoryFlowController.ApplyModifiedPropertiesWithoutUndo();

        return victoryFlowController;
    }

    /// <summary>
    /// Required for any Unity UI Button to receive clicks at all. The
    /// project's Active Input Handling is set to the new Input System only,
    /// so the legacy StandaloneInputModule would throw at runtime —
    /// InputSystemUIInputModule is the correct module for this project.
    /// </summary>
    private static void CreateEventSystem()
    {
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    /// <summary>
    /// Prototype Victory screen: a Canvas holding an initially-inactive
    /// centered panel ("Level Complete" text and a Continue button), with
    /// the VictoryUI component wired to victoryController. No score, coins,
    /// stars, rewards, ads, transitions, or animations — establishing the
    /// architecture only.
    /// </summary>
    private static void CreateVictoryUI(VictoryController victoryController, VictoryFlowController victoryFlowController)
    {
        var canvasObject = new GameObject(
            "VictoryCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(VictoryUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        GameObject panel = CreateVictoryPanel(canvasObject.transform);
        Button continueButton = CreateContinueButton(panel.transform);
        CreateCenteredText(panel.transform, "Level Complete", new Vector2(0f, 60f), new Vector2(360f, 60f), 28, Color.white);

        var victoryUI = canvasObject.GetComponent<VictoryUI>();
        var serializedVictoryUI = new SerializedObject(victoryUI);
        serializedVictoryUI.FindProperty("victoryController").objectReferenceValue = victoryController;
        serializedVictoryUI.FindProperty("victoryFlowController").objectReferenceValue = victoryFlowController;
        serializedVictoryUI.FindProperty("panel").objectReferenceValue = panel;
        serializedVictoryUI.FindProperty("continueButton").objectReferenceValue = continueButton;
        serializedVictoryUI.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateVictoryPanel(Transform parent)
    {
        var panelObject = new GameObject("VictoryPanel", typeof(Image));
        panelObject.transform.SetParent(parent, false);

        var rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(400f, 250f);
        rectTransform.anchoredPosition = Vector2.zero;

        var image = panelObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.85f);

        // Also authored inactive in the saved scene, not just hidden by
        // VictoryUI.Awake() at runtime — keeps the Editor scene view honest
        // about the panel's default (hidden) state.
        panelObject.SetActive(false);

        return panelObject;
    }

    private static Button CreateContinueButton(Transform parent)
    {
        var buttonObject = new GameObject("ContinueButton", typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(160f, 50f);
        rectTransform.anchoredPosition = new Vector2(0f, -60f);

        var image = buttonObject.GetComponent<Image>();
        image.color = Color.white;

        CreateCenteredText(buttonObject.transform, "Continue", Vector2.zero, new Vector2(160f, 50f), 20, Color.black);

        return buttonObject.GetComponent<Button>();
    }

    /// <summary>
    /// Prototype Failure screen: a Canvas holding an initially-inactive
    /// centered panel ("Level Failed" text, a Retry button, and a Continue
    /// button), with the FailureUI component wired to failureController and
    /// failureRecoveryController. Reuses the existing EventSystem created for
    /// Victory — one EventSystem serves every Canvas in the scene, so no
    /// second one is created here. No real restart animation, coins, ads,
    /// stars, or visual polish — establishing the architecture only.
    /// </summary>
    private static void CreateFailureUI(FailureController failureController, FailureRecoveryController failureRecoveryController)
    {
        var canvasObject = new GameObject(
            "FailureCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(FailureUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        GameObject panel = CreateFailurePanel(canvasObject.transform);
        Button retryButton = CreateFailureActionButton(panel.transform, "Retry", new Vector2(-90f, -60f));
        Button continueButton = CreateFailureActionButton(panel.transform, "Continue", new Vector2(90f, -60f));
        CreateCenteredText(panel.transform, "Level Failed", new Vector2(0f, 60f), new Vector2(360f, 60f), 28, Color.white);

        var failureUI = canvasObject.GetComponent<FailureUI>();
        var serializedFailureUI = new SerializedObject(failureUI);
        serializedFailureUI.FindProperty("failureController").objectReferenceValue = failureController;
        serializedFailureUI.FindProperty("failureRecoveryController").objectReferenceValue = failureRecoveryController;
        serializedFailureUI.FindProperty("panel").objectReferenceValue = panel;
        serializedFailureUI.FindProperty("retryButton").objectReferenceValue = retryButton;
        serializedFailureUI.FindProperty("continueButton").objectReferenceValue = continueButton;
        serializedFailureUI.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateFailurePanel(Transform parent)
    {
        var panelObject = new GameObject("FailurePanel", typeof(Image));
        panelObject.transform.SetParent(parent, false);

        var rectTransform = panelObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(400f, 250f);
        rectTransform.anchoredPosition = Vector2.zero;

        var image = panelObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.85f);

        // Also authored inactive in the saved scene, not just hidden by
        // FailureUI.Awake() at runtime — keeps the Editor scene view honest
        // about the panel's default (hidden) state.
        panelObject.SetActive(false);

        return panelObject;
    }

    private static Button CreateFailureActionButton(Transform parent, string label, Vector2 anchoredPosition)
    {
        var buttonObject = new GameObject($"{label}Button", typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(140f, 50f);
        rectTransform.anchoredPosition = anchoredPosition;

        var image = buttonObject.GetComponent<Image>();
        image.color = Color.white;

        CreateCenteredText(buttonObject.transform, label, Vector2.zero, new Vector2(140f, 50f), 20, Color.black);

        return buttonObject.GetComponent<Button>();
    }

    private static void CreateCenteredText(Transform parent, string content, Vector2 anchoredPosition, Vector2 size, int fontSize, Color color)
    {
        var textObject = new GameObject("Text", typeof(Text));
        textObject.transform.SetParent(parent, false);

        var rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
    }

    private static void CreateLevelBootstrapper(
        PixelGrid pixelGrid,
        ConveyorSystem conveyorSystem,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        CollectorQueueBoard collectorQueueBoard,
        EndgameCleanupController endgameCleanupController,
        LevelProgressionController levelProgressionController)
    {
        var bootstrapperObject = new GameObject("LevelBootstrapper", typeof(LevelBootstrapper));

        var bootstrapper = bootstrapperObject.GetComponent<LevelBootstrapper>();
        var serializedBootstrapper = new SerializedObject(bootstrapper);
        serializedBootstrapper.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedBootstrapper.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedBootstrapper.FindProperty("waitingLine").objectReferenceValue = waitingLine;
        serializedBootstrapper.FindProperty("collectorQueueBoard").objectReferenceValue = collectorQueueBoard;
        serializedBootstrapper.FindProperty("endgameCleanupController").objectReferenceValue = endgameCleanupController;
        serializedBootstrapper.FindProperty("levelProgressionController").objectReferenceValue = levelProgressionController;
        serializedBootstrapper.ApplyModifiedPropertiesWithoutUndo();
    }
}
