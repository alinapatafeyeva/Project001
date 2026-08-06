# Gameplay Contracts

This document is the single source of truth for gameplay *behaviour* —
what must stay true regardless of how the implementation underneath it
changes. `docs/ARCHITECTURE_NOTES.md` describes the current implementation;
this document describes the contract that implementation must keep
satisfying, and the contract any future implementation must also satisfy.

Each section is marked **Implemented** (true today, enforced by existing
code) or **Next** (approved for Phase 1, per `docs/ROADMAP.md`, not yet
built). Nothing in this document describes anything later than Phase 1 —
contracts for later phases will be added as those phases are designed.

---

## What "Satisfied" means

**Implemented.**

A collector is satisfied the instant its `RemainingHunger` reaches zero
(`ConveyorRider.IsSatisfied`). Satisfaction is a pure function of
`RemainingHunger`; nothing else (position, animation state, lap count) is
part of this definition.

A satisfied collector's **gameplay** resolution — no longer counting toward
`RemainingCollectors`, no longer selectable, removed from the Conveyor — is
synchronous and immediate the moment satisfaction is detected
(`CollectorLifecycle.ResolveSatisfaction`). It never waits for a lap, and
never waits for animation. Only the GameObject's destruction may be
deferred, for presentation (see "Ownership of gameplay state vs.
presentation state" below).

---

## FeedTarget semantics

**Next.**

`FeedTarget` is a point near a character's face that in-flight pixels fly
toward. The contract:

- Every `Character_XX` exposes exactly one `FeedTarget` position, read
  through one shared API — gameplay code never branches per species.
- `FeedTarget`'s existence and position are a **presentation** detail. No
  gameplay rule (reservation, satisfaction, hunger) may depend on whether a
  species has a mouth, a mouth bone, or any particular geometry at that
  point. Crab and Turtle (no mouth bone) and Fish/Octopus (mouth bone
  present) must be equally well-supported by the same contract.
- `FeedTarget` is a landing point for the universal reaction (highlight +
  squash/bounce + dissolve-on-arrival — see "Feeding sequence order"). It is
  never itself an animated mouth, and never required to be.

---

## Hunger reservation rules

**Next.**

- A pixel's hunger amount is reserved the instant it detaches from the
  grid — not when it arrives at `FeedTarget`.
- Reserved amount counts against a collector's remaining capacity
  immediately, so no more pixels may be launched at a collector than it can
  actually still consume, even while earlier pixels are still in flight.
- A reservation is only released by that specific pixel actually arriving
  (converting to a real hunger decrease) — never by a timeout, and never
  speculatively reclaimed while still in flight.

---

## When `RemainingHunger` changes

**Next** (current **Implemented** behaviour differs — see note below).

The approved contract: visible `RemainingHunger` changes **only** when an
in-flight pixel actually reaches the character's `FeedTarget` — never at the
moment of grid detachment/reservation. Reservation (above) is bookkeeping
that affects what may be launched; it is not itself a visible hunger change.

> **Current implemented behaviour differs from this contract.** Today,
> `PixelConsumer`/`PixelGrid` consumption is immediate and direct —
> `ConveyorRider.RegisterConsumedPixel()` runs, and `RemainingHunger`
> changes, in the same frame a matching pixel is found. There is no flight,
> no reservation, and no separate arrival event yet. This section describes
> the target for Phase 1, not what runs today.

---

## Feeding sequence order

**Next.**

1. A matching, aligned, reachable pixel is found and detaches from the grid.
2. Its hunger amount is reserved against the target collector immediately.
3. It flies to the collector's `FeedTarget`, as one of a short, staggered
   sequence (a small fixed interval between pixels, never fully
   simultaneous).
4. On arrival: the character highlights, plays a short universal
   squash/bounce reaction, and the pixel dissolves.
5. Visible `RemainingHunger` decreases at this same arrival moment (see
   above) — never earlier.
6. Once every reserved in-flight pixel for a collector has arrived **and**
   that resolves `RemainingHunger` to zero, satisfaction is resolved (see
   "What 'Satisfied' means"). Satisfaction must never resolve while a
   reserved pixel for that collector is still in flight.

Species-specific mouth-bone animation (Fish/Octopus `RigMouth`) may
eventually layer onto step 4 as additive polish. It must never become a
precondition for any other step — see `docs/DECISION_LOG.md` decision 027.

---

## Collector lifecycle

**Implemented.**

A collector, once created by `CollectorQueueBoard`, moves through exactly
these states, in this order (Recovery Row is a detour, not a new terminal
state):

```text
Queued → Selected/pending boarding → Riding Conveyor
                                          ├─→ Satisfied → resolved (removed from gameplay)
                                          │      (destruction deferred for presentation only)
                                          └─→ Lap complete, still hungry
                                                 ├─→ Waiting Line slot free → Waiting
                                                 │        (selectable again → Riding Conveyor)
                                                 └─→ No slot free → stays Riding, Failure notified
```

A Failure Continue diverts any currently-Riding collector into the Recovery
Row (selectable again → Riding Conveyor) instead of the above; a Failure
Retry discards the level state and rebuilds it entirely. A collector, once
resolved as Satisfied, never re-enters any other state.

## Queue lifecycle

**Implemented.**

A `CollectorQueueBoard` queue only ever exposes its front collector as
selectable (`CollectorQueue.IsFirstAvailable`). Removing the front collector
(after it successfully boards the Conveyor) shifts every remaining collector
up by exactly one position, preserving order; nothing else may reorder a
queue. A queue never regenerates or reshuffles its own contents after
`Initialize` — only removal-and-shift ever changes it.

---

## Ownership of gameplay state vs. presentation state

**Implemented.**

Gameplay state (hunger, satisfaction, queue/lap/lifecycle position, victory,
failure) is owned by non-presentation components (`ConveyorRider`,
`CollectorLifecycle`, `PixelGrid`, `VictoryController`, `FailureController`)
and resolves synchronously, on its own timing, independent of any animation.
Presentation (`CollectorView`, `CollectorPresentation`, `CollectorAnimation`)
never delays a gameplay resolution and never feeds gameplay decisions back
from animation state.

The one deliberate coupling point: a satisfied collector's gameplay
resolution happens immediately, but its GameObject's `Destroy()` call is
deferred until `CollectorPresentation` finishes its completion sequence (or
immediately, if presentation is unavailable). This is the only place
presentation timing affects anything observable — the collector already
stopped counting toward gameplay state before that sequence even starts.

---

## MatchId vs. MatchTypeId responsibilities

**Implemented.**

- **`MatchTypeId`** — the only identity gameplay matching logic
  (`PixelGrid.TryConsumeNearestExposed`, `PixelConsumer`, `ConveyorRider`)
  is ever allowed to compare. Level-scoped: the same value can mean
  different things in different levels.
- **Match ID** — the only identity presentation resolution
  (`ColorPalette.TryGetByMatchId`, `CharacterDatabase.GetPrefab`) is ever
  allowed to key on. Permanent and global (1–20), defined once in
  `Assets/Art/ColorPalette.md`.
- Every pixel and every collector sharing one `MatchTypeId`, within one
  level, must resolve to exactly one Match ID —
  `LevelDefinitionValidator.ValidateMatchIdConsistency` enforces this at
  level-construction time, before any runtime system sees the level.
- No gameplay code may derive a `MatchTypeId` from a Match ID, or vice
  versa. They are carried side by side on the same data (`PixelLayoutDefinition`,
  `CollectorDefinition`) but are never substitutable for each other.

See `docs/ARCHITECTURE_NOTES.md`'s "Presentation Identity" section for the
full mechanism this contract rests on.
