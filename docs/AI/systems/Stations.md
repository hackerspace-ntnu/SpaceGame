---
system: Stations
layer: characters
summary: "A spot whose prop calls for one work cue, the tool its clips need and a stand point its reach lands on"
paths:
  - Assets/Game/Scripts/agents/Residents/Plan/StationStand.cs
  - Assets/Game/Scripts/agents/Residents/Editor/StationAuthoring.cs
  - Assets/Game/ScriptableObjects/Animation/Cues
symptoms:
  - "a cook stirs, ploughs or swings a pickaxe at the stove"
  - "a farmer or miner swings the tool short of the bed or the ore, or from a metre too far back"
  - "a resident holds the wrong tool at a work spot, or mimes holding one"
  - "a worker stands at its spot doing nothing, with a clean console"
  - "a spot holds a cue and no looping action fits (resident has nothing to do)"
reads_with: [StationTable, Residents, HumanoidAnimation, HandTools, Errands, AnimationCatalog, NpcAnimationPlan]
updated: 2026-10-03
---

# Stations

A resident at a work spot used to draw a clip from a **pool**: `Hold(cue)` picks among every looping action tagged with the cue, and
`cook`, `work`, `craft`, `dig` and `tend` were each a dozen unrelated loops (a cook could plough, a weaver could chop), re-rolled on
every re-entry, with a fallback to the 48-action `work` pool behind them. And nothing said where the body had to stand for the tool to land.
A **station** is the contract that replaces that, kept entirely in data.

## Model

| Part | Where it lives | Says |
| --- | --- | --- |
| Prop | a decoration or building prefab carrying a `SettlementSpot` | what is worked at |
| Target | the spot's `face` transform (`SettlementSpot.HasTarget`) | the thing the tool should land on |
| Stand point | the spot's transform, authored beside the prop; `StationStand.Derive` moves it toward the target (below) | where the body stands, facing the target |
| Kind of spot | a `SpotUse` asset: role, staffing, default `holdCue`, `seated` | what the place is for, who posts here |
| Cue | `SpotUse.holdCue`, or `SettlementSpot.stationCue` where one prop differs from its use | **one cue per kind of work**: `stir`, `grill`, `cookpan`, `chop`, `mine`, `farm`, `hammer`, `repair`, `weave`, `wipe`, `tend` … |
| Clips | the cue's looping actions (`StationAuthoring.Cues` lists them exactly) | what the body does |
| Tool | `CharacterCue.Tools` (hand tools the clips are made for) or `CharacterCue.BareHands` | what `ResidentHands` draws, or puts away |
| Reach | `CharacterAction.Reach`, measured once by `ActionReachMeasurer` | where the work lands, relative to the body root |

- **A station cue never falls back.** `BodyLanguage` follows `CharacterCue.Fallback` when no action fits, and the job cues used to fall back
  to `work`. Station cues have none: a loop that does not fit leaves the resident standing, which reads as waiting, not as another job.
- **A cue's loops are exactly its list.** `StationAuthoring` tags the listed actions and removes the cue from every other *looping*
  action (a one-shot is never held, and a moment may still ask for it). The cooking actions also keep the family tag `cook`, which no
  spot holds; chat (`/act cook`) and `Express(cook)` still use it.
