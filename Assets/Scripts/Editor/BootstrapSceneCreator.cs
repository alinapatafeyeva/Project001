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
using Project001.Services.Economy;
using Project001.UI.Failure;
using Project001.UI.Hud;
using Project001.UI.Store;
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
        CreateRecoveryLineLayoutController(recoveryRowController, collectorQueueBoard);
        CollectorSelectionController collectorSelectionController = CreateCollectorSelectionController(mainCamera, collectorQueueBoard, waitingLine, recoveryRowController, conveyorSystem);
        VictoryController victoryController = CreateVictoryController(pixelGrid);
        CoinWalletService coinWalletService = CreateCoinWalletService();
        LevelRewardController levelRewardController = CreateLevelRewardController(victoryController, coinWalletService);
        CreateCoinEconomyDebugTools(coinWalletService, levelRewardController);
        GameplayFlowController gameplayFlowController = CreateGameplayFlowController(victoryController, failureController, collectorSelectionController);
        FailureRecoveryController failureRecoveryController = CreateFailureRecoveryController(failureController, gameplayFlowController, conveyorSystem, recoveryRowController);
        EndgameCleanupController endgameCleanupController = CreateEndgameCleanupController(collectorQueueBoard, conveyorSystem, waitingLine, recoveryRowController, failureController);
        LevelProgressionController levelProgressionController = CreateLevelProgressionController();
        VictoryFlowController victoryFlowController = CreateVictoryFlowController(gameplayFlowController, levelProgressionController);
        CreateLevelBootstrapper(pixelGrid, conveyorSystem, waitingLine, collectorQueueBoard, endgameCleanupController, levelProgressionController);
        CreateEventSystem();

        LevelExitFlowController levelExitFlowController = CreateLevelExitFlowController(gameplayFlowController);
        CreateVictoryUI(victoryFlowController, levelExitFlowController, levelRewardController);
        CoinStoreUI coinStoreUI = CreateCoinStoreUI();
        CreateFailureUI(failureController, failureRecoveryController, levelExitFlowController, coinWalletService, coinStoreUI);

        GameplaySpeedController gameplaySpeedController = CreateGameplaySpeedController();
        PauseFlowController pauseFlowController = CreatePauseFlowController(gameplayFlowController);
        CreateTopGameplayHud(levelProgressionController, gameplaySpeedController, pauseFlowController, levelExitFlowController, coinWalletService);

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
    /// Its own fixed, dedicated row — GameplayLayout.WaitingToRecoveryGap
    /// below GameplayLayout.WaitingLinePositionY (Gap A: deliberately
    /// compact, see WaitingToRecoveryGap's own remarks) — directly beneath
    /// the normal Waiting Line, never sharing its row. This position never
    /// changes at runtime; it simply renders nothing while
    /// RecoveryRowController holds zero collectors (see RecoveryRowView's
    /// own remarks), so it costs no extra vertical space in the common
    /// case. Making real physical room for it when it IS occupied —
    /// shifting CollectorQueueBoard and everything on it far enough down to
    /// open GameplayLayout.RecoveryToCollectorsGap (Gap B: deliberately
    /// larger than Gap A) below Recovery Row's own row Y — is
    /// RecoveryLineLayoutController's job (see
    /// CreateRecoveryLineLayoutController and RecoveryLineLayoutController's
    /// own remarks for the exact derivation); both this row's own baseline Y
    /// and that controller's expanded-state math read the same
    /// GameplayLayout.WaitingToRecoveryGap/RecoveryToCollectorsGap pair
    /// rather than separately hand-tuned numbers, so they can never drift
    /// apart. RecoveryRowController owns whatever collectors
    /// ReceiveCollectors is later given — no capacity to configure here,
    /// since the row sizes itself from however many collectors it receives.
    /// RecoveryRowView lives on the same GameObject and handles
    /// reparenting/layout, wired to the controller so it refreshes on
    /// CollectorsChanged rather than being driven directly.
    /// </summary>
    private static RecoveryRowController CreateRecoveryRow()
    {
        var recoveryRowObject = new GameObject(
            "RecoveryRow",
            typeof(RecoveryRowController),
            typeof(RecoveryRowView));
        recoveryRowObject.transform.position = new Vector3(
            0f,
            GameplayLayout.WaitingLinePositionY - GameplayLayout.WaitingToRecoveryGap,
            0f);

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

    /// <summary>
    /// Wires RecoveryLineLayoutController to the two objects it needs:
    /// recoveryRowController (occupancy — the sole source of truth for
    /// whether the layout should be expanded) and collectorQueueBoard's own
    /// Transform (lowerContentRoot — the single thing that actually moves).
    /// A plain gameplay-layer MonoBehaviour like every other controller
    /// here, not UI — created once, lives for the scene's lifetime, entirely
    /// event-driven (no polling).
    /// </summary>
    private static void CreateRecoveryLineLayoutController(RecoveryRowController recoveryRowController, CollectorQueueBoard collectorQueueBoard)
    {
        var controllerObject = new GameObject("RecoveryLineLayoutController", typeof(RecoveryLineLayoutController));

        var controller = controllerObject.GetComponent<RecoveryLineLayoutController>();
        var serializedController = new SerializedObject(controller);
        serializedController.FindProperty("recoveryRowController").objectReferenceValue = recoveryRowController;
        serializedController.FindProperty("lowerContentRoot").objectReferenceValue = collectorQueueBoard.transform;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
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
    /// The player's persistent coin balance — see CoinWalletService's own
    /// remarks for why this is a plain scene-owned MonoBehaviour rather than
    /// a DontDestroyOnLoad singleton (the balance's state of record lives in
    /// PlayerPrefs, not in this instance). No SerializeField wiring needed:
    /// it constructs its own PlayerPrefsCoinStorage lazily on first access.
    /// </summary>
    private static CoinWalletService CreateCoinWalletService()
    {
        var walletObject = new GameObject("CoinWalletService", typeof(CoinWalletService));
        return walletObject.GetComponent<CoinWalletService>();
    }

    /// <summary>
    /// Owns this level's coin reward — see LevelRewardController's own
    /// remarks. Subscribes to victoryController.OnVictory to grant
    /// EconomyConfig.BaseLevelCoinReward exactly once per completion.
    /// </summary>
    private static LevelRewardController CreateLevelRewardController(VictoryController victoryController, CoinWalletService coinWalletService)
    {
        var rewardControllerObject = new GameObject("LevelRewardController", typeof(LevelRewardController));

        var levelRewardController = rewardControllerObject.GetComponent<LevelRewardController>();
        var serializedLevelRewardController = new SerializedObject(levelRewardController);
        serializedLevelRewardController.FindProperty("victoryController").objectReferenceValue = victoryController;
        serializedLevelRewardController.FindProperty("coinWalletService").objectReferenceValue = coinWalletService;
        serializedLevelRewardController.ApplyModifiedPropertiesWithoutUndo();

        return levelRewardController;
    }

    /// <summary>
    /// Editor-only manual verification tool (see CoinEconomyDebugTools's own
    /// remarks — the whole class lives under Assets/Scripts/Editor/, so it
    /// never exists in a Player build). Created unconditionally here since
    /// this whole method only ever runs from the Unity Editor anyway
    /// (CreateBootstrapScene is an Editor menu command) — nothing further
    /// gates it, matching how every other object this method creates is
    /// unconditional.
    /// </summary>
    private static void CreateCoinEconomyDebugTools(CoinWalletService coinWalletService, LevelRewardController levelRewardController)
    {
        var debugToolsObject = new GameObject("CoinEconomyDebugTools", typeof(CoinEconomyDebugTools));

        var debugTools = debugToolsObject.GetComponent<CoinEconomyDebugTools>();
        var serializedDebugTools = new SerializedObject(debugTools);
        serializedDebugTools.FindProperty("coinWalletService").objectReferenceValue = coinWalletService;
        serializedDebugTools.FindProperty("levelRewardController").objectReferenceValue = levelRewardController;
        serializedDebugTools.ApplyModifiedPropertiesWithoutUndo();
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

    private const string LevelCompleteModalSpritePath = "Assets/Art/UI/Classic/LevelCompleteModal.png";

    /// <summary>
    /// All fixed pixel positions/sizes below are authored against
    /// ConfirmationModalContentReferenceWidth exactly like Exit/Pause/Level
    /// Failed (see ExitConfirmButtonHeight's remarks for that convention).
    /// LevelCompleteModal.png was replaced with a taller version (aspect
    /// ~0.664, up from the previous ~0.868) specifically to fit the new
    /// earned-coins section — see LevelCompleteModalMaxWidth's own remarks
    /// for why that alone means this modal now needs its own dedicated
    /// width ceiling instead of reusing ConfirmationModalMaxWidth. Width
    /// fraction, vertical offset, and content reference width are still the
    /// exact same shared ConfirmationModal* constants every other gameplay
    /// modal uses — only the max-width ceiling is now modal-specific.
    /// Proportions were measured directly off
    /// reference/UI/LevelCompleteModalTarget.png (its full 7-item hierarchy:
    /// title, subtitle, separator, "You earned:", coin/value, DoubleCoinsButton,
    /// bottom row).
    ///
    /// This one specifically: vertical anchoredPosition of the two-line
    /// "Level Complete!" title.
    /// </summary>
    private const float LevelCompleteTitleOffsetY = 224f;

    /// <summary>
    /// Dedicated width ceiling for Level Complete, analogous to
    /// LevelFailedModalMaxWidth — its own artwork's new taller aspect ratio
    /// (~0.664, close to LevelFailedModal.png's own ~0.646) means reusing
    /// ConfirmationModalMaxWidth (1400) unclamped would now resolve to
    /// 1400/0.664 ≈ 2109 units on iPad portrait — ~88% of that device's own
    /// 2388-unit canvas height, no longer "doesn't dominate a tablet screen"
    /// now that the artwork is taller than when that reuse was originally
    /// decided. 1200 (the same value LevelFailedModalMaxWidth already uses,
    /// since the two artworks' aspect ratios are now close enough to share
    /// one ceiling) resolves to 1200/0.664 ≈ 1807 units instead — ~75.7% of
    /// the iPad reference's height, back in the same comfortable range every
    /// other gameplay modal targets. Phones are unaffected: at the 1080-wide
    /// portrait reference, 1080*0.90=972 is already well under 1200, so this
    /// ceiling never engages there.
    /// </summary>
    private const float LevelCompleteModalMaxWidth = 1200f;

    /// <summary>
    /// Fixed (not auto-sized, unlike Level Failed's title) at a size large
    /// enough that "Level Complete!" reliably wraps to two lines within its
    /// own 700-wide box (narrower than the interior's own ~852 width,
    /// specifically so it wraps instead of fitting on one line) — matching
    /// the reference's own two-line "Level" / "Complete!" treatment, the
    /// modal's strongest text element.
    /// </summary>
    private const float LevelCompleteTitleFontSize = 100f;

    /// <summary>Pleasant muted green, sampled off reference/UI/LevelCompleteModalTarget.png's own title — consistent with the existing muted palette rather than a saturated "success green".</summary>
    private static readonly Color LevelCompleteTitleColor = new Color(94f / 255f, 117f / 255f, 62f / 255f);

    /// <summary>Vertical anchoredPosition of "Great job! You did it!" (see LevelCompleteTitleOffsetY's remarks). Reuses ConfirmationModalDescriptionFontSize/Color — same supporting-text styling as every other gameplay modal.</summary>
    private const float LevelCompleteDescriptionOffsetY = 48f;

    /// <summary>
    /// Vertical anchoredPosition of the decorative separator between the
    /// text block and the new earned-coins/action area (see
    /// LevelCompleteTitleOffsetY's remarks) — reuses CreateDottedStarSeparator
    /// (see its own remarks) and Level Failed's own dot sizing constants
    /// (LevelFailedSeparator1SegmentWidth/CenterGap/DotDiameter/DotSpacing —
    /// this modal's interior is almost exactly as wide, so the same dot
    /// scale reads correctly here too) with only position/colour differing.
    /// CreateDottedStarSeparator itself only draws the two dotted segments
    /// (includeCenterAccent: false — see its own remarks); the centre
    /// accent here is a plain TextMeshProUGUI "★" (see CreateVictoryUI), not
    /// the generated-sprite star Level Failed uses, specifically to avoid
    /// depending on that shared generated asset at all for this modal.
    /// </summary>
    private const float LevelCompleteSeparatorOffsetY = -16f;

    /// <summary>
    /// Font size for the separator's centre "★" TextMeshProUGUI glyph — small
    /// and subtle relative to LevelCompleteTitleFontSize (100), sized so the
    /// rendered glyph sits comfortably inside LevelFailedSeparator1CenterGap
    /// (60 units) with visible padding on both sides before the dots begin,
    /// reading as "dots — ★ — dots" rather than one continuous line.
    /// </summary>
    private const float LevelCompleteSeparatorStarFontSize = 34f;

    /// <summary>
    /// Soft muted olive-green for the separator's "★" — sampled off
    /// reference/UI/LevelCompleteModalTarget.png's own star, a warm/green
    /// tone that harmonizes with LevelCompleteTitleColor without repeating
    /// it exactly (matching the reference's own slightly lighter star) or
    /// competing with the title/buttons.
    /// </summary>
    private static readonly Color LevelCompleteSeparatorStarColor = new Color(161f / 255f, 175f / 255f, 111f / 255f);

    /// <summary>Vertical anchoredPosition of the "You earned:" caption, directly above the coin/value row so the two read as one reward group (see LevelCompleteEarnedCoinsGroupOffsetY's remarks).</summary>
    private const float LevelCompleteYouEarnedOffsetY = -81f;

    /// <summary>
    /// Font size for "You earned:" — measured against
    /// reference/UI/LevelCompleteModalTarget.png's own cap-heights: its
    /// "You earned:" (~25px) and "Great job! You did it!" (~26px) are
    /// essentially the same weight, not one visibly smaller than the other,
    /// so this now matches ConfirmationModalDescriptionFontSize (32) rather
    /// than sitting a full notch below it — still clearly smaller than
    /// LevelCompleteTitleFontSize (100) and LevelCompleteEarnedCoinsValueFontSize
    /// (86), but no longer reading as tiny helper text.
    /// </summary>
    private const float LevelCompleteYouEarnedFontSize = 34f;

    private const string CoinSpritePath = "Assets/Art/UI/Classic/Coin.png";

    /// <summary>
    /// Vertical anchoredPosition of EarnedCoinsGroup — CoinIcon and
    /// EarnedCoinsValue's shared parent container (see CreateVictoryUI),
    /// positioned as one unit rather than centering the value text
    /// independently and attaching the coin afterward. A short gap below
    /// LevelCompleteYouEarnedOffsetY (see its own remarks) so the caption and
    /// the coin/value row read together as one reward group, per the task's
    /// own requirement.
    /// </summary>
    private const float LevelCompleteEarnedCoinsGroupOffsetY = -173f;

    /// <summary>
    /// Layout/authoring width for EarnedCoinsGroup (no Image on the
    /// container itself — same "invisible layout container" convention as
    /// Level Failed's own AdContent) — CoinIcon/EarnedCoinsValue are
    /// positioned relative to it, not to content directly. Sized to fit
    /// CoinIcon (~96 wide at LevelCompleteCoinIconHeight) + a ~28-unit gap +
    /// EarnedCoinsValue (~156 wide at LevelCompleteEarnedCoinsValueFontSize
    /// 86) with a small margin, derived from measuring
    /// reference/UI/LevelCompleteModalTarget.png's own coin/"120" pair
    /// (84x90 coin, 157x70 text, 24-unit gap between them).
    /// </summary>
    private const float LevelCompleteEarnedCoinsGroupWidth = 284f;

    private const float LevelCompleteEarnedCoinsGroupHeight = 110f;

    /// <summary>
    /// Horizontal offset of CoinIcon from EarnedCoinsGroup's own centre —
    /// immediately to the left of EarnedCoinsValue (see
    /// LevelCompleteEarnedCoinsValueOffsetX), never positioned independently
    /// of the group. Shifted slightly further left (from -94) to re-centre
    /// the [coin + 120] pair as a whole now that
    /// LevelCompleteEarnedCoinsValueFontSize's own increase widened "120" —
    /// the minimum X adjustment needed, not a redesign of the group.
    /// </summary>
    private const float LevelCompleteCoinIconOffsetX = -102f;

    /// <summary>
    /// CoinIcon footprint height — reference/UI/LevelCompleteModalTarget.png
    /// measures its own coin disc at ~90px tall, essentially matching its
    /// title line's own ~81-88px cap-height; the coin is meant to read as a
    /// significant visual moment, not a small inline glyph next to the
    /// number. Raised from the previous pass's 80 accordingly.
    /// </summary>
    private const float LevelCompleteCoinIconHeight = 94f;

    /// <summary>Horizontal offset of EarnedCoinsValue from EarnedCoinsGroup's own centre (see LevelCompleteCoinIconOffsetX's remarks) — leaves a ~30-unit gap from CoinIcon's own right edge, matching the reference's own coin-to-number spacing.</summary>
    private const float LevelCompleteEarnedCoinsValueOffsetX = 64f;

    /// <summary>
    /// Font size for the earned-coins numeric value. Raised a further ~11.6%
    /// from the previous pass's 86 (within the requested 10-15% ceiling) —
    /// a small final bump on top of CreateEarnedCoinsGroup's own
    /// FontStyles.Bold, which is the main source of the requested extra
    /// visual weight (a heavier look using the same Fredoka SemiBold SDF
    /// asset, no new font asset introduced).
    /// </summary>
    private const float LevelCompleteEarnedCoinsValueFontSize = 96f;

    /// <summary>Dark warm brown for the earned-coins value, sampled off reference/UI/LevelCompleteModalTarget.png's own "120" — deliberately a richer/darker tone than ConfirmationModalDescriptionColor so the value itself reads with more weight than the "You earned:" caption above it.</summary>
    private static readonly Color LevelCompleteEarnedCoinsValueColor = new Color(105f / 255f, 84f / 255f, 57f / 255f);

    /// <summary>
    /// Placeholder earned-coins amount shown by EarnedCoinsValue —
    /// presentation only. No coin/reward economy exists anywhere in this
    /// project yet (confirmed by inspecting VictoryController's own OnVictory
    /// event, which carries no reward data, and LevelDefinition's own class
    /// remarks, which explicitly exclude "rewards" as a future level-catalog/
    /// player-progress concern not yet built) — so there is nothing real to
    /// bind to. Named and commented explicitly as a placeholder specifically
    /// so a future reward system has one obvious constant to replace with a
    /// real bound value, rather than a bare literal buried in a
    /// CreateCenteredTMPText call.
    /// </summary>
    private const string LevelCompleteEarnedCoinsPlaceholderValue = "120";

    private const string DoubleCoinsButtonSpritePath = "Assets/Art/UI/Classic/DoubleCoinsButton.png";

    /// <summary>
    /// Vertical anchoredPosition of DoubleCoinsButton — the modal's
    /// visually-strongest reward CTA (see CreateVictoryUI), placed by
    /// refitting the whole action area below LevelCompleteEarnedCoinsGroupOffsetY
    /// now that the taller artwork has room for it: separator →
    /// (moderate gap) → "You earned:" → (small gap, one reward group) →
    /// coin/value → (moderate gap) → DoubleCoinsButton → (moderate gap) →
    /// the Exit/Next Level row → (comfortable bottom breathing room),
    /// matching reference/UI/LevelCompleteModalTarget.png's own relative
    /// proportions rather than its absolute pixel positions.
    /// </summary>
    private const float LevelCompleteDoubleCoinsButtonOffsetY = -366f;

    /// <summary>
    /// Fixed footprint height for DoubleCoinsButton, its width then derived
    /// from the sprite's own native aspect ratio (~3.18 — see
    /// ResolveButtonArtworkSize) exactly like every other button in this
    /// project, never an independent width. Raised from the previous pass's
    /// 195 to read as more dominant/eye-catching, per
    /// reference/UI/LevelCompleteModalTarget.png's own treatment of this as
    /// the single most eye-catching action on the screen — the resulting
    /// width (~685 content-frame units) is clearly wider than either
    /// bottom-row button (~390-394 units each) and comfortably wide relative
    /// to the interior (~850 units) without touching the frame.
    /// </summary>
    private const float LevelCompleteDoubleCoinsButtonHeight = 215f;

    /// <summary>Vertical anchoredPosition shared by the Exit/Next Level row (see LevelCompleteDoubleCoinsButtonOffsetY's remarks).</summary>
    private const float LevelCompleteBottomRowOffsetY = -569f;

    /// <summary>
    /// Fixed footprint height shared by Exit/Next Level, side by side below
    /// DoubleCoinsButton (see CreateVictoryUI) — both read as comfortable
    /// mobile touch targets. Raised moderately from the previous pass's 130;
    /// SecondaryButton.png/PrimaryButton.png's own native aspect (~2.79/2.81,
    /// visibly narrower-per-height than the reference's own illustrated
    /// buttons) caps how far this can grow before the row's combined width
    /// crowds the ~850-unit-wide interior, so this stays a moderate rather
    /// than dramatic increase. Next Level reads as the primary action through
    /// PrimaryButton.png's own more vibrant colour and conventional
    /// right-hand position, not through being physically larger than Exit —
    /// the same convention already used for DoubleCoinsButton vs. this row
    /// and for Level Failed's own Continue/Exit level pair.
    /// </summary>
    private const float LevelCompleteBottomRowButtonHeight = 140f;

    /// <summary>
    /// Shared horizontal offset (from centre) for both Exit (left, negative)
    /// and Next Level (right, positive) — one shared value applied
    /// symmetrically to both, the same convention
    /// ExitConfirmButtonHorizontalOffset already uses for Stay/Exit despite
    /// their own slightly different sprite aspect ratios, rather than two
    /// separate per-button offsets for a sub-2-unit difference. Raised
    /// alongside LevelCompleteBottomRowButtonHeight to keep the same
    /// ~40-unit centre gap between the two buttons at the new, larger size.
    /// </summary>
    private const float LevelCompleteBottomRowHorizontalOffset = 218f;

    /// <summary>Font size for the Exit/Next Level labels, raised alongside LevelCompleteBottomRowButtonHeight to keep the same label-to-button-height ratio already established for this modal's other buttons.</summary>
    private const int LevelCompleteButtonLabelFontSize = 46;

    /// <summary>
    /// Level Complete modal: a two-line "Level Complete!" title, a
    /// supporting line, a decorative separator, "You earned:" over
    /// EarnedCoinsGroup (CoinIcon + a placeholder amount — see
    /// CreateEarnedCoinsGroup's own remarks; presentation only, no reward
    /// economy exists yet), DoubleCoinsButton (the rewarded-ad reward CTA —
    /// see CreateImageButton's own remarks on why it gets no text label),
    /// then an Exit/Next Level row. Reuses the exact same responsive
    /// artwork box/content-scaling infrastructure as Exit/Pause/Level Failed
    /// (see CreateResponsiveModalArtworkBox) —
    /// LevelCompleteModal.png's crab and confetti are baked into the same
    /// texture as the cream panel body, so sizing the whole sprite by its
    /// own aspect ratio (exactly what CreateResponsiveModalArtworkBox
    /// already does for any artwork) keeps them attached and never clips
    /// them; no special-casing was needed. VictoryUI is wired to
    /// victoryController, victoryFlowController, and now
    /// levelExitFlowController — the same single Exit implementation the top
    /// HUD/Pause/Level Failed modals already share — plus doubleCoinsButton,
    /// currently a safe no-op (see VictoryUI.OnDoubleCoinsPressed). Reuses
    /// the existing EventSystem created earlier in CreateBootstrapScene —
    /// one EventSystem serves every Canvas in the scene, so no second one is
    /// created here.
    /// sortingOrder 10 (matching GameplayModalCanvas/FailureCanvas) so this
    /// panel's own full-screen backdrop reliably blocks clicks to the top
    /// HUD underneath it, exactly like the rest of the gameplay modal
    /// system.
    /// </summary>
    /// <summary>
    /// Forces a texture's Sprite Mode to Single (whole image = one sprite),
    /// clearing any Multiple-mode sub-sprite rects in the process.
    ///
    /// LevelCompleteModal.png was originally sliced in Multiple mode with
    /// sub-sprite rects authored against its old 1121x1291 pixel dimensions.
    /// When the artwork was later replaced with a taller 1000x1505 version,
    /// those rects became stale and geometrically invalid against the new
    /// texture, so AssetDatabase.LoadAssetAtPath&lt;Sprite&gt; returned a
    /// garbled sub-sprite instead of the whole illustration - the artwork
    /// rendered as broken rectangular fragments instead of one intact panel.
    /// ConfirmationModal.png and PrimaryButton.png, by contrast, both import
    /// as Single and have never shown this problem.
    ///
    /// Called every time this artwork is loaded (idempotent - a no-op once
    /// already Single) so this class of bug self-heals on the next scene
    /// regeneration if the source PNG is ever swapped again.
    /// </summary>
    private static void NormalizeSpriteImportModeToSingle(string texturePath)
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"BootstrapSceneCreator: could not get TextureImporter at '{texturePath}' to normalize its Sprite Mode.");
            return;
        }

        if (importer.spriteImportMode == SpriteImportMode.Single)
            return;

        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritesheet = new SpriteMetaData[0];
        importer.SaveAndReimport();
    }

    private static void CreateVictoryUI(VictoryFlowController victoryFlowController, LevelExitFlowController levelExitFlowController, LevelRewardController levelRewardController)
    {
        var canvasObject = new GameObject(
            "VictoryCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(VictoryUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        GameObject panel = CreateModalBackdrop(canvasObject.transform, "VictoryPanel");

        NormalizeSpriteImportModeToSingle(LevelCompleteModalSpritePath);
        var levelCompleteModalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LevelCompleteModalSpritePath);
        if (levelCompleteModalSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{LevelCompleteModalSpritePath}'; the Level Complete modal will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Transform content = CreateResponsiveModalArtworkBox(
            panel.transform,
            levelCompleteModalSprite,
            ConfirmationModalWidthFraction,
            LevelCompleteModalMaxWidth,
            ConfirmationModalVerticalOffsetFraction,
            ConfirmationModalContentReferenceWidth);

        var fredokaSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaSemiBoldFontAssetPath);
        if (fredokaSemiBold == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaSemiBoldFontAssetPath}'; the Level Complete modal's title/button labels will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        var fredokaMedium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaMediumFontAssetPath);
        if (fredokaMedium == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaMediumFontAssetPath}'; the Level Complete modal's description will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        CreateCenteredTMPText(content, "Level Complete!", new Vector2(0f, LevelCompleteTitleOffsetY), new Vector2(700f, 240f), LevelCompleteTitleFontSize, LevelCompleteTitleColor, fredokaSemiBold);
        CreateCenteredTMPText(content, "Great job! You did it!", new Vector2(0f, LevelCompleteDescriptionOffsetY), new Vector2(700f, 40f), ConfirmationModalDescriptionFontSize, ConfirmationModalDescriptionColor, fredokaMedium);

        Sprite circleSprite = GenerateCircleSprite();
        CreateDottedStarSeparator(
            content,
            LevelCompleteSeparatorOffsetY,
            LevelFailedSeparator1SegmentWidth,
            LevelFailedSeparator1CenterGap,
            LevelFailedSeparator1DotDiameter,
            LevelFailedSeparator1DotSpacing,
            LevelFailedSeparator1CenterStarDiameter,
            circleSprite,
            starSprite: null,
            dotColor: LevelFailedSeparator1DotColor,
            centerColor: default,
            includeCenterAccent: false);

        // Plain TMP text, not the generated-sprite star Level Failed uses
        // (GenerateStarSprite/StarSprite.asset) — see LevelCompleteSeparatorOffsetY's
        // remarks for why. Falls back to Inter-Regular SDF.asset for the ★
        // glyph itself (registered on fredokaSemiBold by
        // StarFallbackFontAssetBuilder), since neither Fredoka .ttf contains
        // U+2605 — confirmed via each source file's own cmap, not assumed.
        GameObject separatorStar = CreateCenteredTMPText(content, "★", new Vector2(0f, LevelCompleteSeparatorOffsetY), new Vector2(LevelFailedSeparator1CenterGap, LevelFailedSeparator1CenterGap), LevelCompleteSeparatorStarFontSize, LevelCompleteSeparatorStarColor, fredokaSemiBold);
        separatorStar.name = "SeparatorStar";

        CreateCenteredTMPText(content, "You earned:", new Vector2(0f, LevelCompleteYouEarnedOffsetY), new Vector2(700f, 44f), LevelCompleteYouEarnedFontSize, ConfirmationModalDescriptionColor, fredokaMedium);

        var coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoinSpritePath);
        if (coinSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{CoinSpritePath}'; the earned-coins row will show no coin icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        TextMeshProUGUI earnedCoinsValueText = CreateEarnedCoinsGroup(content, coinSprite, fredokaSemiBold);

        var doubleCoinsButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(DoubleCoinsButtonSpritePath);
        if (doubleCoinsButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{DoubleCoinsButtonSpritePath}'; the Double Coins button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        // DoubleCoinsButton.png already bakes in its own coin/ad artwork,
        // "x2 Coins", "Watch Ad", and play icon — CreateImageButton (unlike
        // CreateDialogButton) adds no label child on top of it, so none of
        // that baked-in text is duplicated.
        Vector2 doubleCoinsButtonSize = ResolveButtonArtworkSize(doubleCoinsButtonSprite, LevelCompleteDoubleCoinsButtonHeight);
        Button doubleCoinsButton = CreateImageButton(content, "DoubleCoinsButton", new Vector2(0f, LevelCompleteDoubleCoinsButtonOffsetY), doubleCoinsButtonSize, doubleCoinsButtonSprite);

        var primaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PrimaryButtonSpritePath);
        if (primaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{PrimaryButtonSpritePath}'; the Next Level button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var secondaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SecondaryButtonSpritePath);
        if (secondaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{SecondaryButtonSpritePath}'; the Exit button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Vector2 nextLevelButtonSize = ResolveButtonArtworkSize(primaryButtonSprite, LevelCompleteBottomRowButtonHeight);
        Vector2 exitButtonSize = ResolveButtonArtworkSize(secondaryButtonSprite, LevelCompleteBottomRowButtonHeight);

        // Exit (left, secondary/destructive) / Next Level (right, primary
        // progression — the conventional forward-action position).
        Button exitButton = CreateDialogButton(content, "ExitButton", "Exit", new Vector2(-LevelCompleteBottomRowHorizontalOffset, LevelCompleteBottomRowOffsetY), exitButtonSize, LevelCompleteButtonLabelFontSize, secondaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);
        Button nextLevelButton = CreateDialogButton(content, "NextLevelButton", "Next Level", new Vector2(LevelCompleteBottomRowHorizontalOffset, LevelCompleteBottomRowOffsetY), nextLevelButtonSize, LevelCompleteButtonLabelFontSize, primaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);

        var victoryUI = canvasObject.GetComponent<VictoryUI>();
        var serializedVictoryUI = new SerializedObject(victoryUI);
        serializedVictoryUI.FindProperty("victoryFlowController").objectReferenceValue = victoryFlowController;
        serializedVictoryUI.FindProperty("levelExitFlowController").objectReferenceValue = levelExitFlowController;
        serializedVictoryUI.FindProperty("panel").objectReferenceValue = panel;
        serializedVictoryUI.FindProperty("nextLevelButton").objectReferenceValue = nextLevelButton;
        serializedVictoryUI.FindProperty("exitButton").objectReferenceValue = exitButton;
        serializedVictoryUI.FindProperty("doubleCoinsButton").objectReferenceValue = doubleCoinsButton;
        serializedVictoryUI.FindProperty("levelRewardController").objectReferenceValue = levelRewardController;
        serializedVictoryUI.FindProperty("earnedCoinsValueText").objectReferenceValue = earnedCoinsValueText;
        serializedVictoryUI.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// EarnedCoinsGroup: a pure-layout container (RectTransform only, no
    /// Image — same "invisible layout container" convention as Level
    /// Failed's own AdContent) with CoinIcon and EarnedCoinsValue as its OWN
    /// children, positioned relative to the group's own centre — never
    /// content or the screen directly — so the coin and the number always
    /// move together as one compact, horizontally-centered composition
    /// rather than the value centering independently and the coin being
    /// attached afterward.
    /// </summary>
    private static TextMeshProUGUI CreateEarnedCoinsGroup(Transform parent, Sprite coinSprite, TMP_FontAsset fredokaSemiBold)
    {
        var groupObject = new GameObject("EarnedCoinsGroup", typeof(RectTransform));
        groupObject.transform.SetParent(parent, false);

        var groupRect = (RectTransform)groupObject.transform;
        groupRect.anchorMin = new Vector2(0.5f, 0.5f);
        groupRect.anchorMax = new Vector2(0.5f, 0.5f);
        groupRect.pivot = new Vector2(0.5f, 0.5f);
        groupRect.sizeDelta = new Vector2(LevelCompleteEarnedCoinsGroupWidth, LevelCompleteEarnedCoinsGroupHeight);
        groupRect.anchoredPosition = new Vector2(0f, LevelCompleteEarnedCoinsGroupOffsetY);

        var coinObject = new GameObject("CoinIcon", typeof(Image));
        coinObject.transform.SetParent(groupRect, false);

        float coinAspect = 1f;
        if (coinSprite != null && coinSprite.rect.height > 0f)
            coinAspect = coinSprite.rect.width / coinSprite.rect.height;

        var coinRect = coinObject.GetComponent<RectTransform>();
        coinRect.anchorMin = new Vector2(0.5f, 0.5f);
        coinRect.anchorMax = new Vector2(0.5f, 0.5f);
        coinRect.pivot = new Vector2(0.5f, 0.5f);
        coinRect.sizeDelta = new Vector2(LevelCompleteCoinIconHeight * coinAspect, LevelCompleteCoinIconHeight);
        coinRect.anchoredPosition = new Vector2(LevelCompleteCoinIconOffsetX, 0f);

        var coinImage = coinObject.GetComponent<Image>();
        coinImage.sprite = coinSprite;
        coinImage.preserveAspect = true;

        GameObject earnedCoinsValueObject = CreateCenteredTMPText(
            groupRect,
            LevelCompleteEarnedCoinsPlaceholderValue,
            new Vector2(LevelCompleteEarnedCoinsValueOffsetX, 0f),
            new Vector2(200f, 110f),
            LevelCompleteEarnedCoinsValueFontSize,
            LevelCompleteEarnedCoinsValueColor,
            fredokaSemiBold);
        earnedCoinsValueObject.name = "EarnedCoinsValue";

        // Fredoka SemiBold is already the heaviest weight this project has
        // an SDF asset for (see FredokaFontAssetBuilder) — no Bold-specific
        // .ttf/.asset exists, so FontStyles.Bold here triggers TMP's own
        // faux-bold rendering (thicker strokes via the shared material's
        // outline/dilate) on top of the same font asset, rather than
        // switching fonts or generating a new one, to make "120" read as
        // chunkier/heavier than a plain SemiBold render.
        var earnedCoinsValueText = earnedCoinsValueObject.GetComponent<TextMeshProUGUI>();
        earnedCoinsValueText.fontStyle = FontStyles.Bold;

        // The placeholder string above ("120") is only this object's
        // edit-time/authoring content — VictoryUI overwrites it at runtime
        // via CurrentLevelReward/RewardChanged (see CreateVictoryUI's own
        // wiring of this returned component into VictoryUI's
        // earnedCoinsValueText field), so the number displayed in Play Mode
        // always comes from LevelRewardController, never this literal.
        return earnedCoinsValueText;
    }

    private const string LevelFailedModalSpritePath = "Assets/Art/UI/Classic/LevelFailedModal.png";
    private const string AdIconSpritePath = "Assets/Art/UI/Classic/Icons/AdIcon.png";
    private const string SadTurtleSpritePath = "Assets/Art/UI/Classic/SadTurtle.png";

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
    ///
    /// Shifted up by ~27 units from its previous 536, as part of a single
    /// rigid shift applied to the entire content stack (title through Exit
    /// level — see LevelFailedNeedRoomHeadingOffsetY's own remarks for the
    /// interior-bounds measurement behind this) to correct a top/bottom
    /// balance problem: measuring LevelFailedModal.png's own source art
    /// directly (its cream interior, excluding the decorative top tab and
    /// bottom frame, spans content_Y ≈ [-687.8, +648.6]) showed noticeably
    /// more empty space above the title than below Exit level. Every other
    /// gap in the modal is unchanged by this shift — it is a pure
    /// translation of title/description/turtle/heading, not a
    /// re-derivation of their relative spacing.
    /// </summary>
    private const float LevelFailedTitleOffsetY = 540f;

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
    private const float LevelFailedDescriptionOffsetY = 434f;

    /// <summary>
    /// Shared font size for this modal's two small headings ("Need a little
    /// more room?" / "One more chance?"). Raised from the previous pass's 30
    /// to ~34 after measuring reference/UI/LevelFailedModalTargetNEW.png
    /// directly: both headings measure ~22-24px cap-height in a 1332px-tall
    /// card there, which scales to this modal's own ~1506-unit content
    /// height as ~27 content units of cap-height — close to what fontSize 34
    /// renders at with Fredoka SemiBold. Both headings measured the SAME
    /// size/weight as each other in the reference (SemiBold already matches
    /// — no weight change needed there), confirming they should keep sharing
    /// this one constant rather than drifting apart.
    /// </summary>
    private const float LevelFailedHeadingFontSize = 34f;

    /// <summary>Shared thickness/colour for every plain decorative line in this modal (second separator) — low-contrast on purpose, per the task's own "decorative, not a hard divider" requirement.</summary>
    private const float LevelFailedSeparatorThickness = 3f;

    private static readonly Color LevelFailedSeparatorColor = new Color(210f / 255f, 180f / 255f, 150f / 255f, 0.6f);

    /// <summary>
    /// Vertical anchoredPosition of SadTurtle.png. Gap from description's own
    /// box-bottom edge to here is unchanged (≈26 content units) — this
    /// constant only moved by the same rigid +27 shift as
    /// LevelFailedTitleOffsetY (see its own remarks for why), preserving its
    /// relationship to Title/Description exactly.
    /// </summary>
    private const float LevelFailedTurtleOffsetY = 271f;

    /// <summary>
    /// Fixed footprint height for SadTurtle.png; width is derived from the
    /// sprite's own native aspect ratio (see CreateSadTurtleWithShadow) so
    /// the artwork is never stretched/cropped, per this task's own explicit
    /// requirement.
    ///
    /// Reference/UI/LevelFailedModalTargetNEW.png measures its own turtle
    /// illustration at y=348-575 (227px tall, ≈17.0% of its 1332px card
    /// height) — that fraction applied to this modal's own 1505.6-unit
    /// content height gives ≈256.6. Trimmed slightly to 225 (≈88% of that)
    /// to keep GROUP B (turtle through SaveMeButton) and the still-required
    /// gap into GROUP C's separator inside this content height without
    /// enlarging LevelFailedModal.png itself — see this task's own "do not
    /// make the modal longer, rebalance internal layout first" requirement.
    /// This is still a ~50% increase over the previous pass's 150, matching
    /// "significantly larger" feedback; the remaining budget was found by
    /// shrinking Continue/Exit level (see LevelFailedButtonHeight) and
    /// AdContent to their own reference-measured proportions instead of
    /// their previous oversized ones — this task's own item F explicitly
    /// permits that ("do not let Continue/Exit dominate... more than they
    /// do in the reference").
    /// </summary>
    private const float LevelFailedTurtleHeight = 225f;

    /// <summary>
    /// SadTurtle's own small ground-contact shadow — a horizontally
    /// elliptical soft-edged patch directly beneath the turtle's body, not
    /// the previous pass's displaced silhouette duplicate (see this task's
    /// own explicit correction: "this turtle is an illustration sitting
    /// inside a modal... NOT a displaced silhouette, NOT a strong blurred
    /// copy, NOT a radial spray-paint blob surrounding it").
    ///
    /// No existing lightweight UI contact-shadow implementation exists to
    /// reuse (confirmed again this pass — ContactShadowMesh remains the
    /// only prior "shadow" anywhere in this project, and it is a 3D
    /// world-space Mesh/Material built for gameplay characters, explicitly
    /// out of scope to touch or extend here). CreateSoftContactShadowSprite
    /// generates a new, small, purely local procedural shape instead — the
    /// same "flat UI primitive, not art" category as GenerateCircleSprite/
    /// GenerateStarSprite already in this file, this one with a soft
    /// smoothstep falloff (conceptually similar to ContactShadowMesh's own
    /// soft radial gradient, but new, separate code — a plain UI Image, not
    /// a Mesh/Material, and never shared with or wired into that gameplay
    /// system).
    ///
    /// Squashed into an ellipse via RectTransform sizeDelta (see
    /// CreateSadTurtleWithShadow) — considerably wider than tall, per this
    /// task's own requirement — sized as a fraction of the turtle's own
    /// displayed width/height so it scales together with
    /// LevelFailedTurtleHeight automatically.
    /// </summary>
    private const float LevelFailedTurtleShadowWidthFraction = 0.62f;

    private const float LevelFailedTurtleShadowHeightFraction = 0.26f;

    /// <summary>
    /// How far below SadTurtle's own visible bottom edge the shadow's
    /// centre sits — "a very small vertical offset" per this task's own
    /// wording, just enough that the ellipse reads as sitting at the
    /// turtle's base rather than floating inside its body.
    /// </summary>
    private const float LevelFailedTurtleShadowBottomNudge = 8f;

    /// <summary>Neutral warm gray-brown, low-to-medium opacity — "compatible with the cream modal", per this task's own wording, not the previous pass's near-black tint.</summary>
    private static readonly Color LevelFailedTurtleShadowColor = new Color(90f / 255f, 74f / 255f, 58f / 255f, 0.28f);

    /// <summary>
    /// Vertical anchoredPosition of "Need a little more room?". Reuses
    /// LevelFailedHeadingFontSize/ConfirmationModalDescriptionColor — the
    /// same heading style as "One more chance?" below it. A small,
    /// intentional gap below SadTurtle (≈19.5 content units) — see
    /// LevelFailedSaveMeButtonOffsetY's own remarks for why this and every
    /// offset below it moved together in this pass.
    /// </summary>
    private const float LevelFailedNeedRoomHeadingOffsetY = 116f;

    /// <summary>
    /// PRODUCT RULE — do not restore a RecoveryLine.png illustration here.
    ///
    /// The upper recovery offer is exactly: SadTurtle, "Need a little more
    /// room?", then SaveMeButton (label + coin/price) — nothing else.
    /// RecoveryLine.png (Assets/Art/UI/Classic/Boosts/RecoveryLine.png)
    /// depicts the Recovery Line mechanic that "Save me!" grants, but per
    /// explicit product direction it is NOT shown in this modal — Save me!
    /// alone represents purchasing/using it. A visual pass has already
    /// added this illustration back once by mistake; if a future one is
    /// tempted to do it again for "visual completeness", don't — check with
    /// product first. The asset itself is intentionally left untouched in
    /// the project (it may be used elsewhere), only unreferenced here.
    ///
    /// Vertical anchoredPosition of the SaveMeButton — this modal's primary
    /// rescue action, following "Need a little more room?" directly with
    /// one compact, intentional gap (45 content units) — the two read as
    /// one recovery offer together with SadTurtle above, not "an element is
    /// missing between them".
    ///
    /// This offset, and every offset below it through LevelFailedExitButtonOffsetY,
    /// together with LevelFailedTitleOffsetY/LevelFailedDescriptionOffsetY/
    /// LevelFailedTurtleOffsetY/LevelFailedNeedRoomHeadingOffsetY above,
    /// were shifted by one uniform amount (-41) to rebalance top/bottom
    /// whitespace after RecoveryLine.png's removal shortened the whole
    /// content stack by ~85 units — every gap AMONG these offsets (turtle to
    /// heading, heading to Save me, separator to "One more chance?", ad
    /// block to Continue, etc.) is numerically unchanged from before; only
    /// the stack's overall position within the modal moved.
    /// </summary>
    private const float LevelFailedSaveMeButtonOffsetY = -25f;

    /// <summary>
    /// Fixed footprint height for SaveMeButton.
    ///
    /// reference/UI/LevelFailedModalTargetNEW.png measures its own Save me!
    /// button at 125px tall (≈9.4% of its 1332px card height) — noticeably
    /// TALLER than its own Continue/Exit level buttons (≈94px/90px, ≈7.1%/
    /// 6.8%), confirming Save me! is meant to read as the more prominent CTA
    /// (see LevelFailedButtonHeight's own remarks for the matching Continue/
    /// Exit level reduction). 145 (≈9.4% of this modal's own 1505.6-unit
    /// content height) reproduces that same relative prominence — up from
    /// the previous pass's 130, which was actually SMALLER than the
    /// unchanged Continue/Exit level height at the time, inverting the
    /// reference's own hierarchy; this task's own item D explicitly calls
    /// that out ("should feel like a major primary action... larger/heavier
    /// than the current implementation").
    /// </summary>
    private const float LevelFailedSaveMeButtonHeight = 145f;

    /// <summary>
    /// Vertical anchoredPosition of the "Save me!" label within
    /// SaveMeButton's own local space (button pivot is its own centre).
    /// reference/UI/LevelFailedModalTargetNEW.png's own label sits ≈18% of
    /// its button's own height above centre; applied to this button's own
    /// 145 height that is ≈26, rounded up slightly for breathing room above
    /// PriceGroup.
    /// </summary>
    private const float LevelFailedSaveMeLabelOffsetY = 30f;

    /// <summary>
    /// "Save me!" font size — raised from the previous pass's 34.
    /// FontStyles.Bold is also applied on top of Fredoka SemiBold (see
    /// CreateSaveMeButton, mirroring CreateEarnedCoinsGroup's own established
    /// "SemiBold is already this project's heaviest Fredoka asset, so
    /// FontStyles.Bold triggers TMP's faux-bold for extra weight" technique)
    /// so the label reads as the heavier, chunkier treatment
    /// reference/UI/LevelFailedModalTargetNEW.png's own "Save me!" shows,
    /// per this task's own "larger/heavier... major primary action"
    /// requirement — no new font asset introduced.
    /// </summary>
    private const int LevelFailedSaveMeLabelFontSize = 42;

    /// <summary>
    /// Vertical anchoredPosition of PriceGroup (CoinIcon + price value) as
    /// one unit within SaveMeButton's own local space — below "Save me!".
    ///
    /// Raised from -38 to -25: at -38 the group's own bottom edge
    /// (-38 - LevelFailedSaveMePriceGroupHeight/2 = -70) sat only 2.5 units
    /// above SaveMeButton's own bottom edge (-72.5, i.e.
    /// -LevelFailedSaveMeButtonHeight/2) — visibly crowding it. -25 gives
    /// the group's own bottom edge (-57) a real ~15-unit margin above the
    /// button's bottom, while the "Save me!"-to-PriceGroup gap stays
    /// comfortable, so the two lines read as one compact, vertically
    /// centred content group rather than PriceGroup being pinned to the
    /// floor. CoinIcon and the price value are untouched — both still
    /// children of this same PriceGroup RectTransform, moving together with
    /// their own relative alignment/gap/size exactly as before (see
    /// CreateSaveMePriceGroup).
    /// </summary>
    private const float LevelFailedSaveMePriceGroupOffsetY = -25f;

    private const float LevelFailedSaveMePriceGroupWidth = 240f;
    private const float LevelFailedSaveMePriceGroupHeight = 64f;

    /// <summary>Horizontal offset of CoinIcon from PriceGroup's own centre — mirrors CreateEarnedCoinsGroup's own coin-left/value-right composition.</summary>
    private const float LevelFailedSaveMeCoinIconOffsetX = -48f;

    /// <summary>
    /// CoinIcon footprint height inside SaveMeButton. reference/UI/LevelFailedModalTargetNEW.png
    /// measures its own coin at ≈50px diameter against a 125px-tall button
    /// (≈40% of button height) — "visually substantial... not a tiny icon"
    /// per this task's own wording. 56 (≈39% of this button's own 145
    /// height) reproduces that same ratio, up from the previous pass's 40.
    /// </summary>
    private const float LevelFailedSaveMeCoinIconHeight = 56f;

    private const float LevelFailedSaveMePriceValueOffsetX = 38f;

    /// <summary>
    /// "1000" font size — reference/UI/LevelFailedModalTargetNEW.png's own
    /// "300" measures ≈47px tall against its 125px-tall button (≈38%); 48
    /// (≈33% of this button's own 145 height) reproduces a comparably bold,
    /// clearly-readable-but-secondary size. FontStyles.Bold applied on top
    /// of Fredoka SemiBold (see CreateSaveMePriceGroup), the same faux-bold
    /// technique LevelFailedSaveMeLabelFontSize's own remarks describe, to
    /// match the reference's own chunky digit weight.
    /// </summary>
    private const float LevelFailedSaveMePriceFontSize = 48f;

    /// <summary>Warm gold, readable against SaveMeButton's own dark green artwork — distinct from the cream "Save me!" label so the price reads as its own accent, matching reference/UI/LevelFailedModalTargetNEW.png's own gold price colour.</summary>
    private static readonly Color LevelFailedSaveMePriceColor = new Color(1f, 214f / 255f, 120f / 255f);

    /// <summary>
    /// Displayed Save me! price — reads EconomyConfig.RecoveryLinePrice
    /// directly (never a hardcoded literal here) so the displayed price and
    /// the amount FailureUI actually charges through CoinWallet.
    /// TrySpendCoins can never drift apart; changing the price changes both
    /// by editing EconomyConfig alone. A plain TextMeshProUGUI value, not
    /// baked into any art, structured the same way VictoryUI's
    /// EarnedCoinsValue is (see CreateEarnedCoinsGroup).
    /// </summary>
    private static readonly string LevelFailedSaveMePriceValue = EconomyConfig.RecoveryLinePrice.ToString();

    /// <summary>
    /// First decorative separator, between SaveMeButton (the paid Recovery
    /// Line rescue) and "One more chance?" (the free ad-rewatch rescue) — a
    /// dotted line on either side of a small centred star (GenerateStarSprite
    /// — see its own remarks), reusing the same procedurally-generated
    /// circle sprite as the second separator's dot at small tinted sizes
    /// rather than a new art asset. Deliberately low contrast (see
    /// LevelFailedSeparator1DotColor) — decorative, not a hard divider, but
    /// still the clear visual break that separates the two rescue options,
    /// per this task's own "dotted divider should clearly separate the paid
    /// recovery option from the ad recovery option" requirement.
    ///
    /// Gap from SaveMeButton's own bottom edge to here: 36 content units.
    /// </summary>
    private const float LevelFailedSeparator1OffsetY = -134f;

    private const float LevelFailedSeparator1SegmentWidth = 300f;
    private const float LevelFailedSeparator1CenterGap = 60f;
    private const float LevelFailedSeparator1DotDiameter = 7f;
    private const float LevelFailedSeparator1DotSpacing = 20f;
    private const float LevelFailedSeparator1CenterStarDiameter = 26f;
    private static readonly Color LevelFailedSeparator1DotColor = new Color(196f / 255f, 168f / 255f, 138f / 255f, 0.75f);

    /// <summary>
    /// Vertical anchoredPosition of the "One more chance?" heading — start
    /// of the ad-rewatch group (heading + AdContent + Continue), kept
    /// together as one cluster. Gap from LevelFailedSeparator1OffsetY: 26
    /// content units.
    /// </summary>
    private const float LevelFailedSecondChanceHeadingOffsetY = -183f;

    /// <summary>
    /// Vertical anchoredPosition of the AdContent group as a whole — AdIcon
    /// and both text lines are children of that one layout container (see
    /// CreateAdStatusCard), positioned relative to IT, never independently
    /// against content or the screen, so the icon can never drift relative
    /// to its text at any resolved modal size. The container carries no
    /// visible background (no Image component at all) — a large tinted
    /// rounded-rect card here previously read as a "giant pink banner" that
    /// both overlapped "One more chance?" above it and visually competed
    /// with the Continue button below it; the container's own size below
    /// exists purely for layout/authoring, not for anything rendered.
    ///
    /// Gap from "One more chance?" to here: 22 content units.
    /// </summary>
    private const float LevelFailedAdContentOffsetY = -288f;

    /// <summary>
    /// AdContent footprint, scaled down from the previous pass's 500x140 by
    /// the same ~14% AdContent itself shrank (see LevelFailedAdContentHeight)
    /// so AdIcon/text stay proportioned to their own container.
    /// </summary>
    private const float LevelFailedAdContentWidth = 430f;

    /// <summary>
    /// AdContent height — reference measures its own ad block (TV icon top
    /// to caption bottom) at 106px (≈8.0% of its 1332px card height); 120
    /// (≈8.0% of this modal's own 1505.6 content height) reproduces that,
    /// down from the previous pass's 140. A pure layout box (see
    /// LevelFailedAdContentOffsetY's own remarks — no Image on it), so this
    /// shrink alone changes nothing rendered; AdIconHeight/AdCountFontSize
    /// below shrink the actually-visible icon/text to match.
    /// </summary>
    private const float LevelFailedAdContentHeight = 120f;

    /// <summary>
    /// Horizontal offset of AdIcon from the AdContent group's own centre
    /// (see LevelFailedAdContentOffsetY's remarks) — AdIcon and the text
    /// column are positioned so the pair reads as one compact, horizontally-
    /// centered composition (icon left, text column right), rather than the
    /// icon sitting far left while the text centers independently across the
    /// whole modal. Scaled down from the previous pass's -174 in lockstep
    /// with LevelFailedAdContentHeight's own ~14% reduction.
    /// </summary>
    private const float LevelFailedAdIconOffsetX = -149f;

    /// <summary>AdIcon (the TV/play icon) footprint height — trimmed from the previous pass's 130 in proportion with AdContentHeight's own reduction, per this task's own "check ad section... relative scale" item.</summary>
    private const float LevelFailedAdIconHeight = 115f;

    /// <summary>Horizontal offset of both ad text lines from the AdContent group's own centre (see LevelFailedAdContentOffsetY's remarks) — same X for both lines, so "3 / 3" centers directly above "Watch an ad to continue". Scaled down from the previous pass's 77 in lockstep with LevelFailedAdContentHeight's own reduction.</summary>
    private const float LevelFailedAdTextOffsetX = 66f;

    /// <summary>Vertical offset of "3 / 3" from the AdContent group's own centre — together with LevelFailedAdCaptionOffsetY, centers the two-line text block so its own vertical midpoint approximately aligns with AdIcon's (Y=0 in the same group). Scaled down from the previous pass's 22 in lockstep with LevelFailedAdContentHeight's own reduction.</summary>
    private const float LevelFailedAdCountOffsetY = 19f;

    private const float LevelFailedAdCaptionOffsetY = -32f;

    /// <summary>"3 / 3" font size — trimmed modestly from the previous pass's 60, matching AdContent's own ~14% reduction, while staying prominent (comparable in weight to the Continue/Exit level button labels).</summary>
    private const float LevelFailedAdCountFontSize = 52f;

    /// <summary>"Watch an ad to continue" font size — reference measures this caption's own cap-height at ≈24px against its 1332px card (≈27 content units), close to what this unchanged 28 already renders — kept as-is, still comfortably readable per the original task's own explicit requirement not to shrink it too far.</summary>
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
    ///
    /// Reduced from the previous pass's 140 to 95 after measuring
    /// reference/UI/LevelFailedModalTargetNEW.png directly: its own
    /// Continue/Exit level buttons measure ≈94px/90px tall against its own
    /// 1332px card (≈7.1%/6.8%) — clearly SMALLER, relative to the card,
    /// than the 140-tall (≈9.3%) buttons the previous pass kept unchanged,
    /// and smaller than the reference's own Save me! (≈9.4%, see
    /// LevelFailedSaveMeButtonHeight). 95 (≈6.3% of this modal's own
    /// 1505.6-unit content height) reproduces that same "Continue/Exit
    /// stay secondary to Save me!" hierarchy — this task's own item F
    /// explicitly calls for this ("do not let Continue/Exit dominate the
    /// modal more than they do in the reference"), superseding the previous
    /// pass's "keep Continue/Exit level buttons unchanged" note. Only the
    /// footprint changes here — same PrimaryButton.png/SecondaryButton.png
    /// artwork, same CreateDialogButton call sites, nothing about their
    /// behaviour or styling.
    /// </summary>
    private const float LevelFailedButtonHeight = 95f;

    /// <summary>
    /// The ad-rewatch group's own Continue button. Gap from AdContent's own
    /// bottom edge to here: 20 content units.
    /// </summary>
    private const float LevelFailedContinueButtonOffsetY = -415f;

    /// <summary>
    /// Exit level button, kept clearly separated at the bottom by
    /// LevelFailedSeparator2OffsetY above it and a comfortable ~20-unit
    /// margin below it to LevelFailedModal.png's own bottom interior edge
    /// (measured directly off the source art — see LevelFailedTitleOffsetY's
    /// own remarks) — never touching or crowding the frame.
    /// </summary>
    private const float LevelFailedExitButtonOffsetY = -558f;

    /// <summary>
    /// Continue/Exit level label font size — scaled down from the previous
    /// pass's 45 in the same ≈68% ratio LevelFailedButtonHeight itself
    /// shrank (95/140), so the label stays proportioned to its own smaller
    /// button rather than looking oversized against it.
    /// </summary>
    private const int LevelFailedButtonLabelFontSize = 31;

    /// <summary>
    /// Second decorative separator, between Continue and Exit level — two
    /// short solid line segments flanking a small centred dot, per the
    /// task's own description ("short horizontal line — small centre dot —
    /// short horizontal line"), reusing CreateHorizontalSeparator/the same
    /// circle sprite rather than new art. Low contrast/decorative, not a
    /// hard divider.
    ///
    /// Gap from Continue's own bottom edge to here: 26 content units.
    /// </summary>
    private const float LevelFailedSeparator2OffsetY = -489f;

    private const float LevelFailedSeparator2LineWidth = 150f;
    private const float LevelFailedSeparator2LineOffsetX = 102f;
    private const float LevelFailedSeparator2DotDiameter = 14f;

    private const string CircleSpriteAssetPath = "Assets/Art/UI/Generated/CircleSprite.asset";
    private const string StarSpriteAssetPath = "Assets/Art/UI/Generated/StarSprite.asset";
    private const string SoftContactShadowSpriteAssetPath = "Assets/Art/UI/Generated/SoftContactShadow.asset";

    /// <summary>
    /// Source texture resolution shared by every procedurally-generated
    /// circle in this modal (both separators' dots) — comfortably above the
    /// largest on-screen pixel size any of them could actually render at
    /// (content is scaled up to ~1.44x on a clamped tablet — see
    /// ResponsiveModalBox), so they stay crisp rather than visibly
    /// upscale-blurred, the same concern EnergyBarPrefabBuilder's own
    /// GenerateStadiumSprite remarks describe for Unity's built-in
    /// UISprite.psd at large sizes. The same sprite is reused at every size
    /// down to LevelFailedSeparator1DotDiameter (7) via each Image's own
    /// RectTransform — downscaling a high-res source is always safe.
    /// </summary>
    private const int CircleTextureDiameter = 320;

    /// <summary>
    /// Placeholder coin-store modal (see CoinStoreUI's own remarks) — its
    /// own always-active Canvas (CoinStoreCanvas, sortingOrder 20 —
    /// deliberately above every other modal canvas in this scene, all at
    /// 10, since this opens ON TOP OF Level Failed rather than replacing
    /// it: the player can still see/return to Continue/Exit level once
    /// they close this) hosting one inactive backdrop panel, reusing the
    /// exact same ConfirmationModal.png/ResponsiveModalBox/title-
    /// description/button building blocks CreateExitConfirmPanel already
    /// uses, so it reads as the same physical modal family despite being a
    /// placeholder. "Large" per this task's own requirement — the same
    /// ConfirmationModalMaxWidth ceiling Exit/Pause already use, the widest
    /// modal footprint this project has. Deliberately minimal content —
    /// a title, "Not enough coins", and one Close button — no purchase
    /// cards, no prices, no IAP: this is only the shell/navigation state a
    /// future real store replaces.
    /// </summary>
    private static CoinStoreUI CreateCoinStoreUI()
    {
        var canvasObject = new GameObject(
            "CoinStoreCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CoinStoreUI));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        GameObject panel = CreateModalBackdrop(canvasObject.transform, "CoinStorePanel");

        var confirmationModalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ConfirmationModalSpritePath);
        if (confirmationModalSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{ConfirmationModalSpritePath}'; the coin store modal will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Transform content = CreateResponsiveModalArtworkBox(
            panel.transform,
            confirmationModalSprite,
            ConfirmationModalWidthFraction,
            ConfirmationModalMaxWidth,
            ConfirmationModalVerticalOffsetFraction,
            ConfirmationModalContentReferenceWidth);

        var fredokaSemiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaSemiBoldFontAssetPath);
        if (fredokaSemiBold == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaSemiBoldFontAssetPath}'; the coin store modal's title/button label will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        var fredokaMedium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FredokaMediumFontAssetPath);
        if (fredokaMedium == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a TMP_FontAsset at '{FredokaMediumFontAssetPath}'; the coin store modal's description will fall back to TMP's default font. Run Tools/UI/Generate Fredoka Font Assets.");

        CreateCenteredTMPText(content, "Coin Store", new Vector2(0f, ExitConfirmTitleOffsetY), new Vector2(840f, 95f), ConfirmationModalTitleFontSize, ConfirmationModalTitleColor, fredokaSemiBold);
        CreateCenteredTMPText(content, "Not enough coins", new Vector2(0f, ExitConfirmDescriptionOffsetY), new Vector2(840f, 44f), ConfirmationModalDescriptionFontSize, ConfirmationModalDescriptionColor, fredokaMedium);

        var primaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PrimaryButtonSpritePath);
        if (primaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{PrimaryButtonSpritePath}'; the Close button will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Vector2 closeButtonSize = ResolveButtonArtworkSize(primaryButtonSprite, ExitConfirmButtonHeight);
        Button closeButton = CreateDialogButton(content, "CloseButton", "Close", new Vector2(0f, ExitConfirmButtonOffsetY), closeButtonSize, ExitConfirmButtonLabelFontSize, primaryButtonSprite, fredokaSemiBold, ConfirmationModalButtonLabelColor);

        var coinStoreUI = canvasObject.GetComponent<CoinStoreUI>();
        var serializedCoinStoreUI = new SerializedObject(coinStoreUI);
        serializedCoinStoreUI.FindProperty("panel").objectReferenceValue = panel;
        serializedCoinStoreUI.FindProperty("closeButton").objectReferenceValue = closeButton;
        serializedCoinStoreUI.ApplyModifiedPropertiesWithoutUndo();

        return coinStoreUI;
    }

    /// <summary>
    /// Level Failed modal: title, description, SadTurtle.png with its own
    /// ground-contact shadow (see CreateSadTurtleWithShadow), a "Need a
    /// little more room?" heading directly over SaveMeButton (see
    /// CreateSaveMeButton — wired to coinWalletService/coinStoreUI, see
    /// FailureUI's own remarks on saveMeButton for the real spend-then-
    /// rescue behaviour; see LevelFailedSaveMeButtonOffsetY's own PRODUCT
    /// RULE remarks for why RecoveryLine.png is deliberately never shown
    /// here), then the lower
    /// section: a separator, a "One more chance?"
    /// heading over a presentation-only ad-rewatch status row (see
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
    private static void CreateFailureUI(FailureController failureController, FailureRecoveryController failureRecoveryController, LevelExitFlowController levelExitFlowController, CoinWalletService coinWalletService, CoinStoreUI coinStoreUI)
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

        var sadTurtleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SadTurtleSpritePath);
        if (sadTurtleSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{SadTurtleSpritePath}'; the Level Failed modal will show no turtle illustration. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Sprite softContactShadowSprite = GenerateSoftContactShadowSprite();
        CreateSadTurtleWithShadow(content, sadTurtleSprite, softContactShadowSprite);

        // Deliberately no RecoveryLine.png here — see LevelFailedSaveMeButtonOffsetY's
        // own remarks (the PRODUCT RULE note) before adding one back.
        CreateCenteredTMPText(content, "Need a little more room?", new Vector2(0f, LevelFailedNeedRoomHeadingOffsetY), new Vector2(700f, 46f), LevelFailedHeadingFontSize, ConfirmationModalDescriptionColor, fredokaSemiBold);

        var primaryButtonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PrimaryButtonSpritePath);
        if (primaryButtonSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{PrimaryButtonSpritePath}'; the Save me/Continue buttons will show no background artwork. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        var coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoinSpritePath);
        if (coinSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{CoinSpritePath}'; the Save me button's price row will show no coin icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        Button saveMeButton = CreateSaveMeButton(content, primaryButtonSprite, coinSprite, fredokaSemiBold);

        Sprite circleSprite = GenerateCircleSprite();
        Sprite starSprite = GenerateStarSprite();
        CreateDottedStarSeparator(
            content,
            LevelFailedSeparator1OffsetY,
            LevelFailedSeparator1SegmentWidth,
            LevelFailedSeparator1CenterGap,
            LevelFailedSeparator1DotDiameter,
            LevelFailedSeparator1DotSpacing,
            LevelFailedSeparator1CenterStarDiameter,
            circleSprite,
            starSprite,
            LevelFailedSeparator1DotColor,
            LevelFailedSeparator1DotColor);
        CreateCenteredTMPText(content, "One more chance?", new Vector2(0f, LevelFailedSecondChanceHeadingOffsetY), new Vector2(700f, 46f), LevelFailedHeadingFontSize, ConfirmationModalDescriptionColor, fredokaSemiBold);

        var adIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AdIconSpritePath);
        if (adIconSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{AdIconSpritePath}'; the Level Failed modal's ad card will show no icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        CreateAdStatusCard(content, adIconSprite, fredokaSemiBold, fredokaMedium);

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
        serializedFailureUI.FindProperty("saveMeButton").objectReferenceValue = saveMeButton;
        serializedFailureUI.FindProperty("continueButton").objectReferenceValue = continueButton;
        serializedFailureUI.FindProperty("exitLevelButton").objectReferenceValue = exitLevelButton;
        serializedFailureUI.FindProperty("coinWalletService").objectReferenceValue = coinWalletService;
        serializedFailureUI.FindProperty("coinStoreUI").objectReferenceValue = coinStoreUI;
        serializedFailureUI.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// SadTurtle.png plus its own small ground-contact shadow. This task's
    /// own explicit correction: the previous pass's silhouette duplicate
    /// (same sprite, tinted + offset upper-left) read as a directional cast
    /// shadow — wrong category entirely for an illustration sitting inside a
    /// modal, which instead needs a soft elliptical patch directly beneath
    /// its body, the way a small object actually contacts a surface. See
    /// GenerateSoftContactShadowSprite for why no existing shadow system in
    /// this project (ContactShadowMesh included) was reused for this new
    /// shape.
    /// </summary>
    private static void CreateSadTurtleWithShadow(Transform parent, Sprite sadTurtleSprite, Sprite softContactShadowSprite)
    {
        float aspect = 1f;
        if (sadTurtleSprite != null && sadTurtleSprite.rect.height > 0f)
            aspect = sadTurtleSprite.rect.width / sadTurtleSprite.rect.height;

        var size = new Vector2(LevelFailedTurtleHeight * aspect, LevelFailedTurtleHeight);
        var position = new Vector2(0f, LevelFailedTurtleOffsetY);

        // Earlier sibling than SadTurtle itself so it renders behind it.
        var shadowObject = new GameObject("TurtleContactShadow", typeof(Image));
        shadowObject.transform.SetParent(parent, false);

        var shadowRect = shadowObject.GetComponent<RectTransform>();
        shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
        shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
        shadowRect.pivot = new Vector2(0.5f, 0.5f);
        shadowRect.sizeDelta = new Vector2(size.x * LevelFailedTurtleShadowWidthFraction, size.x * LevelFailedTurtleShadowWidthFraction * LevelFailedTurtleShadowHeightFraction);

        // Centred under the turtle (no horizontal/directional displacement —
        // this is a straight-down contact shadow, not a cast shadow), just
        // below its own bottom edge (position.y - size.y/2).
        shadowRect.anchoredPosition = new Vector2(position.x, position.y - size.y * 0.5f - LevelFailedTurtleShadowBottomNudge);

        var shadowImage = shadowObject.GetComponent<Image>();
        shadowImage.sprite = softContactShadowSprite;
        shadowImage.color = LevelFailedTurtleShadowColor;

        var turtleObject = new GameObject("SadTurtle", typeof(Image));
        turtleObject.transform.SetParent(parent, false);

        var turtleRect = turtleObject.GetComponent<RectTransform>();
        turtleRect.anchorMin = new Vector2(0.5f, 0.5f);
        turtleRect.anchorMax = new Vector2(0.5f, 0.5f);
        turtleRect.pivot = new Vector2(0.5f, 0.5f);
        turtleRect.sizeDelta = size;
        turtleRect.anchoredPosition = position;

        var turtleImage = turtleObject.GetComponent<Image>();
        turtleImage.sprite = sadTurtleSprite;
        turtleImage.color = Color.white;
        turtleImage.preserveAspect = true;
    }

    /// <summary>
    /// "Save me!" — this modal's primary rescue action. A custom two-row
    /// layout (label on top, coin+price row below) rather than
    /// CreateDialogButton's single centred label, since the hierarchy is
    /// "Save me!" then "[coin] 1000" as two distinct lines, not one.
    /// Presentation only — see FailureUI's own remarks on saveMeButton for
    /// why no click behaviour is wired to any Recovery Line gameplay effect
    /// yet.
    /// </summary>
    private static Button CreateSaveMeButton(Transform parent, Sprite primaryButtonSprite, Sprite coinSprite, TMP_FontAsset fredokaSemiBold)
    {
        Vector2 buttonSize = ResolveButtonArtworkSize(primaryButtonSprite, LevelFailedSaveMeButtonHeight);

        var buttonObject = new GameObject("SaveMeButton", typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = buttonSize;
        rectTransform.anchoredPosition = new Vector2(0f, LevelFailedSaveMeButtonOffsetY);

        var image = buttonObject.GetComponent<Image>();
        image.color = Color.white;
        if (primaryButtonSprite != null)
        {
            image.sprite = primaryButtonSprite;
            image.preserveAspect = true;
        }

        // FontStyles.Bold on top of Fredoka SemiBold (already this project's
        // heaviest Fredoka asset — see CreateEarnedCoinsGroup's own remarks
        // for the same technique) triggers TMP's faux-bold rendering, so
        // "Save me!" reads as the heavier, chunkier treatment the reference
        // shows without a new font asset.
        GameObject labelObject = CreateCenteredTMPText(buttonObject.transform, "Save me!", new Vector2(0f, LevelFailedSaveMeLabelOffsetY), buttonSize, LevelFailedSaveMeLabelFontSize, ConfirmationModalButtonLabelColor, fredokaSemiBold);
        labelObject.name = "Label";
        labelObject.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;

        CreateSaveMePriceGroup(buttonObject.transform, coinSprite, fredokaSemiBold);

        return buttonObject.GetComponent<Button>();
    }

    /// <summary>
    /// PriceGroup: CoinIcon + the "1000" value as one child group, mirroring
    /// CreateEarnedCoinsGroup's own coin-left/value-right composition so the
    /// coin and number always move together. The value is a plain
    /// TextMeshProUGUI (LevelFailedSaveMePriceValue), never baked into art,
    /// so a future Recovery Line economy hook can rebind it the same way
    /// VictoryUI rebinds EarnedCoinsValue.
    /// </summary>
    private static void CreateSaveMePriceGroup(Transform parent, Sprite coinSprite, TMP_FontAsset fredokaSemiBold)
    {
        var groupObject = new GameObject("PriceGroup", typeof(RectTransform));
        groupObject.transform.SetParent(parent, false);

        var groupRect = (RectTransform)groupObject.transform;
        groupRect.anchorMin = new Vector2(0.5f, 0.5f);
        groupRect.anchorMax = new Vector2(0.5f, 0.5f);
        groupRect.pivot = new Vector2(0.5f, 0.5f);
        groupRect.sizeDelta = new Vector2(LevelFailedSaveMePriceGroupWidth, LevelFailedSaveMePriceGroupHeight);
        groupRect.anchoredPosition = new Vector2(0f, LevelFailedSaveMePriceGroupOffsetY);

        var coinObject = new GameObject("CoinIcon", typeof(Image));
        coinObject.transform.SetParent(groupRect, false);

        float coinAspect = 1f;
        if (coinSprite != null && coinSprite.rect.height > 0f)
            coinAspect = coinSprite.rect.width / coinSprite.rect.height;

        var coinRect = coinObject.GetComponent<RectTransform>();
        coinRect.anchorMin = new Vector2(0.5f, 0.5f);
        coinRect.anchorMax = new Vector2(0.5f, 0.5f);
        coinRect.pivot = new Vector2(0.5f, 0.5f);
        coinRect.sizeDelta = new Vector2(LevelFailedSaveMeCoinIconHeight * coinAspect, LevelFailedSaveMeCoinIconHeight);
        coinRect.anchoredPosition = new Vector2(LevelFailedSaveMeCoinIconOffsetX, 0f);

        var coinImage = coinObject.GetComponent<Image>();
        coinImage.sprite = coinSprite;
        coinImage.preserveAspect = true;

        GameObject priceValueObject = CreateCenteredTMPText(
            groupRect,
            LevelFailedSaveMePriceValue,
            new Vector2(LevelFailedSaveMePriceValueOffsetX, 0f),
            new Vector2(160f, LevelFailedSaveMePriceGroupHeight),
            LevelFailedSaveMePriceFontSize,
            LevelFailedSaveMePriceColor,
            fredokaSemiBold);
        priceValueObject.name = "PriceValue";

        // TextAlignmentOptions.Center (CreateCenteredTMPText's default)
        // centres on the FONT's own line-height box (ascender to
        // descender), not the digits' actual rendered bounds — for an
        // all-caps-height numeral with no descender, that box's own
        // asymmetric padding visually pushed "1000" noticeably below
        // CoinIcon's true centre (confirmed by rendering and comparing
        // against CoinIcon's own centre). CenterGeoAligned instead centres
        // on the text's own actual glyph geometry, so it lines up with
        // CoinIcon's centre exactly without any hand-tuned pixel offset —
        // never the "baseline trick that pushes 1000 downward" this task
        // explicitly warns against; this is the opposite correction.
        var priceValueText = priceValueObject.GetComponent<TextMeshProUGUI>();
        priceValueText.alignment = TextAlignmentOptions.CenterGeoAligned;
        priceValueText.fontStyle = FontStyles.Bold;
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
    /// Dotted-line decorative separator, shared by Level Failed's first
    /// separator and Level Complete's own separator between its description
    /// and action buttons — one implementation, each caller supplying its
    /// own offset/sizing/colour constants, rather than two near-duplicate
    /// dot-placement loops. Reuses GenerateCircleSprite for the dots — no new
    /// art asset.
    ///
    /// includeCenterAccent optionally adds a centred star
    /// (GenerateStarSprite) in the gap between the two dotted segments —
    /// Level Failed's own separator still uses this (unchanged); Level
    /// Complete's passes false, since its centre star's sprite reference
    /// went stale (rendering as a plain tinted square, not a star) once
    /// CreateFailureUI's own later GenerateStarSprite call deleted and
    /// recreated the same shared StarSpriteAssetPath asset — both modals were
    /// regenerating one shared file rather than reusing one shared
    /// reference. Removing Level Complete's center accent entirely (per its
    /// own task) sidesteps that without touching Level Failed's — the two
    /// dotted segments alone are unaffected by it either way.
    /// </summary>
    private static void CreateDottedStarSeparator(
        Transform parent,
        float offsetY,
        float segmentWidth,
        float centerGap,
        float dotDiameter,
        float dotSpacing,
        float centerDiameter,
        Sprite circleSprite,
        Sprite starSprite,
        Color dotColor,
        Color centerColor,
        bool includeCenterAccent = true)
    {
        float centerHalfGap = centerGap / 2f;
        int dotsPerSegment = Mathf.Max(1, Mathf.RoundToInt(segmentWidth / dotSpacing));

        for (int side = -1; side <= 1; side += 2)
        {
            float segmentStart = side * centerHalfGap;
            for (int i = 0; i < dotsPerSegment; i++)
            {
                float t = (i + 0.5f) / dotsPerSegment;
                float x = segmentStart + side * t * segmentWidth;
                CreateDot(parent, new Vector2(x, offsetY), dotDiameter, circleSprite, dotColor);
            }
        }

        if (!includeCenterAccent)
            return;

        CreateDot(parent, new Vector2(0f, offsetY), centerDiameter, starSprite, centerColor);
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
    /// Procedurally draws a soft, circular radial-gradient patch (solid-ish
    /// centre, smoothstep fade to fully transparent at the edge) into a new
    /// Texture2D — SadTurtle's own ground-contact shadow shape
    /// (CreateSadTurtleWithShadow squashes it into an ellipse via
    /// RectTransform sizeDelta; the texture itself stays circular, exactly
    /// like GenerateCircleSprite's own "one round shape, stretched per use"
    /// convention).
    ///
    /// Deliberately a NEW, separate generator rather than reusing
    /// GenerateCircleSprite (whose ~1px antialiased SDF edge is a hard
    /// edge, wrong for a "soft edge" contact shadow per this task's own
    /// wording) or ContactShadowMesh (a 3D world-space Mesh/Material for
    /// gameplay characters — a different system this task explicitly says
    /// not to touch or extend). The soft-falloff MATH here is the same
    /// general idea ContactShadowMesh's own CreateRadialTexture already
    /// uses (plateau + smoothstep), because that is simply what "soft
    /// radial gradient" means, not because this calls into or shares any
    /// code with it — this is its own small, local, UI-only primitive, the
    /// same category of thing as GenerateCircleSprite/GenerateStarSprite.
    ///
    /// Saved as its own small .asset (SoftContactShadowSpriteAssetPath),
    /// same idempotent regenerate-on-every-run convention as this file's
    /// other generated sprites.
    /// </summary>
    private static Sprite GenerateSoftContactShadowSprite()
    {
        const float InnerPlateauRadius = 0.3f;

        var texture = new Texture2D(CircleTextureDiameter, CircleTextureDiameter, TextureFormat.RGBA32, false)
        {
            name = "SoftContactShadow",
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
                float normalizedDistance = Mathf.Sqrt(px * px + py * py) / radius;
                float alpha = 1f - Mathf.SmoothStep(InnerPlateauRadius, 1f, normalizedDistance);
                pixels[y * CircleTextureDiameter + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        }

        return SaveGeneratedSprite(texture, pixels, SoftContactShadowSpriteAssetPath, "SoftContactShadow");
    }

    /// <summary>
    /// Procedurally draws a flat white circle into a new Texture2D via an
    /// analytic signed-distance field (~1px antialiased edge) — a generic,
    /// content-free UI primitive shape, the same category of thing as
    /// EnergyBarPrefabBuilder's own GenerateStadiumSprite (a rounded-box SDF;
    /// this is its simpler fully-round special case) and not "art" in the
    /// sense of this task's "do not create new artwork" constraint. Reused,
    /// tinted differently and at different RectTransform sizes, for both
    /// separators' dots and the second separator's centre dot — one shape,
    /// many uses, rather than a texture per use.
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

    /// <summary>
    /// Shared tail end of every GenerateXSprite method above: bakes pixels
    /// into the texture, (re)persists it as its own small .asset (see
    /// GenerateCircleSprite's remarks on why persistence matters), and
    /// returns the reloaded Sprite sub-asset. border defaults to zero
    /// (every current caller's behaviour); a non-zero border would make the
    /// returned sprite usable with Image.Type.Sliced so a rounded-corner
    /// shape stretches cleanly to any width/height instead of distorting
    /// its corners, if a future generated sprite needs that.
    /// </summary>
    private static Sprite SaveGeneratedSprite(Texture2D texture, Color32[] pixels, string assetPath, string spriteName, Vector4 border = default)
    {
        texture.SetPixels32(pixels);
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 100f,
            extrude: 0,
            SpriteMeshType.FullRect,
            border);
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
    /// <summary>Returns the created GameObject (still named "Text" by default) purely so a rare caller can rename it for clarity afterward — e.g. CreateVictoryUI's separator star — every other caller already ignores the return value, so this is not a behavior change for them.</summary>
    private static GameObject CreateCenteredTMPText(Transform parent, string content, Vector2 anchoredPosition, Vector2 size, float fontSize, Color color, TMP_FontAsset font)
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

        return textObject;
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
        LevelExitFlowController levelExitFlowController,
        CoinWalletService coinWalletService)
    {
        (PauseUI pauseUI, ExitConfirmationUI exitConfirmationUI) = CreateGameplayModalUI(pauseFlowController, levelExitFlowController);
        CreateTopHudCanvas(levelProgressionController, gameplaySpeedController, levelExitFlowController, pauseFlowController, exitConfirmationUI, pauseUI, coinWalletService);
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

    /// <summary>
    /// Plain artwork-only button — no label child at all, unlike
    /// CreateDialogButton — for backgroundSprite artwork that already bakes
    /// its own text/icon content into the PNG (e.g. DoubleCoinsButton.png),
    /// where adding a CreateCenteredText(TMP) label on top would duplicate
    /// text the artwork already renders. The whole sprite is the Button's
    /// own hit area (Image + Button on the same RectTransform, same as
    /// CreateDialogButton), so the entire PNG is clickable as one control.
    /// </summary>
    private static Button CreateImageButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Sprite backgroundSprite)
    {
        var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        var rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;
        rectTransform.anchoredPosition = anchoredPosition;

        var image = buttonObject.GetComponent<Image>();
        image.color = Color.white;
        if (backgroundSprite != null)
        {
            image.sprite = backgroundSprite;
            image.preserveAspect = true;
        }

        return buttonObject.GetComponent<Button>();
    }

    private const string HudExitSpritePath = "Assets/Art/UI/Classic/HUD/HudExit.png";
    private const string HudPauseSpritePath = "Assets/Art/UI/Classic/HUD/HudPause.png";
    private const string HudSpeed1xSpritePath = "Assets/Art/UI/Classic/HUD/HudSpeed1x.png";
    private const string HudSpeed2xSpritePath = "Assets/Art/UI/Classic/HUD/HudSpeed2x.png";
    private const string LevelLabelSpritePath = "Assets/Art/UI/Classic/HUD/Level/LevelLabel.png";
    private const string LevelDigitSpritePathFormat = "Assets/Art/UI/Classic/HUD/Level/Digit{0}.png";

    /// <summary>
    /// Reserved horizontal footprint for the whole Coin+Amount group — not
    /// a drawn container any more (see CreateCoinBalanceGroup's own remarks
    /// on why the capsule/background was removed). Grown substantially
    /// (through several passes: 196 -> 216 -> this pass's 256) to fit
    /// BalanceText's own much larger box (see HudCoinBalanceTextBoxWidth)
    /// without changing this group's screen ANCHOR (HudCoinGroupOffsetX —
    /// see that constant's own remarks for the current Speed/Pause gap):
    /// since the group is right-corner-anchored (pivot 1,1), growing this
    /// width only extends its LEFT edge further left (toward Level),
    /// leaving its right edge — and therefore the Speed/Pause gap —
    /// completely unaffected. The resulting extra room eaten from the
    /// Level gap (still ~84 at 1080, ~378 at 1668 — see
    /// CreateCoinBalanceGroup's own remarks) stays comfortably positive.
    /// </summary>
    private const float HudCoinGroupWidth = 256f;

    private const float HudCoinGroupHeight = 64f;

    /// <summary>
    /// Level's own horizontal offset from the HUD canvas's true centre
    /// (anchor 0.5) — no longer 0. The top row has FOUR competing groups
    /// (Exit, Level, Coin, Speed/Pause), and Speed/Pause's own reserved
    /// column (~232 units wide, right-corner-anchored) plus Coin's own
    /// footprint (196 wide) plus the gaps around both need more room than
    /// simply centering Level on the full canvas width leaves on its right
    /// — which is exactly why the coin group used to overlap "Level 1" at
    /// 1080 width (Level's centred right edge and the coin group's
    /// right-corner-anchored left edge crossed by ~30px there; the same
    /// pair of positions only happened to clear each other on a wider iPad
    /// canvas, which is why the bug was resolution-specific). Shifting
    /// Level's own centre left by a fixed, resolution-independent amount
    /// reserves the same guaranteed room for Coin on every canvas width,
    /// rather than assuming Level is centred on screen. See
    /// HudCoinGroupOffsetX's own remarks for the matching derivation on
    /// Coin's side.
    /// </summary>
    private const float HudLevelDisplayOffsetX = -160f;

    /// <summary>
    /// Coin group's EDIT-TIME-ONLY fallback horizontal offset from the HUD
    /// canvas's own top-right corner — a starting position for this
    /// GameObject's own RectTransform, visible only in the Scene view before
    /// Play (much like LevelDisplay's own children don't exist until
    /// TopHudUI.Start runs either). It is NOT the source of truth for where
    /// CoinIcon/BalanceDigits actually render: CoinBalanceLayout repositions
    /// both of those, in world space, every time the balance or Level
    /// display changes (see its own class remarks) — anchored off
    /// SpeedButton's and LevelDisplay's own ACTUAL resolved bounds, never a
    /// hand-picked offset from this corner. An earlier version of this
    /// constant WAS that source of truth (assuming a worst-case content
    /// width and reserving a fixed box for it), which is exactly what broke:
    /// every time the assumed worst case was wrong, the only fix was to
    /// shift this offset further left, which by construction moves the
    /// whole group TOWARD Level — eventually crowding it to a ~2.75-unit
    /// gap. See CoinBalanceLayout for the actual, structural fix.
    /// </summary>
    private const float HudCoinGroupOffsetX = -242f;

    /// <summary>
    /// Top edge offset for the group's anchor=pivot point, chosen so its
    /// own vertical CENTER lands at Y=-96 — the same row centre backButton
    /// (-60, height 72), pauseButton (-60, height 72), and speedButton (-48,
    /// height 96) already all share despite their differing heights/
    /// offsets, so the coin group reads as part of the same HUD row rather
    /// than sitting slightly above or below it.
    /// </summary>
    private const float HudCoinGroupOffsetY = -(96f - HudCoinGroupHeight / 2f);

    /// <summary>
    /// Coin icon footprint — measured directly off
    /// reference/UI/CoinsTarget2.png: its coin's own gold-core diameter
    /// (~60px) is essentially equal to "Level 1"'s own glyph height (~53px)
    /// there, i.e. the coin reads as tall as Level's own digits, not a
    /// small inline accent. Applying that ratio to this project's own real
    /// LevelDigitBoxSize-driven Level glyph height (~63px, since a digit
    /// sprite fills ~99% of its own square box) gives ~64 here — up
    /// substantially from the previous pass's 46, which is exactly the
    /// "too small/light" problem this pass fixes.
    /// </summary>
    private const float HudCoinIconSize = 64f;

    /// <summary>Left inset of CoinIcon from the reserved footprint's own left edge — no visible edge to line up with any more, just enough that the icon doesn't sit exactly at the reserved region's boundary.</summary>
    private const float HudCoinGroupLeftInset = 6f;

    /// <summary>
    /// Gap between CoinIcon's ACTUAL right edge and the digit row's own
    /// left edge — enforced at runtime by CoinBalanceLayout.Reposition
    /// (see its own remarks), not baked into a hand-computed anchoredPosition
    /// here any more. A previous pass computed the digit row's start
    /// position directly in this method using HudCoinIconSize alone, which
    /// silently assumed Coin.png renders exactly that wide — true only if
    /// Coin.png were perfectly square (it is not: 1017x1006), so the
    /// "safe" gap was quietly ~0.7 units smaller than intended, and more
    /// importantly could never adapt if Coin.png's own aspect ratio ever
    /// changed. CoinBalanceLayout removes that assumption by reading
    /// CoinIcon's own resolved rect.width directly every time.
    ///
    /// Reduced from 12 to 9, then to 6 (see CoinBalanceLayout's own
    /// coinToNumberGap remarks) as priority-4 of the "no digit-height
    /// shrink through 99999" fix (see HudCoinDigitsMaxWidth's own remarks
    /// for the full priority order) — a small, fixed, always-enforced
    /// separation is kept, just tighter, freeing a little extra room for
    /// the digit row before it would otherwise need any of the width this
    /// fix's own HudCoinGroupOffsetX/HudCoinDigitsMaxWidth changes provide.
    /// </summary>
    private const float HudCoinToNumberGap = 6f;

    /// <summary>
    /// Enforced gap between the digit row's own actual rendered right edge
    /// and SpeedButton's own actual left edge — CoinBalanceLayout's
    /// right-side anchor (digitRow.right = SpeedButton.left -
    /// HudCoinToSpeedGap), recomputed every time the balance changes. Kept
    /// close to the old fixed reserved gap (10) that this whole group used
    /// to be positioned to assume, so 3/4-digit balances (900, 1000 —
    /// already approved) keep exactly the same visual relationship to
    /// Speed/Pause they already had. Note this value never actually
    /// determines the WORST-CASE gap to Speed/Pause — it algebraically
    /// cancels out of Reposition's own math the instant the
    /// HudMinLevelToCoinGap floor engages (see that constant's own remarks),
    /// so it only ever governs how generous the gap looks for a balance that
    /// does NOT need the floor — safe to keep comfortable here.
    /// </summary>
    private const float HudCoinToSpeedGap = 10f;

    /// <summary>
    /// Minimum enforced gap between LevelDisplay's own actual right edge and
    /// this assembly's own leftmost extent (CoinIcon's left edge) —
    /// CoinBalanceLayout's left-side floor (coin.left &gt;=
    /// LevelDisplay.right + HudMinLevelToCoinGap), which this assembly's
    /// Reposition() will shift itself right to preserve even if that means
    /// growing HudCoinToSpeedGap beyond its nominal value for an
    /// exceptionally wide balance. A real, clearly-visible breathing gap at
    /// the narrowest supported canvas (1080 width) — not the ~2.75-unit
    /// algebraic-only positive gap an earlier, purely offset-driven version
    /// of this group left for a worst-case 5-digit balance, which read as
    /// visually crowded/overlapping in the actual Game View despite being
    /// technically non-negative.
    ///
    /// 24, not a larger round number like 40: measured (via real
    /// GetWorldCorners bounds in a driven Play Mode pass, at BOTH 1080 and
    /// 1668 canvas widths) against the actual fixed geometry on both sides —
    /// LevelDisplay's and SpeedButton's own real positions, both completely
    /// unavailable to move — 1080 width leaves only ~338 units total between
    /// them. A worst-case 5-digit balance ("99999", even at the tightest
    /// HudCoinDigitGapFloor2/HudCoinDigitGroupGapFloor2 spacing this task
    /// allows) needs ~240 of that for its own digit row alone, plus
    /// CoinIcon's own fixed ~64.7, plus HudCoinToNumberGap (6, unchanged so
    /// 3/4-digit balances keep their exact prior look) — leaving only ~27
    /// units to split between this floor and a still-positive Speed/Pause
    /// gap. 24 is the largest value that keeps a real (not razor-thin)
    /// margin on BOTH sides at the single worst case (99999 at 1080 width);
    /// every other tested value/resolution combination clears both sides
    /// with 24-400+ units to spare (see this task's own measured report).
    /// </summary>
    private const float HudMinLevelToCoinGap = 24f;

    private const string HudCoinDigitSpritePathFormat = "Assets/Art/UI/Classic/Digits/Digit{0}.png";

    /// <summary>
    /// Shared displayed height for every digit sprite (before any
    /// HudCoinDigitsMaxWidth shrink) — each digit's own displayed WIDTH
    /// then follows from its own native aspect ratio at this height (see
    /// SpriteDigitNumberDisplay/DigitLayout), never a fixed/equal width.
    /// Replaces the previous (TMP) pass's font-size-driven sizing entirely
    /// — these digit sprites bake in their own dimensional artwork
    /// directly, so there is no font-metric conversion involved any more.
    /// Chosen close to that previous pass's own effective target (a
    /// visible glyph height around 63-65px, ~90% of this project's own
    /// actual "1x" HUD icon glyph height, ~70px — see HudSpeed1x.png):
    /// each digit sprite's own visible ink already fills ~97% of its
    /// source canvas height (confirmed directly against the supplied PNGs,
    /// consistently across digits), so a 66-unit box renders a ~64px-tall
    /// visible digit.
    /// </summary>
    private const float HudCoinDigitHeight = 66f;

    /// <summary>
    /// Gap between adjacent digits within the same thousands group. Raised
    /// from an earlier pass's 4: the source PNGs' own trim margin is indeed
    /// near-zero (confirmed directly against each digit's own imported
    /// sprite rect — e.g. Digit9 is 713x1002, Digit1 477x1002, both already
    /// alpha-trimmed), but that meant a 4-unit gap read as visibly
    /// crowded/near-touching once actually rendered — at HudCoinDigitHeight
    /// (66) a digit is roughly 32-51 units wide, so 4 units of separation
    /// was only ~8-12% of a digit's own width. 7 keeps every digit
    /// unambiguously separated while staying "small"/subtle, and still
    /// keeps "900" (the current common case) comfortably under
    /// HudCoinDigitsMaxWidth with no shrink (~160 of 170 units).
    /// </summary>
    private const float HudCoinDigitGap = 7f;

    /// <summary>Additional gap (added to HudCoinDigitGap) at a thousands-group boundary — represents the grouping separator as spacing (e.g. "1 250", "125 000") since no comma/space sprite exists. Raised alongside HudCoinDigitGap so the boundary gap (16 units total) stays visibly, but only moderately, bigger than the plain digit gap (7) — not huge.</summary>
    private const float HudCoinDigitGroupGap = 9f;

    /// <summary>
    /// Tighter HudCoinDigitGap tried, still at full HudCoinDigitHeight, only
    /// once a balance does not already fit HudCoinDigitsMaxWidth at the
    /// normal gap above (5-digit balances and up — see
    /// SpriteDigitNumberDisplay's own progressive-fit remarks). Reused from
    /// this project's own earlier digit-gap value (HudCoinDigitGap's own
    /// remarks record it was 4 before being raised to 7 for legibility,
    /// still confirmed comfortably separated at the time) rather than an
    /// unvetted new number.
    /// </summary>
    private const float HudCoinDigitGapFloor = 4f;

    /// <summary>Tighter HudCoinDigitGroupGap tried alongside HudCoinDigitGapFloor — kept meaningfully bigger than HudCoinDigitGapFloor (6 vs 4) so a thousands-group boundary still reads as separated from a plain digit gap even at this tighter spacing.</summary>
    private const float HudCoinDigitGroupGapFloor = 6f;

    /// <summary>
    /// Second, tighter HudCoinDigitGap tried — still at full
    /// HudCoinDigitHeight — only for a 5+ digit balance (10000+) whose
    /// HudCoinDigitGapFloor/HudCoinDigitGroupGapFloor width still doesn't fit
    /// HudCoinDigitsMaxWidth. Added because the first floor alone left a
    /// real shortfall of ~65-75 units for 5-digit balances at
    /// HudCoinDigitsMaxWidth (digit glyph widths alone already exceed it),
    /// which the previous single-floor version could only close by falling
    /// straight to the uniform scale-down — visibly shrinking 10000/99999
    /// well below 900/1000. This second floor closes as much of that
    /// shortfall as spacing alone can, so the eventual scale-down only has
    /// to cover the genuinely unavoidable remainder. Explicitly gated on
    /// digit count (not just "still doesn't fit") in SpriteDigitNumberDisplay
    /// itself so a 4-digit balance (e.g. 9999, which also doesn't fit at the
    /// first floor) keeps rendering on exactly its previous path — this
    /// value only ever affects 5+ digit balances.
    ///
    /// Tightened again, from 2 to 1, as part of the Level-collision fix (see
    /// HudMinLevelToCoinGap's own remarks): the fixed geometry on both sides
    /// of this assembly (LevelDisplay, SpeedButton — neither movable) leaves
    /// real room for a worst-case 5-digit digit row of only ~240 units at
    /// 1080 width; still "moderate" per this task's own priority order
    /// (digits stay clearly, individually separated — nowhere near 0), not
    /// the most aggressive tightening tried.
    /// </summary>
    private const float HudCoinDigitGapFloor2 = 1f;

    /// <summary>Second, tighter HudCoinDigitGroupGap tried alongside HudCoinDigitGapFloor2 — kept meaningfully bigger (1.5 vs 1) so a thousands-group boundary still reads as separated even at this tightest spacing. Tightened from 3 to 1.5 alongside HudCoinDigitGapFloor2 — see its own remarks.</summary>
    private const float HudCoinDigitGroupGapFloor2 = 1.5f;

    /// <summary>
    /// Maximum allowed digit-row width at HudCoinDigitHeight before
    /// SpriteDigitNumberDisplay's last-resort uniform shrink ever applies —
    /// only reached after HudCoinDigitGapFloor/HudCoinDigitGroupGapFloor (and,
    /// for 5+ digit balances, HudCoinDigitGapFloor2/HudCoinDigitGroupGapFloor2
    /// too) have already been tried and the row still does not fit (see
    /// SpriteDigitNumberDisplay's own progressive-fit remarks).
    ///
    /// 242: set from the SAME real-geometry measurement HudMinLevelToCoinGap's
    /// own remarks describe, not independently. This is no longer "as large
    /// as possible while still avoiding shrink" (an earlier pass's own
    /// reasoning, which is exactly what let the digit row grow wide enough
    /// to crowd Level once CoinBalanceLayout had to anchor it near
    /// SpeedButton) — it is now set to just clear the worst real 5-digit
    /// case ("99999") at HudCoinDigitGapFloor2/HudCoinDigitGroupGapFloor2
    /// spacing (~240.3 units), with a small margin, so 99999 never needs the
    /// uniform-scale step either, while keeping the digit row itself as
    /// narrow as the tightened floor2 spacing actually allows — the room
    /// this frees up on both sides is what makes HudMinLevelToCoinGap's own
    /// 24-unit floor achievable without the row also overlapping
    /// SpeedButton (see that constant's own remarks for the exact
    /// three-way tradeoff this value is part of).
    /// </summary>
    private const float HudCoinDigitsMaxWidth = 242f;

    /// <summary>
    /// The gameplay HUD's Coin+Amount group: Coin.png immediately followed
    /// by the live wallet balance, with NO background/capsule/border of any
    /// kind — a plain bordered capsule read as a "generic flat UI badge"
    /// pasted between three dimensional, sprite-art HUD elements (Exit,
    /// Level, Speed/Pause), so it was removed entirely rather than
    /// re-themed (see an earlier pass). The balance itself is now rendered
    /// as a row of Digit0-Digit9 sprites (SpriteDigitNumberDisplay) rather
    /// than TMP text — those digit sprites bake in the dimensional artwork
    /// TMP could not reproduce satisfactorily, so no TMP effects/outline/
    /// recolouring are layered on top here; the artwork is used as
    /// supplied. What is left is exactly two elements, CoinIcon and the
    /// digit row, anchored to the HUD canvas's own top-right corner — the
    /// same anchor backButton/speedButton/pauseButton already use — so this
    /// group reads as belonging to the right-side info/control cluster
    /// rather than as an extension of Level: see HudCoinGroupOffsetX's own
    /// remarks for why a fixed, width-independent gap to Speed/Pause (with
    /// Level's own gap growing on wider canvases instead) is exactly the
    /// intended relationship, not an accident of a single-resolution
    /// offset. Presentation only: all the actual reading/subscription
    /// logic lives in CoinBalanceHudView, wired to coinWalletService here
    /// exactly like every other economy-consuming component in this
    /// project — this method never touches CoinWallet/EconomyConfig
    /// itself.
    /// </summary>
    private static void CreateCoinBalanceGroup(Transform parent, CoinWalletService coinWalletService, Button speedButton, TopHudUI topHudUI)
    {
        var groupObject = new GameObject("CoinBalanceGroup", typeof(RectTransform), typeof(CoinBalanceHudView), typeof(CoinBalanceLayout));
        groupObject.transform.SetParent(parent, false);

        // Top-right corner anchored (1, 1) — the same anchor point
        // backButton/speedButton/pauseButton already use (see
        // HudCoinGroupOffsetX's own remarks for why, and CreateCornerButton
        // for the shared convention). No longer centre-anchored like
        // LevelDisplay — that was a previous pass's approach.
        var groupRect = (RectTransform)groupObject.transform;
        groupRect.anchorMin = new Vector2(1f, 1f);
        groupRect.anchorMax = new Vector2(1f, 1f);
        groupRect.pivot = new Vector2(1f, 1f);
        groupRect.sizeDelta = new Vector2(HudCoinGroupWidth, HudCoinGroupHeight);
        groupRect.anchoredPosition = new Vector2(HudCoinGroupOffsetX, HudCoinGroupOffsetY);

        var coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CoinSpritePath);
        if (coinSprite == null)
            Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{CoinSpritePath}'; the HUD coin balance group will show no coin icon. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");

        float coinAspect = 1f;
        if (coinSprite != null && coinSprite.rect.height > 0f)
            coinAspect = coinSprite.rect.width / coinSprite.rect.height;

        var coinObject = new GameObject("CoinIcon", typeof(Image));
        coinObject.transform.SetParent(groupRect, false);
        var coinRect = coinObject.GetComponent<RectTransform>();
        coinRect.anchorMin = new Vector2(0.5f, 0.5f);
        coinRect.anchorMax = new Vector2(0.5f, 0.5f);
        coinRect.pivot = new Vector2(0.5f, 0.5f);
        coinRect.sizeDelta = new Vector2(HudCoinIconSize * coinAspect, HudCoinIconSize);
        coinRect.anchoredPosition = new Vector2(-HudCoinGroupWidth / 2f + HudCoinGroupLeftInset + HudCoinIconSize / 2f, 0f);
        var coinImage = coinObject.GetComponent<Image>();
        coinImage.sprite = coinSprite;
        coinImage.preserveAspect = true;

        var digitSprites = new Sprite[10];
        for (int digit = 0; digit < digitSprites.Length; digit++)
        {
            string digitSpritePath = string.Format(HudCoinDigitSpritePathFormat, digit);
            digitSprites[digit] = AssetDatabase.LoadAssetAtPath<Sprite>(digitSpritePath);
            if (digitSprites[digit] == null)
                Debug.LogError($"BootstrapSceneCreator: could not load a Sprite at '{digitSpritePath}'; the HUD coin balance will show no icon for digit {digit}. Check its TextureImporter Texture Type is set to 'Sprite (2D and UI)'.");
        }

        // digitsRect.anchoredPosition.x is intentionally left at its
        // default (0) here — CoinBalanceLayout.Reposition() (called for
        // real, below, right after it's wired up) is the ONLY place that
        // computes this value, from CoinIcon's actual resolved world
        // bounds. A previous version of this method computed a
        // "placeholder" X here using -HudCoinGroupWidth/2 as an assumed
        // left edge of CoinBalanceGroup — which is only correct if
        // CoinBalanceGroup were centre-pivoted. It is not (it's pivoted at
        // its own parent's top-right corner, see groupRect above), so that
        // placeholder was silently wrong by ~HudCoinGroupWidth/2 and, once
        // CoinBalanceLayout's own runtime formula repeated the identical
        // mistake, nothing ever corrected it — this is exactly why the
        // digit row still visibly overlapped Coin.png in the Game View.
        // Baking the position via the same Reposition() call used at
        // runtime removes any chance of the edit-time and runtime values
        // ever being computed by two different (and divergently wrong)
        // formulas again.
        var digitsObject = new GameObject("BalanceDigits", typeof(RectTransform), typeof(SpriteDigitNumberDisplay));
        digitsObject.transform.SetParent(groupRect, false);
        var digitsRect = (RectTransform)digitsObject.transform;
        digitsRect.anchorMin = new Vector2(0f, 0.5f);
        digitsRect.anchorMax = new Vector2(0f, 0.5f);
        digitsRect.pivot = new Vector2(0f, 0.5f);
        digitsRect.sizeDelta = new Vector2(0f, HudCoinDigitHeight);

        var spriteDigitNumberDisplay = digitsObject.GetComponent<SpriteDigitNumberDisplay>();
        var serializedDigitDisplay = new SerializedObject(spriteDigitNumberDisplay);
        var digitSpritesProperty = serializedDigitDisplay.FindProperty("digitSprites");
        digitSpritesProperty.arraySize = digitSprites.Length;
        for (int digit = 0; digit < digitSprites.Length; digit++)
            digitSpritesProperty.GetArrayElementAtIndex(digit).objectReferenceValue = digitSprites[digit];
        serializedDigitDisplay.FindProperty("digitHeight").floatValue = HudCoinDigitHeight;
        serializedDigitDisplay.FindProperty("digitGap").floatValue = HudCoinDigitGap;
        serializedDigitDisplay.FindProperty("groupGap").floatValue = HudCoinDigitGroupGap;
        serializedDigitDisplay.FindProperty("digitGapFloor").floatValue = HudCoinDigitGapFloor;
        serializedDigitDisplay.FindProperty("groupGapFloor").floatValue = HudCoinDigitGroupGapFloor;
        serializedDigitDisplay.FindProperty("digitGapFloor2").floatValue = HudCoinDigitGapFloor2;
        serializedDigitDisplay.FindProperty("groupGapFloor2").floatValue = HudCoinDigitGroupGapFloor2;
        serializedDigitDisplay.FindProperty("maxWidth").floatValue = HudCoinDigitsMaxWidth;
        serializedDigitDisplay.ApplyModifiedPropertiesWithoutUndo();

        // CoinBalanceLayout derives CoinIcon's/digitsRect's own final
        // position from SpeedButton's and LevelDisplay's own ACTUAL
        // resolved world bounds (see its own class remarks) — LevelDisplay
        // does not exist yet at scene-creation time (TopHudUI builds it at
        // runtime, in Start), so unlike the previous version of this
        // method, Reposition() is deliberately NOT called here: there is no
        // meaningful position to bake yet, and CoinBalanceLayout's own
        // Awake/event wiring (see its class remarks) always computes the
        // real position at runtime instead, the same way TopHudUI's own
        // Level digits do not appear until Play either.
        var coinBalanceLayout = groupObject.GetComponent<CoinBalanceLayout>();
        var serializedCoinBalanceLayout = new SerializedObject(coinBalanceLayout);
        serializedCoinBalanceLayout.FindProperty("coinRectTransform").objectReferenceValue = coinRect;
        serializedCoinBalanceLayout.FindProperty("numberRectTransform").objectReferenceValue = digitsRect;
        serializedCoinBalanceLayout.FindProperty("speedButtonRectTransform").objectReferenceValue = speedButton != null ? speedButton.GetComponent<RectTransform>() : null;
        serializedCoinBalanceLayout.FindProperty("topHudUI").objectReferenceValue = topHudUI;
        serializedCoinBalanceLayout.FindProperty("coinWalletService").objectReferenceValue = coinWalletService;
        serializedCoinBalanceLayout.FindProperty("coinToNumberGap").floatValue = HudCoinToNumberGap;
        serializedCoinBalanceLayout.FindProperty("coinToSpeedGap").floatValue = HudCoinToSpeedGap;
        serializedCoinBalanceLayout.FindProperty("minLevelToCoinGap").floatValue = HudMinLevelToCoinGap;
        serializedCoinBalanceLayout.ApplyModifiedPropertiesWithoutUndo();

        var coinBalanceHudView = groupObject.GetComponent<CoinBalanceHudView>();
        var serializedCoinBalanceHudView = new SerializedObject(coinBalanceHudView);
        serializedCoinBalanceHudView.FindProperty("coinWalletService").objectReferenceValue = coinWalletService;
        serializedCoinBalanceHudView.FindProperty("spriteDigitNumberDisplay").objectReferenceValue = spriteDigitNumberDisplay;
        serializedCoinBalanceHudView.ApplyModifiedPropertiesWithoutUndo();
    }

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
        PauseUI pauseUI,
        CoinWalletService coinWalletService)
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
        RectTransform levelDisplayContainer = CreateAnchoredContainer(canvasObject.transform, "LevelDisplay", new Vector2(0.5f, 1f), new Vector2(HudLevelDisplayOffsetX, -96f));
        Button pauseButton = CreateCornerButton(canvasObject.transform, "PauseButton", hudPauseSprite, new Vector2(1f, 1f), new Vector2(-48f, -60f), new Vector2(72f, 72f), out _);
        Button speedButton = CreateCornerButton(canvasObject.transform, "SpeedButton", hudSpeed1xSprite, new Vector2(1f, 1f), new Vector2(-48f - 72f - 16f, -48f), new Vector2(96f, 96f), out Image speedButtonIcon);
        CreateCoinBalanceGroup(canvasObject.transform, coinWalletService, speedButton, canvasObject.GetComponent<TopHudUI>());

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
