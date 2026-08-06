# Architecture Decision Log

This document records important technical and gameplay decisions and the reasons
behind them.

---

## 001 — Gameplay systems have narrow responsibilities

Decision:

Keep movement, consumption, selection, and lifecycle resolution in separate
components.

Reason:

The game will later include bonuses, different collector types, animations,
level-specific rules, and variable capacities. Narrow responsibilities reduce
coupling and make these systems easier to extend and test.

Examples:

- `ConveyorSystem` moves riders but does not decide their fate.
- `PixelGrid` decides which pixel is edible but does not update hunger.
- `CollectorLifecycle` decides whether a collector disappears or waits.

---

## 002 — Conveyor lap completion is relative to the boarding point

Decision:

A completed lap means returning to `boardingProgress` after travelling one full
path length.

Reason:

The path's raw normalized progress begins near the upper-right corner, while
collectors board in the lower-left area. Using the raw `0 → 1` seam caused a lap
to complete at the wrong visual location.

---

## 003 — Satisfied collectors disappear immediately

Decision:

A collector disappears immediately after its remaining hunger reaches zero.

Reason:

A satisfied collector has completed its purpose and should not continue occupying
conveyor capacity or consume additional pixels.

Unsatisfied collectors still finish their lap before entering the Waiting Line.

---

## 004 — Unsatisfied collectors enter the Waiting Line only after a full lap

Decision:

An unsatisfied collector continues moving until it returns to the boarding point.

Reason:

Moving it earlier would require teleportation and would conflict with the
physical conveyor concept.

---

## 005 — Waiting Line transitions are atomic

Decision:

Reserve a Waiting Line slot before removing a collector from the conveyor. Roll
back the reservation if conveyor removal fails.

Reason:

A collector must never end up in neither system because one half of the transfer
failed.

---

## 006 — Collector launch order must be preserved

Decision:

All collectors enter through one fixed boarding point and travel at the same
speed in insertion order.

Reason:

The order selected by the player is a central strategic mechanic. A later
collector must never appear ahead of an earlier collector.

---

## 007 — Pixel consumption is aligned with the rider

Decision:

A rider consumes only from the row or column currently aligned with its position
and from the side of the grid it is facing.

Reason:

Whole-side nearest-pixel searches allowed collectors to consume pixels before
reaching them or after passing them.

The aligned inward-path model prevents early and backwards consumption.

---

## 008 — Pixel accessibility is calculated per row or column

Decision:

A candidate pixel is accessible when every cell between it and the facing grid
boundary in the same row or column has already been consumed.

Reason:

Pixels in neighbouring rows or columns should not block one another. This also
allows newly exposed inner pixels to be consumed naturally.

---

## 009 — Pixel local positions are cached

Decision:

`PixelCell` stores its local position when generated.

Reason:

Consumption checks need local pixel coordinates frequently. Caching avoids
repeated transform conversions and keeps positional data owned by the cell.

---

## 010 — Use small commits inside feature branches

Decision:

Commit every tested and working development step when it provides a useful
rollback point.

Pull requests are squash-merged into `develop`.

Reason:

Small commits make experiments and regressions easier to inspect or undo, while
squash merging keeps the shared branch history clean.

---

## 011 — Fix local architectural issues while working in their area

Decision:

Small architectural problems should be fixed immediately when they are within
the files and responsibility of the current task.

Reason:

Deferring every small problem creates a growing technical-debt list and makes
later fixes more expensive.

This does not justify unrelated refactors or speculative architecture.

---

## 012 — Optimise for mobile without premature complexity

Decision:

Avoid obviously expensive patterns early, but use profiling before introducing
large optimisation systems.

Reason:

The game should support reasonably low-end mobile devices, but speculative
optimisation can slow development and complicate the codebase.

Current examples:

- shared runtime sprites and textures;
- throttled consumption attempts;
- cached pixel positions;
- minimal physics usage;
- no allocations in frequent movement loops. 

---

## 013 — Separate gameplay data from presentation and debug behaviour

Decision:

Gameplay configuration belongs inside `LevelDefinition`.

Presentation configuration and debug-only behaviour must stay outside
`LevelDefinition`.

Reason:

The same approved level should be playable with different visual themes,
presentation systems, and debug configurations without modifying gameplay
data.

`ColorPalette`/`CharacterDatabase` (presentation) and `FailureTestLevelFactory`
(debug-only) follow this principle today. The runtime color/material-swapping
system this once cited (`MatchTypePresentation`) was removed entirely — see
decision 018.

---

## 014 — Themes are chapter-based, not player-selectable

Decision:

Visual themes belong to fixed level chapters.

A chapter currently targets approximately 30 levels and defines:

- the level-map environment;
- the visual content pool used by levels;
- collector presentation;
- level decorations and related presentation.

Players do not manually select themes.

Event-specific themes are not part of the core progression model.

Reason:

Chapter-based themes create a stronger sense of travel and progression than
reusing one visual set indefinitely or allowing arbitrary theme selection.

