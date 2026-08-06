# Architecture

This document describes the runtime architecture as it is actually
implemented today. It does not describe planned or experimental systems —
see `docs/ROADMAP.md` for those, and `docs/GAMEPLAY_CONTRACTS.md` for the
gameplay behaviour that must stay stable as implementation changes.

---

## Architectural Principles

### Single responsibility

Each gameplay component owns one clearly defined responsibility. Systems
collaborate through small public APIs rather than directly managing each
other's internal state.

### Explicit orchestration

Cross-system gameplay flows are coordinated by dedicated controller or
lifecycle components. For example:

- `CollectorSelectionController` coordinates selection and conveyor boarding.
- `CollectorLifecycle` coordinates post-consumption collector resolution.
- `EndgameCleanupController` coordinates the transition into Endgame Cleanup.
- `FailureRecoveryController` coordinates Retry/Continue after Failure.

### Configurable rules

Gameplay values that may change between levels, bonuses, or devices are not
permanently hard-coded into core behaviour. Global rules that apply
identically to every level live in `GameplayConstants`, not inside
`LevelDefinition`.

### Mobile-conscious implementation

The game targets mobile devices, including reasonably low-end devices. The
codebase avoids unnecessary work in `Update`, repeated runtime allocations,
excessive physics queries, and duplicated derived state. Optimisation is
guided by profiling rather than assumptions — see Phase 2 in
`docs/ROADMAP.md` for the planned large-grid profiling spike.

---

# Presentation Identity: MatchTypeId vs Match ID

The project separates gameplay matching from visual presentation through two
distinct identities. Getting this distinction right is the single most
important thing to understand about the current architecture.

## MatchTypeId — gameplay matching identity

`Assets/Scripts/Gameplay/Levels/MatchTypeId.cs`

A stable, abstract string identity (e.g. `"m001"`) shared by an edible pixel
and the collectors that can consume it. Carries no colour, sprite, or theme
meaning. It is **level-scoped** — the same `MatchTypeId` value can resolve to
a different Match ID in a different level (see `LevelCatalog`, where
`level_001` and `level_002` deliberately assign `"m002"` to different Match
IDs). `PixelGrid.TryConsumeNearestExposed`, `PixelConsumer`, and
`ConveyorRider` are the only things that ever compare `MatchTypeId` — this is
gameplay's one and only matching key.

## Match ID — presentation identity

A permanent integer, 1–20, defined once in `Assets/Art/ColorPalette.md` and
mirrored at runtime in `Assets/Scripts/Gameplay/Presentation/ColorPalette.cs`.
Match ID is:

- the only thing `PixelGrid` resolves a pixel's visible colour from
  (`ColorPalette.TryGetByMatchId`);
- the only thing `CharacterDatabase` resolves a collector's `Character_XX`
  prefab from (`CharacterDatabase.GetPrefab`);
- carried by both `PixelLayoutDefinition` (per cell, alongside its
  `MatchTypeId`) and `CollectorDefinition` (per collector, alongside its
  `MatchTypeId`) — but is never part of either's gameplay identity, and
  nothing derives one identity from the other.

## Why both exist on `PixelLayoutDefinition`

Each cell of a level's pixel layout carries **both** a `MatchTypeId` (what a
collector must match to consume it) and a Match ID (what colour it renders
and, transitively, which `Character_XX` a matching collector displays as).
This lets one `MatchTypeId` mean different colours/characters in different
levels without gameplay logic ever needing to know that a colour or a
character exists.

## Consistency validation

`LevelDefinitionValidator.Validate` runs once, at `LevelCatalog` construction,
before any level ever reaches a runtime system, and enforces two independent
rules for every approved level:

1. **Pixel/hunger balance** — for every `MatchTypeId` appearing anywhere in
   the level, total collector `HungerCapacity` for that `MatchTypeId` must
   exactly equal the pixel count of that `MatchTypeId` (no surplus, no
   shortfall, no one-sided appearance).
