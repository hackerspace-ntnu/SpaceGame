# Versus mode — menu, rules page and team lobby

**Date:** 2026-08-25
**Status:** approved, ready for implementation
**Scope:** the UI system for a second front-menu mode ("VS"), wired to the game that exists today.

## Problem

The front menu offers Singleplayer, Multiplayer and Quit. Every route through it ends in the
same ungrouped lobby: four astronauts in a line, each in a suit colour they picked for
themselves, with no notion of sides. There is no way to set up a match with teams.

Two facts about the current code shape this work:

- `MainMenu.unity` binds its buttons to `MainMenuUI` methods **by string**, so re-routing the
  menu costs no scene surgery beyond the labels and the two new method names.
- `LobbyUI` decides "am I the host?" from `WorldSession.IsActive` — a staged world. A VS host
  stages no world, so that inference stops being true and has to be replaced.

## Goals

- A front menu of **Story** and **VS**.
- Story keeps today's behaviour exactly: Singleplayer, or Multiplayer → Host/Join.
- VS is multiplayer only: Host → a rules page → the lobby; Join → the lobby, as now.
- The rules page tunes **team count** and **team size**.
- In the lobby, astronauts stand in **team clusters** with a gap between teams; each team has
  one colour; players can change team and change their team's colour; the host can retune the
  rules live.
- VS launches the ordinary world scene with the team layer carried into it.

## Non-goals

- Gameplay rules for VS — no friendly fire, no scoring, no win condition. Teams tag sides and
  colour suits; nothing else.
- Persistence for VS matches. A VS session stages no world, so nothing is loaded and nothing is
  saved.
- The orphaned `MinigameConfigUI`. It is reachable from no button in `MainMenu.unity` and is
  left untouched here, but it is dead UI whose fate should be decided separately.

## Design decisions

| Decision | Chosen | Why |
| --- | --- | --- |
| What VS launches | The normal world scene | Teams tag sides; no arena, no win condition |
| Seat count | teams × team size, ceilinged at **24** | Relay is allocated once for 24; the lobby's advertised max follows the rules |
| Team colour authority | Any member recolours their own team | The cycler that exists stays put and changes meaning; nobody waits on the host |
| Team switching | Click a team — no empty seats drawn | Teams are the click target; the cluster is the affordance |
| Host retuning | Steppers in the lobby's top strip | The host stays on the page they are reading |
| Shrinking an occupied team | Refused, with the reason | Nobody is silently moved |

### Flow

```
Story ─→ Singleplayer ─→ world list ─→ world
      └→ Multiplayer  ─→ Host ─→ world list ─→ lobby
                        └→ Join ─→ lobby
VS ───→ Host ─→ RULES ─→ lobby ─→ world (teams)
      └→ Join ────────→ lobby
```

VS host and Story host are both three steps deep: VS trades world-select for the rules page
rather than stacking one on top of the other. The rules page is meaningful friction — it is the
thing VS is configured by — where a world-select on a mode that stages no world would be
incidental tax (`GDC-L1-UX-0007`). The front menu now asks "what kind of game" first and defers
single-vs-multi and host-vs-join one level down, which is progressive disclosure at no extra
depth for anyone (`GDC-L1-UX-0002`).

## Components

### `MenuChoiceUI` (new, replaces `MultiplayerChoiceUI`)

A `MenuScreen` that takes a title and a list of `(label, action)` pairs and draws them as menu
entries over the live scene. Both second-level pages are built from it:

```csharp
MenuChoiceUI.Open(owner, "STORY",   ("Singleplayer", …), ("Multiplayer", …));
MenuChoiceUI.Open(owner, "VERSUS",  ("Host a game", …),  ("Join a game", …));
MenuChoiceUI.Open(owner, "MULTIPLAYER", ("Host a game", …), ("Join a game", …));
```

`MultiplayerChoiceUI` is deleted; three near-identical bespoke screens is the copy-paste
CLAUDE.md forbids. Its reasoning — why host and join are asked before anything else, and why the
screen is built from `MenuEntry` rather than in white — moves to the new class doc.

### `MenuStepper` (new widget)

The `−  3  +` row, in the menu's language: a fixed-width label, two chevron buttons drawn in
`MenuEntry.Caption`, a centred value. Extracted from `MinigameConfigUI`, where it exists
privately today, so the new screens and any future one share one implementation. Sits in
`Presentation/UI/Widgets/` beside `MenuField` and `MenuEntry`.

### `VersusRules` (new, Core assembly)

The clamps and the staged rules, free of `UnityEngine` references so the EditMode tests can
reach it — the same split `MatchRules`/`MatchSettings` already uses.

```
MinTeams     2      MaxTeams     8
MinTeamSize  1      MaxTeamSize  12
MaxSeats     24     Seats => TeamCount * TeamSize
```

Clamping is a pair, not two independent clamps: raising teams must not silently push the seat
total over 24, so the product is clamped after each axis and the caller is told which axis gave.

Values stage in statics consumed once when the lobby is created, the shape `WorldSession` and
`MatchSettings` already use. Reset to defaults on entering the rules page, so a second visit does
not inherit the last match's numbers.

### `VersusRulesUI` (new page)

```
VERSUS

Teams          −   3   +
Team size      −   4   +

12 of 24 seats

Start lobby
Back
```

A `MenuScreen`, title above the horizon in white, controls below it in navy, exactly like the
screens either side of it. The seat caption updates on every step and is the only place the
24-seat ceiling is explained.

### Lobby data model

The lobby is the authority for everything a joiner has to see:

| Where | Key | Holds |
| --- | --- | --- |
| Lobby data | `Mode` | `story` / `versus` |
| Lobby data | `TeamCount`, `TeamSize` | the host's live rules |
| Player data | `Team` | which team that player stands in |
| Player data | `TeamColor` | `"<swatch>:<stampMs>"` — the colour this player last set for their team |

