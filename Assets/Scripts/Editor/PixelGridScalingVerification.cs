using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Levels;
using Project001.Gameplay.Pixels;
using Project001.Gameplay.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Project001.EditorTools
{
    /// <summary>
    /// One-shot batch-mode verification for the Pixel Grid scaling
    /// investigation/implementation (see PixelGrid.TryComputeCellMetrics,
    /// PixelGridScalingTestLevelFactory). Two independent checks:
    ///
    /// RunMetrics (Edit Mode, no Play Mode, no scene) - calls
    /// PixelGrid.Initialize directly on a throwaway GameObject for every
    /// PixelGridScalingTestSize, timing it and reading back CellSize/
    /// CellGap/CellSpacing/Width/Height/child-object counts, then destroys
    /// the throwaway object. Fast, precise (isolated from any other
    /// system's Awake cost), and leaves no scene/asset changes.
    ///
    /// RunPlayModeVerification (real Play Mode, uses Assets/Scenes/
    /// Bootstrap.unity opened in memory, never saved) - for every
    /// PixelGridScalingTestSize, enables LevelBootstrapper's Enable Pixel
    /// Grid Scaling Test Setup at that size, enters Play Mode, lets the
    /// scene settle, captures a forced-portrait-1080x1920 screenshot via an
    /// offscreen RenderTexture (real runtime camera/scene state, not the
    /// literal OS window - batch mode has no interactive Game View to
    /// screenshot directly), then exits Play Mode before moving to the next
    /// size. For the last size (Grid30x40) it additionally boards riders
    /// via the same public CollectorSelectionController.Select production
    /// entry point CollectorLifecycleRegressionCheck already uses, and
    /// records per-tick frame time before vs. after 5 riders are actively
    /// consuming, as a coarse (Editor-timing, not device-timing) check for
    /// any gross stall introduced by the larger grid's exposed-pixel search.
    ///
    /// Never touches LevelCatalog or any approved level. Never saves
    /// Bootstrap.unity - every SerializedObject edit made here is an
    /// in-memory, unsaved scene change that Unity discards on exiting Play
    /// Mode / closing without save, so the committed scene file is never
    /// modified by running this.
    /// </summary>
    public static class PixelGridScalingVerification
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        private static readonly PixelGridScalingTestSize[] Sizes =
        {
            PixelGridScalingTestSize.Grid6x6,
            PixelGridScalingTestSize.Grid10x10,
            PixelGridScalingTestSize.Grid16x16,
            PixelGridScalingTestSize.Grid20x20,
            PixelGridScalingTestSize.Grid30x40,
        };

        // ------------------------------------------------------------
        // Metrics (Edit Mode, no scene) — cell size/gap/field size/counts/
        // isolated init timing, for every representative size.
        // ------------------------------------------------------------

        [MenuItem("Tools/Pixel Grid Scaling/Run Metrics (Edit Mode)")]
        public static void RunMetrics()
        {
            var report = new StringBuilder();
            report.AppendLine("size,width,height,cellSize,cellGap,cellSpacing,fieldWidth,fieldHeight,gameObjectCount,spriteRendererCount,initMs");

            foreach (PixelGridScalingTestSize size in Sizes)
            {
                LevelDefinition level = PixelGridScalingTestLevelFactory.BuildTestLevel(size);
                MeasureOne(size, level, out string row);
                report.AppendLine(row);
            }

            string outputPath = ResolveOutputPath("pixel_grid_metrics.csv");
            File.WriteAllText(outputPath, report.ToString());
            Debug.Log($"PixelGridScalingVerification: metrics written to '{outputPath}'.\n{report}");

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static void MeasureOne(PixelGridScalingTestSize size, LevelDefinition level, out string csvRow)
        {
            var gridObject = new GameObject("MetricsPixelGrid_TEMP", typeof(PixelGrid));
            try
            {
                PixelGrid grid = gridObject.GetComponent<PixelGrid>();

                var serialized = new SerializedObject(grid);
                serialized.FindProperty("preferredCellSize").floatValue = GameplayLayout.GridPreferredCellSize;
                serialized.FindProperty("preferredGap").floatValue = GameplayLayout.GridPreferredGap;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                grid.SetFieldBounds(GameplayLayout.GridRegionWidth, GameplayLayout.GridRegionHeight);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                grid.Initialize(level.PixelLayout);
                stopwatch.Stop();

                int gameObjectCount = 0;
                int spriteRendererCount = gridObject.GetComponentsInChildren<SpriteRenderer>(true).Length;
                foreach (Transform _ in gridObject.GetComponentsInChildren<Transform>(true))
                    gameObjectCount++;

                float fieldWidth = (grid.Width - 1) * grid.CellSpacing + grid.CellSize;
                float fieldHeight = (grid.Height - 1) * grid.CellSpacing + grid.CellSize;

                csvRow = string.Join(",", new[]
                {
                    size.ToString(),
                    grid.Width.ToString(CultureInfo.InvariantCulture),
                    grid.Height.ToString(CultureInfo.InvariantCulture),
                    grid.CellSize.ToString("F5", CultureInfo.InvariantCulture),
                    grid.CellGap.ToString("F5", CultureInfo.InvariantCulture),
                    grid.CellSpacing.ToString("F5", CultureInfo.InvariantCulture),
                    fieldWidth.ToString("F4", CultureInfo.InvariantCulture),
                    fieldHeight.ToString("F4", CultureInfo.InvariantCulture),
                    gameObjectCount.ToString(CultureInfo.InvariantCulture),
                    spriteRendererCount.ToString(CultureInfo.InvariantCulture),
                    stopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }

        // ------------------------------------------------------------
        // Play Mode verification — screenshots at every size, plus 30x40
        // idle-vs-consuming frame timing.
        // ------------------------------------------------------------

        private static int _sizeIndex;
        private static int _frameCount;
        private static double _lastTickTime;
        private static Phase _phase;
        private static readonly List<double> _idleFrameTimesMs = new List<double>();
        private static readonly List<double> _consumingFrameTimesMs = new List<double>();
        private static int _boardedCount;
        private static bool _advancing;
        private static double _boardingPhaseStartTime;
        private static readonly StringBuilder _playModeReport = new StringBuilder();

        private enum Phase
        {
            Settling,
            Boarding,
            MeasuringConsuming,
            Done
        }

        [MenuItem("Tools/Pixel Grid Scaling/Run Play Mode Verification")]
        public static void RunPlayModeVerification()
        {
            _sizeIndex = 0;
            _playModeReport.Clear();
            _playModeReport.AppendLine("size,fieldScreenshot,idleAvgMs,idleMaxMs,consumingAvgMs,consumingMaxMs,ridersBoarded");

            if (!OpenSceneAndConfigure(Sizes[_sizeIndex]))
            {
                Debug.LogError("PixelGridScalingVerification: aborting, could not configure scene for the first size.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                return;
            }

            _phase = Phase.Settling;
            _frameCount = 0;
            _advancing = false;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        private static bool OpenSceneAndConfigure(PixelGridScalingTestSize size)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var bootstrapper = UnityEngine.Object.FindFirstObjectByType<LevelBootstrapper>();
            if (bootstrapper == null)
            {
                Debug.LogError($"PixelGridScalingVerification: no LevelBootstrapper found in '{ScenePath}'.");
                return false;
            }

            var serialized = new SerializedObject(bootstrapper);
            serialized.FindProperty("enableFailureTestSetup").boolValue = false;
            serialized.FindProperty("enableFeedingFlowTestSetup").boolValue = false;
            serialized.FindProperty("enablePixelGridScalingTestSetup").boolValue = true;
            serialized.FindProperty("pixelGridScalingTestSize").enumValueIndex = (int)size;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
                return;

            _frameCount++;
            double now = EditorApplication.timeSinceStartup;
            double deltaMs = (now - _lastTickTime) * 1000.0;
            _lastTickTime = now;

            switch (_phase)
            {
                case Phase.Settling:
                    // A handful of real frames so every Awake/Start in the
                    // BuildLevel chain (PixelGrid, ConveyorSystem, WaitingLine,
                    // CollectorQueueBoard) has genuinely run.
                    if (_frameCount >= 10)
                    {
                        CaptureAndLogStatic();
                        _phase = Sizes[_sizeIndex] == PixelGridScalingTestSize.Grid30x40
                            ? Phase.Boarding
                            : Phase.Done;
                        _frameCount = 0;
                        _boardedCount = 0;
                        _idleFrameTimesMs.Clear();
                        _consumingFrameTimesMs.Clear();
                        _boardingPhaseStartTime = now;
                    }
                    break;

                case Phase.Boarding:
                    // Idle frame timing for the first ~30 ticks of this
                    // phase (no rider boarded yet — genuinely idle), then
                    // start attempting to board. Boarding is gated by real
                    // elapsed time, not frame/tick count: ConveyorSystem's
                    // boardingClearance is a real-time-driven distance (see
                    // GameplayLayout.ConveyorRiderMinimumSpacing at
                    // moveSpeed world units/sec), so how many editor ticks
                    // that takes depends entirely on how fast this batch-mode
                    // session happens to tick, not on a fixed frame count. A
                    // generous 8-second real-time budget comfortably covers
                    // 5 sequential boardings (~0.53s clearance wait each,
                    // ~2.1s total) with margin for a slow first tick.
                    if (_frameCount <= 30)
                    {
                        _idleFrameTimesMs.Add(deltaMs);
                    }
                    else
                    {
                        TryBoardAvailableFronts();
                        var conveyor = UnityEngine.Object.FindFirstObjectByType<ConveyorSystem>();
                        _boardedCount = conveyor != null ? conveyor.OccupiedCount : 0;
                        if (_boardedCount >= 5 || (now - _boardingPhaseStartTime) > 8.0)
                            _phase = Phase.MeasuringConsuming;
                    }
                    break;

                case Phase.MeasuringConsuming:
                    _consumingFrameTimesMs.Add(deltaMs);
                    if (_consumingFrameTimesMs.Count >= 120)
                        _phase = Phase.Done;
                    break;

                case Phase.Done:
                    if (!_advancing)
                    {
                        _advancing = true;
                        LogPhaseResultsAndAdvance();
                    }
                    break;
            }
        }

        private static void CaptureAndLogStatic()
        {
            PixelGrid grid = UnityEngine.Object.FindFirstObjectByType<PixelGrid>();
            string sizeLabel = Sizes[_sizeIndex].ToString();

            string screenshotPath = CaptureScreenshot(sizeLabel);

            if (grid != null)
            {
                Debug.Log($"(DEBUG) PixelGridScalingVerification: {sizeLabel} static — Width={grid.Width} Height={grid.Height} CellSize={grid.CellSize:F5} CellGap={grid.CellGap:F5} CellSpacing={grid.CellSpacing:F5} screenshot='{screenshotPath}'.");
            }

            _playModeReport.Append(sizeLabel).Append(',').Append(screenshotPath).Append(',');
        }

        private static string CaptureScreenshot(string label)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("PixelGridScalingVerification: no Camera.main found; skipping screenshot.");
                return string.Empty;
            }

            var fitter = camera.GetComponent<PortraitCameraFitter>();
            // Force exact Portrait 1080x1920 framing regardless of whatever
            // resolution the Editor's own (possibly headless) Game View
            // happens to be at — the same public entry point
            // PortraitCameraFitter.Awake already calls with the real
            // Screen.width/height at runtime.
            if (fitter != null)
                fitter.Fit(1080f, 1920f);

            const int width = 1080;
            const int height = 1920;
            var renderTexture = new RenderTexture(width, height, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            string path = ResolveOutputPath($"pixel_grid_{label}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());

            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(texture);

            return path;
        }

        private static void TryBoardAvailableFronts()
        {
            var selection = UnityEngine.Object.FindFirstObjectByType<CollectorSelectionController>();
            var board = UnityEngine.Object.FindFirstObjectByType<CollectorQueueBoard>();
            var conveyor = UnityEngine.Object.FindFirstObjectByType<ConveyorSystem>();
            if (selection == null || board == null || conveyor == null)
                return;

            for (int queueIndex = 0; queueIndex < 3 && conveyor.OccupiedCount < 5; queueIndex++)
            {
                CollectorView front = FindFrontOfQueue(board, queueIndex);
                if (front != null)
                    selection.Select(front);
            }
        }

        private static CollectorView FindFrontOfQueue(CollectorQueueBoard board, int queueIndex)
        {
            for (int rowIndex = 0; rowIndex < 4; rowIndex++)
            {
                var go = GameObject.Find($"Collector_{queueIndex}_{rowIndex}");
                if (go == null)
                    continue;

                var view = go.GetComponent<CollectorView>();
                if (view != null && board.CanSelect(view))
                    return view;
            }

            return null;
        }

        private static void LogPhaseResultsAndAdvance()
        {
            if (Sizes[_sizeIndex] == PixelGridScalingTestSize.Grid30x40)
            {
                (double idleAvg, double idleMax) = Stats(_idleFrameTimesMs);
                (double consumingAvg, double consumingMax) = Stats(_consumingFrameTimesMs);

                Debug.Log($"(DEBUG) PixelGridScalingVerification: Grid30x40 perf — riders boarded={_boardedCount}, idle avg={idleAvg:F3}ms max={idleMax:F3}ms (n={_idleFrameTimesMs.Count}), consuming avg={consumingAvg:F3}ms max={consumingMax:F3}ms (n={_consumingFrameTimesMs.Count}).");

                _playModeReport.Append(idleAvg.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(idleMax.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(consumingAvg.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(consumingMax.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(_boardedCount);
            }
            else
            {
                _playModeReport.Append(",,,,");
            }

            _playModeReport.AppendLine();

            _sizeIndex++;
            if (_sizeIndex >= Sizes.Length)
            {
                Finish();
                return;
            }

            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += NextSizeAfterExit;
        }

        private static void NextSizeAfterExit()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += NextSizeAfterExit;
                return;
            }

            if (!OpenSceneAndConfigure(Sizes[_sizeIndex]))
            {
                Debug.LogError("PixelGridScalingVerification: aborting, could not configure scene for the next size.");
                Finish();
                return;
            }

            _phase = Phase.Settling;
            _frameCount = 0;
            _advancing = false;
            EditorApplication.isPlaying = true;
        }

        private static (double avg, double max) Stats(List<double> values)
        {
            if (values.Count == 0)
                return (0.0, 0.0);

            double sum = 0.0;
            double max = double.MinValue;
            foreach (double value in values)
            {
                sum += value;
                if (value > max)
                    max = value;
            }

            return (sum / values.Count, max);
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;

            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            string outputPath = ResolveOutputPath("pixel_grid_playmode_report.csv");
            File.WriteAllText(outputPath, _playModeReport.ToString());
            Debug.Log($"PixelGridScalingVerification: Play Mode report written to '{outputPath}'.\n{_playModeReport}");

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static string ResolveOutputPath(string fileName)
        {
            string directory = Environment.GetEnvironmentVariable("PIXEL_GRID_VERIFICATION_OUTPUT_DIR");
            if (string.IsNullOrEmpty(directory))
                directory = Path.Combine(Path.GetTempPath(), "PixelGridScalingVerification");

            Directory.CreateDirectory(directory);
            return Path.Combine(directory, fileName);
        }
    }
}
