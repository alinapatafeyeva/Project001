# Technical and Product TODO

This is the actionable backlog — concrete, implementation-level items not
yet done. **`docs/ROADMAP.md` is the authoritative ordering**; this document
only groups backlog items under that ordering and adds implementation detail
the roadmap deliberately keeps brief. If an item here ever seems to
contradict `docs/ROADMAP.md`, the roadmap wins and this file is wrong.

Completed items are not listed here — see `docs/DECISION_LOG.md` and
`docs/ARCHITECTURE_NOTES.md` for what already exists.

---

## Phase 1 — Feeding Flow

See `docs/ROADMAP.md` Phase 1 for the full task list and
`docs/GAMEPLAY_CONTRACTS.md` for the approved contract this must satisfy.
Implementation reminders not already spelled out there:

- Restore Crab's natural claw pose specifically on the Conveyor — the
  current compact queue pose (claws rotated in, see
  `CharacterAssetBuilder.ApplyCrabClawPoseAdjustment`) must stay unchanged
  for queue/waiting presentation.
- `FeedTarget` position should be authored per `Character_XX` (near the
  face) but read through one shared API — no per-species branching in
  gameplay code.
- Reservation bookkeeping belongs on `ConveyorRider` (or a new component it
  owns), not on `PixelConsumer` — `PixelConsumer` already only reacts to a
  successful grid consume, and reservation must persist across multiple
  in-flight pixels per collector.
- **Post-MVP / experimental:** a simple Turtle mouth. Only build this if
  visual testing of the universal reaction (highlight + squash/bounce +
  dissolve) shows it specifically reads poorly on Turtle. Do not build it
  speculatively, and never make any other species depend on it existing.

---

## Phase 2 — Pixel Grid Visuals and Large Grid Technical Spike

- Define the source format for pixel-art images and food-type-to-MatchTypeId
  mapping, so representative image-based levels can be authored at all.
- Profile CPU, memory, GC, rendering, loading, and battery use at target
  grid sizes on a real lower-end Android device, not just the Editor.
- Avoid unnecessary complete-grid scans as dimensions grow —
  `PixelGrid.TryConsumeNearestExposed` is currently a full double loop over
  every cell per consumption attempt; re-check whether this still holds up
  at ~30×40+.
- Decide the practical GameObject-per-pixel limit before Phase 5 content
  production locks in a target grid size.

---

## Phase 3 — Full Level Visual Design

- Replace `CollectorView`'s prototype `TextMesh` hunger display with
  production UI.
- Add a departure animation into the Waiting Line (currently an instant
  reparent + snap — see `CollectorLifecycle.ResolveLap`, which explicitly
  never animates this transfer today).
- Define whether Failure happens immediately or after a short visual
  warning once a full Waiting Line is detected.
- Define how production collectors visually communicate "waiting" vs.
  "satisfied" beyond the current facing-away/idle-breathing pose.
- Add an in-game Help (ⓘ) screen explaining hunger-bar colours and gameplay
  rules (extend later, in Phase 7, to cover boosters once they exist).
- Replace `Physics2D.OverlapPoint()` selection with a deterministic
  multi-collider strategy once selectable colliders may overlap (VFX, UI,
  and production visuals are likely to introduce this).
- Introduce object pooling before production animations/VFX create frequent
  object creation/destruction — not before, per
  `docs/DECISION_LOG.md` decision 012 (no premature optimisation).
- Lock the production colour palette in `Assets/Art/ColorPalette.md` (the
  current table is approved and stable, but treated as provisional pending
  final art direction).
- Add a configurable gameplay-speed multiplier (including ×2 speed), shared
  by conveyor movement, animations, effects, and timers — see
  `GameplayConstants`'s own remarks on `BaseConveyorMoveSpeed` for where
  this hooks in. Re-check pixel-consumption alignment sampling at ×2 speed
  so a fast-moving rider cannot skip a row/column.

---

## Phase 4 — Responsive Layout

- Position gameplay elements relative to `PixelGrid`/camera bounds instead
  of `GameplayLayout`'s current fixed prototype constants.
