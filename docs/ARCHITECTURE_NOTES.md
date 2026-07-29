# Architecture

This document describes the current runtime architecture of the project.

It reflects the implemented prototype, not planned future systems.

---

## Architectural Principles

### Single responsibility

Each gameplay component should own one clearly defined responsibility.

Systems should collaborate through small public APIs rather than directly
managing each other's internal state.

### Explicit orchestration

Cross-system gameplay flows are coordinated by dedicated controller or
lifecycle components.

For example:

- `CollectorSelectionController` coordinates selection and conveyor boarding.
- `CollectorLifecycle` coordinates post-consumption collector resolution.

### Configurable rules

Gameplay values that may change between levels, bonuses, or devices should not
be permanently hard-coded into core behaviour.

Current prototype defaults may be temporary, but the architecture should allow
them to become configurable later.

### Mobile-conscious implementation

The game is intended for mobile devices, including reasonably low-end devices.

Avoid unnecessary work in `Update`, repeated runtime allocations, excessive
physics queries, and duplicated derived state.

Optimisation should still be guided by profiling rather than assumptions.

---

# Runtime Gameplay Flow

```text
CollectorQueueBoard
        ↓
CollectorSelectionController
        ↓
ConveyorSystem
        ↓
PixelConsumer
        ↓
PixelGrid
        ↓
CollectorLifecycle
        ↓
Satisfied → removed
Unsatisfied → WaitingLine
```

---

# Gameplay Systems

## PixelCell

Location:

```text
Assets/Scripts/Gameplay/Pixels/PixelCell.cs
```

Responsibility:

Represents one edible pixel in the grid.

Owns:

- grid coordinates;
- cached local position;
- food colour;
- active/consumed state;
- visual deactivation after consumption.

Does not know about:

- monsters;
- the conveyor;
- hunger;
- level completion.

---

## PixelGrid

Location:

```text
Assets/Scripts/Gameplay/Pixels/PixelGrid.cs
```

Responsibility:

Owns the generated pixel-cell structure and decides which pixel can currently
be consumed.

Current behaviour:

- generates the temporary prototype grid;
- stores cells in a two-dimensional structure;
- supports variable dimensions in its consumption logic;
- determines the side of the grid currently facing a rider;
- restricts consumption to the row or column aligned with the rider;
- verifies that the inward path from that side is clear;
- consumes at most one matching pixel per request.

The grid does not know why a consumer is requesting a pixel and does not modify
monster hunger directly.

---

## PixelConsumer

Location:

```text
Assets/Scripts/Gameplay/Pixels/PixelConsumer.cs
```

Responsibility:

Controls when a riding monster attempts to consume a pixel.

Owns:

- references to `PixelGrid` and `ConveyorRider`;
- consumption attempt cooldown;
- alignment tolerance.

Behaviour:

- only attempts consumption while its rider is on the conveyor;
- stops consuming after the rider is satisfied;
- decreases hunger only after `PixelGrid` confirms successful consumption;
- consumes at most one pixel per attempt.

It does not decide when a monster leaves the conveyor.

---

## ConveyorPath

Location:

```text
Assets/Scripts/Gameplay/Conveyor/ConveyorPath.cs
```

Responsibility:

Represents the closed route followed by conveyor riders.

Owns:

- procedural rounded-rectangle path points;
- total path length;
- normalized progress sampling.

The route is independent from rider movement and capacity.

---

## ConveyorPathRenderer

Location:

```text
Assets/Scripts/Gameplay/Conveyor/ConveyorPathRenderer.cs
```

Responsibility:

Displays the temporary visual representation of `ConveyorPath`.

It contains no movement or gameplay decisions.

---

## ConveyorRider

Location:

```text
Assets/Scripts/Gameplay/Conveyor/ConveyorRider.cs
```

Responsibility:

Stores the minimal runtime state required by a monster while interacting with
the conveyor and pixel-consumption systems.

Owns:

- favourite food colour;
- hunger capacity;
- RemainingHungerChanged event;
- satisfied state;
- riding state;
- world-position assignment.

It does not:

- move itself;
- search for pixels;
- decide whether to disappear;
- decide whether to enter the Waiting Line.

---

## ConveyorSystem

Location:

```text
Assets/Scripts/Gameplay/Conveyor/ConveyorSystem.cs
```

Responsibility:

Moves riders around the closed path and manages conveyor capacity.

Owns:

- rider collection;
- per-rider path progress;
- boarding point;
- boarding clearance;
- movement speed;
- capacity;
- completed-lap tracking.

Behaviour:

- riders enter from one fixed boarding point;
- launch order is preserved;
- movement is counter-clockwise;
- lap completion is measured relative to the boarding point;
- removing a rider does not reorder other riders.

The system reports completed laps but does not decide what should happen to a
monster afterwards.

---

## CollectorView

Location:

```text
Assets/Scripts/Gameplay/Collectors/CollectorView.cs
```

Responsibility:

Represents the temporary visual and selectable form of a monster.

Owns:

- SpriteRenderer
- selection collider
- visual colour
- Remaining Hunger display

It has no knowledge of conveyor behaviour or lifecycle rules.

---

## CollectorQueue

Location:

```text
Assets/Scripts/Gameplay/Collectors/CollectorQueue.cs
```

Responsibility:

Stores one ordered logical queue of collectors.

Behaviour:

- exposes the first available collector;
- only allows removal of the first collector;
- preserves the order of remaining collectors.

---

