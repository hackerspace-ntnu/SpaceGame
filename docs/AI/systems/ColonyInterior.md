---
system: ColonyInterior
layer: world
summary: The Mars colony walked into directly - hollow hulls, clicked airlocks, stairs and furnished rooms
paths:
  - Assets/Game/Scripts/World/Colony/
  - Assets/Game/Scripts/Core/Persistence/Adapters/ShelfStockSaveable.cs
  - Assets/Game/Prefabs/Environment/Structures/AstronautSettlement/
  - Assets/Game/Prefabs/Environment/Decorations/Astronaut/
  - Assets/Game/Art/Models/_Source~/models/buildings/mars_colony_export.py
  - Assets/Game/Art/Models/_Source~/models/buildings/astronaut_decorations_export.py
symptoms:
  - "I right-click a colony hatch and nothing happens, not even a message"
  - "I am stuck in the colony airlock chamber and neither hatch opens"
  - "the inner hatch only says close the outer hatch first"
  - "the airlock opened for the host and stays shut for a client"
  - "[Net] 'Colony_X' handled message 124 locally"
  - "[Airlock] 'X' is missing a hatch or a zone"
  - "the airlock cycles silently with no mist, beacon or flashing lamps"
  - "I can see the desert straight through a colony wall from inside"
  - "the colony windows are opaque orange from inside"
  - "a colony doorway or tube is blocked by an invisible wall"
  - "I cannot walk up the colony stairs, the last step is a metre off the ground"
  - "the colony oxygen filler is dark with no battery in it"
  - "the weapon rack lays its guns again after every reload"
  - "my oxygen drains inside the colony"
reads_with: [ArtPipeline, Oxygen, Backpack, Seats, Errands, TerrainGeneration, Persistence]
updated: 2026-10-03
---

# Colony interiors

## Model
The `Colony_*` buildings are walked into directly: no scene load. Each hull is hollow (cut in `mars_colony.blend`, see
[ArtPipeline.md](ArtPipeline.md)), lined with an interior shell, and every exterior door is an airlock. Windows and the
hub domes are real glass: from inside you see the desert and the sky, from outside the lit rooms.

| Piece | What it is |
|---|---|
| Hull | The building FBX. Rooms, doorways (1.70 m round) and windows are Boolean-cut; `<Building>.<unit>.Liner` meshes line every hub, module and tube |
| Outer hatch | The hull's own door plug, cloned under `Interior/OuterHatch_n` (a pivot on its hinge) and swung 100 degrees by an `AirlockHatch`; the FBX node is hidden |
| Airlock | `AstroDeco_AirlockBulkhead` (a wall across a module, 2.08 m in from a door end) or `AstroDeco_AirlockVestibule` (a 2.6 m chamber built out from a hub wall), each with two sliding inner leaves, an `AirlockChamber` and (once assembled) an `AirlockPressureEffects` |
| Stub / blind face | `AstroDeco_SealedHatch` over the doorway, and a solid block outside over the docking stub |
| Stairs | `colony_stairs.fbx` at each door's outer face, plus a hidden `StairsRamp_n` box in `Colliders` |
| Air | a `BreathableVolume` trigger per module, hub and tube (`Interior/Air_n`): `SuitOxygen` stops draining inside |
| Props | `Prefabs/Environment/Decorations/Astronaut/AstroDeco_*` (16, one per `Coll_AstroDeco_*` in `astronaut_decorations.blend`), placed as nested prefab instances |
| Fixtures | `OxygenGenerator.prefab` (scale 1.7, `startingBattery` 1), `HoloProjector.prefab` (1.7), `InventoryWall.prefab` (+ `PersistentFixture`), the small gear board (see Extending), `ShelfStock` on every weapon rack and workbench |

Rooms per building (module roles in brackets): `Large` MA/ME (quarters), MB (workshop), MC (lab), MD (galley), H1 commons (projector, oxygen filler, consoles, stools), H2 (gear wall, hydroponics, table); `TwinDome` H1 commons, H2 gear wall + bunk + workbench + hydroponics; `HubWing` MA bunk + workbench, H1 commons + gear wall + rack; `DomeHub` commons + gear wall + rack + bunk; `HabRow` MD outpost (bunk, rack, oxygen filler, projector), ME lab; `DockPod` outpost; `HabPod` bunk, oxygen filler, projector.