2. **Match ID consistency** (`ValidateMatchIdConsistency`) — every pixel and
   every collector sharing one `MatchTypeId`, within one level, must resolve
   to the exact same Match ID. Two collectors of the same `MatchTypeId`
   carrying different Match IDs would mean that `MatchTypeId`'s pixels have
   no single consistent colour/character to agree with.

A violation throws `InvalidOperationException` identifying the offending
`LevelId` and `MatchTypeId`, at construction time — a mismatch can never
reach a running scene silently.

---

# Runtime Gameplay Flow

```text
LevelBootstrapper (Awake)
        ↓ resolves LevelDefinition from LevelCatalog via LevelProgressionController
PixelGrid.Initialize / ConveyorSystem.Configure / WaitingLine.Initialize / CollectorQueueBoard.Initialize
        ↓
CollectorQueueBoard  →  CollectorSelectionController  →  ConveyorSystem
                                                              ↓
                                                        PixelConsumer → PixelGrid
                                                              ↓
                                                        CollectorLifecycle
                                                          ↓                 ↓
                                              Satisfied → removed    Unsatisfied, lap complete
                                                                        ↓                    ↓
                                                              WaitingLine slot free   no slot free
                                                                                          ↓
                                                                                  FailureController
```

In parallel:

- `VictoryController` watches `PixelGrid.IsComplete` and fires `OnVictory`
  once.
- `EndgameCleanupController` watches total remaining collectors (queued +
  riding + waiting + in Recovery Row) and, once that drops to
  `GameplayConstants.WaitingLineCapacity` or below, permanently disables
  `FailureController`, stops `WaitingLine` from accepting new collectors, and
  speeds up `ConveyorSystem`.
- `FailureRecoveryController` owns what Retry (full scene reload) and
  Continue (transfer every Conveyor rider into `RecoveryRowController`,
  rearm `FailureController`) actually do after a Failure.

---

# Gameplay Systems

## LevelDefinition / LevelCatalog / LevelDefinitionValidator

Location: `Assets/Scripts/Gameplay/Levels/`

`LevelDefinition` is one complete, immutable, deterministic level: a
`PixelLayoutDefinition` and a list of `CollectorQueueDefinition`s — only what
actually varies between levels. It owns no runtime state and no
player-progress concerns. `LevelCatalog` owns construction of every approved
`LevelDefinition` (currently `level_001` and `level_002`, both hand-authored
test/prototype levels) and validates each one at construction time via
`LevelDefinitionValidator`. Conveyor capacity, conveyor speed, and Waiting
Line capacity are never level data — they live in `GameplayConstants` and are
identical for every level.

## PixelCell / PixelGrid

Location: `Assets/Scripts/Gameplay/Pixels/`

`PixelCell` represents one edible pixel: grid coordinates, cached local
position, `MatchTypeId`, Match ID, active/consumed state. It knows nothing
about collectors, the conveyor, or hunger.

`PixelGrid` generates a centred grid of `PixelCell`s from a
`PixelLayoutDefinition`, using a single shared runtime-generated 1×1 sprite.
Visible colour is resolved per cell directly from its Match ID via
`ColorPalette.TryGetByMatchId` — there is exactly one presentation-colour
source in the project. Consumption
(`TryConsumeNearestExposed`) is restricted to the row or column currently
aligned with the requesting position, from the grid side that position is
closest to, and only once every cell between the candidate and that boundary
is already consumed (no whole-side nearest-pixel search, no consuming
through unconsumed cells). The grid does not know why a consumer wants a
pixel and does not touch hunger.

## PixelConsumer

Location: `Assets/Scripts/Gameplay/Pixels/PixelConsumer.cs`