`Mode` is Public (the browser labels rows the player has not joined, and a VS joiner's browser
lists only VS lobbies); the rest are Member, like the name and suit colour, because they are only
meaningful once you are inside.

**Team colour lives in player data, not lobby data, and that is forced rather than chosen.**
`LobbyService.UpdateLobbyAsync` is host-only, so a shared `TeamColors` blob in lobby data could
not be written by the member who pressed the arrow — the decision that any member recolours their
own team would need a round trip through the host to survive. Player data has no such
restriction: each player writes only their own row.

A team therefore has as many opinions about its colour as it has members, and they are resolved
by the **stamp**: a team's colour is the highest-stamped value among the players standing in it,
ties broken by lobby order. Last writer wins, which is what a colour cycler means. The stamp is
the writer's own `UtcNow` in milliseconds — clocks between friends disagree by seconds at worst,
and the only thing at stake in a disagreement is which of two swatches a team wears for one poll.

`LobbyUI` takes an explicit route — `StoryHost`, `StoryJoin`, `VersusHost`, `VersusJoin` —
instead of inferring hosting from `WorldSession.IsActive`. This supersedes the reasoning
currently written into that field's doc comment: with VS, a staged world is no longer the
difference between the two routes.

Team switches and colour steps publish **debounced**, because Lobby rate-limits updates and
stepping a palette is a dozen presses in two seconds. The suit-colour flush is factored into one
debounced-publish helper rather than copied twice more.

`Render(names, colors, localSlot, hostSlot, localColor)` becomes one `RosterSnapshot` readonly
struct built by `LobbySession` from the `Lobby`. That keeps the parameter list from growing with
every feature and preserves the property the views were deliberately built to have: they render
without a network, an authentication service, or Unity Gaming Services.

### Capacity

Relay's allocation size is fixed at allocation time, so a VS host allocates **24** connections up
front and the lobby's advertised max follows teams × size. That is the only shape in which live
retuning can work: a host who adds a team after allocating for 8 would otherwise have joiners who
can reach the lobby and not the server. Story keeps its 4.

### The lobby page

```
 CODE  4F2K9B   Copy      Private off        Teams − 3 +   Team size − 4 +   12 seats
                                             └─ host only ─────────────────┘

        TEAM ONE                TEAM TWO                TEAM THREE
     ┌──────────────┐        ┌──────────────┐        ┌──────────────┐
     │  🧍  🧍  🧍  │        │  🧍  🧍      │        │              │
     │ Ferd Ola Kari│        │ Sindre Ida   │        │              │
     └──────────────┘        └──────────────┘        └──────────────┘
          < Cobalt >

 Waiting for the host to start.
 Start game        Leave
```

- **Teams are the click target.** No empty seats are drawn; clicking a team's nameplate joins it.
  A full team's plate dims and the status line says why, rather than the plate silently refusing
  — the legal action obvious and the illegal one visibly unavailable (`GDC-L1-UX-0004`).
- **Seat positions are reserved even though empty seats draw nothing**, so nobody slides sideways
  when someone joins. This is the rule `LobbyPreviewRank` already holds for its four fixed slots.
- **The cycler under your own boots stays and changes meaning**: it steps *your team's* colour,
  skipping swatches another team wears, so two teams can never be confused for each other.
- **Host steppers** sit in the top strip beside the code. Shrinking below what is occupied is
  refused with the reason ("Team One has 3 players").

Two things the current rank cannot survive at 24 figures:

- The camera **dollies back** along its own forward from `LobbyCameraView` to fit the rank's
  width, rather than sitting at the authored distance framing four.
- A team of more than four **wraps to a second row**, so a 24-seat lobby is six clusters rather
  than a forty-metre line.

Individual nameplates scale with their figure's projected height; **team plates stay large and
constant**. At 24 players the team is what you read and the name is secondary — hierarchy by size
and contrast (`GDC-L1-UX-0003`).

The grouping is the feature, not decoration. The lobby is where a team becomes a team, and
standing your side physically together before a match is the social design `GDC-L1-MP-0001` is
about: the netcode only delivers the people.

### Wiring into the world

`PlayerIdentity` already publishes `GameSettings.SuitColorIndex` as a NetworkVariable and paints
the body from it on every peer. VS adds:

- `VersusSession` — a static carrying team index, team count and team colours across the load
  into the world scene.
- `PlayerIdentity` publishes the **team's** colour and a `team` value while that session is
  active, and the personal swatch when it is not.

The personal preference in `GameSettings` is never overwritten with a team colour. It is a
property of the install and a match must not spend it.

## Naming and lifecycle

- A VS lobby is named after the host's player name ("FERD'S MATCH"), since there is no world to
  name it after.
- VS stages no world, so a VS session is transient: nothing is loaded, nothing is saved.

## Verification

Per CLAUDE.md's non-negotiables:

1. **Multiplayer.** Every value a joiner needs — mode, rules, team colours, who is on which team
   — lives in lobby data and is polled, so a client renders the same lobby the host does. Verified
   on an actual client, not only the host: two peers, one switching teams and recolouring while
   the other watches.
2. **Save/quit/load.** VS holds no state worth persisting: the rules live in the lobby for the
   life of the session and the match is transient by design. The rules page's staged statics are
   reset on entry, so nothing survives that should not. Stated explicitly rather than skipped.
3. **No code smells.** `MultiplayerChoiceUI` is deleted rather than left beside its replacement;
   the stepper is extracted rather than copied; the debounce is extracted rather than copied a
   third time; every tunable (team limits, cluster gap, row wrap threshold, camera fit margin) is
   a named constant with the reasoning beside it, in the style the surrounding files already use.