## Key types
| Type | Role |
|---|---|
| [`AirlockCycle`](Assets/Game/Scripts/World/Colony/AirlockCycle.cs) | Pure decision, unit-tested (`AirlockCycleTests`): a click on a hatch (and whether the clicker stands in the chamber) in, `AirlockState` or an `AirlockRefusal` out. Holds `cycleSeconds` |
| `AirlockState` (same file) | The whole replicated state: inner/outer open, `Vented` (chamber holds outside air), `Phase` (`Settled`/`Venting`/`Pressurising`), `Pending` (the hatch waiting on a cycle). One int on the wire |
| [`AirlockChamber`](Assets/Game/Scripts/World/Colony/AirlockChamber.cs) | Binds its leaves, sends clicks to the server, runs the cycle there, announces the state, explains refusals on the clicker's visor (`PlayerHints`), feeds the hatch readout |
| [`AirlockHatch`](Assets/Game/Scripts/World/Colony/AirlockHatch.cs) | One leaf: authored shut pose plus `openOffset` (slide) and `openEuler` (swing). `IInteractable` + `IInteractionReadout`: right-click operates its whole hatch; label, prompt, cycle bar, air state |
| [`AirlockPressureEffects`](Assets/Game/Scripts/World/Colony/AirlockPressureEffects.cs) | Presentation only, every machine: vent / fill jets, chamber haze, rotating beacon, flashing lamps, klaxon + hiss + valve sound, a fenced camera kick |
| [`PresenceZone`](Assets/Game/Scripts/World/Colony/PresenceZone.cs) | Trigger tracking player bodies (`SuitOxygen`), per body, not per collider: `Occupied`, `Contains(body)` |
| [`ShelfStock`](Assets/Game/Scripts/World/Colony/ShelfStock.cs) / `ShelfStockSaveable` | Spawns its item pickups once per world on the server; `stocked` is saved (`shelfStock`) |
| [`PersistentFixture`](Assets/Game/Scripts/World/Colony/PersistentFixture.cs) | `IPersistentEntity` marker so a placed `InventoryWall` gets an entity and its contents save |

## Flows
1. **Coming in.** Right-click the outer hatch ("RMB: vent and open") -> the chamber vents (`cycleSeconds`, 3 s: jets, beacon, amber flashing, klaxon) -> the outer hatch swings out -> step in -> right-click the inner hatch: refused, "Close the outer hatch first" -> right-click the outer hatch shut behind you -> right-click the inner hatch -> the chamber pressurises (haze fills it) -> the inner leaves slide apart. Nothing closes on its own.
2. **Leaving.** Right-click the inner hatch ("RMB: open") -> it opens at once (the chamber holds room air) -> step in, close it behind you -> right-click the outer hatch -> vent -> it opens.
3. **Interlock.** A hatch never opens while the other is not fully shut. The clicker stands in the chamber (`chamber` zone, read on their own machine and sent in the request) -> they can reach the other hatch, so they are told to close it. They stand outside the chamber (the deadlock: someone left the far hatch open) -> the far hatch is sealed for them, unless someone stands in its doorway ("Someone is standing in the inner hatch"). A hatch is never told to shut on someone in its own doorway ("Step clear of the hatch first"). Every click during a cycle is refused ("The airlock is cycling"). Why it is built this way: every press is answered, by a move or by a message saying why not, and the crosshair prompt says beforehand what the press will do (`GDC-L1-UX-0003`); the lamps, haze and readout show which air the chamber holds before anyone clicks (`GDC-L1-SYS-0006`); the cycle, the one slow event the player waits on, is layered across sight, sound and camera (`GDC-L1-FEEL-0004`), with the kick small, local and on the shake setting (`GDC-L1-FEEL-0006`).
4. **Pressure.** The chamber holds one side's air (`Vented`). A hatch on that side opens at once; the other side waits for both hatches to report fully shut, then runs `Venting` or `Pressurising` for `cycleSeconds`, then opens. The readout's bar is the cycle's progress and its value text the chamber's air.
5. **One click, end to end.** `Interactor` -> `AirlockHatch.Interact` -> `AirlockChamber.Operate` checks with the state this machine already holds; a refusal is a visor message (`PlayerHints`, id `airlock`) + `InteractDenied` here and nothing is sent -> otherwise `NetMsg.AirlockOperate` -> the server re-checks (`AirlockCycle.Operate`, doorway zones read there), ticks the cycle in `Update`, and on every change `NetMsg.AirlockState` to all -> every machine moves the leaves; `AirlockPressureEffects` reads `AirlockChamber.State`.