Polls (on a cooldown, not every frame) whether its `ConveyorRider` can
consume a matching, aligned, exposed pixel from `PixelGrid`. On a successful
consume it calls `ConveyorRider.RegisterConsumedPixel()` and triggers exactly
one `CollectorPresentation` reaction: the normal bite reaction, or — only
when that consume brought `RemainingHunger` to zero — the full final-bite
completion sequence. It owns no animation logic itself.

**Current limitation (see Phase 1 in `docs/ROADMAP.md`):** consumption is
immediate and direct — there is no pixel-flight animation, no `FeedTarget`,
and no reserved-hunger concept yet. `RemainingHunger` changes the instant a
pixel is matched, in the same frame.

## ConveyorPath / ConveyorPathRenderer / ConveyorRider / ConveyorSystem

Location: `Assets/Scripts/Gameplay/Conveyor/`

`ConveyorPath` is a procedural rounded-rectangle closed route with
normalized progress sampling. `ConveyorPathRenderer` is its purely visual
representation. `ConveyorRider` holds the minimal state a riding collector
needs — `MatchTypeId`, `HungerCapacity`, `RemainingHunger`,
`RemainingHungerChanged` event, riding state, and (for presentation only)
`RidingOrientation`. `ConveyorSystem` moves every rider counter-clockwise at
one shared speed, all boarding through one fixed `boardingProgress` point
gated by `boardingClearance`, so launch order is preserved for as long as a
rider stays on the conveyor. A completed lap is measured relative to
`boardingProgress`, not the path's raw 0→1 seam (the seam sits near the
upper-right corner; boarding happens lower-left). `TakeAllRiders()` removes
and returns every current rider in launch order without cloning or
recreating them — the mechanism `FailureRecoveryController.ContinueCurrentLevel`
uses to move every rider into the Recovery Row while preserving
`MatchTypeId`/`RemainingHunger` exactly.

## CollectorView / CollectorPresentation / CollectorAnimation

Location: `Assets/Scripts/Gameplay/Collectors/`

This is the current 3D character presentation hierarchy, replacing the
earlier sprite-based prototype entirely:

```text
Collector root (gameplay-owned: queue placement, Conveyor movement,
                WaitingLine/RecoveryRow reparenting — always at world Z=0)
├── HungerText (sibling of Visual, not a child of it)
└── Visual — the yaw pivot; only CollectorAnimation.Update() ever touches
    its localRotation
    └── VisualMotion — the scale/position pivot; breathing, boarding bounce,
        eating/satisfied punches, and the heart pulse/collapse are the only
        things that ever touch its localScale/localPosition
        └── CharacterVisual — the resolved Character_XX prefab instance
            (injected via CollectorView.Initialize, pivot-corrected against
            its own mesh bounds), already carrying its own baked
            Character_XX material
```

`CollectorView` only decides which prefab to instantiate and owns the
`RemainingHunger` text label; it holds no pose or facing logic.
`CollectorPresentation` is the single authority that decides which pose/
reaction to play (facing away + idle breathing while waiting anywhere;
facing the pixel grid, continuously re-aimed, for the entire time a
collector actively rides; the terminal eating-punch → satisfied-punch →
heart-pulse → collapse sequence on the final bite) and is the only caller
into `CollectorAnimation`, which owns every actual transform tween. At most
one presentation sequence runs at a time; starting a new one stops whatever
is currently running. `PlayFinalBiteSequence` is terminal and idempotent —
either `PixelConsumer` or `CollectorLifecycle` may trigger it, never both
competing.

Queue-row and terminal-sequence depth separation is achieved by pulling
`Visual` along `-GameplayLayout.CameraForward` (a real camera-space forward
pull, genuinely resolved by the Z-test, not a `sortingOrder` guess) — see
"Camera and Layout" below. The collector **root** itself never leaves world
Z=0 at any point in its lifecycle (queued, selected, boarding, riding,
waiting, in Recovery Row); only `Visual`'s child-local depth changes.

## CollectorQueue / CollectorQueueBoard

Location: `Assets/Scripts/Gameplay/Collectors/`

