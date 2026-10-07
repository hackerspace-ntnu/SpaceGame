# Phase 5: NPC worn gear and Sky tribe flight — design (revised)

**Date:** 2026-10-06 · **Status:** decisions taken by the user in conversation; awaiting spec review ·
**Supersedes:** Phase 5 of `docs/superpowers/plans/2026-09-07-faction-system.md` and §3.8 "Flying —
Option B" of `docs/superpowers/specs/2026-09-07-faction-system-design.md` · **Evidence:**
`.superpowers/phase5-design-refresh.md` (design refresh + Spike 5.2a, 2026-10-06)

## Intent

The user asked: "can you add the worn gear and skytribe wings?" — NPCs wear gear the way the player does,
and Sky tribe nomads fly on their wing packs.

**Success:** Sky nomads visibly wear a folded wing pack; they launch off the drifting Sky City and fly
between ground sites in the same flapping craft the player flies, shoot from it, land, step off and walk
on; one shot down crashes as a wreck and drops its pack, which the player can wear and fly; NPCs use
opted-in worn gauntlets; all of it identical on host and clients.

## What changed since the 2026-09-07 design (and why the approach changes)

- The player's `WingPackItem` does **not** fly its wearer's body: it spawns a 10 m `DuneOrnithopter`
  craft (`GameServices.World.Spawn`) and seats the pilot in its cradle.
