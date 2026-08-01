using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Pixels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project001.EditorTools
{
    /// <summary>
    /// One-off spike tooling: enters Play Mode on Bootstrap.unity and
    /// captures a timed sequence of main-camera screenshots via the real
    /// CollectorSelectionController.Select API (not a bypass) — the queue at
    /// rest, one collector's boarding turn (mid-turn and settled), five
    /// collectors selected in rapid succession to verify the conveyor
    /// spacing/release fix, the same tour rider (the first of those five)
    /// followed around the bottom/right/top/left straightaways and all four
    /// rounded corners to verify facing, and finally three untouched queued
    /// collectors posed toward the camera for a colour/material close-up
    /// each. Not part of the shipped game — a verification aid for the 3D
    /// presentation spike on branch experiment/3d-mofu, driven via
    /// -executeMethod in batch mode (no -quit — this exits the process
    /// itself once capture finishes). Reads the output directory from the
    /// MOFU3D_SCREENSHOT_DIR environment variable.
    /// </summary>
    public static class Mofu3DScreenshotCapture
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
        private const int Width = 1080;
        private const int Height = 1920;

        // Seconds between the four rapid-selection taps that open the
        // "5+ rapidly selected" test - short enough to be a realistic burst
        // of taps, not meant to individually clear ConveyorSystem's own
        // boarding-clearance gate (that gate, and CollectorSelectionController's
        // pending-boarding queue behind it, is exactly what is being verified).
        private const float RapidSelectStagger = 0.05f;

        private const float CloseUpOrthographicSize = 1.4f;

        // Fixed absolute schedule time (not relative to any other rider's
        // own boarding) for the terminal-foreground overlap test below -
        // chosen well after the facing tour + colour close-ups chain's own
        // nominal finish (~boardedAt(~3.0s) + 8.4s + 0.9s ~= 12.3s). A
        // variant that instead deferred this test until RestoreCamera
        // actually fired (chaining strictly after every earlier event had
        // genuinely completed in real time) was tried and reverted: in an
        // actual batch-mode run it made the whole capture pass dramatically
        // slower (background gameplay - PixelConsumer matches, WaitingLine
        // churn - keeps running the entire time this tool waits, and by the
        // time all 11 earlier events had each individually elapsed for
        // real, that background churn had compounded into a very heavy
        // frame). A fixed schedule mark reached via one large elapsed-time
        // jump (Step() fires every already-due event inside a single
        // Update() call) does not pay that same compounding cost, even
        // though - as this constant's own value already accounts for - real
        // elapsed time does not track simulated Time 1:1 here.
        private const double TerminalForegroundLeadBoardTime = 14.0;

        // How long after leadRider boards to request trailRider's own
        // boarding - ConveyorSystem's boarding-clearance gate (see
        // ConveyorSystem.boardingClearance) then delays trailRider's actual
        // board until leadRider has moved roughly one clearance-width ahead,
        // the same natural spacing every other rider on this conveyor keeps.
        private const double TerminalForegroundTrailBoardDelay = 0.15;

        private sealed class TimelineEvent
        {
            public double Time;
            public Action Action;
        }

        private static readonly List<TimelineEvent> _events = new List<TimelineEvent>();
        private static int _nextEventIndex;
        private static double _timelineStart = -1;
        private static string _outputDir;
        private static Camera _camera;
        private static bool _finishing;
        private static float _defaultOrthographicSize;
        private static Vector3 _defaultCameraPosition;

        // A capture redirects the camera's targetTexture and waits a few
        // real per-frame renders before reading pixels back, rather than
        // calling Camera.Render() out of band - an on-demand Render() call
        // was observed to skip URP's per-frame main-light setup entirely
        // (the result was pixel-identical to the material's raw _BaseColor,
        // with zero shading), where letting Unity's own normal render loop
        // draw into the redirected target does not.
        private static string _pendingCaptureName;
        private static RenderTexture _pendingRenderTexture;
        private static int _pendingFramesRemaining;

        // Board() bypasses CollectorSelectionController's own retry-until-
        // clear pending-boarding queue (see CollectorSelectionController),
        // so it needs an equivalent of its own: TryAddRider is retried every
        // frame here too, rather than attempted once and given up on,
        // exactly like a real selection would behave.
        private static CollectorView _pendingRawBoardView;
        private static ConveyorSystem _pendingRawBoardSystem;
        private static bool _pendingRawBoardDisableConsumption;
        private static Action<double> _pendingRawBoardCallback;

        [MenuItem("Tools/Mofu3D/Capture Verification Screenshots")]
        public static void Run()
        {
            _outputDir = Environment.GetEnvironmentVariable("MOFU3D_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(_outputDir))
            {
                Debug.LogError("Mofu3DScreenshotCapture: MOFU3D_SCREENSHOT_DIR is not set.");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(_outputDir);
            EditorSceneManager.OpenScene(ScenePath);

            _events.Clear();
            _nextEventIndex = 0;
            _timelineStart = -1;
            _finishing = false;
            _camera = null;
            EditorApplication.update += Update;
            EditorApplication.isPlaying = true;
        }

        private static void Update()
        {
            try
            {
                Step();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Mofu3DScreenshotCapture: aborting after exception - {exception}");
                Finish(1);
            }
        }

        private static void Step()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return;

                _defaultOrthographicSize = _camera.orthographicSize;
                _defaultCameraPosition = _camera.transform.position;
            }

            if (_pendingCaptureName != null)
            {
                _pendingFramesRemaining--;
                if (_pendingFramesRemaining <= 0)
                    FinishCapture();
                return;
            }

            if (_pendingRawBoardView != null && !TryResolvePendingRawBoard())
                return;

            if (_timelineStart < 0)
            {
                _timelineStart = EditorApplication.timeSinceStartup;
                BuildTimeline();
                // Step() consumes _events sequentially by index, firing
                // whichever is next once elapsed passes its Time - that only
                // matches "fire at the intended real time" if the list is in
                // non-decreasing Time order.
                _events.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            double elapsed = EditorApplication.timeSinceStartup - _timelineStart;

            while (_nextEventIndex < _events.Count && elapsed >= _events[_nextEventIndex].Time)
            {
                _events[_nextEventIndex].Action();
                _nextEventIndex++;

                // A capture just started - let it resolve (a few real
                // per-frame renders) before evaluating any further events.
                if (_pendingCaptureName != null)
                    return;

                // A raw board was just requested - stop here and let the
                // check at the top of Step() retry it every frame until it
                // succeeds, exactly like a pending capture above. Its own
                // completion callback (see Board) is what schedules
                // whatever comes after boarding, so there may be nothing
                // left in _events yet even though the timeline is not done.
                if (_pendingRawBoardView != null)
                    return;
            }

            if (_nextEventIndex >= _events.Count && !_finishing && _pendingRawBoardView == null)
            {
                _finishing = true;
                Finish(0);
            }
        }

        private static void BuildTimeline()
        {
            var collectors = UnityEngine.Object.FindObjectsByType<CollectorView>(FindObjectsSortMode.None)
                .ToDictionary(v => v.name, v => v);

            var selectionController = UnityEngine.Object.FindAnyObjectByType<CollectorSelectionController>();
            var conveyorSystem = UnityEngine.Object.FindAnyObjectByType<ConveyorSystem>();
            if (selectionController == null || conveyorSystem == null)
            {
                Debug.LogError("Mofu3DScreenshotCapture: no CollectorSelectionController/ConveyorSystem found in scene.");
                _events.Add(new TimelineEvent { Time = 0.1, Action = () => Finish(1) });
                return;
            }

            // Collector_0_0 (Purple, hunger 5) is queue 0's front, so it is
            // the first legitimately selectable collector - used for the
            // boarding-turn captures and as one of the "5 rapid selections"
            // below, but NOT as the long-lived facing tour: hunger 5 turned
            // out not to reliably survive a full ~8.2s lap against real
            // background gameplay (PixelConsumer satisfying and destroying
            // it mid-lap in an earlier run of this exact timeline).
            CollectorView boardingDemoRider = ResolveCollector(collectors, "Collector_0_0");

            // Collector_0_2 (Green, hunger 6 - the highest on level_001's
            // board) is queue 0's third/last collector, not selectable via
            // the real CollectorSelectionController path (only a queue's
            // front is ever selectable) - boarded directly against
            // ConveyorSystem instead, same as CollectorSelectionController's
            // own ReleaseCollector call underneath a real selection, just
            // skipping the "which source" lookup a non-front collector would
            // fail. Used purely as the long-lived facing tour below; it
            // plays no part in the rapid-boarding/spacing test.
            CollectorView facingTourRider = ResolveCollector(collectors, "Collector_0_2");

            _events.Add(new TimelineEvent { Time = 0.05, Action = () => LogCollectorHierarchy(boardingDemoRider) });
            _events.Add(new TimelineEvent { Time = 0.4, Action = () => Capture("01-queue-waiting") });

            // The "5+ rapidly selected" test: four taps in a tight burst
            // across the four queues' fronts, immediately followed by
            // boardingDemoRider's own queue's new front once it boards and
            // shifts - five distinct selections inside about a second, all
            // through the real CollectorSelectionController.Select path
            // (never the old raw ConveyorSystem.TryAddRider bypass), which
            // is what actually exercises the pending-boarding queue/spacing
            // fix.
            _events.Add(new TimelineEvent { Time = 0.4, Action = () => Select(selectionController, boardingDemoRider) });
            AddSelect(selectionController, collectors, "Collector_1_0", 0.4 + RapidSelectStagger);
            AddSelect(selectionController, collectors, "Collector_2_0", 0.4 + RapidSelectStagger * 2);
            AddSelect(selectionController, collectors, "Collector_3_0", 0.4 + RapidSelectStagger * 3);
            // Collector_0_0 boards almost immediately (the conveyor starts
            // empty), shifting Collector_0_1 into queue 0's front well
            // before this fires - a fifth rapid, genuinely front-of-queue
            // selection, not a re-tap of an already-pending collector.
            AddSelect(selectionController, collectors, "Collector_0_1", 1.0);

            // BoardingBounceDuration is 0.22s - one shot mid-turn, one after
            // it settles into its riding facing.
            _events.Add(new TimelineEvent { Time = 0.51, Action = () => Capture("02-boarding-mid-turn") });
            _events.Add(new TimelineEvent { Time = 0.75, Action = () => Capture("03-boarding-turned-toward") });
            _events.Add(new TimelineEvent { Time = 0.76, Action = () => LogCollectorHierarchy(boardingDemoRider) });

            // By 2.8s every one of the five rapid selections above has had
            // time to clear GameplayLayout.ConveyorRiderMinimumSpacing
            // behind whichever rider boarded before it (spacing/moveSpeed ≈
            // 1.85 / 3.5 ≈ 0.53s per rider, four gaps ≈ 2.1s after the
            // first boards at 0.4s) and should all be visibly on the
            // conveyor, in order, not touching.
            _events.Add(new TimelineEvent { Time = 2.8, Action = LogAllRiderPositions });
            _events.Add(new TimelineEvent { Time = 2.8, Action = () => Capture("04-rapid-boarding-no-overlap") });

            // facingTourRider boards well after the rapid-boarding rush
            // above starts, so it never competes with that test's own
            // spacing/ordering, then keeps riding for the eight timed
            // captures ScheduleFacingTourAndCloseUps schedules once it
            // actually boards (see that method's own remarks for the
            // bottom/right/top/left/corner visit order and timing
            // derivation). PixelConsumer is disabled the instant it boards
            // (disableConsumption below), purely for this verification pass
            // - real background gameplay reliably satisfied-and-destroyed
            // even a hunger-6 rider well inside one lap in earlier runs of
            // this exact timeline, faster than the ~8.2s the full facing
            // tour takes; facing/pivot behaviour and hunger consumption are
            // unrelated systems, so disabling consumption here does not
            // change what is being verified.
            //
            // Board() retries every frame until the conveyor actually
            // accepts it (see its own remarks), so 3.0s here only needs to
            // be a reasonable first attempt, not a guaranteed-clear time -
            // and onBoarded schedules everything that follows relative to
            // whenever boarding actually completes, not this nominal
            // attempt time, so a retry delay here no longer bunches up the
            // captures that follow (see onBoarded's own remarks on why that
            // used to happen).
            const double facingTourBoardTime = 3.0;
            _events.Add(new TimelineEvent
            {
                Time = facingTourBoardTime,
                Action = () => Board(facingTourRider, conveyorSystem, disableConsumption: true,
                    onBoarded: boardedAt => ScheduleFacingTourAndCloseUps(boardedAt, facingTourRider, collectors)),
            });

            // Queue 1's and queue 2's second slots (Collector_1_1,
            // Collector_2_1) are never touched by any test above - free to
            // dedicate to the terminal-foreground overlap test.
            ScheduleTerminalForegroundOverlapTest(collectors, conveyorSystem);
        }

        /// <summary>
        /// Verifies requirement 2 (render the final Satisfied/Heart sequence
        /// above other Mofus): boards leadRider, then trailRider just behind
        /// it at the conveyor's own natural minimum spacing, then forces
        /// leadRider satisfied (bypassing PixelConsumer/background gameplay
        /// for deterministic timing - see ForceSatisfied) the instant
        /// trailRider boards. CollectorLifecycle.Update (real gameplay code,
        /// not bypassed) then removes leadRider from ConveyorSystem on its
        /// very next frame, freezing it at its last ridden position while
        /// CollectorPresentation plays the real terminal sequence on it -
        /// meanwhile trailRider keeps moving at the shared moveSpeed and
        /// closes that spacing gap in roughly gap/moveSpeed seconds (~0.53s
        /// at this conveyor's configured spacing/speed), reaching leadRider's
        /// frozen screen position while the terminal sequence is still
        /// playing (which runs ~1.03s total: EatingPunchDuration 0.16s +
        /// SatisfiedPunchDuration 0.3s + HeartPulseDuration 0.35s +
        /// HeartCollapseDuration 0.22s) - exactly the overlap scenario
        /// CollectorAnimation.EnterTerminalForeground exists to keep from
        /// visually merging.
        /// </summary>
        private static void ScheduleTerminalForegroundOverlapTest(Dictionary<string, CollectorView> collectors, ConveyorSystem conveyorSystem)
        {
            CollectorView leadRider = ResolveCollector(collectors, "Collector_1_1");
            CollectorView trailRider = ResolveCollector(collectors, "Collector_2_1");

            _events.Add(new TimelineEvent
            {
                Time = TerminalForegroundLeadBoardTime,
                Action = () => Board(leadRider, conveyorSystem, disableConsumption: true,
                    onBoarded: leadBoardedAt => ScheduleTerminalForegroundTrailBoard(leadBoardedAt, leadRider, trailRider, conveyorSystem)),
            });
        }

        private static void ScheduleTerminalForegroundTrailBoard(double leadBoardedAt, CollectorView leadRider, CollectorView trailRider, ConveyorSystem conveyorSystem)
        {
            _events.Add(new TimelineEvent
            {
                Time = leadBoardedAt + TerminalForegroundTrailBoardDelay,
                Action = () => Board(trailRider, conveyorSystem, disableConsumption: true,
                    onBoarded: trailBoardedAt => BeginTerminalForegroundSequence(trailBoardedAt, leadRider, trailRider)),
            });

            // Safe for the same reason ScheduleFacingTourAndCloseUps's own
            // re-sort is: leadBoardedAt was just read as "elapsed so far",
            // so the new event's Time cannot sort before _nextEventIndex's
            // current position.
            _events.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private static void BeginTerminalForegroundSequence(double trailBoardedAt, CollectorView leadRider, CollectorView trailRider)
        {
            // Forced the instant trailRider boards, not before - leadRider
            // must still be riding normally (and thus still moving, still
            // spaced ahead of trailRider) right up to this point, or the
            // "trailing rider closes the gap after leadRider freezes" setup
            // this test depends on never actually happens.
            ForceSatisfied(leadRider.GetComponent<ConveyorRider>());

            AddTerminalForegroundCapture(trailBoardedAt + 0.0, "16-terminal-foreground-00", leadRider, trailRider);
            AddTerminalForegroundCapture(trailBoardedAt + 0.2, "17-terminal-foreground-02", leadRider, trailRider);
            AddTerminalForegroundCapture(trailBoardedAt + 0.4, "18-terminal-foreground-04", leadRider, trailRider);
            AddTerminalForegroundCapture(trailBoardedAt + 0.6, "19-terminal-foreground-06", leadRider, trailRider);
            AddTerminalForegroundCapture(trailBoardedAt + 0.8, "20-terminal-foreground-08", leadRider, trailRider);

            _events.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private static void AddTerminalForegroundCapture(double time, string name, CollectorView leadRider, CollectorView trailRider)
        {
            _events.Add(new TimelineEvent { Time = time, Action = () => Capture(name) });
            _events.Add(new TimelineEvent { Time = time, Action = () => LogTerminalForegroundDistance(name, leadRider, trailRider) });
        }

        /// <summary>
        /// Forces rider satisfied by directly registering consumed pixels
        /// (bypassing PixelConsumer/background gameplay entirely), the same
        /// public API PixelConsumer itself calls on a real match - not a
        /// shortcut around CollectorLifecycle's own satisfaction handling,
        /// which still runs normally and is what actually removes the rider
        /// from the conveyor and starts the real terminal presentation
        /// sequence (see this method's caller's remarks).
        /// </summary>
        private static void ForceSatisfied(ConveyorRider rider)
        {
            if (rider == null)
                return;

            for (int i = 0; i < rider.HungerCapacity && !rider.IsSatisfied; i++)
                rider.RegisterConsumedPixel();
        }

        /// <summary>
        /// Logs both riders' world positions and the distance between them
        /// at each terminal-foreground capture point - a numeric trace of
        /// how close trailRider actually gets to leadRider's frozen position
        /// alongside each screenshot, not just eyeballed from the image.
        /// </summary>
        private static void LogTerminalForegroundDistance(string label, CollectorView leadRider, CollectorView trailRider)
        {
            if (leadRider == null || trailRider == null)
                return;

            Vector3 leadPosition = leadRider.transform.position;
            Vector3 trailPosition = trailRider.transform.position;
            float distance = Vector3.Distance(leadPosition, trailPosition);

            Debug.Log($"Mofu3DScreenshotCapture: TERMINAL '{label}' leadPos={leadPosition} trailPos={trailPosition} distance={distance:F2}");
        }

        /// <summary>
        /// The eight riding captures plus the three colour close-ups,
        /// scheduled relative to boardedAt (facingTourRider's real boarding
        /// completion time - see Board's onBoarded remarks) rather than a
        /// guessed-in-advance timestamp. Elapsed-from-boarding offsets are
        /// solved analytically from ConveyorPath's actual geometry
        /// (GameplayLayout.ConveyorSize = 7.6, cornerRadius = 1, matching
        /// BootstrapSceneCreator.CreateConveyor) and
        /// GameplayConstants.BaseConveyorMoveSpeed (3.5 world units/sec),
        /// against ConveyorSystem's fixed boardingProgress (0.55). Progress
        /// moves counter-clockwise from the lower-left boarding point, so
        /// this is the real visit order: bottom, bottom-right corner,
        /// right, top-right corner, top, top-left corner, left, bottom-left
        /// corner.
        /// </summary>
        private static void ScheduleFacingTourAndCloseUps(double boardedAt, CollectorView facingTourRider, Dictionary<string, CollectorView> collectors)
        {
            AddRidingCapture(boardedAt + 0.61, "05-riding-bottom", facingTourRider);
            AddRidingCapture(boardedAt + 1.64, "06-riding-corner-bottom-right", facingTourRider);
            AddRidingCapture(boardedAt + 2.66, "07-riding-right", facingTourRider);
            AddRidingCapture(boardedAt + 3.68, "08-riding-corner-top-right", facingTourRider);
            AddRidingCapture(boardedAt + 4.71, "09-riding-top", facingTourRider);
            AddRidingCapture(boardedAt + 5.73, "10-riding-corner-top-left", facingTourRider);
            AddRidingCapture(boardedAt + 6.76, "11-riding-left", facingTourRider);
            AddRidingCapture(boardedAt + 7.78, "12-riding-corner-bottom-left", facingTourRider);

            // Colour/material close-ups: three collectors never touched by
            // either test above (queue row 2 of queues 1/2/3), still sitting
            // untouched in their queue at this point. Posed face-toward-
            // camera and framed tightly purely for this diagnostic shot -
            // not a facing-behaviour test (that is fully covered by the
            // riding captures above); the real waiting pose (face away) is
            // already visible in 01-queue-waiting.
            double closeUpStart = boardedAt + 8.4;
            AddColorCloseUp(closeUpStart, "13-closeup-purple", collectors, "Collector_1_2");
            AddColorCloseUp(closeUpStart + 0.3, "14-closeup-orange", collectors, "Collector_2_2");
            AddColorCloseUp(closeUpStart + 0.6, "15-closeup-green", collectors, "Collector_3_2");
            _events.Add(new TimelineEvent { Time = closeUpStart + 0.9, Action = RestoreCamera });

            // New events were just appended after _nextEventIndex's current
            // position with times that are all provably later than every
            // already-fired event's own Time (boardedAt itself was read
            // as "elapsed so far" at the moment this fired, and every
            // offset added above is positive) - re-sorting the whole list
            // is safe here for that reason: nothing new can sort to an
            // index before _nextEventIndex, so nothing already consumed
            // gets a chance to re-fire.
            _events.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private static void AddSelect(CollectorSelectionController controller, Dictionary<string, CollectorView> collectors, string name, double time)
        {
            CollectorView view = ResolveCollector(collectors, name);
            _events.Add(new TimelineEvent { Time = time, Action = () => Select(controller, view) });
        }

        private static void Select(CollectorSelectionController controller, CollectorView view)
        {
            if (view == null)
                return;

            // Disabled before selection, not after boarding: real background
            // gameplay consumed three of the five rapid-boarding riders
            // within ~1.3-2.4s of their own boarding in an earlier run of
            // this exact timeline (their hunger - 2, 3, 4 - is low enough
            // that a hungry pixel grid clears it fast), well before
            // 04-rapid-boarding-no-overlap's own capture could show all
            // five still on the conveyor together. Spacing/ordering is what
            // this test verifies, not hunger consumption, so disabling it
            // up front for every rider this test selects is harmless to
            // what is actually being checked.
            var pixelConsumer = view.GetComponent<PixelConsumer>();
            if (pixelConsumer != null)
                pixelConsumer.enabled = false;

            controller.Select(view);
        }

        private static void AddRidingCapture(double time, string name, CollectorView tourRider)
        {
            _events.Add(new TimelineEvent { Time = time, Action = () => Capture(name) });
            _events.Add(new TimelineEvent { Time = time, Action = () => LogFacing(name, tourRider) });
        }

        private static void AddColorCloseUp(double time, string captureName, Dictionary<string, CollectorView> collectors, string collectorName)
        {
            CollectorView view = ResolveCollector(collectors, collectorName);
            _events.Add(new TimelineEvent { Time = time, Action = () => CaptureColorCloseUp(captureName, view) });
        }

        /// <summary>
        /// Poses view's Visual to face the camera and temporarily zooms the
        /// main camera in on it, purely so the fur colour and untinted
        /// facial details are clearly legible in the capture - see
        /// BuildTimeline's remarks on why a direct pose is fine here.
        /// RestoreCamera puts the camera back afterward.
        /// </summary>
        private static void CaptureColorCloseUp(string captureName, CollectorView view)
        {
            if (view == null || view.Visual == null)
            {
                Debug.LogError($"Mofu3DScreenshotCapture: CaptureColorCloseUp - '{captureName}' has no collector/Visual to pose.");
                return;
            }

            // Yaw 180, not identity - the mesh's confirmed authored face
            // points toward the camera at yaw 180, not yaw 0 (see
            // CollectorAnimation.WaitingAwayYawDegrees's remarks).
            view.Visual.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Vector3 targetPosition = _camera.transform.position;
            targetPosition.x = view.transform.position.x;
            targetPosition.y = view.transform.position.y;
            _camera.transform.position = targetPosition;
            _camera.orthographicSize = CloseUpOrthographicSize;

            Capture(captureName);
        }

        private static void RestoreCamera()
        {
            _camera.orthographicSize = _defaultOrthographicSize;
            _camera.transform.position = _defaultCameraPosition;
        }

        /// <summary>
        /// Logs the tour rider's current world position, its
        /// ConveyorRider.RidingOrientation (FromEdge/ToEdge/CornerT - see
        /// ConveyorPath.SampleOrientation), and Visual's actual facing (as a
        /// Y euler angle) at each riding capture point, alongside an
        /// independently-computed expected yaw (ExpectedEdgeYawDegrees below
        /// - a deliberate re-derivation, not a call into
        /// CollectorAnimation.EdgeYawDegrees, so a bug in that method cannot
        /// silently agree with itself here) - so segment-based facing (fixed
        /// per straight, interpolated only across a corner) can be checked
        /// numerically, not just eyeballed from the screenshot.
        /// </summary>
        private static void LogFacing(string label, CollectorView view)
        {
            if (view == null || view.Visual == null)
                return;

            var rider = view.GetComponent<ConveyorRider>();
            Vector3 position = view.transform.position;
            float facingYawDegrees = view.Visual.localRotation.eulerAngles.y;

            if (rider == null)
            {
                Debug.Log($"Mofu3DScreenshotCapture: FACING '{label}' pos={position} visualFacingYaw={facingYawDegrees:F1} (no ConveyorRider)");
                return;
            }

            ConveyorOrientationSample orientation = rider.RidingOrientation;
            float expectedYawDegrees;
            if (orientation.FromEdge == orientation.ToEdge)
            {
                expectedYawDegrees = ExpectedEdgeYawDegrees(orientation.FromEdge);
            }
            else
            {
                Quaternion fromRotation = Quaternion.Euler(0f, ExpectedEdgeYawDegrees(orientation.FromEdge), 0f);
                Quaternion toRotation = Quaternion.Euler(0f, ExpectedEdgeYawDegrees(orientation.ToEdge), 0f);
                expectedYawDegrees = Quaternion.Slerp(fromRotation, toRotation, orientation.CornerT).eulerAngles.y;
            }

            Debug.Log($"Mofu3DScreenshotCapture: FACING '{label}' pos={position} fromEdge={orientation.FromEdge} toEdge={orientation.ToEdge} cornerT={orientation.CornerT:F2} visualFacingYaw={facingYawDegrees:F1} expectedYaw={expectedYawDegrees:F1}");
        }

        private static float ExpectedEdgeYawDegrees(ConveyorEdge edge)
        {
            switch (edge)
            {
                case ConveyorEdge.Bottom:
                    return 0f;
                case ConveyorEdge.Right:
                    return -90f;
                case ConveyorEdge.Top:
                    return 180f;
                case ConveyorEdge.Left:
                    return 90f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// Logs one collector's full Visual/VisualMotion/MofuModel hierarchy
        /// (names, local position/rotation/scale) plus the collector's
        /// current world position and rotation — a text-based check of the
        /// spike's structural changes, read from the batch-mode log
        /// alongside the screenshots rather than eyeballed from the images
        /// alone.
        /// </summary>
        private static void LogCollectorHierarchy(CollectorView view)
        {
            if (view == null || view.Visual == null)
            {
                Debug.LogError("Mofu3DScreenshotCapture: LogCollectorHierarchy - collector or its Visual is null.");
                return;
            }

            Transform visual = view.Visual;
            Debug.Log($"Mofu3DScreenshotCapture: HIERARCHY '{view.name}' root pos={view.transform.position} rot={view.transform.rotation.eulerAngles}");
            Debug.Log($"Mofu3DScreenshotCapture: HIERARCHY   Visual '{visual.name}' localPos={visual.localPosition} localRot={visual.localRotation.eulerAngles} localScale={visual.localScale}");

            foreach (Transform child in visual)
            {
                Debug.Log($"Mofu3DScreenshotCapture: HIERARCHY     {child.name} localPos={child.localPosition} localRot={child.localRotation.eulerAngles} localScale={child.localScale}");
                foreach (Transform grandchild in child)
                {
                    Debug.Log($"Mofu3DScreenshotCapture: HIERARCHY       {grandchild.name} localPos={grandchild.localPosition} localRot={grandchild.localRotation.eulerAngles} localScale={grandchild.localScale}");
                    foreach (Transform greatGrandchild in grandchild)
                        Debug.Log($"Mofu3DScreenshotCapture: HIERARCHY         {greatGrandchild.name} localPos={greatGrandchild.localPosition}");
                }
            }
        }

        /// <summary>
        /// Logs every currently-riding collector's name and world position,
        /// plus the straight-line distance to whichever other rider is
        /// closest to it - a numeric check of "not touching or overlapping"
        /// (see GameplayLayout.ConveyorRiderMinimumSpacing/
        /// CollectorVisibleHeight) alongside 04-rapid-boarding-no-overlap's
        /// screenshot, not just eyeballed from the image.
        /// </summary>
        private static void LogAllRiderPositions()
        {
            var riders = UnityEngine.Object.FindObjectsByType<ConveyorRider>(FindObjectsSortMode.None)
                .Where(r => r.IsRiding)
                .Select(r => (name: r.name, pos: r.transform.position))
                .OrderBy(r => r.name)
                .ToList();

            foreach ((string name, Vector3 pos) in riders)
            {
                float closest = float.MaxValue;
                string closestName = "(none)";
                foreach ((string otherName, Vector3 otherPos) in riders)
                {
                    if (otherName == name)
                        continue;

                    float distance = Vector3.Distance(pos, otherPos);
                    if (distance < closest)
                    {
                        closest = distance;
                        closestName = otherName;
                    }
                }

                Debug.Log($"Mofu3DScreenshotCapture: RIDER '{name}' pos={pos} nearestOther='{closestName}' distance={closest:F2}");
            }
        }

        private static CollectorView ResolveCollector(Dictionary<string, CollectorView> collectors, string name)
        {
            if (collectors.TryGetValue(name, out CollectorView view))
                return view;

            Debug.LogWarning($"Mofu3DScreenshotCapture: collector '{name}' not found in scene.");
            return null;
        }

        /// <summary>
        /// Queues view to board directly against conveyorSystem, bypassing
        /// CollectorSelectionController/ICollectorSource entirely - only
        /// used for facingTourRider, which is not a queue's front and so
        /// could never be legitimately selected through the real path this
        /// tool otherwise exercises (see BuildTimeline's remarks). Retried
        /// every frame via TryResolvePendingRawBoard until the conveyor
        /// actually accepts it, the same as a real selection's own pending-
        /// boarding queue would - a single TryAddRider attempt with no
        /// retry was observed to just give up and silently never board
        /// once the rapid-boarding riders ahead of it stopped clearing the
        /// boarding point (their own PixelConsumer disabled too, so unlike
        /// normal gameplay they never leave the loop to free it up faster).
        /// disableConsumption, if true, also disables PixelConsumer the
        /// instant boarding actually succeeds - see facingTourBoardTime's
        /// remarks for why the long-lived facing tour needs this.
        /// onBoarded, if given, fires once boarding actually succeeds, with
        /// the real elapsed-since-timeline-start time boarding completed at
        /// - not the time Board() was called, which can be well before
        /// boarding actually clears if the conveyor was not immediately
        /// free. Whatever follow-up captures depend on "time since this
        /// rider boarded" must be scheduled from that real completion time,
        /// not guessed in advance, or they end up firing back-to-back the
        /// moment a delayed board finally resolves instead of at the real
        /// paced intervals they were meant to sample - exactly what
        /// happened before onBoarded existed.
        /// </summary>
        private static void Board(CollectorView view, ConveyorSystem conveyorSystem, bool disableConsumption = false, Action<double> onBoarded = null)
        {
            if (view == null)
                return;

            if (view.GetComponent<ConveyorRider>() == null)
            {
                Debug.LogError($"Mofu3DScreenshotCapture: '{view.name}' has no ConveyorRider.");
                return;
            }

            view.Presentation.ShowConveyorFrontEating();
            _pendingRawBoardView = view;
            _pendingRawBoardSystem = conveyorSystem;
            _pendingRawBoardDisableConsumption = disableConsumption;
            _pendingRawBoardCallback = onBoarded;
        }

        /// <summary>
        /// Retries _pendingRawBoardView's boarding attempt. Returns true
        /// once it either succeeds or the view/rider has become invalid
        /// (nothing left to retry), false while still waiting - Step() does
        /// not advance the timeline while this returns false, the same way
        /// it already waits out a pending screenshot capture.
        /// </summary>
        private static bool TryResolvePendingRawBoard()
        {
            var rider = _pendingRawBoardView != null ? _pendingRawBoardView.GetComponent<ConveyorRider>() : null;
            if (rider == null)
            {
                _pendingRawBoardView = null;
                _pendingRawBoardCallback = null;
                return true;
            }

            if (!_pendingRawBoardSystem.TryAddRider(rider))
                return false;

            if (_pendingRawBoardDisableConsumption)
            {
                var pixelConsumer = _pendingRawBoardView.GetComponent<PixelConsumer>();
                if (pixelConsumer != null)
                    pixelConsumer.enabled = false;
            }

            double boardedAtElapsed = EditorApplication.timeSinceStartup - _timelineStart;
            Action<double> callback = _pendingRawBoardCallback;
            _pendingRawBoardView = null;
            _pendingRawBoardCallback = null;
            callback?.Invoke(boardedAtElapsed);
            return true;
        }

        private static void Capture(string name)
        {
            if (_camera == null)
            {
                Debug.LogError($"Mofu3DScreenshotCapture: no camera available, skipping '{name}'.");
                return;
            }

            if (_pendingCaptureName != null)
            {
                Debug.LogWarning($"Mofu3DScreenshotCapture: capture '{name}' requested while '{_pendingCaptureName}' still pending; skipping.");
                return;
            }

            _pendingRenderTexture = new RenderTexture(Width, Height, 24);
            _camera.targetTexture = _pendingRenderTexture;
            _pendingCaptureName = name;
            _pendingFramesRemaining = 3;
        }

        private static void FinishCapture()
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = _pendingRenderTexture;

            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();

            RenderTexture.active = previousActive;
            _camera.targetTexture = null;

            string path = Path.Combine(_outputDir, $"{_pendingCaptureName}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            Debug.Log($"Mofu3DScreenshotCapture: wrote '{path}'.");

            _pendingRenderTexture.Release();
            UnityEngine.Object.DestroyImmediate(_pendingRenderTexture);
            _pendingRenderTexture = null;
            _pendingCaptureName = null;
        }

        private static void Finish(int exitCode)
        {
            EditorApplication.update -= Update;

            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;

            Debug.Log($"Mofu3DScreenshotCapture: done, exit code {exitCode}.");
            EditorApplication.Exit(exitCode);
        }
    }
}