`CollectorQueue` stores one ordered logical queue, exposing only its first
available collector for removal. `CollectorQueueBoard` generates the
configured number of queues and collectors per queue from level data,
resolves each collector's Match ID to a `Character_XX` prefab via
`CharacterDatabase`, and instantiates it as that collector's Visual. Queued
collectors start facing away; `CollectorSelectionController` switches a
collector to facing the grid the moment it boards. Queue spacing uses
`GameplayLayout.CollectorVisibleWidth`/`Height` — the character's actual
visible extent, not its raw sprite/transform scale square, which includes
empty margin.

## CollectorSelectionController

Location: `Assets/Scripts/Gameplay/Collectors/CollectorSelectionController.cs`

Detects pointer selection (mouse or touch, via the Input System) of an
eligible `CollectorView` from any configured `ICollectorSource`
(`CollectorQueueBoard`, `WaitingLine`, or `RecoveryRowController`) and moves
it onto `ConveyorSystem`. A tap is accepted immediately even if the boarding
point is not yet clear — it enqueues a pending boarding retried every frame,
strictly in selection order, so a later tap can never board ahead of an
earlier one still waiting. Boarding is rolled back (collector restored to
its exact original parent/position/pose/presentation-depth) if the source
fails to release the collector after the conveyor already accepted it.
Selection reverse-projects a screen tap onto the shared world Z=0 plane,
accounting for the camera's tilt, then resolves a 2D collider there — there
is exactly one target plane, which is what makes this reverse-projection
unambiguous.

## CollectorLifecycle

Location: `Assets/Scripts/Gameplay/Collectors/CollectorLifecycle.cs`

The only system that decides whether a riding collector disappears or enters
the Waiting Line. A satisfied rider is removed from the Conveyor and
resolved **immediately** — its collider disables and the static
`CollectorSatisfied` event fires synchronously — without waiting for a lap.
Only `Destroy(gameObject)` is deferred, until `CollectorPresentation`
finishes its completion sequence (or immediately, if presentation is
unavailable/fails to start). An unsatisfied rider continues until it
completes a full lap, then either lands in the first free `WaitingSlot`
(reparented and snapped, no travel animation; pose switches to facing away)
or, if no slot is free, stays riding while `FailureController` is notified.

## WaitingSlot / WaitingLine

Location: `Assets/Scripts/Gameplay/WaitingLine/`

`WaitingSlot` represents one position and its occupant. `WaitingLine`
generates `GameplayConstants.WaitingLineCapacity` slots (a fixed global rule,
never level data) and exposes the first empty one. `StopAcceptingCollectors`
is an irreversible switch used by `EndgameCleanupController`.

## RecoveryRowController / RecoveryRowView

Location: `Assets/Scripts/Gameplay/Recovery/`

`RecoveryRowController` owns whichever `ConveyorRider` instances
`FailureRecoveryController.ContinueCurrentLevel` transfers into it after a
Continue — the same instances, never cloned, so `MatchTypeId`,
`RemainingHunger`, and `HungerCapacity` carry over exactly. It is purely a
gameplay-ownership component; `RecoveryRowView` (driven by the
`CollectorsChanged` event) owns reparenting and layout. Doubles as an
`ICollectorSource` so a held collector can be launched back onto the
Conveyor manually through the same selection path as any other source.

## FailureController / EndgameCleanupController / VictoryController

Location: `Assets/Scripts/Gameplay/Failure/`, `Assets/Scripts/Gameplay/`,
`Assets/Scripts/Gameplay/Victory/`

`FailureController.NotifyWaitingLineFull` triggers Failure exactly once, only
if `PixelGrid` still has active pixels (victory takes precedence).
`DisablePermanently` (called by `EndgameCleanupController`) is irreversible
within a level; `ResetFailure` (called by `FailureRecoveryController` on
Continue) rearms detection for a new attempt. `VictoryController` fires
`OnVictory` exactly once, the first frame `PixelGrid.IsComplete` becomes
true. `EndgameCleanupController.NotifyLevelBuilt()` (called once by
`LevelBootstrapper`, after every other system is populated) is the only safe
point for its first threshold check; every later check is driven by the
`CollectorLifecycle.CollectorSatisfied` event, never per-frame polling.