Keeping the theme fixed for a level range also makes level generation,
balancing, testing, and art production more predictable.

The current target of approximately 30 levels per chapter is provisional and
may change during testing before production content is locked.

## Asset folder structure

Decision:
All visual assets are organized by Theme.

Each character and collectible has a stable numeric ID instead of a semantic name.

Examples:
Character_01 ↔ Food_01
Character_02 ↔ Food_02

Reasons:
- Themes can replace all assets without changing code.
- Asset names are independent of visual appearance.
- Easier to create seasonal themes.
- Simplifies procedural loading.

---

## 015 — Gameplay and presentation transforms are separated

Decision:

Gameplay owns and moves the collector root transform. Presentation owns a
`Visual` child transform, animated only by `CollectorAnimation`. `HungerText`
is a sibling of `Visual`, not a child of it.

Reason:

Keeping gameplay movement and presentation animation on different transforms
means animating a collector can never desynchronise it from its gameplay
position, and presentation code never needs to reason about queue, conveyor,
or Waiting Line placement.

---

## 016 — A visual completion sequence plays after gameplay completion

Decision:

Gameplay resolves a satisfied collector immediately: its collider is disabled
and the `CollectorSatisfied` event fires synchronously. Destruction of the
GameObject is deferred until `CollectorPresentation` finishes its completion
sequence (eating punch → satisfied punch → heart pulse → collapse). If
presentation is unavailable, destruction happens immediately instead.

Reason:

Gameplay state (capacity, hunger, victory conditions) must resolve immediately
and must not wait on animation. Delaying only the destroy call lets the
satisfied collector play a readable completion sequence without affecting
gameplay timing.

---

## 017 — Queue spacing is based on visible character width

Decision:

`CollectorQueueBoard` spaces collectors using `GameplayLayout.CollectorVisibleWidth`/
`Height` — the character's actual visible extent — not its raw transform
scale square.

Reason:

The `CollectorSpriteScale` square includes empty margin around the character
model. Spacing by that raw scale would count the margin as gap, producing
uneven-looking queues. (This decision predates the move to 3D characters and
originally described sprite margin; the same reasoning now applies to the
model's own measured bounds — see `docs/ARCHITECTURE_NOTES.md`.)

---

## 018 — Match ID is the single presentation identity

Decision:

`MatchTypeId` (level-scoped, gameplay-matching only) and Match ID (permanent,
1–20, presentation-only) are two separate identities. Match ID is the only
thing pixel colour (`ColorPalette`) and collector visuals
(`CharacterDatabase`) ever resolve from. `LevelDefinitionValidator` enforces
that every pixel/collector sharing one `MatchTypeId`, within one level,
agrees on one Match ID.

Reason:

Earlier prototypes let a single identity carry both matching and colour
meaning, which made it impossible for one abstract match type to mean
different colours/characters in different levels, and made presentation code
a hidden dependency of gameplay matching. Splitting them removed that
coupling entirely — see `docs/ARCHITECTURE_NOTES.md`'s "Presentation
Identity" section for the full mechanism.

---

## 019 — A Character_XX prefab is a complete, static visual asset

Decision:

Each `Character_XX` folder (`Character_XX.prefab`/`.mat`/`.png`) is built
once, at editor time, by `CharacterAssetBuilder`, and used exactly as
authored at runtime — no runtime instantiation of materials, no per-instance
tinting, no swap based on level or theme beyond selecting which `Character_XX`
to use.

Reason:

A fully baked asset is simpler to verify (see `CharacterVerification`),
cannot drift from its approved appearance at runtime, and avoids the
per-frame or per-instance cost of runtime material work on mobile.

---

## 020 — Runtime material/colour recoloring was removed

Decision:

The earlier HSV-recoloring pipeline (a single shared model recolored at
runtime per Match ID) was removed entirely and replaced by 20 pre-baked
`Character_XX` prefabs, each already carrying its own material.

Reason:

Runtime recoloring made verification harder (a shared model's actual
rendered colour could only be checked by actually rendering it), risked
runtime-instanced materials on mobile, and coupled presentation logic into
what should be a simple prefab lookup. Baking the result once, at editor
time, removed all of that at the cost of 20 fixed assets instead of 1
parametric one — an acceptable trade for a fixed 20-Match-ID roster.

---

## 021 — Pixel colour and character colour resolve from the same Match ID mapping

Decision:

`PixelGrid` and `CharacterDatabase` both resolve their visible colour from
the exact same `Assets/Art/ColorPalette.md` Match ID table — `PixelGrid` via
`ColorPalette.TryGetByMatchId`, `CharacterDatabase` via the baked material on
its resolved `Character_XX` prefab, which was itself built from that same
table.

Reason:

A pixel and the collector meant to eat it must always visually agree on
colour. Resolving both from one canonical table, rather than two
independently-maintained mappings, makes that agreement structural instead
of something that has to be kept in sync by hand.

---

## 022 — Gameplay roots remain on the shared Z=0 plane

Decision:

A collector's root transform never leaves world Z=0, in any state (queued,
selected, boarding, riding, waiting, Recovery Row). All presentation depth
(queue-row separation, terminal-sequence foregrounding) happens on the
`Visual` child transform only, pulled along `-GameplayLayout.CameraForward`.

