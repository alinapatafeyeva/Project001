using System;
using System.Collections.Generic;
using System.Reflection;
using Project001.Gameplay;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using Project001.Gameplay.Failure;
using Project001.Gameplay.Feeding;
using Project001.Gameplay.Levels;
using Project001.Gameplay.Pixels;
using Project001.Gameplay.WaitingLine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Project001.EditorTools
{
    /// <summary>
    /// TEMPORARY regression verification for the "unsatisfied collector
    /// circles the Conveyor forever instead of reaching WaitingLine" bug
    /// (EndgameCleanupController closing WaitingLine regardless of actual
    /// capacity) and the related food-in-flight/lap-boundary race fixed
    /// alongside it in CollectorLifecycle. Not part of the shipped project —
    /// delete once the fix is confirmed. Drives the real Feeding Flow debug
    /// level in real Play Mode (no -nographics; only console/log output and
    /// reflection-read state are used, no screenshots needed here).
    /// </summary>
    public static class CollectorLifecycleRegressionCheck
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        private static double _startRealtime;
        private static int _errorCount;
        private static int _phase;
        private static bool _finishing;

        private static LevelBootstrapper _bootstrapper;
        private static CollectorQueueBoard _board;
        private static ConveyorSystem _conveyor;
        private static Project001.Gameplay.WaitingLine.WaitingLine _waitingLine;
        private static FailureController _failureController;
        private static PixelGrid _pixelGrid;
        private static EndgameCleanupController _endgameCleanup;
        private static CollectorSelectionController _selection;
        private static GameplayFlowController _gameplayFlowController;

        // Row-2 (permanently-hungry) match IDs for this debug level: matchId
        // = queueIndex + rowIndex*QueueCount + 1, rowIndex 2, QueueCount 4.
        private static readonly int[] PermanentlyHungryMatchIds = { 9, 10, 11, 12 };

        private static readonly HashSet<string> _confirmedEnteredWaitingLine = new HashSet<string>();
        private static readonly HashSet<string> _confirmedStayedInWaitingLine = new HashSet<string>();
        private static readonly Dictionary<string, bool> _sawAwaitingFoodResolution = new Dictionary<string, bool>();
        private static readonly HashSet<string> _confirmedFoodRaceResolvedCorrectly = new HashSet<string>();
        private static readonly List<string> _foodRaceFailures = new List<string>();
        private static bool _waitingLineFullTestPassed;
        private static bool _waitingLineFullTestRan;
        private static bool _simultaneousTestPassed;
        private static bool _simultaneousTestRan;
        private static readonly HashSet<string> _everRode = new HashSet<string>();

        [MenuItem("Tools/Gameplay/Collector Lifecycle Regression Check")]
        public static void Run()
        {
            EditorApplication.delayCall += () =>
            {
                EditorSceneManager.OpenScene(BootstrapScenePath);

                _bootstrapper = UnityEngine.Object.FindFirstObjectByType<LevelBootstrapper>();
                if (_bootstrapper == null)
                {
                    Debug.LogError("CollectorLifecycleRegressionCheck: no LevelBootstrapper found.");
                    EditorApplication.Exit(1);
                    return;
                }

                var serialized = new SerializedObject(_bootstrapper);
                serialized.FindProperty("enableFeedingFlowTestSetup").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Application.logMessageReceived += HandleLog;
                EditorApplication.update += Step;
                _startRealtime = EditorApplication.timeSinceStartup;
                EditorApplication.isPlaying = true;
            };
        }

        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                _errorCount++;
        }

        private static double Elapsed => EditorApplication.timeSinceStartup - _startRealtime;

        private static void Step()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (_conveyor == null)
            {
                _conveyor = UnityEngine.Object.FindFirstObjectByType<ConveyorSystem>();
                _board = UnityEngine.Object.FindFirstObjectByType<CollectorQueueBoard>();
                _waitingLine = UnityEngine.Object.FindFirstObjectByType<Project001.Gameplay.WaitingLine.WaitingLine>();
                _failureController = UnityEngine.Object.FindFirstObjectByType<FailureController>();
                _pixelGrid = UnityEngine.Object.FindFirstObjectByType<PixelGrid>();
                _endgameCleanup = UnityEngine.Object.FindFirstObjectByType<EndgameCleanupController>();
                _selection = UnityEngine.Object.FindFirstObjectByType<CollectorSelectionController>();
                _gameplayFlowController = UnityEngine.Object.FindFirstObjectByType<GameplayFlowController>();
                return;
            }

            double elapsed = Elapsed;

            // Keep every queue moving on every single frame, for the whole
            // run - Select() is a cheap no-op for a collector already
            // riding/pending (see _pendingViews), so this just means "board
            // whatever is currently at the front the instant it can be."
            // Only boarding once per fixed tick badly starved row-2 in an
            // earlier version of this harness, since row 0/1 need real time
            // to be satisfied and shifted out before row 2 is even
            // selectable - a pacing bug in this test script, not in
            // production code (CollectorSelectionController itself already
            // retries every frame on its own).
            BoardQueueFronts();

            // Protect whichever matchId the food-race phase currently has
            // its eye on from being force-satisfied below - it must be
            // caught "riding with hunger remaining" for its own probe to
            // mean anything, not insta-satisfied by the acceleration meant
            // for everyone else.
            _protectedFoodRaceMatchId = _foodRaceProbeCursor < SatisfiableProbeMatchIds.Length
                ? SatisfiableProbeMatchIds[_foodRaceProbeCursor]
                : -1;
            AccelerateNonTargetRiders();

            TrackWaitingLineMembership();

            // Ticks from the very first frame, independent of the phase
            // switch below - most satisfiable collectors self-resolve
            // within the first few seconds once AccelerateNonTargetRiders
            // is draining every queue, so this cannot wait for the later
            // phases to finish without missing almost every candidate.
            RunFoodInFlightRaceTest(elapsed);

            switch (_phase)
            {
                case 0:
                    if (BothProbeRidersBoarded() || elapsed >= 40.0)
                    {
                        if (elapsed >= 40.0 && !BothProbeRidersBoarded())
                            Debug.LogWarning("CollectorLifecycleRegressionCheck: timed out waiting for MatchId 9/10 to board; running WaitingLine-full/simultaneous tests against whatever is available.");
                        _phase = 1;
                    }
                    break;

                case 1:
                    RunWaitingLineFullTest();
                    _phase = 2;
                    break;

                case 2:
                    RunSimultaneousLapTest();
                    _phase = 3;
                    break;

                case 3:
                    bool foodRaceDone = _foodRaceProbeCursor >= SatisfiableProbeMatchIds.Length;
                    if ((foodRaceDone && elapsed >= 70.0) || elapsed >= 110.0)
                    {
                        if (!_finishing)
                        {
                            _finishing = true;
                            Finish();
                        }
                    }
                    break;
            }
        }

        private static bool BothProbeRidersBoarded()
        {
            return IsRiding(9) && IsRiding(10);
        }

        private static bool IsRiding(int matchId)
        {
            CollectorView view = FindByMatchId(matchId);
            if (view == null)
                return false;

            var rider = view.GetComponent<ConveyorRider>();
            return rider != null && rider.IsRiding;
        }

        private static void BoardQueueFronts()
        {
            if (_selection == null || _board == null)
                return;

            for (int queueIndex = 0; queueIndex < 4; queueIndex++)
            {
                CollectorView front = FindFrontOfQueue(queueIndex);
                if (front != null)
                    _selection.Select(front);
            }
        }

        // Mirrors CollectorQueueBoard.CanSelect: the front of a queue is
        // whichever of its collectors still exists and is first in queue —
        // rather than reflecting into CollectorQueue's own list, this just
        // asks the board itself, the same production entry point
        // CollectorSelectionController uses.
        private static CollectorView FindFrontOfQueue(int queueIndex)
        {
            for (int rowIndex = 0; rowIndex < 5; rowIndex++)
            {
                var go = GameObject.Find($"Collector_{queueIndex}_{rowIndex}");
                if (go == null)
                    continue;

                var view = go.GetComponent<CollectorView>();
                if (view != null && _board.CanSelect(view))
                    return view;
            }

            return null;
        }

        // Currently-being-probed matchId that must NOT be force-fed - set
        // right before injecting its final packet, cleared once that probe
        // finishes. Never includes 9/10/11/12 (see AccelerateNonTargetRiders).
        private static int _protectedFoodRaceMatchId = -1;

        /// <summary>
        /// Speeds up every OTHER currently-riding satisfiable collector to
        /// Satisfied via the same public ConveyorRider.RegisterConsumedPixel
        /// a real FoodPacket arrival calls - never PixelGrid, never hunger
        /// rules themselves, just repeating the real "one pixel landed"
        /// step faster than real consumption cadence would. Exists purely so
        /// this harness does not need to sit through many real minutes of
        /// 0.1s-cooldown pixel consumption across all 20 collectors for row
        /// 2 (permanently-hungry, third in every 5-deep queue) to reach the
        /// front - the thing this whole regression check needs to observe.
        /// Never touches the four permanently-hungry matchIds (9/10/11/12 -
        /// forcing them to 0 would defeat their entire purpose) or whichever
        /// matchId a food-in-flight probe currently has mid-flight.
        /// </summary>
        private static void AccelerateNonTargetRiders()
        {
            for (int queueIndex = 0; queueIndex < 4; queueIndex++)
            {
                for (int rowIndex = 0; rowIndex < 5; rowIndex++)
                {
                    var go = GameObject.Find($"Collector_{queueIndex}_{rowIndex}");
                    if (go == null)
                        continue;

                    int matchId = queueIndex + rowIndex * 4 + 1;
                    if (Array.IndexOf(PermanentlyHungryMatchIds, matchId) >= 0 || matchId == _protectedFoodRaceMatchId)
                        continue;

                    var rider = go.GetComponent<ConveyorRider>();
                    if (rider == null || !rider.IsRiding)
                        continue;

                    while (rider.RemainingHunger > 0)
                        rider.RegisterConsumedPixel();
                }
            }
        }

        private static void TrackWaitingLineMembership()
        {
            foreach (int matchId in PermanentlyHungryMatchIds)
            {
                CollectorView view = FindByMatchId(matchId);
                if (view == null)
                    continue;

                var rider = view.GetComponent<ConveyorRider>();
                if (rider != null && rider.IsRiding)
                    _everRode.Add(view.name);

                if (_waitingLine != null && _waitingLine.Contains(view))
                {
                    _confirmedEnteredWaitingLine.Add(view.name);
                    _confirmedStayedInWaitingLine.Add(view.name);
                }
                else if (_confirmedEnteredWaitingLine.Contains(view.name) && rider != null && rider.IsRiding)
                {
                    // Was in WaitingLine, now riding again with nobody having
                    // selected it back onto the conveyor — would indicate a
                    // stale/duplicated state. Selection only happens via
                    // BoardQueueFronts, which never targets a WaitingLine
                    // occupant, so this should never happen.
                    _confirmedStayedInWaitingLine.Remove(view.name);
                }
            }
        }

        private static CollectorView FindByMatchId(int matchId)
        {
            for (int queueIndex = 0; queueIndex < 4; queueIndex++)
            {
                int rowIndex = (matchId - 1 - queueIndex) / 4;
                if (rowIndex < 0 || rowIndex > 4)
                    continue;
                if (queueIndex + rowIndex * 4 + 1 != matchId)
                    continue;

                var go = GameObject.Find($"Collector_{queueIndex}_{rowIndex}");
                return go != null ? go.GetComponent<CollectorView>() : null;
            }

            return null;
        }

        private static bool IsSafeFillerCandidate(CollectorView view, int queueIndex, int rowIndex)
        {
            if (view == null)
                return false;

            // Never borrow the collector the food-race phase currently has
            // mid-flight as a filler in a different sub-test - the two
            // would otherwise contaminate each other (this test's own
            // WaitingSlot.Assign making the food-race poll see a false
            // "stranded in WaitingLine" for a collector actually resolving
            // correctly via its real Satisfied path).
            int matchId = queueIndex + rowIndex * 4 + 1;
            return matchId != _protectedFoodRaceMatchId;
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        /// <summary>
        /// Forces the given rider to be one Update() away from completing a
        /// lap, by directly setting its ConveyorSystem-tracked progress just
        /// short of the boarding point again. Deterministic stand-in for
        /// waiting out a real ~7 second lap, so the food-in-flight race
        /// (Part 2/3) and the simultaneous-completion race (Part 2 items
        /// 6-8) can be exercised on demand instead of by chance.
        /// </summary>
        private static bool ForceNearLapCompletion(ConveyorRider rider, float remainingFraction)
        {
            var riders = (List<ConveyorRider>)GetPrivateField(_conveyor, "_riders");
            var riderProgress = (List<float>)GetPrivateField(_conveyor, "_riderProgress");
            float boardingProgress = (float)GetPrivateField(_conveyor, "boardingProgress");

            int index = riders.IndexOf(rider);
            if (index < 0)
                return false;

            float nearCompletion = Mathf.Repeat(boardingProgress - remainingFraction, 1f);
            riderProgress[index] = nearCompletion;
            return true;
        }

        // --- Phase: WaitingLine-full boundary (Part 2 items 7-8, Part 5) ---

        private static void RunWaitingLineFullTest()
        {
            _waitingLineFullTestRan = true;

            if (_waitingLine == null || _failureController == null)
            {
                Debug.LogError("CollectorLifecycleRegressionCheck: WaitingLine-full test could not run - missing WaitingLine/FailureController.");
                return;
            }

            WaitingSlot[] slots2 = (WaitingSlot[])GetPrivateField(_waitingLine, "_slots");
            if (slots2 == null)
            {
                Debug.LogError("CollectorLifecycleRegressionCheck: WaitingLine has no slots yet.");
                return;
            }

            // Fill every slot but one with a filler occupant (a currently-
            // queued, not-yet-boarded CollectorView - never removed from its
            // queue, so this cannot affect real gameplay/state elsewhere;
            // only WaitingSlot.Assign, a plain occupancy write, is used).
            // Some slots may already be genuinely occupied by a real
            // permanently-hungry collector that already completed its own
            // lap before this test phase ran (queues progress
            // independently and concurrently) - toFill accounts for that
            // rather than assuming the line starts empty.
            var fillers = new List<CollectorView>();
            int existingOccupied = _waitingLine.OccupiedSlotCount;
            int emptyToLeave = 1;
            int toFill = Mathf.Max(0, slots2.Length - emptyToLeave - existingOccupied);

            for (int i = 0, filled = 0; i < 20 && filled < toFill; i++)
            {
                int q = i % 4;
                int r = i / 4;
                var go = GameObject.Find($"Collector_{q}_{r}");
                if (go == null)
                    continue;
                var view = go.GetComponent<CollectorView>();
                if (view == null || _waitingLine.Contains(view) || !IsSafeFillerCandidate(view, q, r))
                    continue;

                foreach (WaitingSlot slot in slots2)
                {
                    if (!slot.IsOccupied && slot.Assign(view))
                    {
                        fillers.Add(view);
                        filled++;
                        break;
                    }
                }
            }

            bool hasOneFreeSlot = _waitingLine.GetFirstEmptySlot() != null;
            bool hadFailedBefore = _failureController.HasFailed;

            // Occupy the last remaining slot too, so GetFirstEmptySlot must
            // now return null - the genuinely-full case.
            bool lastAssigned = false;
            for (int i = 0; i < 20 && !lastAssigned; i++)
            {
                int q = i % 4;
                int r = i / 4;
                var go2 = GameObject.Find($"Collector_{q}_{r}");
                var lastFillerView = go2 != null ? go2.GetComponent<CollectorView>() : null;
                if (lastFillerView == null || fillers.Contains(lastFillerView) || _waitingLine.Contains(lastFillerView)
                    || !IsSafeFillerCandidate(lastFillerView, q, r))
                    continue;

                foreach (WaitingSlot slot in slots2)
                {
                    if (!slot.IsOccupied && slot.Assign(lastFillerView))
                    {
                        fillers.Add(lastFillerView);
                        lastAssigned = true;
                        break;
                    }
                }
            }

            bool nowFull = _waitingLine.GetFirstEmptySlot() == null;

            // Exercise the real fallback path an unsatisfied lap-completing
            // collector uses when it finds the line genuinely full.
            if (!hadFailedBefore)
                _failureController.NotifyWaitingLineFull();
            bool failureTriggered = _failureController.HasFailed && !hadFailedBefore;

            // Free every filler slot again immediately - this is a
            // same-frame occupy/release test, never meant to persist or
            // affect the rest of the run.
            foreach (CollectorView filler in fillers)
                _waitingLine.ClearSlotContaining(filler);

            bool freedCorrectly = _waitingLine.GetFirstEmptySlot() != null;

            _waitingLineFullTestPassed = hasOneFreeSlot && lastAssigned && nowFull && failureTriggered && freedCorrectly;

            Debug.Log($"CollectorLifecycleRegressionCheck: WaitingLine-full test - "
                + $"hadOneFree={hasOneFreeSlot}, becameFull={nowFull}, failureTriggered={failureTriggered}, freedAfter={freedCorrectly} -> "
                + (_waitingLineFullTestPassed ? "PASS" : "FAIL"));

            // Undo the manual HasFailed flip so the rest of this run (and
            // Endgame Cleanup) is not left in a false Failure state. Real
            // NotifyWaitingLineFull raises the real OnFailure event, which
            // GameplayFlowController reacts to by pausing gameplay
            // (Time.timeScale = 0) - ResetFailure alone does NOT undo that
            // pause, so this must also explicitly resume gameplay, or every
            // subsequent frame in this run silently stops simulating
            // (conveyor movement, hunger, everything) for the rest of the
            // process, which would look exactly like a fresh "stuck
            // forever" bug despite being purely this test's own doing.
            if (failureTriggered)
            {
                _failureController.ResetFailure();
                _gameplayFlowController?.ResumeGameplay();
            }
        }

        // --- Phase: simultaneous lap completion (Part 2 items 6, 8) ---

        private static void RunSimultaneousLapTest()
        {
            _simultaneousTestRan = true;

            CollectorView viewA = FindByMatchId(9);
            CollectorView viewB = FindByMatchId(10);

            if (viewA == null || viewB == null || _conveyor == null || _waitingLine == null)
            {
                Debug.LogError("CollectorLifecycleRegressionCheck: simultaneous-lap test could not run - missing collectors.");
                return;
            }

            var riderA = viewA.GetComponent<ConveyorRider>();
            var riderB = viewB.GetComponent<ConveyorRider>();

            if (riderA == null || !riderA.IsRiding || riderB == null || !riderB.IsRiding)
            {
                Debug.LogError("CollectorLifecycleRegressionCheck: simultaneous-lap test could not run - riders not both boarded yet.");
                return;
            }

            WaitingSlot[] slots = (WaitingSlot[])GetPrivateField(_waitingLine, "_slots");

            // Leave exactly one real free slot for these two riders to race
            // over, by occupying every other slot with harmless fillers.
            // Accounts for slots a real permanently-hungry collector may
            // already genuinely occupy (queues progress independently), the
            // same as RunWaitingLineFullTest.
            var fillers = new List<CollectorView>();
            int existingOccupied = _waitingLine.OccupiedSlotCount;
            int toFill = Mathf.Max(0, slots.Length - 1 - existingOccupied);
            int filled = 0;
            for (int i = 0; i < 20 && filled < toFill; i++)
            {
                int q = i % 4;
                int r = i / 4;
                var go = GameObject.Find($"Collector_{q}_{r}");
                var view = go != null ? go.GetComponent<CollectorView>() : null;
                var candidateRider = view != null ? view.GetComponent<ConveyorRider>() : null;

                // Only a still-queued (never boarded) collector is safe as a
                // filler here: this test's own result check runs on a LATER
                // frame (via delayCall), by which time AccelerateNonTargetRiders
                // would have force-satisfied-and-removed any filler that was
                // still riding at assignment time, silently un-filling its
                // slot before the check runs.
                if (view == null || view == viewA || view == viewB || candidateRider == null
                    || candidateRider.IsRiding || _waitingLine.Contains(view) || !IsSafeFillerCandidate(view, q, r))
                    continue;

                foreach (WaitingSlot slot in slots)
                {
                    if (!slot.IsOccupied && slot.Assign(view))
                    {
                        fillers.Add(view);
                        filled++;
                        break;
                    }
                }
            }

            int freeSlotsForRace = _waitingLine.GetFirstEmptySlot() != null
                ? slots.Length - (existingOccupied + filled)
                : 0;

            bool forcedA = ForceNearLapCompletion(riderA, 0.0005f);
            bool forcedB = ForceNearLapCompletion(riderB, 0.0005f);

            EditorApplication.delayCall += () =>
            {
                bool aInLine = _waitingLine.Contains(viewA);
                bool bInLine = _waitingLine.Contains(viewB);
                int admittedCount = (aInLine ? 1 : 0) + (bInLine ? 1 : 0);

                // With N free slots for these two riders to race over, up to
                // N of them may legitimately be admitted (both, if N >= 2) -
                // the actual invariant under test is safety, not an exact
                // "only one wins" outcome that only holds when N == 1:
                // neither rider may vanish, duplicate, or end up in neither
                // riding nor WaitingLine (the stranded state this whole fix
                // exists to prevent).
                bool aResolved = aInLine || riderA.IsRiding;
                bool bResolved = bInLine || riderB.IsRiding;
                bool noDuplication = !(aInLine && riderA.IsRiding) && !(bInLine && riderB.IsRiding);
                bool admittedCountSane = admittedCount <= Mathf.Max(freeSlotsForRace, 0);

                _simultaneousTestPassed = forcedA && forcedB && aResolved && bResolved && noDuplication && admittedCountSane;

                Debug.Log($"CollectorLifecycleRegressionCheck: simultaneous-lap test - "
                    + $"freeSlots={freeSlotsForRace}, aInLine={aInLine} (stillRiding={riderA.IsRiding}), bInLine={bInLine} (stillRiding={riderB.IsRiding}) -> "
                    + (_simultaneousTestPassed ? "PASS" : "FAIL"));

                foreach (CollectorView filler in fillers)
                    _waitingLine.ClearSlotContaining(filler);
            };
        }

        // --- Phase: food-in-flight vs lap-boundary race (Part 2/3, 5) ---

        private static readonly int[] SatisfiableProbeMatchIds = { 1, 2, 3, 5, 6, 7, 13, 14, 15, 17, 18, 19 };
        private static int _foodRaceProbeCursor;
        private static double _foodRaceWaitingSince;
        private static bool _foodRaceProbeInFlight;
        private static readonly List<string> _foodRaceSkipped = new List<string>();

        // Per-probe timeout waiting for a target to be riding with hunger
        // still remaining - satisfiable collectors here are only 6-hunger,
        // so most self-satisfy within a few seconds; a probe that misses
        // its window is logged as skipped (not a failure) and this simply
        // moves on to the next candidate rather than stalling the run.
        private const double FoodRaceReadinessTimeout = 6.0;

        private static void RunFoodInFlightRaceTest(double elapsed)
        {
            if (_foodRaceProbeInFlight)
                return;

            if (_foodRaceProbeCursor >= SatisfiableProbeMatchIds.Length)
                return;

            int matchId = SatisfiableProbeMatchIds[_foodRaceProbeCursor];
            CollectorView view = FindByMatchId(matchId);
            var rider = view != null ? view.GetComponent<ConveyorRider>() : null;

            bool destroyed = view == null;
            bool ridingAndHungry = !destroyed && rider != null && rider.IsRiding && rider.RemainingHunger > 0;
            // Satisfied-while-riding (about to be removed any moment) or
            // already destroyed is a genuine "missed the window" case.
            // Still queued (rider exists, not yet riding, not in
            // WaitingLine) is NOT gone - it just has not boarded yet, and
            // must keep waiting rather than being skipped immediately.
            bool ridingButSatisfied = !destroyed && rider != null && rider.IsRiding && rider.IsSatisfied;
            bool permanentlyMissed = destroyed || ridingButSatisfied;
            bool timedOut = elapsed - _foodRaceWaitingSince >= FoodRaceReadinessTimeout;

            if (!ridingAndHungry)
            {
                if (permanentlyMissed || timedOut)
                {
                    _foodRaceSkipped.Add($"MatchId {matchId} (never caught riding with hunger remaining before it self-resolved)");
                    _foodRaceProbeCursor++;
                    _foodRaceWaitingSince = elapsed;
                }

                return;
            }

            var lifecycle = view.GetComponent<CollectorLifecycle>();
            var flightController = view.GetComponent<FoodFlightController>();
            if (lifecycle == null || flightController == null)
            {
                _foodRaceSkipped.Add($"MatchId {matchId} (missing CollectorLifecycle/FoodFlightController)");
                _foodRaceProbeCursor++;
                _foodRaceWaitingSince = elapsed;
                return;
            }

            _foodRaceProbeInFlight = true;

            // Reduce to exactly one pixel of remaining hunger, deterministically,
            // via the same public API a real arrival uses - never touching
            // PixelGrid, so grid reservation semantics stay untouched. Done in
            // the same synchronous call as the injection/force below so no
            // other Update() can slip in between and change RemainingHunger
            // out from under this probe.
            while (rider.RemainingHunger > 1)
                rider.RegisterConsumedPixel();

            // Inject the final packet using a real FoodFlightController.Enqueue
            // call (the exact production entry point PixelConsumer uses) so its
            // flight timing, launch staggering, and arrival callback are all
            // genuine - only the world-space start position is synthetic.
            flightController.Enqueue(view.transform.position, Color.white);

            // Force the lap to complete before that packet's flightDuration
            // (0.35s default) can possibly have elapsed - deterministically
            // reproducing Part 2 items 3-5 (in-flight food across the lap
            // boundary) on demand rather than waiting for a real ~7s lap to
            // coincide with it by chance.
            ForceNearLapCompletion(rider, 0.0005f);

            string name = view.name;
            double injectedAt = elapsed;
            _sawAwaitingFoodResolution[name] = false;

            void PollAfterInjection()
            {
                if (view == null)
                {
                    // Destroyed already - only valid if it resolved to
                    // Satisfied correctly (never valid while still
                    // "awaiting" - that would mean it vanished mid-wait).
                    if (!_sawAwaitingFoodResolution.TryGetValue(name, out bool sawAwaiting) || sawAwaiting)
                        _confirmedFoodRaceResolvedCorrectly.Add(name);
                    else
                        _foodRaceFailures.Add($"{name}: destroyed without ever observing _awaitingFoodResolution=true");

                    _foodRaceProbeInFlight = false;
                    _foodRaceProbeCursor++;
                    _foodRaceWaitingSince = Elapsed;
                    return;
                }

                bool awaiting = (bool)GetPrivateField(lifecycle, "_awaitingFoodResolution");
                if (awaiting)
                    _sawAwaitingFoodResolution[name] = true;

                bool strandedInWaitingLine = _waitingLine != null && _waitingLine.Contains(view) && rider.IsSatisfied;
                if (strandedInWaitingLine)
                {
                    _foodRaceFailures.Add($"{name}: satisfied but stranded in WaitingLine (never destroyed)");
                    _foodRaceProbeInFlight = false;
                    _foodRaceProbeCursor++;
                    _foodRaceWaitingSince = Elapsed;
                    return;
                }

                if (Elapsed - injectedAt < 2.0)
                {
                    EditorApplication.delayCall += PollAfterInjection;
                    return;
                }

                if (!_sawAwaitingFoodResolution.TryGetValue(name, out bool sawAwaiting2) || !sawAwaiting2)
                    _foodRaceFailures.Add($"{name}: never observed _awaitingFoodResolution=true despite in-flight injection at the lap boundary");
                else if (rider.IsRiding && !rider.IsSatisfied)
                    _foodRaceFailures.Add($"{name}: still riding, unsatisfied, and not awaiting resolution - final packet appears lost");
                else
                    _confirmedFoodRaceResolvedCorrectly.Add(name);

                _foodRaceProbeInFlight = false;
                _foodRaceProbeCursor++;
                _foodRaceWaitingSince = Elapsed;
            }

            EditorApplication.delayCall += PollAfterInjection;
        }

        private static void Finish()
        {
            EditorApplication.update -= Step;
            Application.logMessageReceived -= HandleLog;

            Debug.Log("CollectorLifecycleRegressionCheck: RESULTS ---");
            Debug.Log($"CollectorLifecycleRegressionCheck: permanently-hungry collectors that ever rode: {string.Join(", ", _everRode)}");
            Debug.Log($"CollectorLifecycleRegressionCheck: permanently-hungry collectors confirmed entering WaitingLine: {string.Join(", ", _confirmedEnteredWaitingLine)}");
            Debug.Log($"CollectorLifecycleRegressionCheck: permanently-hungry collectors confirmed STILL in WaitingLine at run end (no stranding back on conveyor): {string.Join(", ", _confirmedStayedInWaitingLine)}");
            Debug.Log($"CollectorLifecycleRegressionCheck: EndgameCleanupController.IsActive at run end: {(_endgameCleanup != null && _endgameCleanup.IsActive)}");
            Debug.Log($"CollectorLifecycleRegressionCheck: WaitingLine-full boundary test ran={_waitingLineFullTestRan}, passed={_waitingLineFullTestPassed}");
            Debug.Log($"CollectorLifecycleRegressionCheck: simultaneous-lap-completion test ran={_simultaneousTestRan}, passed={_simultaneousTestPassed}");
            Debug.Log($"CollectorLifecycleRegressionCheck: food-in-flight race probes attempted: {_foodRaceProbeCursor}, resolved correctly: {_confirmedFoodRaceResolvedCorrectly.Count}, skipped: {_foodRaceSkipped.Count}, failures: {_foodRaceFailures.Count}");
            foreach (string skipped in _foodRaceSkipped)
                Debug.Log($"CollectorLifecycleRegressionCheck: food-in-flight race probe skipped - {skipped}");
            foreach (string failure in _foodRaceFailures)
                Debug.LogError($"CollectorLifecycleRegressionCheck: food-in-flight FAILURE - {failure}");

            bool missingWaitingLineArrival = false;
            foreach (int matchId in PermanentlyHungryMatchIds)
            {
                CollectorView view = FindByMatchId(matchId);
                string name = view != null ? view.name : $"MatchId {matchId}";
                if (!_confirmedStayedInWaitingLine.Contains(name))
                {
                    missingWaitingLineArrival = true;
                    Debug.LogError($"CollectorLifecycleRegressionCheck: FAILURE - permanently-hungry collector '{name}' never reached/stayed in WaitingLine by run end.");
                }
            }

            Debug.Log($"CollectorLifecycleRegressionCheck: console errors/exceptions during run: {_errorCount}");

            bool foodRaceHadCoverage = _confirmedFoodRaceResolvedCorrectly.Count > 0;
            if (!foodRaceHadCoverage)
                Debug.LogError("CollectorLifecycleRegressionCheck: FAILURE - no food-in-flight race probe ever actually resolved (all skipped) - the race was never exercised.");

            bool overallPass = !missingWaitingLineArrival
                && _waitingLineFullTestPassed
                && _simultaneousTestPassed
                && _foodRaceFailures.Count == 0
                && foodRaceHadCoverage
                && _errorCount == 0;

            Debug.Log($"CollectorLifecycleRegressionCheck: OVERALL {(overallPass ? "PASS" : "FAIL")}.");

            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(overallPass ? 0 : 1);
        }
    }
}
