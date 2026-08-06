# UI Reference

This document explains how the local UI benchmark screenshots are used
during layout and composition work. It does not document any implemented
system — see `docs/ARCHITECTURE_NOTES.md` for that.

## Where these images live

The benchmark images are stored locally, under the **git-ignored**
`reference/` folder at the project root (see the `.gitignore` entry
`/reference/`). They are **not part of the repository**, are never committed,
and must never be moved, copied, or referenced from anywhere under `Assets/`.

> **Note on the folder name:** these images currently live directly under
> `reference/` (singular, no `ui-benchmark/` subfolder) —
> `reference/phone_01.png`, `reference/phone_02.png`, `reference/phone_03.png`,
> `reference/tablet_01.jpg`, `reference/tablet_02.png`. If you're looking for
> a `references/ui-benchmark/` path, that folder does not exist in this
> project; this document and any tooling should point at the real path
> above.

Because the folder is git-ignored, anyone setting up the repository fresh
will not have these images locally — that is expected. They are a personal/
team visual-benchmarking aid, not a build dependency.

## What each image is

| File | Device type |
|---|---|
| `phone_01.png` | Phone |
| `phone_02.png` | Phone |
| `phone_03.png` | Phone |
| `tablet_01.jpg` | Tablet |
| `tablet_02.png` | Tablet |

The first three are phone-composition references; the last two are
tablet-composition references.

## How to use them

Use these screenshots only to evaluate, by eye, how a strong comparable game
handles:

- **Density vs. readability** — how much pixel-art detail a grid can carry
  while still reading clearly at a glance.
- **Breathing room** — how much empty space surrounds the grid, queue, and
  UI compared to the current prototype (which is intentionally tighter than
  production should be — see `docs/ROADMAP.md` Phase 3).
- **Separation between grid, WaitingLine, and queue** — how clearly each
  region reads as a distinct zone rather than blending together.
- **Consistent visual hierarchy** — what draws the eye first, second, third,
  and whether that order matches what actually matters to the player.
- **Responsive redistribution of space**, not uniform zooming — on a
  tablet, extra space should go to specific regions (grid, queue, HUD)
  deliberately, not just scale every element up equally. This is directly
  relevant to `docs/ROADMAP.md` Phase 4.
- **Readable controls and counters** — hunger/progress indicators and tap
  targets that stay legible at real device size.
- **Tablet composition using available width/height intentionally** — the
  two tablet references should inform how this project's own tablet layout
  (Phase 4) allocates extra space, not just how it scales the phone layout.

## What these images are not

These are **third-party visual benchmarks** from other, unrelated published
games. They are reference material for composition analysis only.

**Do not**, under any circumstance:

- copy their characters, mascots, or UI artwork;
- copy their branding, logos, or wordmarks;
- copy their exact layouts, spacing values, or grid dimensions verbatim;
- copy their particle/VFX designs or other protected visual expression;
- reference, embed, or ship these images (or anything derived pixel-for-
  pixel from them) inside `Assets/` or any build output.

Use them exclusively to inform *this* project's own, independently designed
composition decisions — density, spacing, hierarchy, and responsive
behaviour, as described above — never as a source to trace, crop, or reuse
from directly.

## Related decisions

See `docs/DECISION_LOG.md` decision 025 for the approved decision this
document implements, and `docs/ROADMAP.md` Phases 3–4 for where these
lessons are expected to actually land in the game.