Reason:

`CollectorSelectionController` reverse-projects a screen tap onto one shared
world plane to find which collector was hit. Letting collector roots move in
Z (tried once, for queue-row depth) made that reverse-projection ambiguous
under the camera's tilt and broke tap selection. Keeping presentation depth
entirely on a child transform keeps the root plane simple and correct.

---

## 023 — Queue presentation uses genuine camera-space depth, not sortingOrder

Decision:

`CollectorPresentation.SetQueueRowDepth`/`EnterTerminalForeground` pull
`Visual` toward the camera along `-GameplayLayout.CameraForward` by a real
world-space amount per row/state, relying on the actual depth test — never a
`SpriteRenderer.sortingOrder` approximation.

Reason:

A `sortingOrder` guess does not hold up once collectors are real 3D meshes
with their own depth extent and a tilted camera; a genuine camera-forward
pull produces correct occlusion regardless of facing or breathing wobble,
and composes cleanly with the shared Z=0 gameplay plane (decision 022).

---

## 024 — Characters share one perceived body height; wider species are handled by layout

Decision:

Every species is scaled, at build time, to the same target visible height
(`GameplayLayout.CollectorVisibleHeightRatio`) regardless of its own raw
width. Layout values that depend on a character's footprint
(`CollectorVisibleWidthRatio`, queue/conveyor spacing) are sized to whichever
species that height-only scaling produces widest, measured after the fact —
never by shrinking an individual species to fit a pre-chosen envelope.

Reason:

An earlier "contain-fit" design (capping scale whenever width would exceed a
fixed envelope) produced up to a ~69% perceived-height spread between
species — a visually inconsistent "size category" difference that a
queue-overlap fix should never have caused. Inverting the dependency (layout
adapts to the roster, not the other way around) fixes the actual visual
inconsistency instead of only the overlap symptom.

---

## 025 — UI benchmark screenshots are internal composition references, not assets to copy

Decision:

The five images under the local, git-ignored `reference/` folder (see
`docs/UI_REFERENCE.md`) are used only to evaluate density, spacing,
hierarchy, and responsive behaviour against comparable published games. They
are never a source of characters, UI artwork, branding, exact layouts,
effects, or other protected expression to copy.

Reason:

Comparing composition and information density against strong examples is
useful design research; copying another game's specific visual expression is
not something this project does, and keeping the images out of the
repository (git-ignored, never referenced by path from shipped assets) keeps
that boundary unambiguous.

---

## 026 — Final camera/queue spacing polish is deferred until production content exists

Decision:

Fine camera framing, queue spacing, and composition polish are intentionally
not finalized yet. That pass is deferred until production background,
conveyor, WaitingLine, UI, and effects all exist (see Phase 3 in
`docs/ROADMAP.md`).

Reason:

Composition constants tuned against placeholder/prototype visuals (flat
lighting, no background, no production UI chrome) would likely need to
change again once real content is in place. Tuning once, against real
content, avoids redoing this work twice.

---

## 027 — Approved Feeding Flow decisions (Phase 1)

Decision:

The following are approved for the upcoming Feeding Flow work (see Phase 1
in `docs/ROADMAP.md` and `docs/GAMEPLAY_CONTRACTS.md` for the full contract):

- `FeedTarget` is positioned near a character's face, but the core feeding
  system must not depend on a real mouth existing.
- A pixel's hunger amount is reserved the instant it detaches from the grid
  (not when it arrives), preventing more pixels from being launched at a
  character than it can actually consume.
- Visible `RemainingHunger` changes only when a pixel actually reaches the
  character — never at the moment of reservation/detachment.
- A collector's satisfied resolution waits until every reserved in-flight
  pixel has resolved, not just until reservation reaches zero.
- The character reaction to a fed pixel starts universal: highlight +
  squash/bounce + dissolve-on-arrival. It does not require species-specific
  animation.
- Fish/Octopus mouth-bone animation (the vendor `RigMouth` scale-pulse — see
  `docs/ARCHITECTURE_NOTES.md`'s "Current Known Limitation") may be added
  later as optional, species-specific polish.
- Crab and Turtle, which have no mouth bone at all, must remain fully
  supported by the same universal contract without any mouth animation.
- A simple Turtle mouth is only a possible future visual experiment, never
  an approved requirement.

Reason:

Designing the feeding contract around "pixels fly to a point near the face"
rather than "pixels fly into an animated mouth" lets every current species
(including the two with no mouth bone at all) use the exact same system on
day one, and keeps future mouth animation an additive visual upgrade rather
than a structural dependency other species would have to fake.