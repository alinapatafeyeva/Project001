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
using TMPro;
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

        LevelExitFlowController levelExitFlowController = CreateLevelExitFlowController(gameplayFlowController);
        CreateFailureUI(failureController, failureRecoveryController, levelExitFlowController);

        GameplaySpeedController gameplaySpeedController = CreateGameplaySpeedController();
        PauseFlowController pauseFlowController = CreatePauseFlowController(gameplayFlowController);
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

    private const string LevelFailedModalSpritePath = "Assets/Art/UI/Classic/LevelFailedModal.png";
    private const string AdIconSpritePath = "Assets/Art/UI/Classic/Icons/AdIcon.png";

    /// <summary>
    /// Absolute width ceiling for the Level Failed modal specifically — its
    /// own artwork (LevelFailedModal.png) is a much taller portrait card
    /// (aspect ~0.646) than ConfirmationModal.png's ~1.384 landscape card
    /// used by Exit/Pause, so reusing ConfirmationModalMaxWidth (1400)
    /// unchanged would resolve to a ~2170-unit-tall modal on iPad portrait —
    /// ~91% of that device's own 2388-unit canvas height, leaving almost no
    /// dimmed backdrop margin above/below it (see CreateModalBackdrop — the
    /// backdrop must stay visible per this task's own requirement). 1200
    /// instead resolves to 1200/0.6457 ≈ 1859 units (~78% of the iPad
    /// reference's 2388 height) — comfortably inside the same "modal
    /// doesn't dominate the whole tablet screen" intent
    /// ConfirmationModalMaxWidth already serves for Exit/Pause, just a
    /// smaller ceiling because this card's own aspect ratio is taller, not a
    /// different responsive philosophy. Width fraction, vertical offset, and
    /// content reference width are still the exact same shared
    /// ConfirmationModal* constants Exit/Pause use (see CreateFailureUI) —
    /// only this one ceiling differs, and only because the artwork's own
    /// shape differs.
    /// </summary>
    private const float LevelFailedModalMaxWidth = 1200f;

    /// <summary>
    /// All fixed pixel positions/sizes below are authored against
    /// ConfirmationModalContentReferenceWidth exactly like Exit/Pause (see
    /// ExitConfirmButtonHeight's remarks for that convention) — but note
    /// this modal's own implied content HEIGHT differs from theirs, since it
    /// shares their width but not their artwork's aspect ratio (see
    /// LevelFailedModalMaxWidth's remarks). Proportions were measured
    /// directly off reference/UI/LevelFailedModalTarget.png as a fraction of
    /// its own total card height, then applied to this modal's own implied
    /// height — the whole composition spans close to the full interior
    /// (title near the top, Exit level near the bottom), matching the
    /// reference's own vertically-balanced distribution, rather than
    /// clustering everything near the top with dead space below.
    ///
    /// This one specifically: vertical anchoredPosition of the "No space
    /// left!" title.
    /// </summary>
    private const float LevelFailedTitleOffsetY = 536f;

    /// <summary>
    /// Title is auto-sized (TMP enableAutoSizing — the same technique
    /// EnergyBarPrefabBuilder.BuildValueText already uses in this project)
    /// rather than a single fixed font size like Exit/Pause's own titles:
    /// "No space left!" is meaningfully longer than "Exit level?"/"Paused",
    /// so a single guessed fixed size risked either overflowing or being too
    /// conservative. TMP instead solves for the largest size in
    /// [LevelFailedTitleFontSizeMin, LevelFailedTitleFontSizeMax] that still
    /// fits the title's own RectTransform width — word-wrap is disabled (see
    /// CreateAutoSizedCenteredTMPText) so it only ever shrinks the single
    /// line, never wraps it to two.
    /// </summary>
    private const float LevelFailedTitleFontSizeMin = 60f;

    /// <summary>See LevelFailedTitleFontSizeMin's remarks.</summary>
    private const float LevelFailedTitleFontSizeMax = 110f;

    /// <summary>
    /// Softer muted terracotta/dusty-brick failure colour, specified
    /// directly by the task rather than sampled off the reference image —
    /// the reference's own title uses a much brighter, more saturated red
    /// than this project's muted palette calls for. Also reused for the ad
    /// row's "3 / 3" count, which should read as the same "failure/urgency"
    /// accent colour as the title, not a third distinct colour.
    /// </summary>
    private static readonly Color LevelFailedTitleColor = new Color(0xB8 / 255f, 0x5F / 255f, 0x52 / 255f);

    /// <summary>Vertical anchoredPosition of the "The waiting line is full." description (see LevelFailedTitleOffsetY's remarks). Reuses ConfirmationModalDescriptionFontSize/Color — same supporting-text styling as Exit/Pause.</summary>
    private const float LevelFailedDescriptionOffsetY = 430f;

    /// <summary>Font size shared by this modal's own two section headings ("Use a booster:" and "One more chance?") — smaller than the title, reusing ConfirmationModalDescriptionColor rather than introducing a third text colour.</summary>
    private const float LevelFailedHeadingFontSize = 30f;

    /// <summary>Vertical anchoredPosition of the "Use a booster:" heading (see LevelFailedTitleOffsetY's remarks).</summary>
    private const float LevelFailedBoosterHeadingOffsetY = 329f;

    /// <summary>
    /// Flanking decoration around "Use a booster:" (short line + ">>" on the
    /// left, "&lt;&lt;" + short line on the right, both pointing inward) —
    /// inspired by the reference, reproduced with a plain Image bar (see
    /// CreateHorizontalSeparator) and ordinary ">>"/"&lt;&lt;" glyphs (safe on
    /// any font, unlike a Unicode star — see LevelFailedSeparator1DotColor's
    /// remarks) rather than a new art asset.
    /// </summary>
    private const float LevelFailedBoosterHeadingChevronOffsetX = 155f;

    private const float LevelFailedBoosterHeadingLineOffsetX = 250f;
    private const float LevelFailedBoosterHeadingLineWidth = 130f;
    private const float LevelFailedBoosterHeadingChevronFontSize = 24f;

    /// <summary>Shared thickness/colour for every plain decorative line in this modal (booster heading flanks, second separator) — low-contrast on purpose, per the task's own "decorative, not a hard divider" requirement.</summary>
    private const float LevelFailedSeparatorThickness = 3f;

    private static readonly Color LevelFailedSeparatorColor = new Color(210f / 255f, 180f / 255f, 150f / 255f, 0.6f);

    /// <summary>Vertical anchoredPosition shared by all three booster placeholder circles (see LevelFailedTitleOffsetY's remarks).</summary>
    private const float LevelFailedBoosterRowOffsetY = 166f;

    /// <summary>
    /// Diameter shared by all three booster placeholder circles — identical
    /// size, per the task's own requirement. Booster mechanics/icons are not
    /// implemented yet (see CreateFailureUI's remarks); these are pure
    /// layout placeholders: a flat procedural fill circle (GenerateCircleSprite)
    /// plus a dashed ring overlay (GenerateDashedRingSprite) at the same size,
    /// rather than a new authored art asset.
    /// </summary>
    private const float LevelFailedBoosterSlotDiameter = 196f;

    /// <summary>Centre-to-centre horizontal spacing between adjacent booster slots — the 3 slots sit at -spacing/0/+spacing around the content's own horizontal centre, so the row of three stays centered as a group regardless of this value.</summary>
    private const float LevelFailedBoosterSlotSpacing = 256f;

    private const int LevelFailedBoosterSlotCount = 3;

    /// <summary>Muted dark taupe fill colour for the booster placeholder circles, sampled off reference/UI/LevelFailedModalTarget.png.</summary>
    private static readonly Color LevelFailedBoosterPlaceholderColor = new Color(178f / 255f, 151f / 255f, 123f / 255f);

    /// <summary>Lighter warm tan for the booster placeholders' dashed ring border (GenerateDashedRingSprite), so the dashes read against the darker taupe fill instead of blending into it.</summary>
    private static readonly Color LevelFailedBoosterBorderColor = new Color(214f / 255f, 187f / 255f, 155f / 255f);

    private const int LevelFailedBoosterBorderDashCount = 28;
    private const float LevelFailedBoosterBorderDashDutyCycle = 0.55f;
    private const float LevelFailedBoosterBorderThicknessFraction = 0.05f;

    /// <summary>
    /// First decorative separator, between the booster row and "One more
    /// chance?" — a dotted line on either side of a small centred star
    /// (GenerateStarSprite — see its own remarks), reusing the same
    /// procedurally-generated circle sprite as the booster fill for the dots
    /// at small tinted sizes rather than a new art asset. Deliberately low
    /// contrast (see LevelFailedSeparator1DotColor) — decorative, not a hard
    /// divider.
    /// </summary>
    private const float LevelFailedSeparator1OffsetY = -24f;

    private const float LevelFailedSeparator1SegmentWidth = 300f;
    private const float LevelFailedSeparator1CenterGap = 60f;
    private const float LevelFailedSeparator1DotDiameter = 7f;
    private const float LevelFailedSeparator1DotSpacing = 20f;
    private const float LevelFailedSeparator1CenterStarDiameter = 26f;
    private static readonly Color LevelFailedSeparator1DotColor = new Color(196f / 255f, 168f / 255f, 138f / 255f, 0.75f);

    /// <summary>
    /// Vertical anchoredPosition of the "One more chance?" heading (see
    /// LevelFailedTitleOffsetY's remarks) — together with
    /// LevelFailedAdContentOffsetY/LevelFailedContinueButtonOffsetY/
    /// LevelFailedSeparator2OffsetY, refit as one lower-section pass so every
    /// item below the first separator has a real, non-overlapping gap while
    /// LevelFailedExitButtonOffsetY stays exactly where it already read
    /// correctly.
    /// </summary>
    private const float LevelFailedSecondChanceHeadingOffsetY = -103f;

    /// <summary>
    /// Vertical anchoredPosition of the AdContent group as a whole (see
    /// LevelFailedSecondChanceHeadingOffsetY's remarks) — AdIcon and both
    /// text lines are children of that one layout container (see
    /// CreateAdStatusCard), positioned relative to IT, never independently
    /// against content or the screen, so the icon can never drift relative
    /// to its text at any resolved modal size. The container carries no
    /// visible background (no Image component at all) — the earlier large
    /// tinted rounded-rect card read as a "giant pink banner" that both
    /// overlapped "One more chance?" above it and visually competed with the
    /// Continue button below it; the container's own size below exists
    /// purely for layout/authoring, not for anything rendered.
    /// </summary>
    private const float LevelFailedAdContentOffsetY = -216f;

    private const float LevelFailedAdContentWidth = 500f;
    private const float LevelFailedAdContentHeight = 140f;

    /// <summary>
    /// Horizontal offset of AdIcon from the AdContent group's own centre
    /// (see LevelFailedAdContentOffsetY's remarks) — AdIcon and the text
    /// column are positioned so the pair reads as one compact, horizontally-
    /// centered composition (icon left, text column right), rather than the
    /// icon sitting far left while the text centers independently across the
    /// whole modal.
    /// </summary>
    private const float LevelFailedAdIconOffsetX = -174f;

    private const float LevelFailedAdIconHeight = 130f;

    /// <summary>Horizontal offset of both ad text lines from the AdContent group's own centre (see LevelFailedAdContentOffsetY's remarks) — same X for both lines, so "3 / 3" centers directly above "Watch an ad to continue".</summary>
    private const float LevelFailedAdTextOffsetX = 77f;

    /// <summary>Vertical offset of "3 / 3" from the AdContent group's own centre — together with LevelFailedAdCaptionOffsetY, centers the two-line text block so its own vertical midpoint approximately aligns with AdIcon's (Y=0 in the same group).</summary>
    private const float LevelFailedAdCountOffsetY = 22f;

    private const float LevelFailedAdCaptionOffsetY = -37f;

    /// <summary>"3 / 3" font size — kept prominent, comparable in weight to the Continue/Exit level button labels. Unchanged from the previous pass — only the surrounding layout moved.</summary>
    private const float LevelFailedAdCountFontSize = 60f;

    /// <summary>"Watch an ad to continue" font size — kept comfortably readable per the task's own explicit requirement not to shrink it back down; unchanged from the previous pass — only the surrounding layout moved.</summary>
    private const float LevelFailedAdCaptionFontSize = 28f;

    /// <summary>
    /// Static placeholder for "remaining / total" ad-rewatch attempts —
    /// presentation only (see CreateFailureUI's own remarks): no ad SDK
    /// exists yet, and the required count may eventually depend on level
    /// difficulty. Kept as this one named constant specifically so a future
    /// dynamic system has exactly one value (or one call site) to replace,
    /// rather than a string literal buried inside a CreateCenteredTMPText
    /// call.
    /// </summary>
    private const string LevelFailedAdWatchCountLabel = "3 / 3";

    /// <summary>
    /// Fixed footprint height shared by the Continue/Exit level buttons —
    /// its own constant (like Pause's PauseButtonHeight) rather than reusing
    /// ExitConfirmButtonHeight, since these two stack vertically (not side by
    /// side like Stay/Exit) and this modal has substantially more content
    /// above them competing for vertical space than Pause's single button
    /// does.
    /// </summary>
    private const float LevelFailedButtonHeight = 140f;

    private const float LevelFailedContinueButtonOffsetY = -391f;
    private const float LevelFailedExitButtonOffsetY = -610f;
    private const int LevelFailedButtonLabelFontSize = 45;

    /// <summary>
    /// Second decorative separator, between Continue and Exit level — two
    /// short solid line segments flanking a small centred dot, per the
    /// task's own description ("short horizontal line — small centre dot —
    /// short horizontal line"), reusing CreateHorizontalSeparator/the same
    /// circle sprite rather than new art. Low contrast/decorative, not a
    /// hard divider.
    /// </summary>
    private const float LevelFailedSeparator2OffsetY = -503f;

    private const float LevelFailedSeparator2LineWidth = 150f;
    private const float LevelFailedSeparator2LineOffsetX = 102f;
    private const float LevelFailedSeparator2DotDiameter = 14f;

    private const string CircleSpriteAssetPath = "Assets/Art/UI/Generated/CircleSprite.asset";
    private const string BoosterDashedRingSpriteAssetPath = "Assets/Art/UI/Generated/BoosterDashedRing.asset";
    private const string StarSpriteAssetPath = "Assets/Art/UI/Generated/StarSprite.asset";

    /// <summary>
    /// Source texture resolution shared by every procedurally-generated
    /// circle in this modal (booster fill/border, both separators' dots) —
    /// comfortably above the largest on-screen pixel size any of them could
    /// actually render at (content is scaled up to ~1.44x on a clamped
    /// tablet — see ResponsiveModalBox), so they stay crisp rather than
    /// visibly upscale-blurred, the same concern EnergyBarPrefabBuilder's own
    /// GenerateStadiumSprite remarks describe for Unity's built-in
    /// UISprite.psd at large sizes. The same sprite is reused at every size
    /// from LevelFailedBoosterSlotDiameter (196) down to
    /// LevelFailedSeparator1DotDiameter (7) via each Image's own
    /// RectTransform — downscaling a high-res source is always safe.
    /// </summary>
    private const int CircleTextureDiameter = 320;

    /// <summary>
    /// Level Failed modal: title, description, a "Use a booster:" heading
    /// over three placeholder slots (no booster mechanics/icons yet — see
    /// LevelFailedBoosterSlotDiameter's remarks), a separator, a "One more
    /// chance?" heading over a presentation-only ad-rewatch status row (see
    /// LevelFailedAdWatchCountLabel's remarks — no ad SDK is integrated),
    /// then Continue/Exit level. Reuses the exact same responsive artwork
    /// box/content-scaling infrastructure as Exit/Pause (see
    /// CreateResponsiveModalArtworkBox) with only this modal's own width
    /// ceiling differing (LevelFailedModalMaxWidth) — so it feels like the
    /// same physical panel family despite LevelFailedModal.png's own taller
    /// aspect ratio. FailureUI is wired to failureController,
    /// failureRecoveryController, and now levelExitFlowController — the same
    /// single Exit implementation the top HUD/Pause modal already share, per
    /// this task's own "reuse existing level-exit flow" requirement. Retry
    /// is intentionally not created — see FailureUI's own remarks on
    /// retryButton staying null-safe. Reuses the existing EventSystem
    /// created for Victory — one EventSystem serves every Canvas in the
    /// scene, so no second one is created here. sortingOrder 10 (matching
    /// GameplayModalCanvas) so this panel's own full-screen backdrop
    /// reliably blocks clicks to the top HUD underneath it, exactly like the
    /// Exit/Pause modal system.
    /// </summary>
    private static void CreateFailureUI(FailureController failureController, FailureRecoveryController failureRecoveryController, LevelExitFlowController levelExitFlowController)
    {
        var canvasObject = new GameObject(
            "FailureCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(FailureUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        GameObject panel = CreateModalBackdrop(canvasObject.transform, "FailurePanel");

        var levelFailedModalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LevelFailedModalSpritePath);
        if (levelFailedModalSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{LevelFailedModalSpritePath}'; the Level Failed modal will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Transform content = CreateResponsiveModalArtworkBox(
            panel.transform,
            levelFailedModalSprite,
            ConfirmationModalWidthFraction,
            LevelFailedModalMaxWidth,
            ConfirmationModalVerticalOffsetFraction,
            ConfirmationModalContentReferenceWidth);

        var fredokaSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaSemiBoldFontAssetPath);
        if (fredokaSemiBold == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaSemiBoldFontAssetPath}'; the Level Failed modal's title/headings/button labels will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        var fredokaMedium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaMediumFontAssetPath);
        if (fredokaMedium == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaMediumFontAssetPath}'; the Level Failed modal's description/caption text will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        CreateAutoSizedCenteredTMPText(content, "No space left!", new Vector2(0f, LevelFailedTitleOffsetY), new Vector2(800f, 130f), LevelFailedTitleFontSizeMin, LevelFailedTitleFontSizeMax, LevelFailedTitleColor, fredokaSemiBold);
        CreateCenteredTMPText(content, "The waiting line is full.", new Vector2(0f, LevelFailedDescriptionOffsetY), new Vector2(840f, 50f), ConfirmationModalDescriptionFontSize, ConfirmationModalDescriptionColor, fredokaMedium);
        CreateCenteredTMPText(content, "Use a booster:", new Vector2(0f, LevelFailedBoosterHeadingOffsetY), new Vector2(260f, 44f), LevelFailedHeadingFontSize, ConfirmationModalDescriptionColor, fredokaSemiBold);
        CreateBoosterHeadingDecoration(content, LevelFailedBoosterHeadingOffsetY, fredokaMedium);

        Sprite circleSprite = GenerateCircleSprite();
        Sprite dashedRingSprite = GenerateDashedRingSprite();
        Sprite starSprite = GenerateStarSprite();
        for (int i = 0; i < LevelFailedBoosterSlotCount; i++)
        {
            float offsetX = (i - 1) * LevelFailedBoosterSlotSpacing;
            CreateBoosterPlaceholderSlot(content, $"BoosterSlot{i + 1}", new Vector2(offsetX, LevelFailedBoosterRowOffsetY), circleSprite, dashedRingSprite);
        }

        CreateSeparator1(content, circleSprite, starSprite);
        CreateCenteredTMPText(content, "One more chance?", new Vector2(0f, LevelFailedSecondChanceHeadingOffsetY), new Vector2(700f, 46f), LevelFailedHeadingFontSize, ConfirmationModalDescriptionColor, fredokaSemiBold);

        var adIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AdIconSpritePath);
        if (adIconSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{AdIconSpritePath}'; the Level Failed modal's ad card will show no icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        CreateAdStatusCard(content, adIconSprite, fredokaSemiBold, fredokaMedium);

        var primaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PrimaryButtonSpritePath);
        if (primaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{PrimaryButtonSpritePath}'; the Continue button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var secondaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SecondaryButtonSpritePath);
        if (secondaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{SecondaryButtonSpritePath}'; the Exit level button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Vector2 continueButtonSize = ResolveButtonArtworkSize(primaryButtonSprite, LevelFailedButtonHeight);
        Vector2 exitButtonSize = ResolveButtonArtworkSize(secondaryButtonSprite, LevelFailedButtonHeight);

        Button continueButton = CreateDialogButton(content, "ContinueButton", "Continue", new Vector2(0f, LevelFailedContinueButtonOffsetY), continueButtonSize, LevelFailedButtonLabelFontSize, primaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);
        CreateSeparator2(content, circleSprite);
        Button exitLevelButton = CreateDialogButton(content, "ExitLevelButton", "Exit level", new Vector2(0f, LevelFailedExitButtonOffsetY), exitButtonSize, LevelFailedButtonLabelFontSize, secondaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);

        var failureUI = canvasObject.GetComponent<FailureUI>();
        var serializedFailureUI = new SerializedObject(failureUI);
        serializedFailureUI.FindProperty("failureController").objectReferenceValue = failureController;
        serializedFailureUI.FindProperty("failureRecoveryController").objectReferenceValue = failureRecoveryController;
        serializedFailureUI.FindProperty("levelExitFlowController").objectReferenceValue = levelExitFlowController;
        serializedFailureUI.FindProperty("panel").objectReferenceValue = panel;
        serializedFailureUI.FindProperty("continueButton").objectReferenceValue = continueButton;
        serializedFailureUI.FindProperty("exitLevelButton").objectReferenceValue = exitLevelButton;
        serializedFailureUI.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Booster placeholder: the tinted fill circle plus a dashed ring overlay at the same size/position — two stacked Images (see GenerateCircleSprite/GenerateDashedRingSprite), both children of parent, so they move together as one slot.</summary>
    private static void CreateBoosterPlaceholderSlot(Transform parent, string name, Vector2 anchoredPosition, Sprite circleSprite, Sprite dashedRingSprite)
    {
        var slotObject = new GameObject(name, typeof(RectTransform));
        slotObject.transform.SetParent(parent, false);

        var slotRect = (RectTransform)slotObject.transform;
        slotRect.anchorMin = new Vector2(0.5f, 0.5f);
        slotRect.anchorMax = new Vector2(0.5f, 0.5f);
        slotRect.pivot = new Vector2(0.5f, 0.5f);
        slotRect.sizeDelta = new Vector2(LevelFailedBoosterSlotDiameter, LevelFailedBoosterSlotDiameter);
        slotRect.anchoredPosition = anchoredPosition;

        CreateFullRectImage(slotRect, "Fill", circleSprite, LevelFailedBoosterPlaceholderColor);
        CreateFullRectImage(slotRect, "Border", dashedRingSprite, LevelFailedBoosterBorderColor);
    }

    /// <summary>Child Image stretched to fill the given parent RectTransform exactly — used to stack a booster slot's fill/border as two same-sized layers without repeating anchor/size boilerplate.</summary>
    private static Image CreateFullRectImage(RectTransform parent, string name, Sprite sprite, Color color)
    {
        var imageObject = new GameObject(name, typeof(Image));
        imageObject.transform.SetParent(parent, false);

        var rectTransform = imageObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        var image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private static void CreateHorizontalSeparator(Transform parent, Vector2 anchoredPosition, float width, float thickness, Color color)
    {
        var separatorObject = new GameObject("Separator", typeof(Image));
        separatorObject.transform.SetParent(parent, false);

        var rectTransform = separatorObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(width, thickness);
        rectTransform.anchoredPosition = anchoredPosition;

        var image = separatorObject.GetComponent<Image>();
        image.color = color;
    }

    private static void CreateDot(Transform parent, Vector2 anchoredPosition, float diameter, Sprite circleSprite, Color color)
    {
        var dotObject = new GameObject("Dot", typeof(Image));
        dotObject.transform.SetParent(parent, false);

        var rectTransform = dotObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(diameter, diameter);
        rectTransform.anchoredPosition = anchoredPosition;

        var image = dotObject.GetComponent<Image>();
        image.sprite = circleSprite;
        image.color = color;
    }

    /// <summary>
    /// Flanking decoration around "Use a booster:" — short line + ">>" on
    /// the left, "&lt;&lt;" + short line on the right, both pointing inward
    /// (see LevelFailedBoosterHeadingChevronOffsetX's remarks).
    /// </summary>
    private static void CreateBoosterHeadingDecoration(Transform parent, float rowOffsetY, TMP_FontAsset fredokaMedium)
    {
        CreateHorizontalSeparator(parent, new Vector2(-LevelFailedBoosterHeadingLineOffsetX, rowOffsetY), LevelFailedBoosterHeadingLineWidth, LevelFailedSeparatorThickness, LevelFailedSeparatorColor);
        CreateHorizontalSeparator(parent, new Vector2(LevelFailedBoosterHeadingLineOffsetX, rowOffsetY), LevelFailedBoosterHeadingLineWidth, LevelFailedSeparatorThickness, LevelFailedSeparatorColor);
        CreateCenteredTMPText(parent, ">>", new Vector2(-LevelFailedBoosterHeadingChevronOffsetX, rowOffsetY), new Vector2(60f, 40f), LevelFailedBoosterHeadingChevronFontSize, ConfirmationModalDescriptionColor, fredokaMedium);
        CreateCenteredTMPText(parent, "<<", new Vector2(LevelFailedBoosterHeadingChevronOffsetX, rowOffsetY), new Vector2(60f, 40f), LevelFailedBoosterHeadingChevronFontSize, ConfirmationModalDescriptionColor, fredokaMedium);
    }

    /// <summary>
    /// First decorative separator (see LevelFailedSeparator1OffsetY's
    /// remarks): a dotted line on each side of a small centred star
    /// (GenerateStarSprite), matching the reference's own star-in-the-middle
    /// treatment.
    /// </summary>
    private static void CreateSeparator1(Transform parent, Sprite circleSprite, Sprite starSprite)
    {
        float centerHalfGap = LevelFailedSeparator1CenterGap / 2f;
        int dotsPerSegment = Mathf.Max(1, Mathf.RoundToInt(LevelFailedSeparator1SegmentWidth / LevelFailedSeparator1DotSpacing));

        for (int side = -1; side <= 1; side += 2)
        {
            float segmentStart = side * centerHalfGap;
            for (int i = 0; i < dotsPerSegment; i++)
            {
                float t = (i + 0.5f) / dotsPerSegment;
                float x = segmentStart + side * t * LevelFailedSeparator1SegmentWidth;
                CreateDot(parent, new Vector2(x, LevelFailedSeparator1OffsetY), LevelFailedSeparator1DotDiameter, circleSprite, LevelFailedSeparator1DotColor);
            }
        }

        CreateDot(parent, new Vector2(0f, LevelFailedSeparator1OffsetY), LevelFailedSeparator1CenterStarDiameter, starSprite, LevelFailedSeparator1DotColor);
    }

    /// <summary>
    /// Second decorative separator, between Continue and Exit level (see
    /// LevelFailedSeparator2OffsetY's remarks): two short solid lines
    /// flanking a small centred dot.
    /// </summary>
    private static void CreateSeparator2(Transform parent, Sprite circleSprite)
    {
        CreateHorizontalSeparator(parent, new Vector2(-LevelFailedSeparator2LineOffsetX, LevelFailedSeparator2OffsetY), LevelFailedSeparator2LineWidth, LevelFailedSeparatorThickness, LevelFailedSeparatorColor);
        CreateHorizontalSeparator(parent, new Vector2(LevelFailedSeparator2LineOffsetX, LevelFailedSeparator2OffsetY), LevelFailedSeparator2LineWidth, LevelFailedSeparatorThickness, LevelFailedSeparatorColor);
        CreateDot(parent, new Vector2(0f, LevelFailedSeparator2OffsetY), LevelFailedSeparator2DotDiameter, circleSprite, LevelFailedSeparator1DotColor);
    }

    /// <summary>
    /// AdContent group: a pure-layout container (RectTransform only, no
    /// Image/visible background at all — see LevelFailedAdContentOffsetY's
    /// remarks) with AdIcon.png and both text lines as its OWN children,
    /// positioned relative to the container's own centre — never content or
    /// the screen — so the icon/count/caption always move together as one
    /// compact, horizontally-centered composition (icon left, text column
    /// right) and the icon can never drift relative to its text at any
    /// resolved modal size.
    /// </summary>
    private static void CreateAdStatusCard(Transform parent, Sprite adIconSprite, TMP_FontAsset fredokaSemiBold, TMP_FontAsset fredokaMedium)
    {
        var contentObject = new GameObject("AdContent", typeof(RectTransform));
        contentObject.transform.SetParent(parent, false);

        var contentRect = (RectTransform)contentObject.transform;
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(LevelFailedAdContentWidth, LevelFailedAdContentHeight);
        contentRect.anchoredPosition = new Vector2(0f, LevelFailedAdContentOffsetY);

        var iconObject = new GameObject("AdIcon", typeof(Image));
        iconObject.transform.SetParent(contentRect, false);

        float iconAspect = 1f;
        if (adIconSprite != null && adIconSprite.rect.height > 0f)
            iconAspect = adIconSprite.rect.width / adIconSprite.rect.height;

        var iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(LevelFailedAdIconHeight * iconAspect, LevelFailedAdIconHeight);
        iconRect.anchoredPosition = new Vector2(LevelFailedAdIconOffsetX, 0f);

        var iconImage = iconObject.GetComponent<Image>();
        iconImage.sprite = adIconSprite;
        iconImage.preserveAspect = true;

        CreateCenteredTMPText(
            contentRect,
            LevelFailedAdWatchCountLabel,
            new Vector2(LevelFailedAdTextOffsetX, LevelFailedAdCountOffsetY),
            new Vector2(320f, 70f),
            LevelFailedAdCountFontSize,
            LevelFailedTitleColor,
            fredokaSemiBold);

        CreateCenteredTMPText(
            contentRect,
            "Watch an ad to continue",
            new Vector2(LevelFailedAdTextOffsetX, LevelFailedAdCaptionOffsetY),
            new Vector2(320f, 40f),
            LevelFailedAdCaptionFontSize,
            ConfirmationModalDescriptionColor,
            fredokaMedium);
    }

    /// <summary>
    /// Procedurally draws a flat white circle into a new Texture2D via an
    /// analytic signed-distance field (~1px antialiased edge) — a generic,
    /// content-free UI primitive shape, the same category of thing as
    /// EnergyBarPrefabBuilder's own GenerateStadiumSprite (a rounded-box SDF;
    /// this is its simpler fully-round special case) and not "art" in the
    /// sense of this task's "do not create new artwork" constraint. Reused,
    /// tinted differently and at different RectTransform sizes, for the
    /// booster fill, both separators' dots, and the second separator's
    /// centre dot — one shape, many uses, rather than a texture per use.
    ///
    /// Saved as its own small .asset (CircleSpriteAssetPath) rather than
    /// left as an in-memory-only Sprite, for the same reason
    /// GenerateStadiumSprite's own remarks give: a referenced-but-never-
    /// persisted Sprite/Texture2D silently serializes as a null reference
    /// once the scene is saved. Deleting and recreating this asset on every
    /// run keeps this tool idempotent/re-runnable like the rest of this
    /// file's own build steps.
    /// </summary>
    private static Sprite GenerateCircleSprite()
    {
        var texture = new Texture2D(CircleTextureDiameter, CircleTextureDiameter, TextureFormat.RGBA32, false)
        {
            name = "CircleSprite",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        float radius = CircleTextureDiameter / 2f;
        var pixels = new Color32[CircleTextureDiameter * CircleTextureDiameter];
        for (int y = 0; y < CircleTextureDiameter; y++)
        {
            for (int x = 0; x < CircleTextureDiameter; x++)
            {
                float px = x + 0.5f - radius;
                float py = y + 0.5f - radius;
                float distance = Mathf.Sqrt(px * px + py * py) - radius;
                float alpha = Mathf.Clamp01(0.5f - distance);
                pixels[y * CircleTextureDiameter + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        return SaveGeneratedSprite(texture, pixels, CircleSpriteAssetPath, "CircleSprite");
    }

    /// <summary>
    /// Procedurally draws a dashed ring (an annulus split into
    /// LevelFailedBoosterBorderDashCount arc segments via an angular duty
    /// cycle) into a new Texture2D — the booster placeholders' "subtle
    /// dashed/dotted circular border", layered on top of GenerateCircleSprite's
    /// solid fill at the same RectTransform size (see CreateBoosterPlaceholderSlot).
    /// Same SDF-plus-antialiasing approach as GenerateCircleSprite, extended
    /// with an angular mask; not "art" for the same reason that one isn't.
    /// </summary>
    private static Sprite GenerateDashedRingSprite()
    {
        var texture = new Texture2D(CircleTextureDiameter, CircleTextureDiameter, TextureFormat.RGBA32, false)
        {
            name = "BoosterDashedRing",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        float radius = CircleTextureDiameter / 2f;
        float thickness = radius * LevelFailedBoosterBorderThicknessFraction;
        float ringCenterRadius = radius - thickness * 0.5f;
        var pixels = new Color32[CircleTextureDiameter * CircleTextureDiameter];
        for (int y = 0; y < CircleTextureDiameter; y++)
        {
            for (int x = 0; x < CircleTextureDiameter; x++)
            {
                float px = x + 0.5f - radius;
                float py = y + 0.5f - radius;
                float distFromCenter = Mathf.Sqrt(px * px + py * py);
                float ringDistance = Mathf.Abs(distFromCenter - ringCenterRadius) - thickness * 0.5f;
                float bandAlpha = Mathf.Clamp01(0.5f - ringDistance);

                byte alpha = 0;
                if (bandAlpha > 0f)
                {
                    float angle01 = Mathf.Atan2(py, px) / (Mathf.PI * 2f) + 0.5f;
                    float dashPhase = (angle01 * LevelFailedBoosterBorderDashCount) % 1f;
                    if (dashPhase < LevelFailedBoosterBorderDashDutyCycle)
                        alpha = (byte)(bandAlpha * 255f);
                }

                pixels[y * CircleTextureDiameter + x] = new Color32(255, 255, 255, alpha);
            }
        }

        return SaveGeneratedSprite(texture, pixels, BoosterDashedRingSpriteAssetPath, "BoosterDashedRing");
    }

    /// <summary>
    /// Procedurally draws a flat white 5-point star into a new Texture2D via
    /// Inigo Quilez's analytic star signed-distance field (sdStar5 —
    /// https://iquilezles.org/articles/distfunctions2d/), the same public
    /// SDF category EnergyBarPrefabBuilder's own rounded-box SDF already
    /// draws on for this project's "generate a flat UI primitive shape
    /// procedurally, not art" convention. Used for the first decorative
    /// separator's centre element (see CreateSeparator1), replacing a plain
    /// dot with the reference's own star treatment — verified against a
    /// standalone Python/PIL render of the same formula before porting it
    /// here, rather than shipped unverified.
    /// </summary>
    private static Sprite GenerateStarSprite()
    {
        var texture = new Texture2D(CircleTextureDiameter, CircleTextureDiameter, TextureFormat.RGBA32, false)
        {
            name = "StarSprite",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        float outerRadius = CircleTextureDiameter * 0.42f;
        const float innerRadiusRatio = 0.5f;
        var pixels = new Color32[CircleTextureDiameter * CircleTextureDiameter];
        for (int y = 0; y < CircleTextureDiameter; y++)
        {
            for (int x = 0; x < CircleTextureDiameter; x++)
            {
                float px = x + 0.5f - CircleTextureDiameter / 2f;
                float py = y + 0.5f - CircleTextureDiameter / 2f;
                float distance = SignedDistanceStar5(px, py, outerRadius, innerRadiusRatio);
                float alpha = Mathf.Clamp01(0.5f - distance);
                pixels[y * CircleTextureDiameter + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        return SaveGeneratedSprite(texture, pixels, StarSpriteAssetPath, "StarSprite");
    }

    /// <summary>See GenerateStarSprite's remarks — Inigo Quilez's sdStar5 formula, transcribed directly (k1/k2 are its own fixed constants for a regular 5-point star, not tunable parameters).</summary>
    private static float SignedDistanceStar5(float px, float py, float outerRadius, float innerRadiusRatio)
    {
        const float k1X = 0.809016994375f;
        const float k1Y = -0.587785252292f;
        const float k2X = -k1X;
        const float k2Y = k1Y;

        px = Mathf.Abs(px);
        float d = k1X * px + k1Y * py;
        if (d > 0f)
        {
            px -= 2f * d * k1X;
            py -= 2f * d * k1Y;
        }

        d = k2X * px + k2Y * py;
        if (d > 0f)
        {
            px -= 2f * d * k2X;
            py -= 2f * d * k2Y;
        }

        px = Mathf.Abs(px);
        py -= outerRadius;

        float baX = innerRadiusRatio * -k1Y;
        float baY = innerRadiusRatio * k1X - 1f;
        float h = Mathf.Clamp((px * baX + py * baY) / (baX * baX + baY * baY), 0f, outerRadius);
        float dx = px - baX * h;
        float dy = py - baY * h;
        float sign = (py * baX - px * baY) < 0f ? 1f : -1f;
        return Mathf.Sqrt(dx * dx + dy * dy) * sign;
    }

    /// <summary>Shared tail end of every GenerateXSprite method above: bakes pixels into the texture, (re)persists it as its own small .asset (see GenerateCircleSprite's remarks on why persistence matters), and returns the reloaded Sprite sub-asset.</summary>
    private static Sprite SaveGeneratedSprite(Texture2D texture, Color32[] pixels, string assetPath, string spriteName)
    {
        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 100f);
        sprite.name = spriteName;

        string directory = Path.GetDirectoryName(assetPath);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);

        AssetDatabase.CreateAsset(texture, assetPath);
        AssetDatabase.AddObjectToAsset(sprite, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
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

    /// <summary>
    /// TextMeshPro counterpart to CreateCenteredText, used only where a
    /// specific TMP_FontAsset (e.g. Fredoka) must be assigned explicitly —
    /// the Exit confirmation, Pause, and Level Failed modals' own
    /// title/description/heading/button-label text. Deliberately a separate
    /// helper rather than converting CreateCenteredText itself, so every
    /// other caller (Victory/Failure panels' own legacy text, top HUD) keeps
    /// rendering with the legacy UI.Text/default font exactly as before.
    /// </summary>
    private static void CreateCenteredTMPText(Transform parent, string content, Vector2 anchoredPosition, Vector2 size, float fontSize, Color color, TMP_FontAsset font)
    {
        var textObject = new GameObject("Text", typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        var rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = content;
        if (font != null)
            text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
    }

    /// <summary>
    /// CreateCenteredTMPText's auto-sizing counterpart, used only by the
    /// Level Failed modal's title (see LevelFailedTitleFontSizeMin's
    /// remarks) — TMP's enableAutoSizing solves for the largest font size in
    /// [fontSizeMin, fontSizeMax] that still fits the given size, the same
    /// technique EnergyBarPrefabBuilder.BuildValueText already uses.
    /// Word-wrap is explicitly disabled (TextWrappingModes.NoWrap, the
    /// current non-obsolete TMP API — not the deprecated
    /// enableWordWrapping bool) so a too-narrow box shrinks the single line
    /// instead of wrapping it to two.
    /// </summary>
    private static void CreateAutoSizedCenteredTMPText(Transform parent, string content, Vector2 anchoredPosition, Vector2 size, float fontSizeMin, float fontSizeMax, Color color, TMP_FontAsset font)
    {
        var textObject = new GameObject("Text", typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        var rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = content;
        if (font != null)
            text.font = font;
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSizeMin;
        text.fontSizeMax = fontSizeMax;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Center;
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
    /// TopHudUI's own Exit entry point/ExitConfirmationUI.Open; Pause's MVP
    /// composition currently has no Exit Level button of its own — see
    /// CreatePausePanel) — rather than two separate canvases, since both
    /// share the same "modal above the HUD" sorting concern. PauseUI/ExitConfirmationUI live on this canvas
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

    /// <summary>
    /// MVP Pause composition per reference/UI/PauseModalTarget.png: title,
    /// two-line description, single Continue button — reusing the exact
    /// same ConfirmationModal.png artwork/ResponsiveModalBox sizing and
    /// title/description styling as CreateExitConfirmPanel (see the shared
    /// ConfirmationModal* constants) rather than a second, unrelated set of
    /// magic numbers, so Pause and Exit read as the same physical panel.
    /// Deliberately does not create an Exit Level button — leaving
    /// PauseUI's exitLevelButton field unassigned (null) below is enough,
    /// since PauseUI already null-checks it in Awake/OnExitLevelPressed;
    /// the top HUD's own Exit entry point (TopHudUI.OnBackPressed ->
    /// ExitConfirmationUI) is unaffected and still reachable independently
    /// of Pause. This is a scene-composition choice, not a PauseUI.cs
    /// behavior change — that file is untouched.
    /// </summary>
    private static void CreatePausePanel(Transform parent, PauseUI pauseUI, PauseFlowController pauseFlowController, ExitConfirmationUI exitConfirmationUI)
    {
        GameObject panel = CreateModalBackdrop(parent, "PausePanel");

        var confirmationModalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ConfirmationModalSpritePath);
        if (confirmationModalSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{ConfirmationModalSpritePath}'; the Pause modal will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Transform content = CreateResponsiveModalArtworkBox(
            panel.transform,
            confirmationModalSprite,
            ConfirmationModalWidthFraction,
            ConfirmationModalMaxWidth,
            ConfirmationModalVerticalOffsetFraction,
            ConfirmationModalContentReferenceWidth);

        var fredokaSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaSemiBoldFontAssetPath);
        if (fredokaSemiBold == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaSemiBoldFontAssetPath}'; the Pause modal's title/button label will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        var fredokaMedium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaMediumFontAssetPath);
        if (fredokaMedium == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaMediumFontAssetPath}'; the Pause modal's description will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        // Positions/sizes below are authored against
        // ConfirmationModalContentReferenceWidth and placed under content —
        // see ExitConfirmButtonHeight's remarks for the shared convention.
        // Proportions were measured directly off
        // reference/UI/PauseModalTarget.png.
        CreateCenteredTMPText(content, "Paused", new Vector2(0f, PauseTitleOffsetY), new Vector2(840f, 95f), ConfirmationModalTitleFontSize, ConfirmationModalTitleColor, fredokaSemiBold);
        CreateCenteredTMPText(content, "Take a moment,\nthen get back to it!", new Vector2(0f, PauseDescriptionOffsetY), new Vector2(840f, 80f), ConfirmationModalDescriptionFontSize, ConfirmationModalDescriptionColor, fredokaMedium);

        var primaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PrimaryButtonSpritePath);
        if (primaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{PrimaryButtonSpritePath}'; the Continue button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Vector2 continueButtonSize = ResolveButtonArtworkSize(primaryButtonSprite, PauseButtonHeight);

        Button continueButton = CreateDialogButton(content, "ContinueButton", "Continue", new Vector2(0f, PauseButtonOffsetY), continueButtonSize, PauseButtonLabelFontSize, primaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);

        var serializedPauseUI = new SerializedObject(pauseUI);
        serializedPauseUI.FindProperty("panel").objectReferenceValue = panel;
        serializedPauseUI.FindProperty("continueButton").objectReferenceValue = continueButton;
        serializedPauseUI.FindProperty("pauseFlowController").objectReferenceValue = pauseFlowController;
        serializedPauseUI.FindProperty("exitConfirmationUI").objectReferenceValue = exitConfirmationUI;
        serializedPauseUI.ApplyModifiedPropertiesWithoutUndo();
    }

    private const string ConfirmationModalSpritePath = "Assets/Art/UI/Classic/ConfirmationModal.png";

    /// <summary>
    /// Reference-resolution width this project's UI is authored/reviewed
    /// against everywhere else (see GameplayLayout, CharacterVerification,
    /// PixelGridScalingVerification's own 1080x1920 portrait comments).
    /// Used only to seed DialogBox's initial baked sizeDelta.y so the saved
    /// scene shows a sensible value before the panel is ever activated —
    /// AspectRatioFitter recomputes the real height live from the
    /// RectTransform's actual resolved width the moment the panel becomes
    /// active (ExitConfirmationUI.Open), so this constant never hardcodes
    /// the runtime size.
    /// </summary>
    private const float ExitConfirmReferenceCanvasWidth = 1080f;

    /// <summary>
    /// Target fraction of the available Canvas/backdrop width a
    /// ConfirmationModal.png-backed modal occupies — not a fixed pixel
    /// width, and not the final width by itself either: ResponsiveModalBox
    /// (attached by CreateResponsiveModalArtworkBox) resolves the actual
    /// width every frame as min(availableWidth * this fraction,
    /// ConfirmationModalMaxWidth), so the modal tracks whatever the Canvas
    /// actually renders at on a phone, but stops growing past the ceiling
    /// on a wider/tablet screen. Shared by the Exit confirmation and Pause
    /// panels (see CreateExitConfirmPanel/CreatePausePanel) so both read as
    /// the same physical panel rather than each authoring its own sizing.
    /// 0.90 keeps portrait phones at ~88-90% of their width (unclamped —
    /// see ConfirmationModalMaxWidth's own remarks for where the ceiling
    /// starts to bite).
    /// </summary>
    private const float ConfirmationModalWidthFraction = 0.90f;

    /// <summary>
    /// Absolute ceiling on a ConfirmationModal.png-backed modal's width, in
    /// the same units as ExitConfirmReferenceCanvasWidth (this project's
    /// Canvas is left at CanvasScaler's default Constant Pixel Size, so
    /// those units are actual screen pixels) — shared by Exit and Pause,
    /// see ConfirmationModalWidthFraction's remarks. Below ~1556px-wide
    /// canvases (every realistic phone, portrait or landscape-ish tall)
    /// this never engages and ConfirmationModalWidthFraction alone decides
    /// the width; above it — iPad portrait's 1668px being the case this
    /// project must support — this caps the modal instead of letting it
    /// keep growing with the screen. 1400 is chosen so the iPad-portrait
    /// result (1400/1668 ≈ 84% of screen width) visually resembles
    /// reference/UI/ExitConfirmationTarget.png's own proportions (measured
    /// directly off that reference image: the modal there occupies ~83% of
    /// its iPad-portrait screenshot's width), rather than an arbitrary
    /// number.
    /// </summary>
    private const float ConfirmationModalMaxWidth = 1400f;

    /// <summary>
    /// The contentRoot width every modal's fixed pixel positions/sizes are
    /// authored against — specifically a ConfirmationModal.png-backed
    /// modal's own width on a 1080-wide portrait canvas (this project's
    /// authoring reference — see ExitConfirmReferenceCanvasWidth) at
    /// ConfirmationModalWidthFraction, i.e. 1080 * 0.90. ResponsiveModalBox
    /// scales contentRoot by (resolvedWidth / this constant) every frame,
    /// so those fixed values stay in the same visual proportion to the
    /// modal at any resolved width — including the clamped tablet case —
    /// instead of only the artwork box resizing while its contents stayed a
    /// fixed pixel size. Shared by Exit and Pause: both use the identical
    /// artwork/sizing formula, so both resolve to the same box at any given
    /// canvas size.
    /// </summary>
    private const float ConfirmationModalContentReferenceWidth = 972f;

    /// <summary>
    /// Fraction of the available Canvas/backdrop height a
    /// ConfirmationModal.png-backed modal (artwork + all its content, since
    /// they're children of DialogBox and move with it) is nudged upward
    /// from exact geometric vertical center, for a better optical center —
    /// expressed the same way ConfirmationModalWidthFraction expresses
    /// horizontal size: a fraction of the live Canvas, not a fixed pixel
    /// offset, so it stays "~100px at 1080x1920" proportionally on any
    /// resolution. Shared by Exit and Pause, per the "consistent with the
    /// finished Exit-modal positioning philosophy" requirement. 100 / 1920
    /// (this project's 1080x1920 portrait reference height) ≈ 5.2%, within
    /// the requested 5-6% band.
    /// </summary>
    private const float ConfirmationModalVerticalOffsetFraction = 100f / 1920f;

    private const string PrimaryButtonSpritePath = "Assets/Art/UI/Classic/PrimaryButton.png";
    private const string SecondaryButtonSpritePath = "Assets/Art/UI/Classic/SecondaryButton.png";

    /// <summary>
    /// Generated by FredokaFontAssetBuilder (Tools/UI/Generate Fredoka Font
    /// Assets) from the source .ttf files under the same folder. Loaded by
    /// path and assigned explicitly to this modal's title/button labels
    /// (SemiBold) and description (Medium) — never set as a global TMP
    /// Settings default font, so no other TMP text in the project is
    /// affected by these two assets existing.
    /// </summary>
    private const string FredokaSemiBoldFontAssetPath = "Assets/Art/UI/Fonts/Fredoka/Fredoka-SemiBold SDF.asset";
    private const string FredokaMediumFontAssetPath = "Assets/Art/UI/Fonts/Fredoka/Fredoka-Medium SDF.asset";

    /// <summary>
    /// All fixed pixel positions/sizes on both the Exit confirmation and
    /// Pause panels (this file's ExitConfirm*/Pause* offset and size
    /// constants, plus the shared ConfirmationModalTitleFontSize/
    /// ConfirmationModalDescriptionFontSize/color constants below) are
    /// authored against ConfirmationModalContentReferenceWidth/its implied
    /// height (contentReferenceWidth / the artwork's own aspect ratio) and
    /// live under contentRoot, which ResponsiveModalBox scales uniformly to
    /// match each modal's own actual resolved width — see that constant's
    /// and ResponsiveModalBox's own remarks. Proportions (offsets/sizes as
    /// a fraction of that reference box) were measured directly off
    /// reference/UI/ExitConfirmationTarget.png and reference/UI/PauseModalTarget.png
    /// respectively, rather than guessed, then converted to fixed pixels at
    /// this one shared reference width — this is what lets Pause reuse
    /// Exit's sizing infrastructure instead of inventing its own unrelated
    /// set of magic numbers.
    ///
    /// This one specifically: fixed footprint height shared by the Stay/Exit
    /// button artwork. Each button's width is then derived per-sprite from
    /// its own native aspect ratio (see ResolveButtonArtworkSize), never a
    /// shared fixed width, so PrimaryButton.png and SecondaryButton.png are
    /// each shown at their true proportions even though their aspect ratios
    /// differ slightly. Pause's single Continue button intentionally uses
    /// its own, taller PauseButtonHeight instead of this one — see its
    /// remarks — so it reads as more prominent than one individual
    /// half-width Exit button, per PauseModalTarget.png.
    /// </summary>
    private const float ExitConfirmButtonHeight = 118f;

    /// <summary>
    /// Horizontal offset (from center) of each Exit button's
    /// anchoredPosition — see ExitConfirmButtonHeight's remarks for the
    /// shared authoring reference. The pair stays symmetric about x=0
    /// (horizontally centered as a pair). Pause has only one button, so it
    /// has no equivalent constant — see PauseButtonOffsetY.
    /// </summary>
    private const float ExitConfirmButtonHorizontalOffset = 189f;

    /// <summary>Shared vertical anchoredPosition for both Exit buttons (see ExitConfirmButtonHeight's remarks).</summary>
    private const float ExitConfirmButtonOffsetY = -116f;

    /// <summary>Font size for the Stay/Exit button labels (see ExitConfirmButtonHeight's remarks). Pause's Continue label is bigger — see PauseButtonLabelFontSize.</summary>
    private const int ExitConfirmButtonLabelFontSize = 38;

    /// <summary>
    /// Light cream/off-white label color shared by every button on both
    /// modals (Stay/Exit/Continue) — each button's own artwork (sage green
    /// / warm orange) is dark enough that white or near-white text is what
    /// reference/UI/ExitConfirmationTarget.png and reference/UI/PauseModalTarget.png
    /// both use for readable contrast; this is deliberately not reused for
    /// the title/description, which sit on the light cream panel body
    /// instead.
    /// </summary>
    private static readonly Color ConfirmationModalButtonLabelColor = new Color(1f, 250f / 255f, 240f / 255f);

    /// <summary>Vertical anchoredPosition of the "Exit level?" title (see ExitConfirmButtonHeight's remarks). Pause's own title offset is PauseTitleOffsetY.</summary>
    private const float ExitConfirmTitleOffsetY = 124f;

    /// <summary>Font size shared by the "Exit level?" and "Paused" titles — each modal's strongest text element (see ExitConfirmButtonHeight's remarks).</summary>
    private const float ConfirmationModalTitleFontSize = 76f;

    /// <summary>
    /// Dark muted sage/green title color belonging to this UI's existing
    /// palette (sampled off reference/UI/ExitConfirmationTarget.png, and
    /// confirmed against reference/UI/PauseModalTarget.png's own title) —
    /// shared by both modals' titles, deliberately not white, which would
    /// sit poorly on the cream panel.
    /// </summary>
    private static readonly Color ConfirmationModalTitleColor = new Color(72f / 255f, 85f / 255f, 42f / 255f);

    /// <summary>Vertical anchoredPosition of the Exit description line (see ExitConfirmButtonHeight's remarks). Pause's own description offset is PauseDescriptionOffsetY.</summary>
    private const float ExitConfirmDescriptionOffsetY = 30f;

    /// <summary>Font size shared by the Exit and Pause descriptions — clearly smaller than the title, still comfortably readable (see ExitConfirmButtonHeight's remarks).</summary>
    private const float ConfirmationModalDescriptionFontSize = 32f;

    /// <summary>
    /// Soft muted neutral/sage-brown description color (sampled off
    /// reference/UI/ExitConfirmationTarget.png, confirmed against
    /// reference/UI/PauseModalTarget.png) — shared by both modals'
    /// descriptions; enough contrast against the cream panel without the
    /// harder contrast of the title color.
    /// </summary>
    private static readonly Color ConfirmationModalDescriptionColor = new Color(155f / 255f, 146f / 255f, 127f / 255f);

    /// <summary>Vertical anchoredPosition of the "Paused" title (see ExitConfirmButtonHeight's remarks for the shared authoring reference).</summary>
    private const float PauseTitleOffsetY = 132f;

    /// <summary>Vertical anchoredPosition of the (two-line) Pause description block, centered as a whole via TMP's own vertical-middle alignment across both lines.</summary>
    private const float PauseDescriptionOffsetY = 21f;

    /// <summary>
    /// Fixed footprint height for the single Continue button — deliberately
    /// its own constant rather than ExitConfirmButtonHeight, taller so the
    /// sprite-aspect-derived width comes out noticeably wider than one
    /// individual Exit button (per PauseModalTarget.png's single, prominent
    /// action button — see ResolveButtonArtworkSize, which derives width
    /// from this height and PrimaryButton.png's own aspect ratio, never an
    /// independent width).
    /// </summary>
    private const float PauseButtonHeight = 175f;

    /// <summary>Vertical anchoredPosition of the Continue button.</summary>
    private const float PauseButtonOffsetY = -137f;

    /// <summary>
    /// Font size for the Continue label — bigger than ExitConfirmButtonLabelFontSize
    /// in the same proportion PauseButtonHeight is bigger than
    /// ExitConfirmButtonHeight, so the label keeps the same visual weight
    /// relative to its own (larger) button as Stay/Exit's labels do to
    /// theirs.
    /// </summary>
    private const int PauseButtonLabelFontSize = 56;

    private static void CreateExitConfirmPanel(Transform parent, ExitConfirmationUI exitConfirmationUI, LevelExitFlowController levelExitFlowController)
    {
        GameObject panel = CreateModalBackdrop(parent, "ExitConfirmPanel");

        var confirmationModalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ConfirmationModalSpritePath);
        if (confirmationModalSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{ConfirmationModalSpritePath}'; the Exit confirmation modal will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Transform content = CreateResponsiveModalArtworkBox(
            panel.transform,
            confirmationModalSprite,
            ConfirmationModalWidthFraction,
            ConfirmationModalMaxWidth,
            ConfirmationModalVerticalOffsetFraction,
            ConfirmationModalContentReferenceWidth);

        var fredokaSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaSemiBoldFontAssetPath);
        if (fredokaSemiBold == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaSemiBoldFontAssetPath}'; the Exit confirmation modal's title/button labels will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        var fredokaMedium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaMediumFontAssetPath);
        if (fredokaMedium == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaMediumFontAssetPath}'; the Exit confirmation modal's description will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        // Positions/sizes below are authored against contentReferenceWidth
        // (ConfirmationModalContentReferenceWidth) and placed under content, which
        // ResponsiveModalBox scales as a whole to match the modal's actual
        // resolved width — see ExitConfirmButtonHeight's remarks. They stay
        // comfortably inside the artwork's inner cream "safe" region
        // (measured from the source pixels, avoiding the rounded-corner/
        // top-tab decorative frame), following the same
        // fixed-pixel-at-a-reference-width convention already used
        // throughout this file's other UI (HUD icons, Pause panel, etc.).
        CreateCenteredTMPText(content, "Exit level?", new Vector2(0f, ExitConfirmTitleOffsetY), new Vector2(840f, 95f), ConfirmationModalTitleFontSize, ConfirmationModalTitleColor, fredokaSemiBold);
        CreateCenteredTMPText(content, "Your current progress will be lost.", new Vector2(0f, ExitConfirmDescriptionOffsetY), new Vector2(840f, 44f), ConfirmationModalDescriptionFontSize, ConfirmationModalDescriptionColor, fredokaMedium);

        var primaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PrimaryButtonSpritePath);
        if (primaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{PrimaryButtonSpritePath}'; the Stay button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var secondaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SecondaryButtonSpritePath);
        if (secondaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{SecondaryButtonSpritePath}'; the Exit button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Vector2 stayButtonSize = ResolveButtonArtworkSize(primaryButtonSprite, ExitConfirmButtonHeight);
        Vector2 exitButtonSize = ResolveButtonArtworkSize(secondaryButtonSprite, ExitConfirmButtonHeight);

        Button stayButton = CreateDialogButton(content, "StayButton", "Stay", new Vector2(-ExitConfirmButtonHorizontalOffset, ExitConfirmButtonOffsetY), stayButtonSize, ExitConfirmButtonLabelFontSize, primaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);
        Button exitButton = CreateDialogButton(content, "ExitButton", "Exit", new Vector2(ExitConfirmButtonHorizontalOffset, ExitConfirmButtonOffsetY), exitButtonSize, ExitConfirmButtonLabelFontSize, secondaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);

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

    /// <summary>
    /// Exit confirmation's dialog box, sized as min(availableWidth *
    /// widthFraction, maxWidth) rather than a fixed pixel number or an
    /// uncapped fraction — see ResponsiveModalBox, attached below, which
    /// owns that live computation every frame against availableAreaSource
    /// (ExitConfirmPanel, itself anchored to fill the Canvas — see
    /// CreateModalBackdrop) and also uniformly scales the returned content
    /// container to match. Anchors here are a single point (not a stretch)
    /// on both axes, since ResponsiveModalBox — not the anchor itself —
    /// is what's authoritative for sizeDelta.x; the vertical anchor is
    /// offset from the Canvas's exact vertical center (0.5) by
    /// verticalCenterOffsetFraction — a fraction of Canvas height, so the
    /// nudge scales the same responsive way the width does.
    ///
    /// AspectRatioFitter (WidthControlsHeight) derives the height live from
    /// whatever width ResponsiveModalBox resolves and the artwork's own
    /// pixel aspect ratio, so the art is never independently stretched on X
    /// or Y; Image.preserveAspect stays on as a second safeguard. sizeDelta
    /// is seeded with the same min(...) formula purely so the saved scene
    /// shows a sane value before the panel is ever activated (both
    /// ResponsiveModalBox and AspectRatioFitter only recompute once their
    /// GameObject is active, which happens on ExitConfirmationUI.Open) — it
    /// is not the authoritative runtime value.
    /// </summary>
    private static Transform CreateResponsiveModalArtworkBox(
        Transform parent,
        Sprite artwork,
        float widthFraction,
        float maxWidth,
        float verticalCenterOffsetFraction,
        float contentReferenceWidth)
    {
        var dialogObject = new GameObject("DialogBox", typeof(Image), typeof(AspectRatioFitter), typeof(ResponsiveModalBox));
        dialogObject.transform.SetParent(parent, false);

        float aspectRatio = 1.5f;
        if (artwork != null && artwork.rect.height > 0f)
            aspectRatio = artwork.rect.width / artwork.rect.height;

        float seededWidth = Mathf.Min(ExitConfirmReferenceCanvasWidth * widthFraction, maxWidth);
        float seededHeight = seededWidth / aspectRatio;
        float verticalAnchor = 0.5f + verticalCenterOffsetFraction;

        var rectTransform = dialogObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, verticalAnchor);
        rectTransform.anchorMax = new Vector2(0.5f, verticalAnchor);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(seededWidth, seededHeight);
        rectTransform.anchoredPosition = Vector2.zero;

        var image = dialogObject.GetComponent<Image>();
        image.sprite = artwork;
        image.color = Color.white;
        image.preserveAspect = true;

        var aspectRatioFitter = dialogObject.GetComponent<AspectRatioFitter>();
        aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        aspectRatioFitter.aspectRatio = aspectRatio;

        var contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(dialogObject.transform, false);
        var contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(contentReferenceWidth, contentReferenceWidth / aspectRatio);
        contentRect.anchoredPosition = Vector2.zero;

        var responsiveModalBox = dialogObject.GetComponent<ResponsiveModalBox>();
        var serializedResponsiveModalBox = new SerializedObject(responsiveModalBox);
        serializedResponsiveModalBox.FindProperty("availableAreaSource").objectReferenceValue = parent.GetComponent<RectTransform>();
        serializedResponsiveModalBox.FindProperty("contentRoot").objectReferenceValue = contentRect;
        serializedResponsiveModalBox.FindProperty("widthFraction").floatValue = widthFraction;
        serializedResponsiveModalBox.FindProperty("maxWidth").floatValue = maxWidth;
        serializedResponsiveModalBox.FindProperty("contentReferenceWidth").floatValue = contentReferenceWidth;
        serializedResponsiveModalBox.ApplyModifiedPropertiesWithoutUndo();

        return contentRect;
    }

    /// <summary>
    /// Derives a button's RectTransform size from its own background
    /// artwork's native pixel aspect ratio at a fixed height, so the sprite
    /// is never independently stretched on X or Y. Falls back to the
    /// previous flat-color button box's own ratio (254/76) if the sprite
    /// failed to load, so the error-logging call site above still lays out
    /// reasonably.
    /// </summary>
    private static Vector2 ResolveButtonArtworkSize(Sprite artwork, float height)
    {
        float aspectRatio = 254f / 76f;
        if (artwork != null && artwork.rect.height > 0f)
            aspectRatio = artwork.rect.width / artwork.rect.height;

        return new Vector2(height * aspectRatio, height);
    }

    /// <summary>
    /// size/fontSize default to the original fixed button footprint, and
    /// backgroundSprite/tmpFont/labelColor default to null/black leaving the
    /// flat white Image color and legacy UI.Text label untouched (identical
    /// output to before these parameters existed) so the Pause panel's
    /// Continue/Exit Level buttons are unaffected; the Exit confirmation
    /// panel passes its own explicit size (from ResolveButtonArtworkSize)/
    /// fontSize/backgroundSprite (PrimaryButton.png/SecondaryButton.png)/
    /// tmpFont (Fredoka SemiBold)/labelColor (light cream, for contrast
    /// against the buttons' own dark artwork) — supplying tmpFont switches
    /// the label from CreateCenteredText to CreateCenteredTMPText.
    /// </summary>
    private static Button CreateDialogButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2? size = null, int fontSize = 20, Sprite backgroundSprite = null, TMP_FontAsset tmpFont = null, Color? labelColor = null)
    {
        var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        Vector2 resolvedSize = size ?? new Vector2(200f, 64f);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = resolvedSize;
        rectTransform.anchoredPosition = anchoredPosition;

        var image = buttonObject.GetComponent<Image>();
        image.color = Color.white;
        if (backgroundSprite != null)
        {
            image.sprite = backgroundSprite;
            image.preserveAspect = true;
        }

        Color resolvedLabelColor = labelColor ?? Color.black;
        if (tmpFont != null)
            CreateCenteredTMPText(buttonObject.transform, label, Vector2.zero, resolvedSize, fontSize, resolvedLabelColor, tmpFont);
        else
            CreateCenteredText(buttonObject.transform, label, Vector2.zero, resolvedSize, fontSize, resolvedLabelColor);

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
