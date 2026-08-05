using System;
using System.Collections.Generic;
using System.IO;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Levels;
using Project001.Gameplay.Pixels;
using Project001.Gameplay.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project001.EditorTools
{
    /// <summary>
    /// Verification aid for the Character_XX migration (replacing the HSV
    /// recoloring pipeline): first an asset-level pass over all 20 Match IDs
    /// (no Play Mode required — prefab exists, its body renderer has a
    /// non-null, non-error-shader material whose texture is exactly that
    /// Match ID's Character_XX.png), then a real Play Mode pass on
    /// Bootstrap.unity that drives the actual CollectorSelectionController/
    /// ConveyorSystem/PixelGrid APIs to confirm the queue, conveyor, and
    /// pixel-matching gameplay still function against the new prefabs, with
    /// no missing references, no magenta (error-shader) materials, and no
    /// runtime-instanced materials anywhere in the live scene.
    ///
    /// Deliberately never requests an actual GPU render (no screenshot
    /// capture, no Camera.Render(), no camera.targetTexture redirect): this
    /// project's headless (-nographics) batch-mode environment crashes the
    /// native renderer the moment anything forces a real draw (confirmed by
    /// two crashing runs of an earlier version of this tool that redirected
    /// the camera to a RenderTexture — see OctopusColorVariants' own remarks
    /// on this same environment not "reliably providing a working GPU
    /// device"). Every check here instead reads component/material/shader
    /// state directly, which needs no render pass at all.
    ///
    /// Not part of the shipped game — driven via -executeMethod in batch
    /// mode (no -quit — this exits the process itself once verification
    /// finishes).
    /// </summary>
    public static class CharacterVerification
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string CharacterRoot = "Assets/Art/Themes/Classic/Character";

        private static bool _assetPassOk;
        private static int _assetPassCount;
        private static int _assetFailCount;

        private static double _elapsedStart = -1;
        private static bool _checkedWaiting;
        private static bool _selected1;
        private static bool _selected2;
        private static bool _checkedRiding;
        private static bool _overlapCheckStarted;
        private static bool _checkedOverlap;
        private static GameObject _overlapCheckBoardObject;
        private static int _overlapCheckPendingFrames;
        private static int _initialRemainingPixels = -1;
        private static int _initialRemainingCollectors = -1;
        private static bool _liveCheckOk = true;
        private static bool _finishing;

        [MenuItem("Tools/Characters/Run Verification")]
        public static void Run()
        {
            _assetPassOk = RunAssetLevelChecks();

            _elapsedStart = -1;
            _checkedWaiting = false;
            _selected1 = false;
            _selected2 = false;
            _checkedRiding = false;
            _overlapCheckStarted = false;
            _checkedOverlap = false;
            _overlapCheckBoardObject = null;
            _overlapCheckPendingFrames = 0;
            _initialRemainingPixels = -1;
            _initialRemainingCollectors = -1;
            _liveCheckOk = true;
            _finishing = false;

            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update += Update;
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Match ID 1-20, no Play Mode: loads each Character_XX.prefab,
        /// finds its body SkinnedMeshRenderer, and checks it has a material
        /// that (a) is not null, (b) is not the magenta error-shader
        /// fallback, and (c) has its _BaseMap/_MainTex pointed at exactly
        /// that Match ID's own Character_XX.png (compared by asset path, not
        /// by reference equality across domain reloads).
        /// </summary>
        private static bool RunAssetLevelChecks()
        {
            _assetPassCount = 0;
            _assetFailCount = 0;

            for (int matchId = 1; matchId <= 20; matchId++)
            {
                string idLabel = matchId.ToString("D2");
                string folder = $"{CharacterRoot}/Character_{idLabel}";
                string prefabPath = $"{folder}/Character_{idLabel}.prefab";
                string texturePath = $"{folder}/Character_{idLabel}.png";

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"CharacterVerification: ASSET FAIL Match ID {idLabel} - prefab missing at '{prefabPath}'.");
                    _assetFailCount++;
                    continue;
                }

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null)
                {
                    Debug.LogError($"CharacterVerification: ASSET FAIL Match ID {idLabel} - texture missing at '{texturePath}'.");
                    _assetFailCount++;
                    continue;
                }

                var skinnedMeshRenderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skinnedMeshRenderer == null)
                {
                    Debug.LogError($"CharacterVerification: ASSET FAIL Match ID {idLabel} - no SkinnedMeshRenderer found on prefab.");
                    _assetFailCount++;
                    continue;
                }

                Material material = skinnedMeshRenderer.sharedMaterial;
                if (material == null)
                {
                    Debug.LogError($"CharacterVerification: ASSET FAIL Match ID {idLabel} - body renderer has no material.");
                    _assetFailCount++;
                    continue;
                }

                if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                {
                    Debug.LogError($"CharacterVerification: ASSET FAIL Match ID {idLabel} - body material uses the magenta error shader.");
                    _assetFailCount++;
                    continue;
                }

                Texture assignedTexture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
                string assignedPath = assignedTexture != null ? AssetDatabase.GetAssetPath(assignedTexture) : null;
                if (assignedPath != texturePath)
                {
                    Debug.LogError($"CharacterVerification: ASSET FAIL Match ID {idLabel} - body material's texture is '{assignedPath}', expected '{texturePath}'.");
                    _assetFailCount++;
                    continue;
                }

                var animator = prefab.GetComponentInChildren<Animator>(true);
                string animatorNote = animator != null ? $", animator.enabled={animator.enabled}" : "";

                Debug.Log($"CharacterVerification: ASSET PASS Match ID {idLabel} - prefab='{prefabPath}', material='{AssetDatabase.GetAssetPath(material)}', texture OK{animatorNote}.");
                _assetPassCount++;
            }

            Debug.Log($"CharacterVerification: asset-level pass complete - {_assetPassCount}/20 passed, {_assetFailCount}/20 failed.");
            return _assetFailCount == 0;
        }

        private static void Update()
        {
            try
            {
                Step();
            }
            catch (Exception exception)
            {
                Debug.LogError($"CharacterVerification: aborting after exception - {exception}");
                _liveCheckOk = false;
                Finish();
            }
        }

        private static void Step()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (_elapsedStart < 0)
                _elapsedStart = EditorApplication.timeSinceStartup;

            double elapsed = EditorApplication.timeSinceStartup - _elapsedStart;

            var board = UnityEngine.Object.FindAnyObjectByType<CollectorQueueBoard>();
            var characterDatabase = UnityEngine.Object.FindAnyObjectByType<CharacterDatabase>();
            var pixelGrid = UnityEngine.Object.FindAnyObjectByType<PixelGrid>();
            var selectionController = UnityEngine.Object.FindAnyObjectByType<CollectorSelectionController>();

            if (board == null || characterDatabase == null || pixelGrid == null || selectionController == null)
            {
                if (elapsed > 5.0)
                {
                    Debug.LogError("CharacterVerification: LIVE FAIL - core scene objects (CollectorQueueBoard/CharacterDatabase/PixelGrid/CollectorSelectionController) never appeared after 5s.");
                    _liveCheckOk = false;
                    Finish();
                }
                return;
            }

            if (_initialRemainingPixels < 0)
            {
                _initialRemainingPixels = pixelGrid.RemainingPixelCount;
                _initialRemainingCollectors = board.RemainingCollectorCount;
                Debug.Log($"CharacterVerification: LIVE scene ready at t={elapsed:F2}s - RemainingPixelCount={_initialRemainingPixels}, RemainingCollectorCount={_initialRemainingCollectors}.");
            }

            if (!_checkedWaiting && elapsed >= 0.3)
            {
                CheckAllLiveCollectors("waiting");
                _checkedWaiting = true;
            }

            if (!_selected1 && elapsed >= 0.5)
            {
                CollectorView firstSelectable = FindFirstSelectable(board);
                if (firstSelectable != null)
                {
                    Debug.Log($"CharacterVerification: LIVE selecting '{firstSelectable.name}' via CollectorSelectionController.Select (real gameplay API).");
                    selectionController.Select(firstSelectable);
                }
                else
                {
                    Debug.LogError("CharacterVerification: LIVE FAIL - no selectable collector found to exercise Select().");
                    _liveCheckOk = false;
                }
                _selected1 = true;
            }

            if (!_selected2 && elapsed >= 0.8)
            {
                CollectorView nextSelectable = FindFirstSelectable(board);
                if (nextSelectable != null)
                {
                    Debug.Log($"CharacterVerification: LIVE selecting '{nextSelectable.name}' via CollectorSelectionController.Select (real gameplay API).");
                    selectionController.Select(nextSelectable);
                }
                _selected2 = true;
            }

            if (!_checkedRiding && elapsed >= 1.2)
            {
                CheckAllLiveCollectors("riding");
                _checkedRiding = true;
            }

            if (!_overlapCheckStarted && elapsed >= 1.5)
            {
                _overlapCheckBoardObject = BeginAllTwentyOverlapCheck(characterDatabase);
                // A short settle delay before measuring - not because live
                // bounds-reading is trusted (it isn't; see
                // FinishAllTwentyOverlapCheck's remarks, which measures size
                // via a separate, reliable single-level re-Instantiate
                // rather than the board's own live hierarchy), but so
                // CollectorQueueBoard.Initialize's own placement/pivot logic
                // has had a moment to run before positions are read.
                _overlapCheckPendingFrames = 3;
                _overlapCheckStarted = true;
            }
            else if (_overlapCheckStarted && !_checkedOverlap)
            {
                _overlapCheckPendingFrames--;
                if (_overlapCheckPendingFrames <= 0)
                {
                    FinishAllTwentyOverlapCheck(_overlapCheckBoardObject, characterDatabase);
                    _checkedOverlap = true;
                }
            }

            if (elapsed >= 3.0 && !_finishing)
            {
                int remainingCollectorsNow = board.RemainingCollectorCount;
                int remainingPixelsNow = pixelGrid.RemainingPixelCount;

                bool queueAdvanced = remainingCollectorsNow < _initialRemainingCollectors;
                bool pixelsConsumed = remainingPixelsNow < _initialRemainingPixels;

                Debug.Log($"CharacterVerification: LIVE gameplay check at t={elapsed:F2}s - RemainingCollectorCount {_initialRemainingCollectors} -> {remainingCollectorsNow} (queueAdvanced={queueAdvanced}), RemainingPixelCount {_initialRemainingPixels} -> {remainingPixelsNow} (pixelsConsumed={pixelsConsumed}).");

                if (!queueAdvanced)
                {
                    Debug.LogError("CharacterVerification: LIVE FAIL - queue did not advance after Select(); conveyor/queue gameplay appears broken.");
                    _liveCheckOk = false;
                }

                if (!pixelsConsumed)
                {
                    Debug.LogError("CharacterVerification: LIVE FAIL - no pixels were consumed; PixelConsumer/matching gameplay appears broken.");
                    _liveCheckOk = false;
                }

                _finishing = true;
                Finish();
            }
        }

        private static CollectorView FindFirstSelectable(CollectorQueueBoard board)
        {
            foreach (CollectorView view in UnityEngine.Object.FindObjectsByType<CollectorView>(FindObjectsSortMode.InstanceID))
            {
                if (board.CanSelect(view))
                    return view;
            }

            return null;
        }

        /// <summary>
        /// Walks every live CollectorView in the scene and checks its
        /// instantiated MofuModel: at least one renderer, no null
        /// sharedMaterial, no magenta error-shader material, and no
        /// runtime-instanced material (Unity suffixes an instanced
        /// Material's name with " (Instance)" the first time
        /// Renderer.material, rather than .sharedMaterial, is read on it —
        /// its absence here confirms CollectorView never triggers that).
        /// </summary>
        private static void CheckAllLiveCollectors(string phaseLabel)
        {
            var views = UnityEngine.Object.FindObjectsByType<CollectorView>(FindObjectsSortMode.InstanceID);
            int checkedCount = 0;

            foreach (CollectorView view in views)
            {
                if (view.VisualMotion == null || view.VisualMotion.childCount == 0)
                {
                    Debug.LogError($"CharacterVerification: LIVE FAIL ({phaseLabel}) - '{view.name}' has no MofuModel instance under VisualMotion.");
                    _liveCheckOk = false;
                    continue;
                }

                Transform model = view.VisualMotion.GetChild(0);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogError($"CharacterVerification: LIVE FAIL ({phaseLabel}) - '{view.name}' MofuModel has no renderers.");
                    _liveCheckOk = false;
                    continue;
                }

                foreach (Renderer renderer in renderers)
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null)
                    {
                        Debug.LogError($"CharacterVerification: LIVE FAIL ({phaseLabel}) - '{view.name}' renderer '{renderer.name}' has a null material.");
                        _liveCheckOk = false;
                        continue;
                    }

                    if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                    {
                        Debug.LogError($"CharacterVerification: LIVE FAIL ({phaseLabel}) - '{view.name}' renderer '{renderer.name}' uses the magenta error shader.");
                        _liveCheckOk = false;
                        continue;
                    }

                    if (material.name.EndsWith(" (Instance)", StringComparison.Ordinal))
                    {
                        Debug.LogError($"CharacterVerification: LIVE FAIL ({phaseLabel}) - '{view.name}' renderer '{renderer.name}' is using a runtime-instanced material '{material.name}'.");
                        _liveCheckOk = false;
                        continue;
                    }
                }

                checkedCount++;
            }

            Debug.Log($"CharacterVerification: LIVE ({phaseLabel}) checked {checkedCount} collector(s), {views.Length} found in scene.");
        }

        // The default level_001/level_002 boards only ever exercise the 3-5
        // Match IDs each hand-authored LevelCatalog level actually assigns
        // (see LevelCatalog.cs) - not nearly enough to check every species'
        // queue placement. This builds one extra, temporary
        // CollectorQueueBoard directly (never added to LevelCatalog, never
        // saved to disk) covering all 20 Match IDs through the exact same
        // public CollectorQueueBoard.Initialize API real gameplay uses, so
        // the overlap check below exercises the real placement code path,
        // not a synthetic approximation of it. Destroyed again immediately
        // after measuring.
        private const int OverlapCheckQueueCount = 5;
        private const int OverlapCheckRowsPerQueue = 4;

        private static GameObject BeginAllTwentyOverlapCheck(CharacterDatabase characterDatabase)
        {
            var boardObject = new GameObject("DiagnosticAllTwentyBoard", typeof(CollectorQueueBoard));
            var board = boardObject.GetComponent<CollectorQueueBoard>();
            var serializedBoard = new SerializedObject(board);
            serializedBoard.FindProperty("characterDatabase").objectReferenceValue = characterDatabase;
            serializedBoard.ApplyModifiedPropertiesWithoutUndo();

            var matchTypeId = new MatchTypeId("character_verification_diagnostic");
            var queues = new List<CollectorQueueDefinition>();
            int matchId = 1;
            for (int q = 0; q < OverlapCheckQueueCount; q++)
            {
                var collectors = new List<CollectorDefinition>();
                for (int r = 0; r < OverlapCheckRowsPerQueue; r++)
                {
                    collectors.Add(new CollectorDefinition(matchTypeId, 1, matchId));
                    matchId++;
                }
                queues.Add(new CollectorQueueDefinition(collectors));
            }

            board.Initialize(queues);
            return boardObject;
        }

        /// <summary>
        /// Measures and checks the board BeginAllTwentyOverlapCheck built.
        /// Deliberately does NOT read live bounds off the board's own
        /// deeply-nested collector hierarchy (collectorRoot -&gt; Visual -&gt;
        /// VisualMotion -&gt; MofuModel -&gt; nested vendor body prefab): confirmed
        /// empirically (per-species-consistent but wrong inflation - e.g.
        /// Crab +9%, Octopus +17%, Turtle +26%, Fish +34% over the known
        /// build-time size, reproducible across every instance of the same
        /// species regardless of when in the board-build loop it was
        /// created) that CollectorAnimation.ComputeLocalRendererBounds
        /// (BakeMesh-based) reads back incorrect skinned bounds through this
        /// specific multi-level nested-prefab-instance chain, and that
        /// waiting extra real frames after Instantiate() (tried up to a
        /// several-frame delay) does not fix it - ruling out a same-frame
        /// timing race as the cause. Position (collectorTransform.position)
        /// is unaffected - Transform hierarchy math, not skinning - so this
        /// combines each collector's real, reliable board position with its
        /// expected size measured by ComputeExpectedWorldBounds, which
        /// re-derives that size from a single-level Instantiate of the same
        /// saved Character_XX prefab (the same shallow nesting depth
        /// CharacterAssetBuilder's own build-time measurement and
        /// RunDiagnostics both already use reliably) rather than trusting
        /// the deep hierarchy's live measurement at all.
        /// </summary>
        private static void FinishAllTwentyOverlapCheck(GameObject boardObject, CharacterDatabase characterDatabase)
        {
            bool ok = true;
            var grid = new Bounds?[OverlapCheckQueueCount, OverlapCheckRowsPerQueue];
            var views = new CollectorView[OverlapCheckQueueCount, OverlapCheckRowsPerQueue];

            for (int q = 0; q < boardObject.transform.childCount && q < OverlapCheckQueueCount; q++)
            {
                Transform queueTransform = boardObject.transform.GetChild(q);
                for (int r = 0; r < queueTransform.childCount && r < OverlapCheckRowsPerQueue; r++)
                {
                    Transform collectorTransform = queueTransform.GetChild(r);
                    int matchId = q * OverlapCheckRowsPerQueue + r + 1;
                    grid[q, r] = ComputeExpectedWorldBounds(characterDatabase, matchId, collectorTransform.position);
                    views[q, r] = collectorTransform.GetComponent<CollectorView>();
                }
            }

            // Horizontal: adjacent queues (columns) at the same row must not
            // overlap in world X.
            for (int r = 0; r < OverlapCheckRowsPerQueue; r++)
            {
                for (int q = 0; q < OverlapCheckQueueCount - 1; q++)
                {
                    Bounds? left = grid[q, r];
                    Bounds? right = grid[q + 1, r];
                    if (!left.HasValue || !right.HasValue)
                        continue;

                    if (left.Value.max.x > right.Value.min.x)
                    {
                        Debug.LogError($"CharacterVerification: LIVE FAIL (overlap) - Match ID {q * OverlapCheckRowsPerQueue + r + 1:D2} and {(q + 1) * OverlapCheckRowsPerQueue + r + 1:D2} (row {r}, queues {q}/{q + 1}) overlap horizontally: leftMaxX={left.Value.max.x:F3} rightMinX={right.Value.min.x:F3}.");
                        ok = false;
                    }
                }
            }

            // Vertical: adjacent rows within the same queue must not overlap
            // in world Y.
            for (int q = 0; q < OverlapCheckQueueCount; q++)
            {
                for (int r = 0; r < OverlapCheckRowsPerQueue - 1; r++)
                {
                    Bounds? top = grid[q, r];
                    Bounds? bottom = grid[q, r + 1];
                    if (!top.HasValue || !bottom.HasValue)
                        continue;

                    if (bottom.Value.max.y > top.Value.min.y)
                    {
                        Debug.LogError($"CharacterVerification: LIVE FAIL (overlap) - Match ID {q * OverlapCheckRowsPerQueue + r + 1:D2} and {q * OverlapCheckRowsPerQueue + r + 2:D2} (queue {q}, rows {r}/{r + 1}) overlap vertically: topMinY={top.Value.min.y:F3} bottomMaxY={bottom.Value.max.y:F3}.");
                        ok = false;
                    }
                }
            }

            // Hunger label sanity: every one of the 20 collectors was given
            // HungerCapacity 1, so RemainingHunger starts at 1 - a quick,
            // cheap readability/position check (the label itself already
            // renders at GameplayLayout.HungerLabelWorldSize regardless of
            // this collector's own visual scale - see CollectorView).
            for (int q = 0; q < OverlapCheckQueueCount; q++)
            {
                for (int r = 0; r < OverlapCheckRowsPerQueue; r++)
                {
                    CollectorView view = views[q, r];
                    if (view == null)
                        continue;

                    if (view.HungerDisplayText != "1")
                    {
                        Debug.LogError($"CharacterVerification: LIVE FAIL (overlap) - Match ID {q * OverlapCheckRowsPerQueue + r + 1:D2} hunger label reads '{view.HungerDisplayText}', expected '1'.");
                        ok = false;
                    }
                }
            }

            if (ok)
                Debug.Log($"CharacterVerification: LIVE PASS (overlap) - all {OverlapCheckQueueCount * OverlapCheckRowsPerQueue} Match IDs (1-{OverlapCheckQueueCount * OverlapCheckRowsPerQueue}) placed via the real CollectorQueueBoard.Initialize API with no horizontal/vertical overlap and readable hunger labels.");
            else
                _liveCheckOk = false;

            UnityEngine.Object.DestroyImmediate(boardObject);
        }

        /// <summary>
        /// Neither Renderer.bounds (confirmed unreliable: on a posed Crab
        /// collector it reported size.x=2.03, far closer to the vendor's
        /// original wide-claw pose than the actual ~1.55 the posed prefab
        /// really measures - nothing has actually rendered/culled this
        /// specific instance yet by the time this check runs) nor a live
        /// BakeMesh read off the board's own deep collector hierarchy
        /// (confirmed separately unreliable there too - see
        /// FinishAllTwentyOverlapCheck's remarks). Instead, re-Instantiates
        /// this Match ID's saved Character_XX prefab standalone - a single
        /// level of nesting, the same shallow depth CharacterAssetBuilder's
        /// own build-time measurement and RunDiagnostics both already use
        /// reliably - measures it there via the same
        /// CollectorAnimation.ComputeLocalRendererBounds (BakeMesh-based;
        /// correct for THIS shallow nesting depth), scales by
        /// GameplayLayout.CollectorSpriteScale (the only additional scale
        /// factor a real collector applies beyond what is already baked
        /// into the saved prefab), and places the result at the real,
        /// reliable board position the caller already read from the live
        /// collector's Transform (plain hierarchy math, not skinning, so it
        /// is never subject to the same-BakeMesh unreliability). A waiting
        /// collector's real facing is always yaw 0 (WaitingAwayYawDegrees)
        /// with no additional rotation, matching this prefab's own
        /// unrotated authored orientation, so no rotation handling is
        /// needed here either.
        ///
        /// The raw prefab is NOT itself centered on its own origin (an
        /// imported model is typically pivoted at its feet - see
        /// CollectorView.Initialize's own remarks); real gameplay only ends
        /// up centered at the collector's Transform position because
        /// CollectorView.Initialize explicitly cancels that raw pivot
        /// offset (offsetting MofuModel by -bounds.center). This method
        /// must replicate that same cancellation - treating worldPosition
        /// as the corrected geometric center, not adding the raw,
        /// uncorrected local.center offset on top of it - or it reports a
        /// center shifted by roughly half the model's own height, which is
        /// exactly the bug an earlier version of this method had.
        ///
        /// ComputeLocalRendererBounds(tempInstance.transform) reports size
        /// in tempInstance's OWN local frame, which cancels out
        /// tempInstance's own localScale entirely (a transform's bounds
        /// relative to itself is never affected by its own scale) - so it
        /// returns the UNSCALED size CharacterAssetBuilder measured before
        /// computing and baking rootScale onto the prefab, not the actual
        /// scaled/authored size. tempInstance.transform.localScale (the
        /// baked rootScale a plain Instantiate of this saved prefab always
        /// carries) must be multiplied back in - on top of
        /// CollectorSpriteScale, the one additional factor a real collector
        /// applies beyond what's already baked into the prefab - or this
        /// reports a size roughly root-scale-times too large, which is
        /// exactly the second bug an earlier version of this method had.
        /// </summary>
        private static Bounds? ComputeExpectedWorldBounds(CharacterDatabase characterDatabase, int matchId, Vector3 worldPosition)
        {
            GameObject prefab = characterDatabase.GetPrefab(matchId);
            if (prefab == null)
                return null;

            GameObject tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                Bounds? local = CollectorAnimation.ComputeLocalRendererBounds(tempInstance.transform);
                if (!local.HasValue)
                    return null;

                Vector3 scale = tempInstance.transform.localScale * GameplayLayout.CollectorSpriteScale;
                Vector3 worldSize = Vector3.Scale(local.Value.size, scale);
                return new Bounds(worldPosition, worldSize);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempInstance);
            }
        }

        private static void Finish()
        {
            EditorApplication.update -= Update;

            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            bool overallOk = _assetPassOk && _liveCheckOk;
            int exitCode = overallOk ? 0 : 1;

            Debug.Log($"CharacterVerification: DONE - assetLevelPass={_assetPassOk} ({_assetPassCount}/20), livePass={_liveCheckOk}, overall={(overallOk ? "PASS" : "FAIL")}, exit code {exitCode}.");
            EditorApplication.Exit(exitCode);
        }

        // =====================================================================
        // Color + bounds diagnostics
        // =====================================================================
        //
        // Measures each Character_XX's actual RENDERED body color (not its
        // raw texture RGB) and its real assembled bounds, under the exact
        // Main Camera / Directional Light / ambient Bootstrap.unity itself
        // authors (see BootstrapSceneCreator.CreateCamera/CreateKeyLight/
        // ConfigureEnvironmentLighting) - reusing the real scene's own
        // camera/light rather than reconstructing one is what makes this
        // measurement faithful to "the actual portrait Game View" rather
        // than a guessed approximation of it.
        //
        // Deliberately Edit Mode only, never Play Mode: every root
        // GameObject in the opened scene except Main Camera and the
        // Directional Light is disabled for the duration of this pass (see
        // HideNonEssentialSceneContent), which removes both PixelGrid's many
        // 2D sprite pixels and every UI Canvas from the render entirely -
        // the same 2D/SpriteMask content implicated in the batch-mode
        // -nographics native-renderer crash two earlier capture attempts
        // hit while Play Mode was running the full scene (see this class's
        // own top-of-file remarks). Isolating the render to one 3D character
        // against an otherwise-empty scene was never actually attempted
        // before, and does not require entering Play Mode at all - Edit
        // Mode's own EditorApplication.update ticks are enough to drive the
        // same "redirect targetTexture, wait a few real frames" capture
        // pattern used elsewhere in this project's batch-mode tooling.
        //
        // The opened scene is never saved (no EditorSceneManager.SaveScene
        // call anywhere in this pass), so Bootstrap.unity on disk is
        // unaffected regardless of how many objects this temporarily
        // disables in memory.

        private const int DiagnosticRenderSize = 512;
        private static readonly Color DiagnosticChromaKey = new Color(1f, 0f, 1f, 1f);
        private const float ChromaKeyDistanceThreshold = 0.25f;

        private static Queue<int> _diagnosticQueue;
        private static Camera _diagnosticCamera;
        private static GameObject _diagnosticInstance;
        private static string _diagnosticOutputDir;
        private static RenderTexture _diagnosticRenderTexture;
        private static int _diagnosticCurrentMatchId;
        private static readonly List<string> _diagnosticResults = new List<string>();

        [MenuItem("Tools/Characters/Run Color And Bounds Diagnostics")]
        public static void RunDiagnostics()
        {
            _diagnosticOutputDir = Environment.GetEnvironmentVariable("CHARACTER_DIAGNOSTIC_DIR");
            if (string.IsNullOrEmpty(_diagnosticOutputDir))
                _diagnosticOutputDir = Path.Combine(Path.GetTempPath(), "character-diagnostics");
            Directory.CreateDirectory(_diagnosticOutputDir);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            _diagnosticCamera = Camera.main;
            if (_diagnosticCamera == null)
            {
                Debug.LogError("CharacterVerification: DIAGNOSTIC FAIL - no Main Camera found in Bootstrap.unity.");
                EditorApplication.Exit(1);
                return;
            }

            HideNonEssentialSceneContent(_diagnosticCamera);

            _diagnosticCamera.clearFlags = CameraClearFlags.SolidColor;
            _diagnosticCamera.backgroundColor = DiagnosticChromaKey;

            _diagnosticQueue = new Queue<int>();
            string singleMatchId = Environment.GetEnvironmentVariable("CHARACTER_DIAGNOSTIC_MATCH_ID");
            if (!string.IsNullOrEmpty(singleMatchId) && int.TryParse(singleMatchId, out int onlyMatchId))
            {
                _diagnosticQueue.Enqueue(onlyMatchId);
            }
            else
            {
                for (int matchId = 1; matchId <= 20; matchId++)
                    _diagnosticQueue.Enqueue(matchId);
            }

            _diagnosticInstance = null;
            _diagnosticRenderTexture = null;
            _diagnosticResults.Clear();

            EditorApplication.update += DiagnosticUpdate;
        }

        /// <summary>
        /// Disables every root object in the active scene except the one
        /// carrying mainCamera and every root carrying a Light - leaves the
        /// render target with nothing in it but the real camera/lighting rig
        /// plus whatever this pass instantiates itself. Never touches
        /// mainCamera or any Light component's own configuration.
        /// </summary>
        private static void HideNonEssentialSceneContent(Camera mainCamera)
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == mainCamera.gameObject)
                    continue;

                if (root.GetComponentInChildren<Light>(true) != null)
                    continue;

                root.SetActive(false);
            }
        }

        private static void DiagnosticUpdate()
        {
            try
            {
                DiagnosticStep();
            }
            catch (Exception exception)
            {
                Debug.LogError($"CharacterVerification: DIAGNOSTIC aborting after exception - {exception}");
                DiagnosticFinish(1);
            }
        }

        private static void DiagnosticStep()
        {
            if (_diagnosticInstance != null)
                return;

            if (_diagnosticQueue.Count == 0)
            {
                DiagnosticFinish(0);
                return;
            }

            _diagnosticCurrentMatchId = _diagnosticQueue.Dequeue();
            SpawnAndBeginCapture(_diagnosticCurrentMatchId);
        }

        private static void SpawnAndBeginCapture(int matchId)
        {
            string idLabel = matchId.ToString("D2");
            string prefabPath = $"{CharacterRoot}/Character_{idLabel}/Character_{idLabel}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                _diagnosticResults.Add($"Match ID {idLabel}: FAIL - could not load prefab at '{prefabPath}'.");
                return;
            }

            _diagnosticInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _diagnosticInstance.transform.position = Vector3.zero;
            _diagnosticInstance.transform.rotation = Quaternion.identity;

            Bounds? bounds = CollectorAnimation.ComputeLocalRendererBounds(_diagnosticInstance.transform);
            if (!bounds.HasValue)
            {
                _diagnosticResults.Add($"Match ID {idLabel}: FAIL - no renderer bounds found on instantiated prefab.");
                UnityEngine.Object.DestroyImmediate(_diagnosticInstance);
                _diagnosticInstance = null;
                return;
            }

            Vector3 size = bounds.Value.size;
            Vector3 center = bounds.Value.center;

            // Framing only - not gameplay math. Sized generously (1.4x the
            // largest extent) so the whole character is inside frame
            // regardless of which axis dominates for this species.
            float largestExtent = Mathf.Max(size.x, size.y, size.z);
            _diagnosticCamera.orthographicSize = Mathf.Max(0.1f, largestExtent * 0.7f);
            _diagnosticCamera.transform.position = new Vector3(center.x, center.y, center.z - 10f);
            _diagnosticCamera.transform.rotation = Quaternion.identity;

            _diagnosticResults.Add($"Match ID {idLabel}: BOUNDS size=({size.x:F3}, {size.y:F3}, {size.z:F3}) center=({center.x:F3}, {center.y:F3}, {center.z:F3}).");

            // Edit Mode does not automatically render any Camera on its own
            // the way Play Mode renders every enabled camera each frame
            // (confirmed empirically: an earlier version of this method
            // redirected targetTexture and waited several
            // EditorApplication.update ticks with no explicit Render() call,
            // matching the "never call Camera.Render() directly, let the
            // normal render loop draw into the redirected target" pattern
            // this project's Play Mode tooling already uses safely - in Edit
            // Mode that produced an untouched RenderTexture read back as a
            // flat, zero-saturation grey for every single Match ID, since
            // nothing had actually drawn into it). An explicit Render() call
            // is therefore required here, unlike in Play Mode. This was the
            // one call implicated in the earlier -nographics crash, but that
            // crash's own stack trace ran through 2D SpriteMask/BatchRenderer
            // draw calls - content that no longer exists in this scene once
            // HideNonEssentialSceneContent has run, so a direct Render() call
            // against this reduced, 3D-only scene is a materially different,
            // untested case, not a known-bad repeat of the earlier crash.
            _diagnosticRenderTexture = new RenderTexture(DiagnosticRenderSize, DiagnosticRenderSize, 24);
            _diagnosticCamera.targetTexture = _diagnosticRenderTexture;
            _diagnosticCamera.Render();
            FinishDiagnosticCapture();
        }

        /// <summary>
        /// Reads the capture back, classifies every pixel as "subject" (not
        /// close to the magenta chroma-key background) or background, and
        /// takes the per-channel MARGINAL MEDIAN of the subject pixels as
        /// this Match ID's measured rendered body color - a median rather
        /// than a mean specifically because it is robust to the face/eyes/
        /// mouth/highlight pixels that are a real but small minority of the
        /// silhouette, so they cannot skew the result away from the
        /// dominant body color the way a straight average could.
        /// </summary>
        private static void FinishDiagnosticCapture()
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _diagnosticRenderTexture;

            var texture = new Texture2D(DiagnosticRenderSize, DiagnosticRenderSize, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, DiagnosticRenderSize, DiagnosticRenderSize), 0, 0);
            texture.Apply();

            RenderTexture.active = previousActive;
            _diagnosticCamera.targetTexture = null;

            string idLabel = _diagnosticCurrentMatchId.ToString("D2");
            string screenshotPath = Path.Combine(_diagnosticOutputDir, $"Character_{idLabel}.png");
            File.WriteAllBytes(screenshotPath, texture.EncodeToPNG());

            Color32[] pixels = texture.GetPixels32();
            var reds = new List<byte>();
            var greens = new List<byte>();
            var blues = new List<byte>();

            foreach (Color32 pixel in pixels)
            {
                float dr = pixel.r / 255f - DiagnosticChromaKey.r;
                float dg = pixel.g / 255f - DiagnosticChromaKey.g;
                float db = pixel.b / 255f - DiagnosticChromaKey.b;
                float distance = Mathf.Sqrt(dr * dr + dg * dg + db * db);
                if (distance < ChromaKeyDistanceThreshold)
                    continue;

                reds.Add(pixel.r);
                greens.Add(pixel.g);
                blues.Add(pixel.b);
            }

            UnityEngine.Object.DestroyImmediate(texture);
            _diagnosticRenderTexture.Release();
            UnityEngine.Object.DestroyImmediate(_diagnosticRenderTexture);
            _diagnosticRenderTexture = null;
            UnityEngine.Object.DestroyImmediate(_diagnosticInstance);
            _diagnosticInstance = null;

            if (reds.Count == 0)
            {
                _diagnosticResults.Add($"Match ID {idLabel}: FAIL - no subject pixels found (camera framing or chroma key issue). Screenshot: '{screenshotPath}'.");
                return;
            }

            byte medianR = Median(reds);
            byte medianG = Median(greens);
            byte medianB = Median(blues);
            string measuredHex = $"#{medianR:X2}{medianG:X2}{medianB:X2}";

            string canonicalNote;
            if (ColorPalette.TryGetByMatchId(_diagnosticCurrentMatchId, out Color canonicalColor))
            {
                var canonical32 = (Color32)canonicalColor;
                int deltaR = medianR - canonical32.r;
                int deltaG = medianG - canonical32.g;
                int deltaB = medianB - canonical32.b;
                string canonicalHex = $"#{canonical32.r:X2}{canonical32.g:X2}{canonical32.b:X2}";

                Color.RGBToHSV(new Color(medianR / 255f, medianG / 255f, medianB / 255f), out float measuredH, out float measuredS, out float measuredV);
                Color.RGBToHSV(canonicalColor, out float canonicalH, out float canonicalS, out float canonicalV);

                canonicalNote = $"canonical={canonicalHex} deltaRGB=({deltaR:+0;-0;0},{deltaG:+0;-0;0},{deltaB:+0;-0;0}) " +
                    $"measuredHSV=({measuredH:F2},{measuredS:F2},{measuredV:F2}) canonicalHSV=({canonicalH:F2},{canonicalS:F2},{canonicalV:F2}) " +
                    $"deltaV={measuredV - canonicalV:F2} deltaS={measuredS - canonicalS:F2}";
            }
            else
            {
                canonicalNote = "canonical=<none found in ColorPalette>";
            }

            _diagnosticResults.Add($"Match ID {idLabel}: COLOR measured={measuredHex} {canonicalNote} (subjectPixels={reds.Count}/{pixels.Length}). Screenshot: '{screenshotPath}'.");
        }

        private static byte Median(List<byte> values)
        {
            var sorted = new List<byte>(values);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        private static void DiagnosticFinish(int exitCode)
        {
            EditorApplication.update -= DiagnosticUpdate;

            Debug.Log("CharacterVerification: DIAGNOSTIC RESULTS ---");
            foreach (string line in _diagnosticResults)
                Debug.Log($"CharacterVerification: {line}");
            Debug.Log($"CharacterVerification: DIAGNOSTIC DONE - {_diagnosticResults.Count} entries, exit code {exitCode}.");

            EditorApplication.Exit(exitCode);
        }
    }
}
