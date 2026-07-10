# README_GAME.md

# 🍓 Project (Working Title)

> A cozy puzzle game about feeding hungry little monsters with pixel treats.

---

# Vision

The player restores beautiful pixel-art images by feeding hungry monsters.

Each monster loves only one type of food.
The player decides which monster to send next, trying to clear the picture as efficiently as possible.

The game focuses on planning rather than reaction speed.

---

# Core Gameplay Loop

```
Pixel image appears
        ↓
Hungry monsters wait in their queues
        ↓
Player selects one monster
        ↓
If the conveyor has a free slot,
the monster enters the conveyor
Otherwise it stays in the queue
        ↓
Remaining monsters move forward
        ↓
The monster reaches the Pixel Grid
        ↓
The monster eats connected food of its own type
        ↓
If it becomes full,
it happily leaves
Otherwise it moves to the Waiting Line
        ↓
Player continues until every pixel has been eaten
```

---

# Core Systems

## Pixel Grid

Represents the current image.

- Variable width
- Variable height
- Contains edible pixel data
- Every level may have different dimensions

---

## Conveyor

A circular conveyor transporting monsters.

Properties:

- configurable capacity
- monsters move in order
- monsters enter only if a slot is available

---

## Monster Queues

Default:

- 4 queues

Future bonuses may increase this number.

Queues can be:

- shuffled
- expanded
- modified by bonuses

---

## Waiting Line

Temporary storage for monsters that couldn't finish eating.

Default:

- 5 waiting slots

Monsters can be selected from here exactly like from the main queues.

---

# Monster

Every monster has:

- favourite food
- hunger value
- current state

Example:

```
Favourite food:
🍓 Strawberry

Hunger:
18
```

Meaning:

The monster still wants to eat 18 strawberry pixels.

---

# Monster States

```
WaitingInQueue

↓

MovingOnConveyor

↓

Eating

↓

Satisfied
```

or

```
WaitingInQueue

↓

MovingOnConveyor

↓

Eating

↓

WaitingLine
```

---

# Victory

The player wins when every edible pixel has been consumed.

---

# Failure

(To be defined.)

Current prototype has no lose condition.

---

# Planned Bonuses

Examples:

- +1 monster queue
- +1 conveyor capacity
- Shuffle queues
- Random monster activation
- Super Hungry Monster
- Extra Waiting Line slots

---

# Out of Scope (MVP)

Not part of the first playable version:

- animations
- sounds
- VFX
- progression
- shop
- cosmetics
- achievements
- daily rewards
- monetisation

---

# Design Principles

## Monsters are living creatures

The player is feeding monsters.

Not firing ammunition.

Not spending bullets.

---

## Food instead of colours

Internally, colours represent food types.

Examples:

🍓 Strawberry

🍋 Lemon

🍫 Chocolate

🫐 Blueberry

🍇 Grape

The player naturally associates colours with food rather than abstract colour matching.

---

## Strategy over reflexes

The game rewards planning.

Choosing the next monster is the main decision.

---

## Simple systems

Every gameplay system has one responsibility.

Small systems compose into more complex behaviour.

---

# Identity

This project is inspired by conveyor-based puzzle games.

However, its identity is intentionally different.

Instead of shooting blocks:

- monsters eat food

Instead of ammunition:

- monsters have hunger

Instead of disposable units:

- monsters are charming living creatures

The emotional goal is helping hungry monsters restore pixel-art worlds rather than destroying objects.

Future mechanics should reinforce this identity rather than move away from it.