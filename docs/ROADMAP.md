# Roadmap

This is the single authoritative, phased ordering for the project. It
supersedes any ordering implied elsewhere. `docs/TODO.md` is the actionable
backlog *within* this ordering — if the two ever disagree, this document
wins.

No dates are assigned to any phase. A phase is "done" only when its
completion criterion is met, not on a schedule.

Status legend: **Implemented** (Phase 0 only, see below) · **Next** (Phase 1)
· **Planned** (Phase 2 onward, approved ordering, not yet started) ·
**Post-MVP / experimental** (called out explicitly where it applies within a
phase).

---

## Phase 0 — Current milestone (Implemented)

**Goal:** Replace the early sprite-based prototype's identity/presentation
model with the current Match ID architecture, and clear out everything the
replacement made obsolete.

**Major tasks (all complete):**

- Match ID character architecture: `MatchTypeId` (gameplay matching) and
  Match ID (presentation) as two separate identities, validated consistent
  per level by `LevelDefinitionValidator`.
- 20 static `Character_XX` assets (`.prefab`/`.mat`/`.png`), built once by
  `CharacterAssetBuilder` from the vendor Cube Animals pack.
- Unified pixel/character colour mapping: both resolve from the same
  `Assets/Art/ColorPalette.md` Match ID table.
- Tilted orthographic camera and current queue presentation (genuine
  camera-space depth per row, shared Z=0 gameplay plane).
- Legacy Mofu-era sprite pipeline, editor tools, and documentation removed.

**Dependencies:** none — this is the current foundation everything else
builds on.

**Completion criterion:** met. See `docs/ARCHITECTURE_NOTES.md` for the
implemented architecture this phase produced.

---

## Phase 1 — Feeding Flow (Next)

**Goal:** Replace instant, invisible pixel consumption with a readable
feeding sequence, without making any species' presentation depend on a real
mouth.

**Major tasks:**

- Restore Crab's natural claw pose on the Conveyor while keeping its
  existing compact queue pose.
- Add a universal `FeedTarget` near each character's face (see
  `docs/GAMEPLAY_CONTRACTS.md` for its exact semantics).
- Consumed pixels detach from the grid instead of vanishing in place.
- Detached pixels fly to their collector's `FeedTarget` in a short,
  staggered sequence (small interval between pixels, not simultaneous).
- Character highlight on arrival.
- Short universal squash/bounce reaction on arrival.
- Pixel dissolves on arrival.
- Visible `RemainingHunger` decreases only on arrival, not on detachment.
- Hunger capacity is reserved the instant a pixel detaches from the grid,
  preventing more pixels from being launched at a character than it can
  consume while pixels are still in flight.
- Satisfied resolution occurs only after every reserved in-flight pixel has
  arrived — not as soon as reservation alone reaches zero.
- Optional experiment: a simple small mouth for Turtle, only if visual
  testing shows the universal reaction reads poorly on that species.
- Mouth animation is never required as part of the universal feeding
  contract — see `docs/DECISION_LOG.md` decision 027 and
  `docs/GAMEPLAY_CONTRACTS.md`.

**Dependencies:** Phase 0 (Match ID architecture; `CollectorPresentation`/
`CollectorAnimation` as the existing reaction-sequencing seam).

**Completion criterion:** every approved level's feeding sequence is
readable at real gameplay speed, hunger reservation prevents overfeeding
under rapid selection, and Crab/Turtle (no mouth bone) look and behave
identically in contract terms to Fish/Octopus (mouth bone present but not
required).

---

## Phase 2 — Pixel Grid Visuals and Large Grid Technical Spike (Planned)

**Goal:** Validate that the current architecture (GameObject-per-pixel,
alignment-based consumption) scales to production grid sizes and production
pixel density before committing to it for real levels.

**Major tasks:**

- Production pixel appearance (replace the current flat generated 1×1
  sprite).
- Denser pixel grids with more breathing room around them.
- Representative image-based test levels (not the current 6×6/4×8
  hand-authored prototypes).
- Test approximately 30×40 and larger grids.
- Mobile profiling under those grid sizes.
- Readability testing at production density.
- Determine the practical GameObject-per-pixel limit.
- Redesign batching/rendering only if profiling actually proves the current
  approach insufficient — not speculatively.

**Dependencies:** Phase 1 (feeding readability should be validated before
scaling grid size and pixel density together).

**Completion criterion:** a documented maximum practical grid size and pixel
density on the minimum supported device tier, and an explicit decision on
whether GameObject-per-pixel remains acceptable at that size.

---

## Phase 3 — Full Level Visual Design (Planned)

**Goal:** Replace every remaining placeholder visual with production art and
a coherent theme, and finalize composition polish deferred from Phase 0.

**Major tasks:**

- Background.
- Production conveyor visuals.
- Production WaitingLine visuals.
- Production RecoveryRow visuals.
- HUD.
- Victory/Failure windows.
- Effects.
- Sounds.
- One coherent visual theme across all of the above.
- Final camera/queue spacing polish (deferred from Phase 0 — see
  `docs/DECISION_LOG.md` decision 026).

**Dependencies:** Phase 1 (feeding presentation should be final before
building effects/sounds around it); Phase 2 informs pixel-grid visual
density decisions made here.

**Completion criterion:** a full level plays start to finish with no
placeholder visual, sound, or UI element remaining.

