Project001 Roadmap

✅ Foundation (Completed)

* ✅ Core gameplay prototype
* ✅ Conveyor system
* ✅ Pixel consumption
* ✅ Waiting Line
* ✅ Victory / Failure conditions
* ✅ Level Catalog
* ✅ Adaptive Grid Layout
* ✅ Per-Collector Hunger
* ✅ Failure Test Mode

⸻

✅ Documentation Update (Current)

✅ Update README_GAME
✅ Update Architecture
✅ Update Decision Log
✅ Update TODO
✅ Remove obsolete information
✅ Document new gameplay rules

⸻

✅  Gameplay UX

Victory UI

✅  Victory popup
✅  Stars / completion screen (simple placeholder)
✅  Continue button

Failure UI

✅  Failure popup
✅  Retry button

⸻

✅  Level Flow

✅  Restart level
✅  Next level
✅  Load next LevelId
✅  Remove manual level switching from testing flow

⸻

✅ Continue Recovery Mechanic

✅ Add a Recovery Row above the Waiting Line.
✅ On Continue after Failure, move every collector currently on the Conveyor into the Recovery Row.
✅ Preserve MatchTypeId and RemainingHunger.
✅ Allow collectors to be launched back onto the Conveyor manually.
✅ Collectors must never return to the Recovery Row after later Conveyor loops.
✅ Populate the Recovery Row only when Continue is confirmed.
✅ Size the Recovery Row from the actual number of collectors on the Conveyor.
✅ Support future Conveyor capacity upgrades.
✅ Move the Waiting Line lower so Recovery Row and Waiting Line never overlap.
✅ Keep repeated Failure working after Continue.
✅ Later connect Continue to coins / rewarded ads.

⸻

✅ Endgame Cleanup

✅ Activate when RemainingCollectors <= WaitingLineCapacity.
✅ Failure is no longer possible.
✅ Collectors no longer enter the Waiting Line.
✅ Remaining collectors continuously circulate on the Conveyor.
✅ Player still controls collector selection.
✅ Increase Conveyor speed.
✅ Configure the multiplier through GameplayConstants.
✅ Final multiplier will be determined during playtesting.

⸻

## Visual Vertical Slice

- Real monster presentation
- Hunger presentation
- Idle / Eat / Satisfied animations
- Basic VFX & SFX
- Beautiful handcrafted levels

## Developer Infrastructure

- Developer test levels
- Performance benchmark scene
- Testing documentation


⸻

## Responsive Layout

* Safe Area support.
* Different aspect ratios.
* iPhone SE → iPhone Pro Max.
* iPad portrait.
* Decide later whether iPad landscape is supported.
* Android.
* Dynamic UI sizing.
* Gameplay field scaling.
* Recalculate Conveyor / Recovery Row / Waiting Line positions.
* Adaptive spacing between gameplay layers.

⸻

## Large Level Technical Spike

Проверяем, выдержит ли архитектура большие уровни.

* Test large grids (~30×40 and larger)
* Mobile performance profiling
* Determine practical grid size limit
* Evaluate readability
* Decide if GameObject-per-pixel is still acceptable
* Optimize pixel rendering if required

⸻

## Advanced Level Mechanics

• multi-cell pixels

• pixels with internal food amount greater than 1

• repeated-hit / multi-layer pixels

• pixel restoration during gameplay

• monsters that add pixels back

• limited restore counts

• dynamic-grid validation

• solvability checks for mutable levels

⸻

## Shape-based Levels

Вместо рисования картинок цветами.

Shape masks

* Heart
* Butterfly
* Flower
* Star
* Animals
* etc.

Active / inactive cells

Support empty cells inside the rectangular grid.

MatchType generation

Generate colours inside the active shape.

Pattern generation

Instead of pure randomness:

* clusters
* stripes
* gradients
* symmetry
* controlled noise

Validation

* preview generated levels
* validate collector capacities
* validate distribution

⸻

## Chapter & Theme System

- Define chapter boundaries.
- Start with a provisional target of approximately 30 levels per chapter.
- Assign one visual theme to each chapter.
- Use the chapter theme for:
  - level-map environment;
  - monster presentation;
  - collectible / food asset pool;
  - decorations;
  - effects and audio where appropriate.
- Restrict level generation to the content pool allowed by the chapter.
- Do not expose manual theme selection to the player.
- Do not use event themes as the core progression structure.
- Rebalance chapter length during testing before production level ranges are
  locked.

⸻

## Progress

* Save progress
* Unlock next level
* Resume game
* Current LevelId

⸻

## Level Map

* Path between levels
* One available level at a time
* Completed levels
* Locked levels
* Current level marker
* Support themed map segments per chapter.
* Transition visually between chapter environments.

⸻

## Boosters

Examples:

* Shuffle collectors
* Slow conveyor
* Remove collector
* Auto-feed
* etc.

⸻

## Full Polish

* Final balancing
* Animations
* Visual polish
* Sound polish
* Accessibility
* Optimisation
* App Store / Google Play preparation

⸻
