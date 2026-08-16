# World Select: menu-style redesign

2026-08-16

## Problem

The world screen is the only full-screen menu in the game that does not look like the game's menu.
`WorldSelectBuilder` builds it as a `UITheme.Backdrop` panel with a scrolling list on the left and a
sidebar of boxed buttons on the right — a different visual language from `MainMenu.unity`, which is
bold text over the live 3D scene with nothing behind it.

It also deletes a world the instant the button is pressed, with no confirmation. A save is the only
thing in the game a player cannot recreate.

## What the main menu's design actually is

Read from `MainMenu.unity` and the embedded clips in
`Assets/Game/Art/Animations/UI/Buttons/Menu Button.controller`:

| | value |
|---|---|
| Title `Space Game` | white, bold, 180, left-aligned |
| Entry, normal | authored near-black navy `(0.043, 0.118, 0.227)`, bold, 90, left |
| Entry, hover | white, root scales to 1.1, FMOD `event:/UI/No` |
| Entry, press | dark red `(0.545, 0, 0)`, FMOD `event:/UI/Yes` |
| Column | left edge, `VerticalLayoutGroup` spacing 60, entries 600x50, tilted 2° |

The background is the 3D menu scene, undimmed. `MultiplayerChoiceUI` and `MinigameConfigUI` get that
by switching every other `Canvas` *off* rather than covering it.

**The constraint this puts on the design:** the animator drives `m_fontColor` on the child named
`Text (TMP)` every frame, and `m_LocalScale` on the root. A selected row therefore cannot be
indicated by tinting its label — the clip overwrites it on the next frame. Selection needs a marker
the animator does not own.

## Screens

One component, three pages, no panels, scene visible throughout.

### A. World list

```
WORLDS                                          title, white

+  New world                                    entry

   Dune Camp          2026-08-16 14:02          scrolls when it overflows
>  Test World         2026-08-14 09:31          selected: marker + brightened date
   Rusty Flats        2026-08-11 21:07
   Old Save           (unreadable)

Back          Delete          Start game        bottom row
```

- Rows are `Menu Button.prefab` clones, so hover animation and both FMOD sounds come for free.
- Selection is shown by a `>` marker glyph in the left gutter — a separate child the animator does
  not touch — plus the date label, which is also outside the animated path.
- `Delete` and the start action are built only when a world is selected, and the row is rebuilt on
  every selection change.
- The start action reads **Start game** for `Destination.Singleplayer` and **Continue to lobby** for
  `Destination.Lobby`, because on the host route it does not start anything.
- Unreadable saves stay listed. A world the player can see and delete beats one that silently is
  not there.
- Load errors (a save stamped with another world's config id) appear as a dark-red caption above the
  bottom row.

### B. New world

```
NEW WORLD

World name
Dune Camp|                                      underline rule, autofocused, Enter submits
A world called 'Dune Camp' already exists.      only on error, dark red

Start game
Back
```

40-character limit kept. Creating refuses a duplicate rather than overwriting it: the two are one
keystroke apart and only one of them is recoverable.

### C. Delete confirmation

```
DELETE WORLD

Test World
Last played 2026-08-14 09:31

This cannot be undone.

Delete world
Cancel
```

Confirming deletes the file and returns to the list with the selection cleared.

`Delete world` renders in the same near-black as every other entry, because the animator owns entry
colour. The page title and the warning line carry the signal instead; the press state is already
dark red.

## Code

- **`WorldSelectUI` rewritten** as a runtime-built screen:
  `public static WorldSelectUI Open(MainMenuUI owner, Destination target)`. It owns a `Page` enum and
  rebuilds the page root on every switch. No scene objects, no serialized wiring.
- **New `MenuScreen` base class** (`Presentation/UI/Widgets/MenuScreen.cs`) holding the canvas
  creation, `HideMenuCanvases` / `RestoreMenuCanvases` and cursor handling that `MinigameConfigUI`
  and `MultiplayerChoiceUI` already carry one copy each of. `WorldSelectUI` and
  `MultiplayerChoiceUI` derive from it. `MinigameConfigUI` is left alone — it is 460+ lines and sits
  on the minigame flow, so migrating it is not this change's business.
- **`MainMenuUI`**: `worldSelect` field dropped, `menuButtonPrefab` added, `worldConfig` exposed.
  `StartSinglePlayer` and `HostMultiplayer` call the static `Open`.
- **`WorldSelectBuilder.cs` and `WorldRow.prefab` deleted**, replaced by a one-shot
  `WorldSelectSetup.cs` (`Tools ▸ SpaceGame ▸ Menus ▸ Setup World Select`) that strips the legacy
  `WorldSelect` canvas out of `MainMenu.unity` and assigns the button prefab. The scene has to be
  re-saved either way — the old panel is authored into it.
- **`WorldIdentity.ValidateNewName(typed, slots, out error)`** in the Format assembly, beside the
  rules it belongs with, so empty / duplicate / sanitise-collision (`Dune Camp!` and `Dune Camp` are
  one file) are covered by an EditMode test rather than by pressing buttons. The UI keeps no
  validation logic of its own.

Unchanged: `LobbyMenu.unity`, `SaveSlots`, `WorldSession`, the save format.

## Testing

- `WorldIdentityTests` gains the validation cases against a temp save root.
- The screens themselves are checked by running the menu: list renders, selection reveals the two
  actions, delete asks first, create refuses a duplicate name.
