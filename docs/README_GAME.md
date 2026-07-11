# Core Gameplay Loop

```text
A pixel-art food image appears
        ↓
Hungry monsters wait in several queues
        ↓
The player selects the first monster from a queue
or a monster already in the Waiting Line
        ↓
If the conveyor has capacity and its boarding area is clear,
the monster enters from the fixed boarding point
Otherwise it stays where it is
        ↓
The monster moves counter-clockwise around the image
        ↓
While aligned with an accessible pixel of its favourite food,
the monster eats it and reduces its remaining hunger
        ↓
If Remaining Hunger reaches zero,
the satisfied monster leaves immediately
        ↓
If the monster completes a full lap while still hungry,
it enters the first free Waiting Line slot
        ↓
The player may launch it again later
        ↓
The level continues until every edible pixel has been consumed