## Multiplayer
- **The airlock is server-decided.** It used to be presence-driven and send nothing; a click is a player's choice, which only one machine can be allowed to decide. `NetMsg.AirlockOperate` (124, clicker -> server; `B` bit 0 = outer, bit 1 = from the chamber, `-1` = ask) and `NetMsg.AirlockState` (125, server -> everyone; `B` = `AirlockState.ToWire()`, bit 8 = instant) ride the building's channel, `A` = `NetChannel.IndexOf(chamber)` (positional, like latches). The server's handler and tick are gated on `Network.Simulates(chamber)`; every handler is idempotent on the state, because the host hears its own announcement and a joiner's answer goes to everyone.
- **Late joiners and streamed-in chunks** ask once the wrapper is spawned (`NetToServerWhenSpawned`) and land in the answer instantly and silently; the effects pick a running cycle up from its start.
- **Where the channel comes from.** `SettlementNetworking.Wrap` puts a `NetworkObject` + `NetRelay` round any generated building holding an `AirlockChamber` (as for a `NetworkBehaviour` or a latch). **A colony placed by hand has none: every machine then runs its own airlock** (a client's click opens the hatch on that client only), and `NetChannel.WarnUnrelayed` logs `[Net] '<root>' handled message 124 locally` once per session. Place colonies through a settlement, or give the building a scene `NetworkObject` + `NetRelay` the way `Wrap` does.
- **Refusals the server makes are silent.** The clicker's machine already refused anything its replicated state forbids; a click that raced somebody else's (two players, one hatch) is dropped on the server with no message.
- The oxygen filler and gear wall are `NetworkBehaviour`s with no `NetworkObject` of their own: `SettlementNetworking.Wrap` now also wraps any building holding a `NetworkBehaviour`, and they use that wrapper. A colony placed by hand, outside a generated settlement, has no wrapper: the filler and the wall then only work offline and for the host.
- `ShelfStock` spawns through `GameServices.World.Spawn` on the server; the pickups are ordinary networked world items. Rack pickups are never nested in the prefab: a `NetworkObject` inside a building would nest inside the settlement wrapper.
- **Not run on a client, nor in a generated settlement.** The clicked airlock (2026-10-03) has not been run at all yet: it compiles and `AirlockCycleTests` cover the decisions, but no Play session, host or client, has clicked a hatch. Verified in the editor only, for the hulls: physics probes on every prefab (each door ray meets its outer hatch, floors at the sill, every tube passage clear) and renders.

## Persistence
| State | Where |
|---|---|
| Oxygen filler | `OxygenGeneratorSaveable`; an untouched plant saves nothing, so `startingBattery` re-seeds a battery someone removed |
| Gear wall | `WallInventorySaveable` through the entity `PersistentFixture` earns it |
| Rack / bench stock | `ShelfStockSaveable` (`shelfStock`); the pickups save as world items |
| Projector on/off | `ProjectorSaveable` |
| Hatches, airlock | **Not saved, on purpose.** On load (and whenever a chunk reloads or the server restarts the building) every hatch is shut and every chamber holds the room's air, nothing pending: the state a fresh `AirlockCycle` starts in. A hatch somebody left open is shut again, which is safe: nothing else reads it (it is not a `SandstormShelter` door) |

**Not verified by a save/reload.** Unproven in particular: that `ShelfStock` is restored before its `Start` on a chunk hydrate. If it is not, a reload lays the shelf again.

## Gotchas
- **From inside, the hull's faces are back faces.** The liner must enclose every room completely; any gap shows the desert through the "wall". That is also exactly why the glass works.
- **A Boolean into a closed hull makes a blind pocket.** The room volume is subtracted first (hollowing the hull), then doorways cut through the remaining shell. Cut a doorway into an un-hollowed hull and a cap face blocks it.
- **Export moves the cutters too** (`mars_colony_export.py`); `export_collections` would leave them behind and cut every doorway in the wrong place.
- **FBX axes: Unity = (-x, z, -y) of Blender.** A layout read off Blender without the flip lands every door on the wrong side of a non-symmetric building.
- **The gear wall is 4.4 m tall**, taller than a module (3.93 m) or a hub wall (4.13 m). It only fits under a dome, and a 5.15 m board under a dome always crosses some doorway's path, so it stands in front of a sealed docking stub. Buildings without a hub with a stub (`HabPod`, `HabRow`, `DockPod`) have no gear wall.
- **The stairs model ends 0.98 m above the ground** and the player has no step offset: `StairsRamp_n` is a 20 degree slab from the sill to the ground 5.55 m out; the steps are visual only.
- **The cloned door plug's static flags are cleared**: static batching would freeze a swinging hatch.
- **A rerun of the assembly must re-enable the hidden door node before reading its bounds**: a hidden renderer reports empty bounds.
- **A hatch leaf needs a SOLID collider on the same GameObject as its `AirlockHatch`** (both shipped hatch kinds have a `BoxCollider` there): a trigger is see-through to the `Interactor`, and a collider on a child with no `AirlockHatch` above it blocks the crosshair instead of answering it.
- **Every leaf must be in its chamber's `inner` or `outer` array.** `AirlockChamber.Awake` binds them; an unbound leaf is a solid wall that offers no prompt. A missing hatch array or zone logs `[Airlock] ... is missing a hatch or a zone` - and a missing doorway zone is how a hatch would shut on a player.
- **The `chamber` zone decides "told" versus "sealed for you".** It is read on the clicker's machine, where their own body is the truth; if it does not cover the whole floor between the hatches, a player standing in the uncovered strip gets the far hatch slammed for them instead of the "close it first" message.
- **Dead wiring from the presence-driven airlock:** `Zone_Outside_n` / `Zone_RoomApproach` objects (and their `PresenceZone`s) and the chamber's old `roomApproach`, `outsideApproach`, `closeDelaySeconds`, `lampRenderers`, `statusLight` fields no longer exist in code; the lamps and status light are wired on `AirlockPressureEffects` now.
- **`AirlockPressureEffects` only scales what each particle system was authored with** (`rateOverTimeMultiplier` captured in `Awake`): a jet authored with zero emission stays invisible. Jets want `Play On Awake` off and `Looping` on; the haze `Looping` on (it is started by the script).
- **There is no hiss or airlock event in the FMOD catalog** (the Studio project is lost): the cycle borrows `ShipAlarm` (klaxon), `InteractOxygenFillLoop` (hiss) and `InteractOxygenFilled` (valve thud) as stand-ins.
- Bunks, the research desk and the workbench are scenery: the game has no sleep, research or crafting. Stools and chairs are real `Seat`s.

## Extending
- **A new prop:** add a `Coll_AstroDeco_<Name>` (root at its ground centre = `instance_offset`, front -Y) to `astronaut_decorations.blend`, run `astronaut_decorations_export.py`, wrap it as `AstroDeco_<Name>.prefab` (FBX instance, `Colliders`, `SettlementFixture`).
- **Re-export the hulls:** `blender --background --python Assets/Game/Art/Models/_Source~/models/buildings/mars_colony_export.py`. The prefabs keep their overrides; a moved door needs its `OuterHatch_n` pivot, airlock and stairs moved to match.
- **Airlock timing:** `AirlockCycle.cycleSeconds` (on each `AirlockChamber`), each `AirlockHatch.travelSeconds`, and the effects' `jetEnvelope` over the cycle.
- **Wiring an airlock:** on the airlock root, `AirlockChamber` with `inner` = the two sliding leaves, `outer` = the building's `OuterHatch_n`, `chamber` = a trigger box filling the chamber floor to head height, `innerDoorway` / `outerDoorway` = trigger boxes over each hatch's swept volume (all `PresenceZone`, layer Default). Beside it `AirlockPressureEffects` with `chamber`, the jets, the haze, `ventTarget` = the outer hatch, the beacon light + rotor, the lamp renderers and the status light. Each leaf: `AirlockHatch` + a solid collider on the same GameObject.
- **The small gear board** (2-3 items) is the gear wall with a smaller face, not new code: `PackSurface.size` and `PackContainer.displayScale` are already per-prefab. Keep `displayScale` at the wall's 1.8171431 so gear is drawn the size it is on the wall and in the world (`ItemWorldScale`), and author the face as whole cells: 9 x 6 cells = `(0.8505, 0.567)` logical, drawn 1.545 x 1.030 m. It needs the wall's `WallInventory` + `WallInventoryNetwork` + `WallInventorySaveable` + `PersistentFixture`, a solid `BoxCollider` over the drawn face, and a building with a settlement wrapper for its `NetworkBehaviour`. Boards and walls in one building number themselves positionally (`WallIndex`).
