# Project001

## Concept

A pixel-art food image is revealed cell by cell. Hungry collectors, each
wanting one specific colour/pattern of pixel, queue up to be launched onto a
conveyor that carries them past the image. A collector eats matching,
reachable pixels as it passes; once every pixel is consumed, the level is
won. A collector that finishes a lap still hungry waits in line for another
turn — or, if every waiting slot is full, the level is lost.

## Current gameplay loop

```text
Player selects a collector (from a queue or the Waiting Line)
        ↓
It boards the conveyor at the fixed boarding point, once the point is clear
        ↓
It rides counter-clockwise, eating matching/aligned/reachable pixels
        ↓
RemainingHunger reaches zero → satisfied → removed immediately
        (an unsatisfied collector keeps riding until it completes a lap)
        ↓
Lap complete, still hungry → first free Waiting Line slot
        (no free slot → Failure, unless the grid is already fully consumed)
        ↓
Level ends in Victory once every pixel is consumed
```

A Failure can be recovered from: Continue moves every collector currently on
the conveyor into a Recovery Row for manual relaunch, instead of restarting
the level.

## Architecture summary

Gameplay matching (`MatchTypeId`) and visual presentation (a permanent,
1–20 Match ID) are two separate identities that only ever meet through a
validated, level-scoped mapping. A collector's Match ID resolves to one of
20 complete, pre-baked `Character_XX` prefabs via `CharacterDatabase`; a
pixel's Match ID resolves to the same colour through
`Assets/Art/ColorPalette.md`. See **`docs/ARCHITECTURE_NOTES.md`** for the
full implemented architecture, and **`docs/GAMEPLAY_CONTRACTS.md`** for the
gameplay behaviour contracts that stay stable across implementation changes.

## Current status

Phase 0 (Match ID character architecture, unified colour mapping, tilted
camera and queue presentation) is complete. Phase 1 (Feeding Flow — pixels
flying to a `FeedTarget` instead of vanishing in place) is next. See
**`docs/ROADMAP.md`** for the complete phased plan and
**`docs/DECISION_LOG.md`** for why things are built the way they are.

Presentation is still a prototype in several places: pixel appearance,
hunger display (a `TextMesh`, not production UI), and VFX/SFX are all
temporary. Vendor character Animators are disabled — all current motion is
transform-level tweening (`CollectorAnimation`), not skeletal animation.

## Regenerating Bootstrap

`Assets/Scenes/Bootstrap.unity` is fully reproducible from code:
**`Tools/Bootstrap/Create Bootstrap Scene`** in the Unity Editor menu rebuilds
it from scratch and rewires every cross-reference. Safe to re-run after any
manual scene edit you want to discard.

If character prefabs ever need rebuilding from the vendor art pack, use
**`Tools/Characters/Build All Character Prefabs`**, then
**`Tools/Characters/Run Verification`** to confirm all 20 Match IDs are
wired correctly.

## Key project folders

```text
Assets/Scripts/Gameplay/    Runtime gameplay systems
Assets/Scripts/Editor/      Bootstrap/character-build/verification tooling
Assets/Art/Themes/Classic/Character/Character_01..20   Baked character assets
Assets/Art/ColorPalette.md  Canonical Match ID -> color -> species table
Assets/Cube Animals 02/     Vendor asset pack (never modified directly)
Assets/Scenes/Bootstrap.unity   The one gameplay scene, regenerable (see above)
docs/                       This documentation set
reference/                  Local, git-ignored UI benchmark screenshots (see docs/UI_REFERENCE.md)
```

## Further reading

- [`docs/ROADMAP.md`](ROADMAP.md) — the authoritative phased plan.
- [`docs/ARCHITECTURE_NOTES.md`](ARCHITECTURE_NOTES.md) — implemented runtime
  architecture.
- [`docs/DECISION_LOG.md`](DECISION_LOG.md) — why things are built this way.
- [`docs/UI_REFERENCE.md`](UI_REFERENCE.md) — how to use the local UI
  benchmark screenshots.