- **Stand point derived from target and reach** ([`StationStand`](Assets/Game/Scripts/agents/Residents/Plan/StationStand.cs)): for a spot that
  names a target and holds a cue with a measured reach (the mean of the cue's looping actions that fit a standing body), the authored
  point moves toward the target until the reach lands on it, by at most `ResidentTuning.stationMaxShift` (0.6 m), never closer than
  `stationMinDistance` (1.0 m) and never farther than authored, then the usual NavMesh check (`SettlementPlaces.TryStand`). The derived point is
  taken only if it is walkable and no farther from the target than the authored one; otherwise the authored point stands, as it always did.
  A spot with no target, no measured reach, a seat or no cue is untouched.
- **A station with nothing to show stands.** No cue means the resident waits at the place (`stands`); a cue with no loop for its posture fails
  `StationTableTests`. Faking a clip with the nearest unrelated one is what this replaced.

## Key types

| Type | Role |
| --- | --- |
| `SpotUse` / `SettlementSpot` ([Spots/](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Spots)) | the kind of spot; the prop's spot with its optional `stationCue`, `HoldCue`, `HasTarget` |
| `SettlementPlace.HoldCue` ([Core/](Assets/Game/Scripts/agents/Residents/Core/SettlementPlace.cs)) | the loop held at a place; what `ResidentPresence` holds on every machine |
| `StationStand` | pure rule: mean reach of a cue's loops, `Derive` the stand point, `Shortfall` of the tool |
| `CharacterCue.Tools` / `BareHands` | the tool contract; read by `ResidentHands.Station` |
| `ResidentHands.Station` | draws the cue's tool if the resident carries one (its usual tool when that is among them), or puts its tool on the belt for a bare-hands cue |
| `StationAuthoring` | the data: every station cue, its exact loops, its tools, each kind of spot's cue. **Tools > SpaceGame > Residents > Author Station Cues** |
| `StationTable` | the table in [StationTable.md](StationTable.md), written from the data. **Tools > SpaceGame > Residents > Write Station Table** |
| `ActionReachMeasurer` | measures each work action once; **Tools > SpaceGame > Animation > Measure Action Reach** |

## Table, clip honesty and reach

The generated station table (every kind of spot, its props, cue, loops, tools, workers and status), how close each clip is to its job, and the measured
reach of every work action are in [StationTable.md](StationTable.md).

## Multiplayer

Nothing new is replicated. Spots, their `stationCue`, the cues, the actions and their reach are assets and scenery, identical bytes on every machine.
`ResidentPresence` already derives the held place from the replicated place index on every machine and holds `SettlementPlace.HoldCue` of it; the
hand slot follows (`ResidentHands.Station`) from the same state. The stand point is derived where the server walks (`SettlementSociety.RefreshStands`
runs only where `Network.Decides`); clients see the body where its transform sync puts it. **Not run on a client.**

## Persistence

Nothing new is saved. Cues, tags, reach and stand points are assets or derived on load; the hand slot is re-asserted from the activity every frame
as before. State explicitly: no station state is saved, and a reload re-derives every stand point.

## Gotchas

- **A cue is a pool.** Anything tagged with a cue plays for every caller that asks for it. Adding a tag to a station cue's action without adding it to
  `StationAuthoring.Cues` is undone by the next *Author Station Cues* run, which removes every other looping action from the cue.
- **A spot's cue is data on two levels.** `SpotUse.holdCue` is the default for the kind; `SettlementSpot.stationCue` overrides it for one prop. The builder
  ([ResidentErrandContentBuilder](Assets/Game/Scripts/agents/Residents/Editor/ResidentErrandContentBuilder.cs)) no longer sets a cue: it clears the cue and
  calls `StationAuthoring.Author()`, so a re-run lands on the same data. A prefab override lives in the prefab (the grill's spot in `Deco_GrillBrazier` and
  the flattened copy in `NomadKitchen`; nested instances inherit it).
- **A resident on a seat reads `Standing`.** The animator's `Seated` flag is only raised by the player's `ChairPose`, so sit loops are limited to either posture
  and the asset test accepts either for a seated spot.
- **The tool contract is checked against what the post carries.** `TOOL MISMATCH` means an archetype whose `post` is this kind of spot carries (held or on the
  belt) none of the tools the cue's clips are made for. Fixing it is a profession row in `RaxyToolLoadouts` (it rewrites the archetype's tools on *Equip
  Residents*): Brewer now holds a ladle with the flask and the water tank on the belt, and Apprentice a hammer with the chisel and trowel, but the table in
  `RaxyToolLoadouts.cs` still says tank and trowel until its owner edits it.
- **A bare-hands cue stows the tool only if it can be stowed.** The Drover's cart and anyone on `Drifter_RaxyClassic` (no belt) keep it in the hand and play the loop over it.
- **Hand-tracked reach understates a tool.** See above; the stand point never moves more than `stationMaxShift` for that reason, and the exit check counts a tool that lands on the prop's footprint as a hit.
- **`Kneel`, `Mount On Wall` and one-shots keep their `repair` tag** (a Hold never plays a one-shot); only loops were removed from the pools.

## Extending

- **A new kind of work at a prop:** one cue (a row in `StationAuthoring.Cues` with its exact loops and tools), tag nothing by hand, run *Author Station Cues*, set the spot's
  `stationCue` (or its use's `holdCue`), add the action to `ActionReachMeasurer.Jobs` and run *Measure Action Reach*, then *Write Station Table*.
- **A station that has no clip:** leave the use's cue empty. The resident stands; the table says `stands (no clip yet)`.
- **A new prop for an existing station** (a wok, a basin, a spice rack): add a `SettlementSpot` with a `face` and its `stationCue`; the cue and its clips already exist.
- Tests: `StationCueTests`, `StationReachTests`, `StationTableTests`, `ActionReachAssetTests`.