- Support safe areas, display cutouts, rounded corners, and system bars.
- Define the intended tablet layout strategy explicitly (not just "scale
  down the phone layout") — see `docs/UI_REFERENCE.md`'s tablet lessons.
- Recalculate Conveyor/RecoveryRow/WaitingLine positions per aspect ratio,
  not just camera orthographic size.

---

## Phase 5 — Level System and Content

- Replace `LevelCatalog`'s two hand-authored levels with a real
  level-authoring workflow/tool.
- Extend `LevelDefinitionValidator`'s existing pixel/HungerCapacity/Match ID
  checks with static solvability checks.
- **Per-Match Energy Validation** (TODO, not yet implemented): production
  levels must guarantee that the total available energy (pixel supply) for
  every MatchId is sufficient to satisfy the total demand of all collectors
  of that MatchId. When the procedural/final level generator is built, add a
  validation pass that checks, for every MatchId: total collector demand;
  total available pixels (and any future energy sources); whether the level
  is mathematically completable. The generator must never produce a
  production level where collectors remain unsatisfied simply because the
  required MatchId runs out of energy. This validation applies to
  production-generated levels only — debug/test levels (e.g. Feeding Flow,
  see Phase 8 below) are explicitly allowed to intentionally violate this
  invariant when testing WaitingLine, Recovery, Endgame Cleanup, or other
  failure scenarios.
- Measure average completion time per level and per chapter; avoid chapters
  that finish too quickly or feel repetitive.
- Define the provisional chapter length (currently ~30 levels, per
  `docs/DECISION_LOG.md` decision 014) and a repeatable process for adding a
  new chapter.
- Keep chapter ranges editable until production content is locked.

---

## Phase 6 — Save Progress and Level Map

- Save progress: current unlocked level, completed levels, settings.
- Resume directly into the current `LevelId` instead of
  `LevelBootstrapper`'s configured `startingLevelId`.
- Path-style level map: completed / current / locked level states, themed
  map segments per chapter.
- Victory UI: add a Map button that returns to the level map instead of
  always advancing to the next level.
- Handle the last currently available level gracefully (a "more levels
  coming soon" state) rather than erroring when `LevelProgressionController`
  has nowhere left to advance to.

---

## Phase 7 — Boosters

Design only after Phase 5 balancing is understood — see
`docs/ROADMAP.md` Phase 7.

Candidate boosters (not yet approved individually, listed for design
reference):

- Increase conveyor capacity.
- Add an extra collector queue.
- Add extra Waiting Line slots.
- Shuffle a selected queue.
- Activate a random eligible collector.
- A "consumes an entire MatchTypeId at once" booster (previously named
  "Super Hungry Monster" — needs a current, non-Mofu-era name before
  implementation).
- Define whether a booster applies for one level, one action, or a limited
  duration.

---

## Phase 8 — Developer Infrastructure

Named feature-level tests (`Test_EndgameCleanup`, `Test_Recovery`,
`Test_Hunger`, `Test_PixelFlight`, `Test_MatchIdConsistency`,
`Test_LevelValidation`, `Test_SaveProgress`, `Test_ResponsiveLayout`,
`Test_Performance`) are listed in `docs/ROADMAP.md` Phase 8 and are not
repeated here. Lower-level unit coverage to add alongside them:

- EditMode tests: exposed-pixel detection, side/alignment selection, hunger
  reduction, boarding-relative lap completion, conveyor capacity, queue
  first-item removal, Waiting Line transfer rollback.
- PlayMode tests: the complete gameplay loop end to end.
- Debug/cheat panel: configurable game speed (including the ×2 toggle from
  Phase 3), unlimited boosters for testing, skip directly to any level,
  instantly complete/fail the current level, reset the current level. Note:
  `LevelBootstrapper.enableFailureTestSetup` already provides a
  deterministic Inspector-only Failure trigger for one specific case — the
  general debug panel is still open.
- **Feeding Flow test mode** (`LevelBootstrapper.enableFeedingFlowTestSetup`,
  mutually exclusive with `enableFailureTestSetup`): tick this box on the
  `LevelBootstrapper` GameObject in `Assets/Scenes/Bootstrap.unity` and enter
  Play Mode to validate the full Pixel Feed Flow against every
  Character_01-20 in one run. Builds `FeedingFlowTestLevelFactory`'s own
  level instead of `startingLevelId`'s approved one: 4 queues x 5 collectors
  = all 20 Match IDs at once (queue `q` holds Match IDs `q+1, q+5, q+9,
  q+13, q+17`), a dedicated 20x6 pixel layout giving every Match ID exactly
  6 real, matching pixels, and every collector fed through the exact
  production chain (no direct `RegisterConsumedPixel` calls, no fake
  packets). Debug hunger capacity is
  `FeedingFlowTestLevelFactory.FeedingFlowTestHungerCapacity` (18 — larger
  than any one Match ID's 6-pixel supply) for row 2 of every queue (Match
  IDs 9/10/11/12), so those four collectors stay hungry after their own
  species' pixels run out and cycle into WaitingLine and stay there —
  the clear demonstration of "large debug hunger, doesn't just vanish"
  the task calls for. Every other row is satisfiable (hunger set to
  exactly 6, its own full pixel supply), including Match ID 1/Crab
  specifically so a developer can confirm the satisfied/disappear
  sequence on purpose (Crab's own claw-pose switching included). This
  4-large/16-satisfiable split (not a 50/50 or larger-hungry-majority
  split) is deliberate: WaitingLine has no eject/timeout, so a
  permanently-hungry collector occupies a Conveyor or WaitingLine slot
  forever once it gets one, and Conveyor (5) + WaitingLine (5) = 10 slots
  total — confirmed empirically, 11+ permanently-hungry collectors
  reliably fill both and deadlock the remaining queue for the rest of
  that session, while 4 leaves the system reliably, deterministically
  draining through all 20 Match IDs every run.

---

## Phase 9 — Advanced Level Mechanics

See `docs/ROADMAP.md` Phase 9 for the task list. No additional
implementation notes yet — detailed design should happen closer to when
this phase starts, informed by Phase 5's real level content.

---

## Phase 10 — Closed Beta and Full MVP Polish

- Haptic feedback, with distinct patterns for: eating a pixel, satisfying a
  collector, victory, failure, and important UI interactions.
- Monetisation is not part of the approved phase ordering yet — see "Not
  yet scheduled" below.

---

## Not yet scheduled in `docs/ROADMAP.md`

These are real backlog items with no approved phase yet. Listed here rather
than silently dropped, but deliberately **not** attached to a phase number —
doing so without an actual roadmap decision would make this file a second,
competing roadmap. Assign a phase before starting work on any of these.

**Monetisation:**

- Level-completion coin rewards.
- Optional rewarded ad to double level rewards.
- `IRewardedAdService` with a fake development implementation, then a real
  mediation SDK before beta/release.
- Handle ad unavailable/cancelled/failed/rewarded outcomes.
- Privacy consent and app-ads.txt/store readiness setup.
- Never block normal reward collection behind an ad.

**Shape-based level generation** (previously its own roadmap section):

- Shape masks (heart, butterfly, flower, star, animals, etc.) with
  active/inactive cells inside the rectangular grid.
- `MatchTypeId` distribution generation inside the active shape
  (clusters/stripes/gradients/symmetry/controlled noise instead of pure
  randomness).
- Preview and validate generated levels before approval.