## CollectorQueueBoard

Location:

```text
Assets/Scripts/Gameplay/Collectors/CollectorQueueBoard.cs
```

Responsibility:

Generates and owns the collection of visible monster queues.

Current prototype behaviour:

- generates a configurable number of queues;
- generates a configurable number of collectors per queue;
- uses a deterministic mixed-colour pattern;
- creates and initializes all collector-related components;
- shifts a queue upward after successful removal of its first collector.

It does not board collectors onto the conveyor directly.

---

## CollectorSelectionController

Location:

```text
Assets/Scripts/Gameplay/Collectors/CollectorSelectionController.cs
```

Responsibility:

Coordinates player selection and conveyor boarding.

Behaviour:

- supports mouse click and touchscreen tap through the Input System;
- detects a selected `CollectorView`;
- validates whether it is selectable from a queue or Waiting Line;
- attempts conveyor boarding before modifying the source;
- removes the collector from its source only after successful boarding.

It does not manage movement, hunger, or lifecycle resolution.

---

## CollectorLifecycle

Location:

```text
Assets/Scripts/Gameplay/Collectors/CollectorLifecycle.cs
```

Responsibility:

Decides what happens to a collector after eating or completing a lap.

Behaviour:

- a satisfied riding collector is removed and destroyed immediately;
- an unsatisfied collector continues riding until it completes a full lap;
- after completing a lap, it moves into the first available Waiting Line slot;
- if no Waiting Line slot is available, it remains on the conveyor;
- Waiting Line transfer uses reservation and rollback to avoid partial state.

This is the only system that decides whether a monster disappears or enters
the Waiting Line.

---

## WaitingSlot

Location:

```text
Assets/Scripts/Gameplay/WaitingLine/WaitingSlot.cs
```

Responsibility:

Represents one position in the Waiting Line.

Owns:

- its current collector reference;
- occupied state;
- safe assignment;
- conditional clearing.

It does not move collectors itself.

---

## WaitingLine

Location:

```text
Assets/Scripts/Gameplay/WaitingLine/WaitingLine.cs
```

Responsibility:

Owns the ordered set of Waiting Line slots.

Behaviour:

- generates configurable waiting slots;
- finds the first empty slot;
- identifies collectors already waiting;
- clears a collector's matching slot after successful reboarding.

---

# Editor Bootstrap

Location:

```text
Assets/Scripts/Editor/BootstrapSceneCreator.cs
```

Responsibility:

Creates the current prototype scene and wires runtime component references.

It is Editor-only and must not contain production gameplay decisions.

Current generated scene:

```text
Bootstrap
├── Main Camera
├── PixelGrid
├── Conveyor
├── WaitingLine
├── CollectorQueueBoard
└── CollectorSelectionController
```

---

# Current Prototype Values

These values exist for testing and are not final game-design constants:

```text
Queue count:                level-defined
Collectors per queue:       level-defined
Waiting Line capacity:      fixed (5)
Conveyor capacity:          level-defined
Conveyor speed:             level-defined
Collector hunger:           per collector
Pixel grid size:            level-defined
```

Future level configuration and bonuses must be able to change relevant values
without rewriting core systems.

---

## Gameplay Architecture Layers

The project intentionally separates gameplay into three independent layers.

### 1. Level Data

Defines what makes one level different from another.

Examples:

- LevelId
- PixelLayout
- CollectorQueues
- ConveyorCapacity
- Per-collector HungerCapacity

These values belong inside `LevelDefinition`.

---

### 2. Gameplay Rules

Defines rules that stay identical across every level.

Examples:

- Waiting Line capacity
- Base conveyor speed
- Future gameplay speed multiplier
- Victory conditions
- Failure conditions

These values should not be duplicated inside every `LevelDefinition`.

Changing a gameplay rule should affect every level without modifying level data.

---

### 3. Presentation

Defines how gameplay is displayed.

Examples:

- colours
- sprites
- animations
- sounds
- particle effects
- future themes

Presentation must not affect gameplay behaviour.

Changing presentation should never require changing `LevelDefinition`.

---

## Assets structure

Assets
└── Art
    └── Sprites
        └── Themes
            ├── Classic
            │   ├── Characters
            │   │   ├── Character_01
            │   │   │   ├── Mofu_Front_Idle.png
            │   │   │   ├── Mofu_Front_Eating.png
            │   │   │   ├── Mofu_Front_Satisfied.png
            │   │   │   ├── Mofu_Back_Idle.png
            │   │   │   └── Mofu_Heart.png
            │   │   ├── Character_02
            │   │   │   └── те же имена файлов
            │   │   ├── Character_03
            │   │   └── ...
            │   │
            │   ├── Food
            │   │   ├── Food_01.png
            │   │   ├── Food_02.png
            │   │   ├── Food_03.png
            │   │   └── ...
            │   │
            │   ├── UI
            │   └── Backgrounds
            │
            ├── Candy
            │   ├── Characters
            │   │   ├── Character_01
            │   │   ├── Character_02
            │   │   └── ...
            │   ├── Food
            │   │   ├── Food_01.png
            │   │   ├── Food_02.png
            │   │   └── ...
            │   ├── UI
            │   └── Backgrounds
            │
            └── Halloween
                ├── Characters
                │   ├── Character_01
                │   ├── Character_02
                │   └── ...
                ├── Food
                │   ├── Food_01.png
                │   ├── Food_02.png
                │   └── ...
                ├── UI
                └── Backgrounds