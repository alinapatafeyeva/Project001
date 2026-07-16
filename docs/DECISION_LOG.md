# Architecture Decision Log

This document records important technical and gameplay decisions and the reasons
behind them.

---

## 001 — Gameplay systems have narrow responsibilities

Decision:

Keep movement, consumption, selection, and lifecycle resolution in separate
components.

Reason:

The game will later include bonuses, different monster types, animations,
level-specific rules, and variable capacities. Narrow responsibilities reduce
coupling and make these systems easier to extend and test.

Examples:

- `ConveyorSystem` moves riders but does not decide their fate.
- `PixelGrid` decides which pixel is edible but does not update hunger.
- `CollectorLifecycle` decides whether a monster disappears or waits.

---

## 002 — Conveyor lap completion is relative to the boarding point

Decision:

A completed lap means returning to `boardingProgress` after travelling one full
path length.

Reason:

The path's raw normalized progress begins near the upper-right corner, while
monsters board in the lower-left area. Using the raw `0 → 1` seam caused a lap
to complete at the wrong visual location.

---

## 003 — Satisfied monsters disappear immediately

Decision:

A monster disappears immediately after its remaining hunger reaches zero.

Reason:

A satisfied monster has completed its purpose and should not continue occupying
conveyor capacity or consume additional pixels.

Unsatisfied monsters still finish their lap before entering the Waiting Line.

---

## 004 — Unsatisfied monsters enter the Waiting Line only after a full lap

Decision:

An unsatisfied monster continues moving until it returns to the boarding point.

Reason:

Moving it earlier would require teleportation and would conflict with the
physical conveyor concept.

---

## 005 — Waiting Line transitions are atomic

Decision:

Reserve a Waiting Line slot before removing a monster from the conveyor. Roll
back the reservation if conveyor removal fails.

Reason:

A monster must never end up in neither system because one half of the transfer
failed.

---

## 006 — Monster launch order must be preserved

Decision:

All monsters enter through one fixed boarding point and travel at the same
speed in insertion order.

Reason:

The order selected by the player is a central strategic mechanic. A later
monster must never appear ahead of an earlier monster.

---

## 007 — Pixel consumption is aligned with the rider

Decision:

A rider consumes only from the row or column currently aligned with its position
and from the side of the grid it is facing.

Reason:

Whole-side nearest-pixel searches allowed monsters to consume pixels before
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

`MatchTypePresentation` and `FailureTestLevelFactory` follow this principle.

---

## 014 — Themes are chapter-based, not player-selectable

Decision:

Visual themes belong to fixed level chapters.

A chapter currently targets approximately 30 levels and defines:

- the level-map environment;
- the visual content pool used by levels;
- monster presentation;
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