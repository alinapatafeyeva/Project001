# Color Palette

## Goals

- Every color should be distinguishable in under one second.
- Avoid similar shades.
- Colors should remain recognizable for colorblind players whenever possible.
- Colors must look good on both light and dark backgrounds.

---

## Rules

- No two colors should be visually similar.
- Saturated colors are preferred.
- Every color should keep the same lighting and shading style.
- Character color changes only the main body material.
- Eyes and facial proportions remain identical within a species.

---

## Stable Match IDs

Each gameplay Match ID is permanently assigned to one color and one vendor
species. Both are approved and implemented — see
`Assets/Scripts/Editor/CharacterAssetBuilder.cs` (`MatchIdToSpecies`), the
single place this species assignment is authored in code, and
`Assets/Scripts/Gameplay/Presentation/ColorPalette.cs`, the runtime mirror of
this table's colors.

| Match ID | Color | Hex | Species |
|----------:|-------|-------|-------|
| 01 | Red | #f81718 | Crab |
| 02 | Orange | #fe920b | Crab |
| 03 | Yellow | #ffe924 | Turtle |
| 04 | Lime | #a6f108 | Turtle |
| 05 | Green | #00c02d | Turtle |
| 06 | Mint | #3af8cb | Fish |
| 07 | Cyan | #00d1ee | Octopus |
| 08 | Azure | #014de0 | Fish |
| 09 | Peach | #ffb98f | Turtle |
| 10 | Navy | #0c3264 | Fish |
| 11 | Purple | #8e1dfe | Octopus |
| 12 | Violet | #e0b4e7 | Octopus |
| 13 | Pink | #fc4982 | Octopus |
| 14 | Coral | #fc4b2c | Crab |
| 15 | Brown | #7f300a | Crab |
| 16 | Cream | #ffe7c2 | Turtle |
| 17 | Teal | #189b99 | Fish |
| 18 | White | #fbf4f0 | Octopus |
| 19 | Gray | #908781 | Fish |
| 20 | Black | #212121 | Crab |

These IDs, colors, and species assignments are permanent.

`Character_XX` and `Food_XX` use the same numeric ID (Match ID). Gameplay
matching is performed by `MatchTypeId`, a separate, level-scoped identity —
`MatchId` is presentation only. See `docs/ARCHITECTURE_NOTES.md` for how the
two relate.

Current character assets live at
`Assets/Art/Themes/Classic/Character/Character_01` through `Character_20`,
each a complete `Character_XX.prefab` / `Character_XX.mat` /
`Character_XX.png` set built from this table.