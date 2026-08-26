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
using Project001.UI.Hud;
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
        // Application.isBatchMode guard: a headless -executeMethod run (e.g.
        // regenerating the scene from CI or a script after an editor-script
        // change) has no UI to show this dialog to - EditorUtility.
        // DisplayDialog silently answers "Cancel" in that case, which would
        // make the whole regeneration a silent no-op. Interactive Editor use
        // still gets the confirmation prompt.
        if (File.Exists(ScenePath) && !Application.isBatchMode)
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
        CreateGameplayBackground(mainCamera);
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

        GameplaySpeedController gameplaySpeedController = CreateGameplaySpeedController();
        PauseFlowController pauseFlowController = CreatePauseFlowController(gameplayFlowController);
        LevelExitFlowController levelExitFlowController = CreateLevelExitFlowController(gameplayFlowController);
        CreateTopGameplayHud(levelProgressionController, gameplaySpeedController, pauseFlowController, levelExitFlowController);

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
    /// registered on UniversalRP.asset (added alongside the project's
    /// existing, still-default 2D Renderer — see UniversalRP.asset's
    /// m_RendererDataList). Without this override the camera falls back to
    /// the pipeline's default (the 2D Renderer), which does not shade URP
    /// Lit materials against a Directional Light at all — every 3D character
    /// would render flat/unlit regardless of the key light's own settings.
    /// Every other 2D visual (SpriteRenderers, UI, PixelGrid) renders
    /// identically under either renderer, so this only ever affects the character model.
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
            Debug.LogError($"BootstrapSceneCreator: could not load '{UniversalPipelineAssetPath}' and/or '{UniversalRendererDataPath}'; Main Camera will fall back to the pipeline's default renderer and the character will render unlit.");
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
            Debug.LogError($"BootstrapSceneCreator: '{rendererData.name}' is not registered in '{pipelineAsset.name}''s renderer list; Main Camera will fall back to the pipeline's default renderer and the character will render unlit.");
            return;
        }

        var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.SetRenderer(rendererIndex);
    }

    private const string GameplayBackgroundSpritePath = "Assets/Art/UI/Classic/Background.png";
    private const string GameplayBackgroundSortingLayerName = "Background";

    // Local Z offset (along the camera's own forward axis, since this is a
    // child of Main Camera - see below) comfortably beyond
    // GameplayLayout.CameraDistance (10, the distance from the camera to
    // the shared Z=0 gameplay plane every other element sits at or is
    // pulled toward - see GameplayLayout.QueueRowDepthStep's own remarks),
    // so normal depth testing alone already keeps this behind every opaque
    // 3D character, independent of the Background sorting layer below.
    private const float GameplayBackgroundLocalDepth = 15f;

    /// <summary>
    /// Creates the Classic theme gameplay background: a single
    /// SpriteRenderer showing Background.png, parented to Main Camera so it
    /// always exactly fills the camera's view at any tilt (see
    /// GameplayBackground's own remarks for why parenting makes this
    /// trivial for an orthographic camera). Placed first in the scene
    /// build order - behind every other gameplay element both by depth
    /// (GameplayBackgroundLocalDepth) and by sorting layer
    /// (GameplayBackgroundSortingLayerName, the first/lowest layer in
    /// Project Settings > Tags and Layers; every other SpriteRenderer in
    /// this project stays on the default "Default" layer, so this is
    /// guaranteed behind all of them regardless of sortingOrder). Runtime
    /// sizing (cover the viewport, preserve aspect, crop rather than
    /// stretch) happens in GameplayBackground.Awake(); the Fit call here
    /// only bakes a same-reference-aspect preview into the saved scene,
    /// exactly like CreateCamera's own orthographicSize bake above.
    /// </summary>
    private static void CreateGameplayBackground(Camera mainCamera)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GameplayBackgroundSpritePath);
        if (sprite == null)
        {
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{GameplayBackgroundSpritePath}'; gameplay background will be missing. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");
            return;
        }

        var backgroundObject = new GameObject(
            "GameplayBackground",
            typeof(SpriteRenderer),
            typeof(GameplayBackground));
        backgroundObject.transform.SetParent(mainCamera.transform, false);
        backgroundObject.transform.localPosition = new Vector3(0f, 0f, GameplayBackgroundLocalDepth);
        backgroundObject.transform.localRotation = Quaternion.identity;

        var spriteRenderer = backgroundObject.GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingLayerName = GameplayBackgroundSortingLayerName;
        spriteRenderer.sortingOrder = 0;

        backgroundObject.GetComponent<GameplayBackground>().Fit(9f, 16f);
    }

    /// <summary>
    /// The only light in the scene: one Directional Light, still simple,
    /// white, and neutral (default intensity/shadow settings) but no longer
    /// Unity's own standard default rotation for a new light (50, -30, 0) —
    /// that angle's travel direction sits close enough to the camera's own
    /// forward axis (both facing mostly +Z) that a camera-facing character's
    /// visible hemisphere was lit almost head-on, which read as flat/
    /// marker-filled instead of showing the model's actual sculpted
    /// surface relief. (40, -55, 0) keeps the same general "high and to one
    /// side" key-light feel but swings yaw further around (-55 vs -30) and
    /// eases pitch back slightly (40 vs 50), producing a more oblique,
    /// raking angle across the model — still a simple single-light setup,
    /// not a production lighting rig, just aimed so the geometry is
    /// actually readable. Added for the 3D character presentation spike so the
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
    /// this is the entire lighting rig the character renders under: with no
    /// fill light and only that dim/cool default ambient, the side of a
    /// rounded character facing away from the key light read as dark and grayish-blue
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

    private const string PixelBlockFbxPath = "Assets/Art/Gameplay/PixelBlock.fbx";
    private const string PixelBlockMaterialPath = "Assets/Art/Gameplay/Materials/PixelBlock.mat";

    /// <summary>
    /// Wires PixelGrid's shared PixelBlock mesh/material source (see
    /// PixelCell.CreateVisual) - the same two assets every cell across the
    /// whole grid instances/shares, never duplicated per cell. Scene-owned,
    /// exactly like every other cross-reference in this class.
    /// </summary>
    private static PixelGrid CreatePixelGrid()
    {
        var pixelGridObject = new GameObject("PixelGrid", typeof(PixelGrid));
        pixelGridObject.transform.position = new Vector3(0f, GameplayLayout.PixelGridPositionY, 0f);

        var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PixelBlockFbxPath);
        if (fbxRoot == null)
            Debug.LogError($"BootstrapSceneCreator: could not load '{PixelBlockFbxPath}'; PixelGrid cells will have no visual.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(PixelBlockMaterialPath);
        if (material == null)
            Debug.LogError($"BootstrapSceneCreator: could not load '{PixelBlockMaterialPath}'; PixelGrid cells will have no visual.");

        var pixelGrid = pixelGridObject.GetComponent<PixelGrid>();
        var serializedPixelGrid = new SerializedObject(pixelGrid);
        serializedPixelGrid.FindProperty("preferredCellSize").floatValue = GameplayLayout.GridPreferredCellSize;
        serializedPixelGrid.FindProperty("preferredGap").floatValue = GameplayLayout.GridPreferredGap;
        serializedPixelGrid.FindProperty("pixelBlockSource").objectReferenceValue = fbxRoot;
        serializedPixelGrid.FindProperty("pixelBlockMaterial").objectReferenceValue = material;
        serializedPixelGrid.ApplyModifiedPropertiesWithoutUndo();

        // The field's outer bound goes through the public runtime seam
        // (SetFieldBounds), not SerializedObject reflection — unlike
        // preferredCellSize/preferredGap above (authored presentation
        // constants), this is the one value a future responsive layout pass
        // needs to recompute at runtime from the real screen/safe area, so
        // it is exercised through the same API that pass would use, keeping
        // this call site honest about what is actually a public seam.
        pixelGrid.SetFieldBounds(GameplayLayout.GridRegionWidth, GameplayLayout.GridRegionHeight);

        return pixelGrid;
    }

    private static ConveyorSystem CreateConveyor()
    {
        var conveyorObject = new GameObject(
            "Conveyor",
            typeof(ConveyorPath),
            typeof(ConveyorSystem));
        // Same Y as PixelGrid (GameplayLayout.PixelGridPositionY), not a
        // separately hand-typed Vector3.zero - Conveyor is always centered
        // on that same origin (see ConveyorSize's own remarks), so both stay
        // concentric automatically if that shared value is ever revisited.
        conveyorObject.transform.position = new Vector3(0f, GameplayLayout.PixelGridPositionY, 0f);

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
        // this must safely exceed the rendered character footprint (see its own
        // derivation comment) or riders that board close together end up
        // permanently overlapping, since every rider moves at the same
        // fixed speed and never closes or opens that gap afterward.
        serializedSystem.FindProperty("boardingClearance").floatValue = GameplayLayout.ConveyorRiderMinimumSpacing;
        serializedSystem.ApplyModifiedPropertiesWithoutUndo();

        CreateConveyorVisual(conveyorObject.transform);
        CreateConveyorBeltAnimation(conveyorObject.transform, conveyorPath, conveyorSystem);

        return conveyorSystem;
    }

    private const string ConveyorVisualSpritePath = "Assets/Art/UI/Classic/Conveyor.png";

    /// <summary>
    /// The Classic theme conveyor sprite, as a child of Conveyor so it
    /// always shares ConveyorPath/ConveyorSystem's own world origin without
    /// this method (or ConveyorVisual itself) touching either — purely
    /// presentation, added after the gameplay components above. Its sprite
    /// import settings (Pixels Per Unit, pivot — see Conveyor.png.meta) are
    /// calibrated so its drawn belt lines up with ConveyorPath's authored
    /// footprint at ConveyorBeltCalibration.CalibratedConveyorSize, then
    /// rescaled by ConveyorBeltCalibration.VisualScaleVector — the uniform
    /// VisualScale that tracks GameplayLayout.ConveyorSize's current
    /// (possibly larger) value, times the small non-uniform X/Y PNG-fit
    /// correction — see ConveyorVisual's own remarks. Sprite/sortingLayer/scale are set
    /// directly on the SpriteRenderer/Transform (baking a scene-view
    /// preview, mirroring CreateGameplayBackground) as well as on
    /// ConveyorVisual's own serialized field (what Awake re-applies at
    /// runtime).
    /// </summary>
    private static void CreateConveyorVisual(Transform conveyorTransform)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ConveyorVisualSpritePath);
        if (sprite == null)
        {
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{ConveyorVisualSpritePath}'; conveyor visual will be missing. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");
            return;
        }

        var visualObject = new GameObject(
            "ConveyorVisual",
            typeof(SpriteRenderer),
            typeof(ConveyorVisual));
        visualObject.transform.SetParent(conveyorTransform, false);
        // Baked here purely for an accurate scene-view preview - ConveyorVisual.Awake()
        // re-applies this exact same depth push, vertical art offset, and
        // scale (see its own remarks) at runtime regardless of what is saved
        // in the scene.
        visualObject.transform.localPosition = GameplayLayout.CameraForward * ConveyorVisual.DepthPushDistanceValue
            + new Vector3(0f, ConveyorBeltCalibration.VisualVerticalOffset, 0f);
        visualObject.transform.localRotation = Quaternion.identity;
        visualObject.transform.localScale = ConveyorBeltCalibration.VisualScaleVector;

        var spriteRenderer = visualObject.GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sortingLayerName = ConveyorVisual.SortingLayerNameValue;
        spriteRenderer.sortingOrder = 0;

        var conveyorVisual = visualObject.GetComponent<ConveyorVisual>();
        var serializedVisual = new SerializedObject(conveyorVisual);
        serializedVisual.FindProperty("sprite").objectReferenceValue = sprite;
        serializedVisual.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// The first Conveyor movement animation (see ConveyorBeltAnimation's
    /// own remarks): a scrolling chevron-marker ribbon, added as a sibling
    /// of ConveyorVisual rather than replacing it — the static Conveyor.png
    /// frame/belt art stays exactly as CreateConveyorVisual placed it, this
    /// only draws thin moving markers over its belt band. Only reads
    /// conveyorPath/conveyorSystem (never writes either); both are already
    /// fully configured by the time CreateConveyor calls this.
    /// </summary>
    private static void CreateConveyorBeltAnimation(Transform conveyorTransform, ConveyorPath conveyorPath, ConveyorSystem conveyorSystem)
    {
        var animationObject = new GameObject(
            "ConveyorBeltAnimation",
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(ConveyorBeltAnimation));
        animationObject.transform.SetParent(conveyorTransform, false);
        // Baked here purely for an accurate scene-view preview -
        // ConveyorBeltAnimation.Awake() re-applies this exact same push at
        // runtime, mirroring ConveyorVisual's own convention.
        animationObject.transform.localPosition = GameplayLayout.CameraForward * ConveyorBeltAnimation.DepthPushDistanceValue;
        animationObject.transform.localRotation = Quaternion.identity;

        var beltAnimation = animationObject.GetComponent<ConveyorBeltAnimation>();
        var serializedAnimation = new SerializedObject(beltAnimation);
        serializedAnimation.FindProperty("conveyorPath").objectReferenceValue = conveyorPath;
        serializedAnimation.FindProperty("conveyorSystem").objectReferenceValue = conveyorSystem;
        serializedAnimation.ApplyModifiedPropertiesWithoutUndo();
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

    private const string WaitingSlotSpritePath = "Assets/Art/UI/Classic/WaitingSlot.png";

    private static Project001.Gameplay.WaitingLine.WaitingLine CreateWaitingLine()
    {
        var waitingLineObject = new GameObject(
            "WaitingLine",
            typeof(Project001.Gameplay.WaitingLine.WaitingLine));
        waitingLineObject.transform.position = new Vector3(0f, GameplayLayout.WaitingLinePositionY, 0f);

        var waitingLine = waitingLineObject.GetComponent<Project001.Gameplay.WaitingLine.WaitingLine>();

        // Baked here, mirroring CreateConveyorVisual's own sprite-loading
        // convention, rather than inside WaitingLine.GenerateSlots itself
        // (a runtime method with no AssetDatabase access) — WaitingLine only
        // ever needs this reference to hand off to each slot's own
        // WaitingSlotVisual child at Initialize time.
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>(WaitingSlotSpritePath);
        if (slotSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{WaitingSlotSpritePath}'; waiting line slots will show no visual.");

        var serializedWaitingLine = new SerializedObject(waitingLine);
        serializedWaitingLine.FindProperty("slotSprite").objectReferenceValue = slotSprite;
        serializedWaitingLine.ApplyModifiedPropertiesWithoutUndo();

        return waitingLine;
    }

    private const string EnergyBarPrefabPath = "Assets/Prefabs/UI/EnergyBar.prefab";

    private static CollectorQueueBoard CreateCollectorQueueBoard(
        PixelGrid pixelGrid,
        ConveyorSystem conveyorSystem,
        Project001.Gameplay.WaitingLine.WaitingLine waitingLine,
        FailureController failureController,
        CharacterDatabase characterDatabase)
    {
        var boardObject = new GameObject("CollectorQueueBoard", typeof(CollectorQueueBoard));
        boardObject.transform.position = new Vector3(0f, GameplayLayout.CollectorQueueBoardPositionY, 0f);

        var energyBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnergyBarPrefabPath);
        if (energyBarPrefab == null)
            Debug.LogError($"BootstrapSceneCreator: could not load EnergyBar prefab at '{EnergyBarPrefabPath}'; collectors will show no EnergyBar.");

        var collectorQueueBoard = boardObject.GetComponent<CollectorQueueBoard>();
        var serializedBoard = new SerializedObject(collectorQueueBoard);
        serializedBoard.FindProperty("characterDatabase").objectReferenceValue = characterDatabase;
        serializedBoard.FindProperty("energyBarPrefab").objectReferenceValue = energyBarPrefab;
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

    // ================= Top Gameplay HUD =================

    /// <summary>
    /// Sole owner of the selected 1x/2x gameplay speed (see its own
    /// remarks). No dependencies of its own to wire — TopHudUI reads from
    /// it, but this controller depends on neither TopHudUI nor
    /// GameplayFlowController.
    /// </summary>
    private static GameplaySpeedController CreateGameplaySpeedController()
    {
        var speedControllerObject = new GameObject("GameplaySpeedController", typeof(GameplaySpeedController));
        return speedControllerObject.GetComponent<GameplaySpeedController>();
    }

    /// <summary>
    /// Owns what the top HUD's Pause button and the Pause modal's Continue
    /// button actually do (pause/resume through gameplayFlowController) —
    /// presentation only ever calls its OpenPause/ContinuePause API.
    /// </summary>
    private static PauseFlowController CreatePauseFlowController(GameplayFlowController gameplayFlowController)
    {
        var pauseFlowControllerObject = new GameObject("PauseFlowController", typeof(PauseFlowController));

        var pauseFlowController = pauseFlowControllerObject.GetComponent<PauseFlowController>();
        var serializedPauseFlowController = new SerializedObject(pauseFlowController);
        serializedPauseFlowController.FindProperty("gameplayFlowController").objectReferenceValue = gameplayFlowController;
        serializedPauseFlowController.ApplyModifiedPropertiesWithoutUndo();

        return pauseFlowController;
    }

    /// <summary>
    /// Owns what the Exit confirmation dialog actually does, regardless of
    /// which of its two entry points (top HUD Exit, Pause modal Exit Level)
    /// opened it — presentation only ever calls its
    /// OpenExitConfirmation/CancelExit/ConfirmExit API. ConfirmExit is
    /// currently a stub — see its own remarks for why (no destination scene
    /// or navigation controller exists yet in this project).
    /// </summary>
    private static LevelExitFlowController CreateLevelExitFlowController(GameplayFlowController gameplayFlowController)
    {
        var levelExitFlowControllerObject = new GameObject("LevelExitFlowController", typeof(LevelExitFlowController));

        var levelExitFlowController = levelExitFlowControllerObject.GetComponent<LevelExitFlowController>();
        var serializedLevelExitFlowController = new SerializedObject(levelExitFlowController);
        serializedLevelExitFlowController.FindProperty("gameplayFlowController").objectReferenceValue = gameplayFlowController;
        serializedLevelExitFlowController.ApplyModifiedPropertiesWithoutUndo();

        return levelExitFlowController;
    }

    /// <summary>
    /// Builds the Pause and Exit confirmation modals first (GameplayModalCanvas,
    /// sortingOrder 10 — above TopHudCanvas's default 0, so either modal's own
    /// full-screen backdrop reliably blocks clicks to the HUD row underneath
    /// it, with no separate CanvasGroup/interactable bookkeeping needed on
    /// the HUD side), then the top HUD row itself (TopHudCanvas), wired to
    /// both modals plus the gameplay-layer controllers already created by
    /// the caller.
    /// </summary>
    private static void CreateTopGameplayHud(
        LevelProgressionController levelProgressionController,
        GameplaySpeedController gameplaySpeedController,
        PauseFlowController pauseFlowController,
        LevelExitFlowController levelExitFlowController)
    {
        (PauseUI pauseUI, ExitConfirmationUI exitConfirmationUI) = CreateGameplayModalUI(pauseFlowController, levelExitFlowController);
        CreateTopHudCanvas(levelProgressionController, gameplaySpeedController, levelExitFlowController, pauseFlowController, exitConfirmationUI, pauseUI);
    }

    /// <summary>
    /// One Canvas hosting both the Pause and Exit confirmation panels as
    /// siblings — both start inactive, only ever one visible at a time (see
    /// PauseUI.OnExitLevelPressed/ExitConfirmationUI.Open) — rather than two
    /// separate canvases, since both share the same "modal above the HUD"
    /// sorting concern. PauseUI/ExitConfirmationUI live on this canvas
    /// object itself, never on their own panel — the canvas stays active for
    /// the whole scene (only the child panels toggle), exactly like
    /// VictoryUI/FailureUI living on their own always-active canvas rather
    /// than on VictoryPanel/FailurePanel: a component placed directly on an
    /// initially-inactive GameObject would not run Awake (and therefore
    /// would not wire up its own button listeners) until that GameObject was
    /// first activated, one frame after the very panel-show call meant to
    /// reveal it.
    /// </summary>
    private static (PauseUI pauseUI, ExitConfirmationUI exitConfirmationUI) CreateGameplayModalUI(
        PauseFlowController pauseFlowController,
        LevelExitFlowController levelExitFlowController)
    {
        var canvasObject = new GameObject(
            "GameplayModalCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(PauseUI),
            typeof(ExitConfirmationUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var exitConfirmationUI = canvasObject.GetComponent<ExitConfirmationUI>();
        CreateExitConfirmPanel(canvasObject.transform, exitConfirmationUI, levelExitFlowController);

        var pauseUI = canvasObject.GetComponent<PauseUI>();
        CreatePausePanel(canvasObject.transform, pauseUI, pauseFlowController, exitConfirmationUI);

        return (pauseUI, exitConfirmationUI);
    }

    private static void CreatePausePanel(Transform parent, PauseUI pauseUI, PauseFlowController pauseFlowController, ExitConfirmationUI exitConfirmationUI)
    {
        GameObject panel = CreateModalBackdrop(parent, "PausePanel");
        GameObject dialogBox = CreateDialogBox(panel.transform, new Vector2(560f, 360f));

        CreateCenteredText(dialogBox.transform, "Paused", new Vector2(0f, 110f), new Vector2(400f, 60f), 30, Color.white);
        Button continueButton = CreateDialogButton(dialogBox.transform, "ContinueButton", "Continue", new Vector2(0f, -10f));
        Button exitLevelButton = CreateDialogButton(dialogBox.transform, "ExitLevelButton", "Exit Level", new Vector2(0f, -90f));

        var serializedPauseUI = new SerializedObject(pauseUI);
        serializedPauseUI.FindProperty("panel").objectReferenceValue = panel;
        serializedPauseUI.FindProperty("continueButton").objectReferenceValue = continueButton;
        serializedPauseUI.FindProperty("exitLevelButton").objectReferenceValue = exitLevelButton;
        serializedPauseUI.FindProperty("pauseFlowController").objectReferenceValue = pauseFlowController;
        serializedPauseUI.FindProperty("exitConfirmationUI").objectReferenceValue = exitConfirmationUI;
        serializedPauseUI.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateExitConfirmPanel(Transform parent, ExitConfirmationUI exitConfirmationUI, LevelExitFlowController levelExitFlowController)
    {
        GameObject panel = CreateModalBackdrop(parent, "ExitConfirmPanel");
        GameObject dialogBox = CreateDialogBox(panel.transform, new Vector2(620f, 380f));

        CreateCenteredText(dialogBox.transform, "Exit level?", new Vector2(0f, 120f), new Vector2(500f, 60f), 30, Color.white);
        CreateCenteredText(dialogBox.transform, "Your current progress will be lost.", new Vector2(0f, 60f), new Vector2(520f, 60f), 20, Color.white);
        Button stayButton = CreateDialogButton(dialogBox.transform, "StayButton", "Stay", new Vector2(-110f, -110f));
        Button exitButton = CreateDialogButton(dialogBox.transform, "ExitButton", "Exit", new Vector2(110f, -110f));

        var serializedExitConfirmationUI = new SerializedObject(exitConfirmationUI);
        serializedExitConfirmationUI.FindProperty("panel").objectReferenceValue = panel;
        serializedExitConfirmationUI.FindProperty("stayButton").objectReferenceValue = stayButton;
        serializedExitConfirmationUI.FindProperty("exitButton").objectReferenceValue = exitButton;
        serializedExitConfirmationUI.FindProperty("levelExitFlowController").objectReferenceValue = levelExitFlowController;
        serializedExitConfirmationUI.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// A full-screen, semi-transparent, raycast-blocking Image — the modal
    /// "panel" a PauseUI/ExitConfirmationUI toggles active/inactive, unlike
    /// VictoryPanel/FailurePanel's own small floating box: those two modals
    /// live on their own dedicated top-of-everything canvas
    /// (GameplayModalCanvas, sortingOrder 10) specifically so this backdrop
    /// can reliably intercept clicks aimed at the top HUD row underneath it
    /// while either modal is open, with no separate CanvasGroup/interactable
    /// bookkeeping needed on TopHudUI's own buttons.
    /// </summary>
    private static GameObject CreateModalBackdrop(Transform parent, string name)
    {
        var backdropObject = new GameObject(name, typeof(Image));
        backdropObject.transform.SetParent(parent, false);

        var rectTransform = backdropObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        var image = backdropObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.6f);

        backdropObject.SetActive(false);

        return backdropObject;
    }

    private static GameObject CreateDialogBox(Transform parent, Vector2 size)
    {
        var dialogObject = new GameObject("DialogBox", typeof(Image));
        dialogObject.transform.SetParent(parent, false);

        var rectTransform = dialogObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = Vector2.zero;

        var image = dialogObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.85f);

        return dialogObject;
    }

    private static Button CreateDialogButton(Transform parent, string name, string label, Vector2 anchoredPosition)
    {
        var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(200f, 64f);
        rectTransform.anchoredPosition = anchoredPosition;

        var image = buttonObject.GetComponent<Image>();
        image.color = Color.white;

        CreateCenteredText(buttonObject.transform, label, Vector2.zero, new Vector2(200f, 64f), 20, Color.black);

        return buttonObject.GetComponent<Button>();
    }

    private const string HudExitSpritePath = "Assets/Art/UI/Classic/HUD/HudExit.png";
    private const string HudPauseSpritePath = "Assets/Art/UI/Classic/HUD/HudPause.png";
    private const string HudSpeed1xSpritePath = "Assets/Art/UI/Classic/HUD/HudSpeed1x.png";
    private const string HudSpeed2xSpritePath = "Assets/Art/UI/Classic/HUD/HudSpeed2x.png";
    private const string LevelLabelSpritePath = "Assets/Art/UI/Classic/HUD/Level/LevelLabel.png";
    private const string LevelDigitSpritePathFormat = "Assets/Art/UI/Classic/HUD/Level/Digit{0}.png";

    /// <summary>
    /// The always-visible top HUD row: Exit (left), "LEVEL {number}"
    /// (center), speed toggle + Pause (right) — see TopHudUI's own remarks.
    /// Anchored to its own canvas corners/top-center (never a bare absolute
    /// screen position) so the row stays correctly placed across aspect
    /// ratios beyond the 1080x1920 portrait reference, the same corner-anchor
    /// convention EnergyBarView's own fill anchoring already establishes for
    /// resolution-independent UI in this project. Does not move or resize
    /// PixelGrid, Conveyor, WaitingLine, collectors, or EnergyBars — none of
    /// GameplayLayout's world-space composition reserves a top HUD region,
    /// so this row is purely an overlay on top of the existing gameplay
    /// frame, not a change to it.
    /// </summary>
    private static void CreateTopHudCanvas(
        LevelProgressionController levelProgressionController,
        GameplaySpeedController gameplaySpeedController,
        LevelExitFlowController levelExitFlowController,
        PauseFlowController pauseFlowController,
        ExitConfirmationUI exitConfirmationUI,
        PauseUI pauseUI)
    {
        var canvasObject = new GameObject(
            "TopHudCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(TopHudUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var hudExitSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HudExitSpritePath);
        if (hudExitSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{HudExitSpritePath}'; the top HUD Exit button will show no icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var hudPauseSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HudPauseSpritePath);
        if (hudPauseSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{HudPauseSpritePath}'; the top HUD Pause button will show no icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var hudSpeed1xSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HudSpeed1xSpritePath);
        if (hudSpeed1xSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{HudSpeed1xSpritePath}'; the top HUD Speed button will show no icon at 1x. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var hudSpeed2xSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HudSpeed2xSpritePath);
        if (hudSpeed2xSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{HudSpeed2xSpritePath}'; the top HUD Speed button will show no icon at 2x. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var levelLabelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LevelLabelSpritePath);
        if (levelLabelSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{LevelLabelSpritePath}'; the top HUD level display will show no label icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var levelDigitSprites = new Sprite[10];
        for (int digit = 0; digit < levelDigitSprites.Length; digit++)
        {
            string digitSpritePath = string.Format(LevelDigitSpritePathFormat, digit);
            levelDigitSprites[digit] = AssetDatabase.LoadAssetAtPath<Sprite>(digitSpritePath);
            if (levelDigitSprites[digit] == null)
                Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{digitSpritePath}'; the top HUD level display will show no icon for digit {digit}. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");
        }

        Button backButton = CreateCornerButton(canvasObject.transform, "BackButton", hudExitSprite, new Vector2(0f, 1f), new Vector2(48f, -60f), new Vector2(72f, 72f), out _);
        RectTransform levelDisplayContainer = CreateAnchoredContainer(canvasObject.transform, "LevelDisplay", new Vector2(0.5f, 1f), new Vector2(0f, -96f));
        Button pauseButton = CreateCornerButton(canvasObject.transform, "PauseButton", hudPauseSprite, new Vector2(1f, 1f), new Vector2(-48f, -60f), new Vector2(72f, 72f), out _);
        Button speedButton = CreateCornerButton(canvasObject.transform, "SpeedButton", hudSpeed1xSprite, new Vector2(1f, 1f), new Vector2(-48f - 72f - 16f, -48f), new Vector2(96f, 96f), out Image speedButtonIcon);

        var topHudUI = canvasObject.GetComponent<TopHudUI>();
        var serializedTopHudUI = new SerializedObject(topHudUI);
        serializedTopHudUI.FindProperty("levelDisplayContainer").objectReferenceValue = levelDisplayContainer;
        serializedTopHudUI.FindProperty("levelLabelSprite").objectReferenceValue = levelLabelSprite;

        var levelDigitSpritesProperty = serializedTopHudUI.FindProperty("levelDigitSprites");
        levelDigitSpritesProperty.arraySize = levelDigitSprites.Length;
        for (int digit = 0; digit < levelDigitSprites.Length; digit++)
            levelDigitSpritesProperty.GetArrayElementAtIndex(digit).objectReferenceValue = levelDigitSprites[digit];

        serializedTopHudUI.FindProperty("backButton").objectReferenceValue = backButton;
        serializedTopHudUI.FindProperty("speedButton").objectReferenceValue = speedButton;
        serializedTopHudUI.FindProperty("speedButtonIcon").objectReferenceValue = speedButtonIcon;
        serializedTopHudUI.FindProperty("speedNormalSprite").objectReferenceValue = hudSpeed1xSprite;
        serializedTopHudUI.FindProperty("speedFastSprite").objectReferenceValue = hudSpeed2xSprite;
        serializedTopHudUI.FindProperty("pauseButton").objectReferenceValue = pauseButton;
        serializedTopHudUI.FindProperty("levelProgressionController").objectReferenceValue = levelProgressionController;
        serializedTopHudUI.FindProperty("gameplaySpeedController").objectReferenceValue = gameplaySpeedController;
        serializedTopHudUI.FindProperty("levelExitFlowController").objectReferenceValue = levelExitFlowController;
        serializedTopHudUI.FindProperty("pauseFlowController").objectReferenceValue = pauseFlowController;
        serializedTopHudUI.FindProperty("exitConfirmationUI").objectReferenceValue = exitConfirmationUI;
        serializedTopHudUI.FindProperty("pauseUI").objectReferenceValue = pauseUI;
        serializedTopHudUI.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// A button anchored/pivoted to one of the canvas's own corners
    /// (anchorMin == anchorMax == pivot), positioned by a pixel offset from
    /// that corner rather than an absolute screen position — the offset
    /// stays a small, fixed inset regardless of canvas size, unlike a
    /// center-anchored placement (CreateContinueButton's own convention),
    /// which is why this is a separate helper rather than a reuse of that
    /// one. The button's own Image *is* the icon (icon may be null if its
    /// sprite failed to load — the button still exists, just blank) with
    /// preserveAspect on, per the Classic HUD icons' own square, transparent-
    /// background art: no separate background rect and no interior text
    /// label, unlike CreateContinueButton/CreateFailureActionButton's own
    /// text buttons.
    /// </summary>
    private static Button CreateCornerButton(Transform parent, string name, Sprite icon, Vector2 anchor, Vector2 anchoredPosition, Vector2 size, out Image iconImage)
    {
        var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = anchor;
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var image = buttonObject.GetComponent<Image>();
        image.sprite = icon;
        image.color = Color.white;
        image.preserveAspect = true;

        iconImage = image;
        return buttonObject.GetComponent<Button>();
    }

    /// <summary>
    /// A standalone, zero-size anchor point pinned to one of the canvas's
    /// own corners/edges (unlike CreateCenteredText, which is always
    /// center-anchored on its parent) — used as the top-center anchor that
    /// TopHudUI.BuildLevelDisplay builds the LevelLabel + digit icons under
    /// at runtime, since only TopHudUI knows the current level's digit count
    /// and can center that row on this single point regardless of it.
    /// </summary>
    private static RectTransform CreateAnchoredContainer(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition)
    {
        var containerObject = new GameObject(name, typeof(RectTransform));
        containerObject.transform.SetParent(parent, false);

        var rectTransform = containerObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = anchor;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.anchoredPosition = anchoredPosition;

        return rectTransform;
    }
}
