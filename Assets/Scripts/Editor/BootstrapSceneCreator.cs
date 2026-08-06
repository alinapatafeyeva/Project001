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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        CreateKeyLight();
        ConfigureEnvironmentLighting();
        PixelGrid pixelGrid = CreatePixelGrid();
        ConveyorSystem conveyorSystem = CreateConveyor();
        RecoveryRowController recoveryRowController = CreateRecoveryRow();
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine = CreateWaitingLine();
        FailureController failureController = CreateFailureController(pixelGrid);
        CharacterDatabase characterDatabase = CreateCharacterDatabase();
        CollectorQueueBoard collectorQueueBoard = CreateCollectorQueueBoard(pixelGrid, conveyorSystem, waitingLine, failureController, characterDatabase);
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
        var cameraObject = new GameObject(
            "Main Camera",
            typeof(Camera),
            typeof(AudioListener),
            typeof(PortraitCameraFitter));
        cameraObject.tag = "MainCamera";

        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);

        // Saved at a reference 9:16 aspect using the exact same
        // GameplayLayout computation PortraitCameraFitter.Awake() runs at
        // runtime — composition is fixed, so there is no distinction
        // between an edit-time preview and the real runtime framing; this
        // is simply that framing evaluated once, up front, at one reference
        // aspect, so the saved scene isn't left at Unity's arbitrary default.
        camera.orthographicSize = GameplayLayout.ComputeOrthographicSize(9f, 16f);
        cameraObject.transform.position = GameplayLayout.CameraPosition;
        cameraObject.transform.rotation = GameplayLayout.CameraRotation;

        AssignUniversalRenderer(cameraObject);

        return camera;
    }

    private const string UniversalPipelineAssetPath = "Assets/Settings/UniversalRP.asset";
    private const string UniversalRendererDataPath = "Assets/Settings/UniversalRenderer.asset";

    /// <summary>
    /// Overrides just this camera to render through the Universal Renderer
    /// registered on UniversalRP.asset (added by the renderer-compatibility
    /// spike alongside the project's existing, still-default 2D Renderer —
    /// see UniversalRP.asset's m_RendererDataList and
    /// Mofu3DRendererSpike). Without this override the camera falls back to
    /// the pipeline's default (the 2D Renderer), which does not shade URP
    /// Lit materials against a Directional Light at all — every 3D Mofu
    /// would render flat/unlit regardless of the key light's own settings.
    /// Every other 2D visual (SpriteRenderers, UI, PixelGrid) renders
    /// identically under either renderer, so this only ever affects Mofu.
    /// Looks the Universal Renderer's index up on the pipeline asset rather
    /// than hardcoding it, so this keeps working if the renderer list is
    /// ever reordered.
    /// </summary>
    private static void AssignUniversalRenderer(GameObject cameraObject)
    {
        var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UniversalPipelineAssetPath);
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UniversalRendererDataPath);
        if (pipelineAsset == null || rendererData == null)
        {
            Debug.LogError($"BootstrapSceneCreator: could not load '{UniversalPipelineAssetPath}' and/or '{UniversalRendererDataPath}'; Main Camera will fall back to the pipeline's default renderer and Mofu will render unlit.");
            return;
        }

        var serializedPipelineAsset = new SerializedObject(pipelineAsset);
        SerializedProperty rendererDataList = serializedPipelineAsset.FindProperty("m_RendererDataList");

        int rendererIndex = -1;
        for (int i = 0; i < rendererDataList.arraySize; i++)
        {
            if (rendererDataList.GetArrayElementAtIndex(i).objectReferenceValue == rendererData)
            {
                rendererIndex = i;
                break;
            }
        }

        if (rendererIndex < 0)
        {
            Debug.LogError($"BootstrapSceneCreator: '{rendererData.name}' is not registered in '{pipelineAsset.name}''s renderer list; Main Camera will fall back to the pipeline's default renderer and Mofu will render unlit.");
            return;
        }

        var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.SetRenderer(rendererIndex);
    }

    /// <summary>
    /// The only light in the scene: one Directional Light, still simple,
    /// white, and neutral (default intensity/shadow settings) but no longer
    /// Unity's own standard default rotation for a new light (50, -30, 0) —
    /// that angle's travel direction sits close enough to the camera's own
    /// forward axis (both facing mostly +Z) that a camera-facing Mofu's
    /// visible hemisphere was lit almost head-on, which read as flat/
    /// marker-filled instead of showing the model's actual sculpted
    /// surface relief. (40, -55, 0) keeps the same general "high and to one
    /// side" key-light feel but swings yaw further around (-55 vs -30) and
    /// eases pitch back slightly (40 vs 50), producing a more oblique,
    /// raking angle across the model — still a simple single-light setup,
    /// not a production lighting rig, just aimed so the geometry is
    /// actually readable. Added for the 3D Mofu presentation spike so the
    /// model's volume, silhouette, and materials are actually visible (the
    /// URP Lit materials render flat/unlit without any light in scene).
    /// </summary>
    private static void CreateKeyLight()
    {
        var lightObject = new GameObject("Directional Light", typeof(Light));
        lightObject.transform.rotation = Quaternion.Euler(40f, -55f, 0f);

        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1f;
    }

    /// <summary>
    /// Sets a flat, neutral-white ambient fill (RenderSettings), replacing
    /// whatever dim, cool-tinted values a freshly created scene otherwise
    /// defaults to (a new URP scene's own defaults, never previously set by
    /// this class). Alongside CreateKeyLight's single Directional Light,
    /// this is the entire lighting rig Mofu renders under: with no fill
    /// light and only that dim/cool default ambient, the side of a rounded
    /// Mofu facing away from the key light read as dark and grayish-blue
    /// rather than a shaded version of its own fur colour. A stronger,
    /// colour-neutral ambient keeps that far side readable and saturated
    /// (a "bright stylized character" look) without washing out the key
    /// light's own shading, which is what still gives the fur its relief.
    /// Deliberately Flat (a single ambient colour), not Skybox/Trilight -
    /// there is no skybox in this scene for either of those modes to derive
    /// gradient/reflection values from.
    /// </summary>
    private static void ConfigureEnvironmentLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f, 1f);
        RenderSettings.ambientIntensity = 1f;
    }

    private static PixelGrid CreatePixelGrid()
    {
        var pixelGridObject = new GameObject("PixelGrid", typeof(PixelGrid));
        pixelGridObject.transform.position = new Vector3(0f, GameplayLayout.PixelGridPositionY, 0f);

        var pixelGrid = pixelGridObject.GetComponent<PixelGrid>();
        var serializedPixelGrid = new SerializedObject(pixelGrid);
        serializedPixelGrid.FindProperty("availableWidth").floatValue = GameplayLayout.GridRegionWidth;
        serializedPixelGrid.FindProperty("availableHeight").floatValue = GameplayLayout.GridRegionHeight;
        serializedPixelGrid.FindProperty("maximumCellSize").floatValue = GameplayLayout.GridMaximumCellSize;
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

        // Square, centered on the world origin, sized from GameplayLayout's
        // own authored Conveyor extent — never a hand-copied literal — so
        // the Conveyor BootstrapSceneCreator actually builds and the frame
        // GameplayLayout computes the camera around can never drift apart.
        float conveyorSize = GameplayLayout.ConveyorSize;

        var conveyorPath = conveyorObject.GetComponent<ConveyorPath>();
        var serializedPath = new SerializedObject(conveyorPath);
        serializedPath.FindProperty("width").floatValue = conveyorSize;
        serializedPath.FindProperty("height").floatValue = conveyorSize;
        serializedPath.FindProperty("cornerRadius").floatValue = 1f;
        serializedPath.ApplyModifiedPropertiesWithoutUndo();

        var conveyorSystem = conveyorObject.GetComponent<ConveyorSystem>();
        var serializedSystem = new SerializedObject(conveyorSystem);
        serializedSystem.FindProperty("conveyorPath").objectReferenceValue = conveyorPath;
        serializedSystem.FindProperty("boardingProgress").floatValue = 0.55f;
        // GameplayLayout.ConveyorRiderMinimumSpacing, not a bare literal —
        // this must safely exceed the rendered Mofu footprint (see its own
        // derivation comment) or riders that board close together end up
        // permanently overlapping, since every rider moves at the same
        // fixed speed and never closes or opens that gap afterward.
        serializedSystem.FindProperty("boardingClearance").floatValue = GameplayLayout.ConveyorRiderMinimumSpacing;
        serializedSystem.ApplyModifiedPropertiesWithoutUndo();

        return conveyorSystem;
    }

    /// <summary>
    /// Shares WaitingLine's own row instead of reserving one of its own:
    /// positioned at GameplayLayout.WaitingLinePositionY, the same world Y
    /// WaitingLine sits at, since RecoveryRow only ever holds collectors
    /// during failure recovery and a row permanently reserved for it would
    /// sit empty the rest of the time. RecoveryRowController owns whatever
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
        recoveryRowObject.transform.position = new Vector3(0f, GameplayLayout.WaitingLinePositionY, 0f);

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
        waitingLineObject.transform.position = new Vector3(0f, GameplayLayout.WaitingLinePositionY, 0f);

        return waitingLineObject.GetComponent<Project001.Gameplay.WaitingLine.WaitingLine>();
    }

    private static CollectorQueueBoard CreateCollectorQueueBoard(
        PixelGrid pixelGrid,
        ConveyorSystem conveyorSystem,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        FailureController failureController,
        CharacterDatabase characterDatabase)
    {
        var boardObject = new GameObject("CollectorQueueBoard", typeof(CollectorQueueBoard));
        boardObject.transform.position = new Vector3(0f, GameplayLayout.CollectorQueueBoardPositionY, 0f);

        var collectorQueueBoard = boardObject.GetComponent<CollectorQueueBoard>();
        var serializedBoard = new SerializedObject(collectorQueueBoard);
        serializedBoard.FindProperty("characterDatabase").objectReferenceValue = characterDatabase;
        serializedBoard.FindProperty("pixelGrid").objectReferenceValue = pixelGrid;
        serializedBoard.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedBoard.FindProperty("waitingLine").objectReferenceValue = waitingLine;
        serializedBoard.FindProperty("failureController").objectReferenceValue = failureController;
        serializedBoard.ApplyModifiedPropertiesWithoutUndo();

        return collectorQueueBoard;
    }

    private const string CharacterRoot = "Assets/Art/Themes/Classic/Character";

    /// <summary>
    /// Builds the CharacterDatabase scene object and populates one entry per
    /// Match ID (1-20, see Assets/Art/ColorPalette.md) by loading that id's
    /// Character_XX.prefab (built by CharacterAssetBuilder) via
    /// AssetDatabase — editor-only, one-time wiring, exactly like every
    /// other cross-reference this class sets up. Runtime code never loads
    /// prefabs this way: CharacterDatabase only ever reads the serialized
    /// entries this produces. Logs an error, rather than throwing, for any
    /// Match ID whose prefab has not been built yet, so a partial checkout
    /// still produces a scene instead of failing scene creation outright.
    /// </summary>
    private static CharacterDatabase CreateCharacterDatabase()
    {
        var databaseObject = new GameObject("CharacterDatabase", typeof(CharacterDatabase));
        var database = databaseObject.GetComponent<CharacterDatabase>();

        var serializedDatabase = new SerializedObject(database);
        SerializedProperty charactersProperty = serializedDatabase.FindProperty("characters");
        charactersProperty.arraySize = 20;

        for (int matchId = 1; matchId <= 20; matchId++)
        {
            string idLabel = matchId.ToString("D2");
            string prefabPath = $"{CharacterRoot}/Character_{idLabel}/Character_{idLabel}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                Debug.LogError($"BootstrapSceneCreator: could not load Character prefab at '{prefabPath}'; CharacterDatabase will have no prefab for Match ID {idLabel}.");

            SerializedProperty entryProperty = charactersProperty.GetArrayElementAtIndex(matchId - 1);
            entryProperty.FindPropertyRelative("matchId").intValue = matchId;
            entryProperty.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        serializedDatabase.ApplyModifiedPropertiesWithoutUndo();

        return database;
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