---

## Phase 4 — Responsive Layout (Planned)

**Goal:** Make the fixed composition in `GameplayLayout` adapt correctly
across real device sizes instead of one fixed portrait aspect.

**Major tasks:**

- Safe-area support.
- iPhone SE through large phones.
- Android portrait.
- iPad portrait.
- Dynamic grid and UI scaling.
- Adaptive WaitingLine, RecoveryRow, queue, and field spacing.
- Decide later whether landscape is supported at all.

**Dependencies:** Phase 3 (production UI/HUD elements must exist before
their responsive behaviour can be designed).

**Completion criterion:** the same level reads correctly, with no clipped or
overlapping element, across the full supported device/aspect-ratio matrix.

---

## Phase 5 — Level System and Content (Planned)

**Goal:** Move from two hand-authored test levels (`LevelCatalog`) to a real,
authorable level pipeline with enough approved content to ship an initial
chapter.

**Major tasks:**

- Real levels (replacing `level_001`/`level_002` as the only approved
  content).
- Onboarding levels.
- A level-authoring workflow/tool.
- Level validation (building on `LevelDefinitionValidator`'s existing
  pixel/Match ID checks).
- Static solvability checks.
- Difficulty and pacing balance.
- Chapter/theme content pools (see `docs/DECISION_LOG.md` decision 014).

**Dependencies:** Phase 3 (production visuals should exist before producing
production content); Phase 2 (grid-size limits bound what a level can ask
for).

**Completion criterion:** enough validated, balanced levels exist to fill at
least one full chapter, produced through the authoring workflow rather than
by hand-editing `LevelCatalog`.

---

## Phase 6 — Save Progress and Level Map (Planned)

**Goal:** Let a player leave and resume, and navigate between levels, rather
than always starting from `LevelBootstrapper`'s configured starting level.

**Major tasks:**

- Current unlocked level.
- Completed progress.
- Settings persistence.
- Path-style level map.
- Only the current progress level is playable; future levels stay locked.
- Themed chapter segments on the map.

**Dependencies:** Phase 5 (a level map needs real level content to
navigate).

**Completion criterion:** a player can close and reopen the app and resume
exactly where they left off, and cannot play a level beyond their current
unlocked progress.

---

## Phase 7 — Boosters (Planned)

**Goal:** Add optional player-facing power-ups, designed against a
difficulty curve that is actually understood.

**Major tasks:**

- Design only after base difficulty (Phase 5 balancing) is understood.
- Define each booster's effect, usage limits, economy cost, and impact on
  level solvability.

**Dependencies:** Phase 5 (difficulty/balance must exist first — designing
boosters against an unbalanced base game risks solving the wrong problem).

**Completion criterion:** an approved booster list exists with defined
effects, limits, economy, and confirmed solvability impact for each.

---

## Phase 8 — Developer Infrastructure (Planned)

**Goal:** Give the growing feature set automated regression coverage and a
repeatable performance-profiling process.

**Major tasks:**

- `Test_EndgameCleanup`
- `Test_Recovery`
- `Test_Hunger`
- `Test_PixelFlight`
- `Test_MatchIdConsistency`
- `Test_LevelValidation`
- `Test_SaveProgress`
- `Test_ResponsiveLayout`
- `Test_Performance`
- A benchmark scene.
- `README_TEST_LEVELS`.
- A real-device profiling checklist.

**Dependencies:** each listed test depends on its corresponding feature
existing (e.g. `Test_PixelFlight` depends on Phase 1,
`Test_ResponsiveLayout` on Phase 4, `Test_SaveProgress` on Phase 6) — this
phase is populated incrementally as those features land, not held until the
very end.

**Completion criterion:** every listed test exists and passes in CI (or the
project's equivalent), and the benchmark scene + profiling checklist are
runnable by anyone on the team.

---

## Phase 9 — Advanced Level Mechanics (Planned)

**Goal:** Extend the pixel/collector matching model beyond single-hit
pixels, once the base game is stable.

**Major tasks:**

- Multi-cell pixels.
- Food amount greater than 1 per pixel.
- Repeated-hit / multi-layer pixels.
- Pixel restoration.
- Collectors or monsters that add pixels back to the grid.
- Limited restoration counts.
- Mutable-grid validation.
- Dynamic solvability checks (static checks from Phase 5 no longer suffice
  once the grid can change during play).

**Dependencies:** Phase 5 (needs the base level-validation pipeline to
extend); Phase 8 (mutable-grid behaviour needs regression coverage before
shipping).

**Completion criterion:** at least one shipped level exercises each new
mechanic, with dynamic solvability validated automatically, not just
manually playtested.

---

## Phase 10 — Closed Beta and Full MVP Polish (Planned)

**Goal:** Take the feature-complete game through real external feedback and
final release preparation.

**Major tasks:**

- Tutorial/onboarding validation.
- Analytics.
- Crash reporting.
- Accessibility.
- Haptics.
- Balancing (informed by real player data, not just internal playtesting).
- Visual/audio polish.
- Performance and battery checks.
- Store preparation.
- Closed beta and feedback-driven changes.

**Dependencies:** every prior phase — this is the final pass before release
readiness, not a parallel track.

**Completion criterion:** closed beta feedback has been incorporated, and
the build meets the project's store-readiness checklist with no known
blocking issue.