## GameplayFlowController / VictoryFlowController / FailureRecoveryController

Location: `Assets/Scripts/Gameplay/`

`GameplayFlowController` is the sole owner of pause/resume
(`Time.timeScale`), reacting to both `VictoryController.OnVictory` and
`FailureController.OnFailure`. `VictoryFlowController` owns what Continue
does after Victory (resume, then `LevelProgressionController.LoadNextLevel()`).
`FailureRecoveryController` owns Retry (full scene reload) and Continue
(transfer riders to Recovery Row, rearm Failure, resume) — see the Runtime
Gameplay Flow diagram above. Presentation (`VictoryUI`, `FailureUI`) only
ever calls these controllers' public methods; none of them touch
`Time.timeScale`, scene loading, or `Canvas`/`Button` state directly in the
other direction.

## LevelProgressionController / LevelBootstrapper

Location: `Assets/Scripts/Gameplay/Levels/`

`LevelProgressionController` owns which `LevelId` is current for the session
and resolves the next one; `LevelBootstrapper` asks it for a `LevelId` every
`Awake`, resolves the matching `LevelDefinition` from `LevelCatalog`, and
builds `PixelGrid`, `ConveyorSystem` (via `GameplayConstants`, never level
data), `WaitingLine`, and `CollectorQueueBoard` from it, then calls
`EndgameCleanupController.NotifyLevelBuilt()` last. This is what makes the
scene work correctly whether entered fresh, reloaded (Retry), or advanced
(Victory's Continue) — none of the generated runtime state is expected to
survive Unity scene serialization; only `LevelBootstrapper`'s own object
references do.

---

# Character Presentation Architecture

## CharacterDatabase

Location: `Assets/Scripts/Gameplay/Presentation/CharacterDatabase.cs`

Maps a Match ID (1–20) to a `Character_XX` prefab, entirely through
Inspector-configured entries — no `Resources.Load`, no Addressables, no
species/color name baked into gameplay code. `GetPrefab` never returns null
without first falling back to Match ID 1 and logging an error; only if even
that fallback is unconfigured does it return null. This is the only seam
`CollectorQueueBoard` resolves a collector's visual through.

## Character_XX prefabs

Location: `Assets/Art/Themes/Classic/Character/Character_01` through
`Character_20` (the real, current, singular `Character` folder — not
`Characters`, and not the deleted `Assets/Art/Sprites/...` tree).

Each `Character_XX` folder is a **complete, static, baked visual asset**:

- `Character_XX.png` — the authored texture, used exactly as-is (never
  recolored or generated at runtime);
- `Character_XX.mat` — a URP Lit material cloned from the vendor species
  material, with `_BaseMap`/`_MainTex`/`_EmissionMap` repointed at
  `Character_XX.png` and a per-species Emission intensity baked in (see
  `CharacterAssetBuilder`'s remarks for the calibration);
- `Character_XX.prefab` — vendor body + vendor face assembled around that
  material, root-scaled so every species shares the same perceived body
  height (`GameplayLayout.CollectorVisibleHeightRatio`), regardless of each
  species' own raw width.

All 20 are built by `Assets/Scripts/Editor/CharacterAssetBuilder.cs`
(`Tools/Characters/Build All Character Prefabs`) from the fixed
`MatchIdToSpecies` table (Crab/Turtle/Fish/Octopus — see
`Assets/Art/ColorPalette.md`) and the vendor Cube Animals prefabs. No pixel
of any texture is generated or recolored — runtime material/color swapping
was removed entirely (see `docs/DECISION_LOG.md`). Re-running the builder is
idempotent and safe.

## Verification

`Assets/Scripts/Editor/CharacterVerification.cs` (`Tools/Characters/Run
Verification`, `Run Queue Row Depth Diagnostic`, `Run Color And Bounds
Diagnostics`) is the ongoing diagnostic suite for this system: asset-level
checks (prefab/material/texture wiring for all 20 Match IDs) and live
Play Mode checks (no missing references, no error-shader materials, no
runtime-instanced materials, real queue/conveyor gameplay still advances).

---

# Camera and Layout

Location: `Assets/Scripts/Gameplay/Presentation/GameplayLayout.cs`,
`PortraitCameraFitter.cs`

`GameplayLayout` is the single source of truth for the scene's composition —
region sizes, spacing, presentation tokens, and camera framing — as a
deliberate design allocation, never a measurement of any specific level's
content.

## Tilted orthographic camera

The Main Camera is orthographic, but not aimed straight down the Z axis: it
carries a fixed downward pitch, `GameplayLayout.CameraTiltDegrees` (30°),
via `CameraRotation = Quaternion.Euler(CameraTiltDegrees, 0f, 0f)`. This is
what makes the 3D characters read with visible volume instead of a flat
top-down silhouette. `CameraPosition` and `ComputeOrthographicSize` both
account for this tilt so the same fixed composition frames correctly at any
portrait aspect ratio (`PortraitCameraFitter.Fit`, driven by real
`Screen.width`/`Screen.height`, re-applies exactly this at runtime — this
and `BootstrapSceneCreator` can never drift apart from each other).

## The shared Z=0 gameplay plane

Every collector's **root** transform — whether queued, selected, boarding,
riding the Conveyor, waiting, or held in the Recovery Row — stays on world
Z=0 for its entire lifecycle. This is deliberate: an earlier version placed
queue rows at different Z depths directly, which broke
`CollectorSelectionController`'s tap-to-world reverse-projection (a tap
resolved against the wrong row) once the camera gained its tilt. All visible
depth separation instead happens on the **`Visual` child transform**, pulled
along `-GameplayLayout.CameraForward` — a real camera-space depth offset,
resolved by the actual Z-test, never a `sortingOrder` approximation:

- queue rows: `CollectorPresentation.SetQueueRowDepth(rowIndex)`, each row
  `GameplayLayout.QueueRowDepthStep` closer to the camera than the last;
- the terminal Satisfied/Heart sequence:
  `CollectorAnimation.EnterTerminalForeground`, pulled far enough forward to
  clear an ordinary rider sharing the same screen position;
- every other waiting source (WaitingLine, Recovery Row, a rolled-back
  boarding): `ClearPresentationDepth()`, the neutral baseline.

## Queue-row presentation

`CollectorQueueBoard` positions each row from `GameplayLayout.QueueRowStep`
(derived from the character's real visible height plus an authored gap,
`QueueVisibleGap`) and applies genuine camera-forward depth per row (above),
allowing controlled visual overlap in screen Y that depth order — not row
spacing — resolves. Every species shares one target visible height; a wider
species (e.g. Turtle) is handled by measuring its actual resulting width
after height-scaling and sizing `GameplayLayout.CollectorVisibleWidthRatio`
to it, never by shrinking that species independently.

## WaitingLine, RecoveryRow, Conveyor, and satisfied disappearance

- **WaitingLine**: fixed capacity (`GameplayConstants.WaitingLineCapacity`),
  landing markers only — an arriving collector snaps to a slot's position,
  keeping its own scale.
- **RecoveryRow**: sized from however many collectors
  `FailureRecoveryController.ContinueCurrentLevel` actually transfers; not a
  fixed capacity.
- **Conveyor**: one fixed boarding point, one shared move speed, capacity
  and speed from `GameplayConstants` (never level data). Endgame Cleanup
  raises the speed via a multiplier without touching capacity.
- **Satisfied disappearance**: gameplay-immediate (see `CollectorLifecycle`
  above) — the collector stops counting toward `RemainingCollectors` and
  stops being selectable the instant it is removed from the Conveyor, well
  before its completion animation and `Destroy(gameObject)` actually finish.

---

# Editor Bootstrap

Location: `Assets/Scripts/Editor/BootstrapSceneCreator.cs`

`Tools/Bootstrap/Create Bootstrap Scene` rebuilds `Assets/Scenes/Bootstrap.unity`
from scratch, reproducibly: an empty scene, camera + key light + ambient
lighting, `PixelGrid`, `ConveyorSystem`, `RecoveryRowController`/View,
`WaitingLine`, `FailureController`, `CharacterDatabase` (populated with all
20 `Character_XX.prefab` references by `AssetDatabase.LoadAssetAtPath`),
`CollectorQueueBoard`, `CollectorSelectionController`,
`VictoryController`/`GameplayFlowController`/`FailureRecoveryController`/
`EndgameCleanupController`/`LevelProgressionController`,
`LevelBootstrapper`, an `EventSystem` (new Input System module), and
prototype Victory/Failure UI Canvases — wiring every cross-reference between
them. It contains no level content itself: `PixelGrid`, `WaitingLine`,
`CollectorQueueBoard`, and `ConveyorSystem` stay empty/unconfigured in the
saved scene until `LevelBootstrapper` builds them from a `LevelDefinition` at
runtime `Awake`. Re-running this tool is the standard way to recover a clean
Bootstrap scene after any manual scene edit — see `docs/README_GAME.md` for
the exact menu path.

It also assigns the Universal Renderer (registered on `UniversalRP.asset`)
to the Main Camera specifically, alongside the project's still-default 2D
Renderer, since URP Lit materials on the 3D characters need a Directional
Light pass the 2D Renderer does not provide; every other 2D visual
(SpriteRenderers, UI, PixelGrid) renders identically under either renderer.

Editor-only; must never contain production gameplay decisions.

---

# Current Known Limitation: Vendor Animators

Every `Character_XX` prefab ships with its vendor Animator Controller
**disabled** (`animator.enabled = false`, set once by
`CharacterAssetBuilder` and preserved as a prefab override) — the current
presentation is a static queue/conveyor visual with no skeletal animation of
its own; all reaction "animation" (breathing, boarding bounce, eating punch,
satisfied punch, heart pulse/collapse) is transform-level tweening owned by
`CollectorAnimation`, not vendor clip playback. The vendor rig does carry
real per-clip animation, including a Fish/Octopus `RigMouth` bone whose
scale is genuinely keyframed in some clips (e.g. `Bite Attack`), but nothing
in the live scene currently enables or plays it. Enabling vendor Animators
is not yet part of the live presentation contract — see Phase 1 in
`docs/ROADMAP.md` (mouth-bone animation is explicitly optional, species-
specific polish there, never a required part of the feeding contract).

---

## Assets structure

Rules:

- Character folders use stable numeric IDs (Match ID).
- Food uses the same numeric IDs.
- Matching is performed by `MatchTypeId`; Match ID governs presentation only.
- File names inside a `Character_XX` folder are identical in shape for every
  character (`Character_XX.prefab`/`.mat`/`.png`).
- Themes may replace every asset while preserving IDs.

```text
Assets
└── Art
    ├── Themes
    │   └── Classic
    │       └── Character
    │           ├── Character_01
    │           │   ├── Character_01.prefab
    │           │   ├── Character_01.mat
    │           │   └── Character_01.png
    │           ├── Character_02
    │           └── ... Character_20
    └── Sprites
        └── Themes
            └── Classic
                ├── Food
                ├── UI
                └── Backgrounds
```

`Assets/Cube Animals 02/` is the vendor asset pack `CharacterAssetBuilder`
reads from; it is never written to, and is never modified directly.
