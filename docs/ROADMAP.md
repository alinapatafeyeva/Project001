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

## Documentation Update (Current)

* Update README_GAME
* Update Architecture
* Update Decision Log
* Update TODO
* Remove obsolete information
* Document new gameplay rules

⸻

## Gameplay UX

Victory UI

* Victory popup
* Stars / completion screen (simple placeholder)
* Continue button

Failure UI

* Failure popup
* Retry button

⸻

## Level Flow

* Restart level
* Next level
* Load next LevelId
* Remove manual level switching from testing flow

⸻

## Visual Vertical Slice

Первый этап “игра начинает выглядеть как игра”.

Monsters

* Proper monster design
* Idle animation
* Eating animation
* Satisfied animation

Conveyor

* Better visuals
* Small movement polish

Pixel effects

* Eat particles
* Small animations
* Juice

Audio

* Basic SFX
* Eating sounds
* Victory / Failure sounds

Levels

* Create several beautiful real levels
* Start using real art instead of coloured rectangles

⸻

## Production Content

* Create first production levels
* Gameplay balancing
* Hunger balancing
* Conveyor speed balancing
* Booster balancing

⸻

## Large Level Technical Spike

Проверяем, выдержит ли архитектура большие уровни.

* Test large grids (~30×40 and larger)
* Mobile performance profiling
* Determine practical grid size limit
* Evaluate readability
* Decide if GameObject-per-pixel is still acceptable
* Switch to batched rendering if required

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

## Level Map

* Path between levels
* One available level at a time
* Completed levels
* Locked levels
* Current level marker

⸻

## Progress

* Save progress
* Unlock next level
* Resume game
* Current LevelId

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
