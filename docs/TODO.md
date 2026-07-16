# Technical and Product TODO

This document contains planned improvements that are intentionally not part of
the current implementation.

---

## Responsive Layout

- Make the gameplay layout adaptive for different phone and tablet aspect
  ratios.
- Position gameplay elements relative to PixelGrid and camera bounds instead
  of permanent prototype coordinates.
- Support safe areas, display cutouts, rounded corners, and system bars.
- Define the intended tablet layout strategy.

---

## Selection

- Replace `Physics2D.OverlapPoint()` with a deterministic multi-collider
  selection strategy once selectable colliders may overlap.
- Consider selection layers or `OverlapPointAll()` when VFX, UI, and production
  monster visuals are introduced.

---

## Conveyor

- Add a configurable gameplay-speed multiplier, including x2 speed.
- Add a boarding animation from a queue or Waiting Line to the conveyor entry.
- Add a departure animation into the Waiting Line.
- Add a satisfied-monster exit animation.
- Review consumption sampling at x2 speed so rows and columns cannot be skipped.

---

## Monsters

- Replace prototype circles with production monster visuals.
- Display Remaining Hunger on each monster.
- Add food-themed monster identities:
  - Strawberry Monster
  - Chocolate Monster
  - Blueberry Monster
  - Lemon Monster
  - Grape Monster
- Define how production monsters visually communicate satisfaction and waiting.
- Move prototype hunger capacity into level data.

---

## Pixel Grid and Levels

- Replace the temporary 6×6 generated pattern with level-defined pixel data.
- Support variable grid width and height in level configuration.
- Define the source format for pixel-art images and food-type mapping.
- Add victory detection when every edible pixel has been consumed.
- Validate that total monster hunger matches the pixels required by a level.

---

## Waiting Line and Failure

- Implement the final failure rule when an unsatisfied monster needs to leave
  the conveyor but every Waiting Line slot is occupied.
- Define whether failure happens immediately or after a short visual warning.
- Add production Waiting Line visuals and occupancy feedback.

---

## Bonuses

- Increase conveyor capacity.
- Add an extra monster queue.
- Add extra Waiting Line slots.
- Shuffle a selected queue.
- Activate a random eligible monster.
- Add the Super Hungry Monster:
  - consumes all pixels of its food type;
  - removes queued monsters of the same type.
- Define whether bonuses apply for one level, one action, or a limited duration.

---

## Performance

- Define the minimum supported mobile-device tier before release.
- Add regular profiling checkpoints on a lower-end Android device.
- Profile CPU, memory, garbage collection, rendering, loading, and battery use.
- Avoid runtime allocations inside frequent `Update` loops.
- Avoid unnecessary complete-grid scans as level dimensions grow.
- Introduce object pooling before production animations and VFX create frequent
  object creation/destruction.
- Reuse sprites, textures, materials, and temporary collections.
- Test real-device performance with production art before final optimisation.

---

## Testing

- Add EditMode tests for:
  - exposed pixel detection;
  - side and alignment selection;
  - hunger reduction;
  - boarding-relative lap completion;
  - conveyor capacity;
  - queue first-item removal;
  - Waiting Line transfer rollback.
- Add PlayMode tests for the complete prototype loop.

---

## Debug & QA Tools

- Add configurable game speed.
- Add optional 2x gameplay speed.
- Ensure all gameplay systems respect one shared speed multiplier.

- Add developer cheat/debug panel.
- Allow enabling unlimited boosters for testing.
- Allow granting any booster in unlimited quantity.
- Allow skipping directly to any level.
- Allow instantly completing the current level.
- Allow instantly triggering Failure.
- Allow resetting the current level.
- Toggle Failure Test Mode

---

##  Level Shape System

- support inactive/empty cells inside a rectangular grid
- define levels by shape masks
- generate MatchTypeId distribution inside the active mask
- preserve exact per-MatchTypeId collector capacity
- support controlled cluster/pattern generation
- preview and validate generated levels