- `DuneOrnithopter.prefab` **does** have an `AgentController` (the old design assumed it didn't).
- `AirWanderModule` was deleted on 2026-09-22; nothing replaces it for creatures.
- Spike 5.2a (editor, no Play Mode): the craft's `OrnithopterFlightMotor` takes AI input through its
  rider-input channel with no player and no code change; a ~10-line autopilot held 62–65 m for 150 s and
  a glide landed at 1.85 m/s (no damage). The body motor swap also flies but needs ~7 component switches,
  a new `AgentController` API and a dynamic body, and gives rigid non-flapping wings.

## Decisions (the user's, 2026-10-06)

| # | Decision |
| --- | --- |
| D1 | **Ride the wing-pack craft** (Option B′), not the body motor swap. |
| D2 | Sky nomads fly when **launching off the Sky City** and on **ground-to-ground trips** between sites. They do **not** take off when threatened. |
| D3 | The worn pack shows as its **folded model on the back** (seated on the spine bone with an offset — NPCs have no backpack lash rail). |
| D4 | A Sky nomad **flying when the game is saved is not restored**: it and its craft are left out of the save and are gone after a load; its group record brings members back on foot as usual. NPC crafts are an unsaved prefab. |
| D6 | Pilots **may shoot from the cradle** while flying. |
| D7 | Players get flight **only by looting the wing pack** from a dead pilot; no hijacking a landed NPC craft. |
| D8 | When a flying pilot is killed, the **craft crashes as a wreck**; the pilot's body and the wing pack drop where it lands. |
| D9 | **NPC worn-gauntlet use is included** in this phase (opt-in per item). |
| D5 | *Not asked; assumed:* **no formation flying** in this phase (each pilot flies alone to the shared goal). Say so at review if you want formations. |

> **Open, pending the user's decision (recorded 2026-10-06, Task 10):** D7 is contradicted by content that predates this phase — `PlayerCharacterNetworked.prefab` starts wearing a `WingPack`, and a container in `persistentScene.unity` holds three more. Nothing in this phase changed them; remove them or amend D7.

## User decision (post-review, 2026-10-06): NPC-only simple flight motor

On reviewing the implementation plan, the user changed one thing: *"for the ornicopter: we only want
autopilot for the npcs, never the player. And we can make it simple by having a npc motor for this so
that the npc can just fly and not care as much about the physics of it."*

- **The player's flight is untouched.** That covers `OrnithopterFlightMotor` (no AI channel is added), the energy
  flight model and `DuneOrnithopter.prefab`.
- **NPC craft fly a simple motor.** The unsaved `NpcOrnithopter` variant swaps the player's motor for the existing
  `FlyingRigidbodyMotor` (the Sky fleet's motor, given an opt-in bank-into-turns). It is steered by a pure
  NPC flight plan: climb to cruise height, cruise to the goal, descend and land on the
  `LandingSiteFinder` spot, or spiral in as a wreck when the pilot dies. There is no stall, no glide ratio and no stamina.
  The wings still beat, fed by a presenter that measures the craft's motion.
- **What this supersedes below.** It replaces the "Autopilot" bullet of Design §3: the pure `OrnithopterAutopilot` and the AI
  channel on `OrnithopterFlightMotor`. The other decisions (D1–D9) stand. The flat-ground take-off is
  no longer a physics risk, because the NPC craft simply climbs away. The launch no longer uses
  `CraftLaunch`: clients derive the wings from the replicated motion.

## Design

**1. `EntityBodyEquipment` (NPC worn gear).** The NPC counterpart of the player's
`BodyEquipmentController`: the same three `BodySlot`s (Torso/back, Left/Right gauntlet) and
`BodySlotRules`; a server-authoritative record replicated as item ids so every machine seats the visual
itself through `WornSeat.ApplyWithoutRig` + `WornAnchor.Pin` + `WornVisual` (folded form, D3), the instance
sanitized so it is never a world save record. `TryWear` / `Remove`. On death `EntityLootTable` drops
worn items with the bag (server, not while restoring, with the `Remains` lifetime). Saved per NPC
(`npcWorn`). The Sky recipe in `NomadPrefabBuilder` pre-wears the Wing Pack on Sky nomads.

**2. NPC gauntlet use (D9).** `NpcGauntletUseModule` (side effect, never claims movement) fires a worn
gauntlet's `Use` on the entity's channel the way `NpcItemUseModule` fires the hand item — only for
gauntlets whose `InventoryItem` opts in with a new `npcUsable` flag.

**3. Flying = riding the craft (D1).**
- **Autopilot:** a pure `OrnithopterAutopilot` (target point + altitude in, `OrnithopterFlightInput` out:
  angle-of-attack cap below the stall, flap hysteresis, approach and flare), unit-tested in the style of
  `OrnithopterFlightModelTests`, and driven through an AI channel on `OrnithopterFlightMotor` used when no
  rider wrote input that frame (so the player's flying is unchanged).
- **Deploy / land:** the `WingPackItem` flow, extracted and shared rather than copied: launch position →
  server-owned spawn of an **unsaved NPC craft prefab** (`NpcOrnithopter`: same model, motor, wing
  animator and audio; no `MountModule`, `MountSaveable` or `SaveableEntity`; registered as a network
  prefab) → seat the nomad in the cradle via `NpcSeating` (one seat, no dismount on damage) → launch; the
  worn pack's visual hides while deployed. Landing on reachable NavMesh near the goal
  (`LandingSiteFinder`) → step off (`Warp` onto the NavMesh) → craft despawns → worn visual shows again.
- **Decision to fly:** `NpcFlightModule` on the nomad (Override priority, claims movement only while
  deploying/landing) deploys for a `Fly` trip between sites or a launch off the Sky City (D2); the craft
  does the flying. Tunables serialized: `cruiseHeight`, `landingSampleDistance`, `minLaunchClearance`.
- **Shooting from the cradle (D6):** seated passengers keep their brain (`RidesAsPassenger`), so the
  pilot's existing ranged module fires its hand item while flying.
- **Killed in flight (D8):** the craft loses its pilot and falls as a wreck (existing crash detection);
  the body and loot (including the worn wing pack) drop at the crash site.
- **No hijack (D7):** the NPC craft has no `MountModule`; the only way to fly is the looted pack.
- **The Sky City launch:** the city drifts at ~280 m; the craft glides ~9:1, so a launch reaches ~2.5 km
  without flapping.

## Multiplayer

All decisions on the server: wearing, deploy, autopilot, landing, death and crash. The NPC craft is a
spawned `NetworkObject`; the seated pilot is parented through `NpcSeating` (replicated parenting); the
worn visual is derived on every machine from the replicated slot record; launch uses the existing
`CraftLaunch` message. No new `NetMsg` expected. Verify on an actual client (see Testing).

## Persistence

`npcWorn` saves worn items per NPC. Per D4, a nomad flying at save time — and its craft — is excluded
from the save; nothing else new is saved. A landed nomad keeps its worn pack across save/load.

## Testing

- EditMode: slot rules and wear/remove on an NPC; worn items drop on death and are saved/restored;
  gauntlet opt-in; autopilot level-hold, turn-to-waypoint and glide-to-land under the safe closing speed;
  deploy/land seat and teardown; a flying nomad is left out of the save; the NPC craft prefab has no
  mount and is a registered network prefab.
- Human checklist, host + client (agents cannot enter Play Mode): Sky nomads wear the folded pack; a
  launch off the Sky City and a site-to-site trip land within ~15 m of the goal; a pilot shoots while
  flying; shoot one down — wreck falls, pack drops, wear it and fly; save mid-flight and reload — the
  flier is gone, landed ones keep their packs; a late joiner sees the same.

## Out of scope

Formation flying (D5 assumption); taking off when threatened (D2); hijacking NPC crafts (D7); restoring
fliers after a load (D4).
