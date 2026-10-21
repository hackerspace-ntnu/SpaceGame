---
system: GearDoor
layer: world
summary: "Gear-train locks on the workshop gate and its vault: gears carried onto pegs until the drive turns the lock"
paths:
  - Assets/Game/Scripts/World/GearDoor/
  - Assets/Game/Scripts/Items/Placeables/Rules/GearPegPlacement.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/GearDoorSaveable.cs
  - Assets/Game/Editor/Workshop/GearDoorBuilder.cs
  - Assets/Game/Editor/Workshop/GearDoorLayout.cs
  - Assets/Game/Editor/Workshop/GearDoorLayout.json
  - Assets/Game/Editor/Workshop/VaultDoorLayout.json
  - Assets/Game/Editor/Workshop/WorkshopGroundCutter.cs
  - tools/gear_door_layout.py
  - Assets/Game/Editor/Tests/GearTrainTests.cs
  - Assets/Game/Editor/Tests/GearDoorLayoutTests.cs
  - Assets/Game/Art/Models/_Source~/models/props/gear_kit_export.py
  - Assets/Game/Art/Models/Props/Gears/
  - Assets/Game/Prefabs/Items/Workshop/
  - Assets/Game/Resources/Items/Workshop/
symptoms:
  - "the workshop gate is shut and I cannot get into the ship lane"
  - "I put a gear on a peg and it does not turn"
  - "every gear is on the gate and it still does not open"
  - "the gear will not go on the peg: It would hit the gear beside it"
  - "I cannot take a gear back off the gate"
  - "the gears on the gate turn but their teeth pass through each other"
  - "the gear pile lies back in front of the gate after a reload / the gate is shut again after a reload"
  - "Rebuild Workshop Prefab stops with: no gate leaf stands at u = ..."
  - "a peg or a gear on the gate is a hundred times too big"
  - "the gate is sunk into the ground: its bottom pegs are under the sand and I cannot walk into the lane"
  - "putting a gear on the vault door moved a gear on the gate (or the other way round)"
  - "the vault door's drive or lock gear is missing, or floats metres off the door"
  - "a peg sticks out through a gear lying over it"
  - "Rebuild Workshop Prefab stops with: workshop_layout.json has no vault door leaf"
reads_with: [Workshop, Placeables, Inventory, Multiplayer, Persistence, ArtPipeline, WorkshopMission]
updated: 2026-10-12
---

# GearDoor

The workshop's front gate (the two leaves at the ship tunnel's mouth) is locked by a broken gear train. Two fixed
gears are already on it: the **drive** (a rusty 48-tooth wheel on the left leaf, always turning) and the **lock**
(an iron 56-tooth flywheel on the right leaf). Between them stand 46 pegs; three of them, along the way to the lock,
still hold their gear (two XLs and an M). In front of the gate lie the six gears still missing, and nothing else. A player picks one up (RMB), carries it to the gate and puts it on a peg (LMB); anything that meshes with a
turning gear turns at once. When a chain of gears carries the drive to the lock, the lock lets go and the leaves
swing open. The story: raiders smashed the gear train on their way out, and its gears are lying where they fell; a
Shellback outside the gate says so ([WorkshopMission.md](WorkshopMission.md)), and the gate opening is what moves
his story on (`IsSolved`, then `StandsOpen` once the leaves have swung; `LooseGears` for his hints).

**A second door: the vault.** The Shellbacks' vault at the far end of the turning rooms ([WorkshopMission.md](WorkshopMission.md))
has one round leaf with the same kind of lock, smaller but harder: a small iron drive (`Fixed_S`, 14 teeth) low on the
left and a rust lock (`Fixed_M`, 20) on the right, sixteen pegs, and a winding chain of five cogs between them (M, XS, S,
S, XS), **none of them in place**: all five lie on the floor in front of the door. Seven gears in all (the user's call,
2026-10-12: input and output on the door, about five to place, the loose ones on the ground and not on the bench).
Same component, same items, same messages; its own layout file.

## Model

| Idea | Mechanism |
|---|---|
| Two gears mesh when their centres stand exactly the sum of their pitch radii apart | `GearTrain.Between`: all gears share one tooth size, so the distance is `(teeth a + teeth b) * perTooth` (0.03 m: the kit's module 0.04 at gear scale 1.5, halved). Within `tolerance` (0.02 m) they mesh; closer than their tips plus `clearance` they **hit**, and a gear that would hit is refused |
| What turns, and how fast | `GearTrain.Drive`: from the drive at speed 1, a meshed gear turns the other way at `-speed * teethA / teethB`. A gear reached twice at two speeds is a lock-up and its whole train stands still. |
| The puzzle | `GearDoorLayout.json`, written by a seeded search (`tools/gear_door_layout.py`) and checked by an exhaustive one: every way to carry the drive to the lock uses **all nine** chain gears (XS, S, M x2, L, XL x4: one solution), so the gate cannot open with gears left over. Three start on their pegs (`preinstalled`, the chain's 4th, 6th and 8th, `Peg.startKind`): the hint where the way runs, the 8th by the lock where nothing turns to say a gear is right. The pile holds exactly the other six: **no spare gears** (the user's call, 2026-10-11, after nine and then seven were too hard; the broken decoy that was in the pile is gone, and with it the kind). Nine pegs are lures, one exact spacing off a solution gear for some size, leading nowhere; the rest are filler |
| State is slots | `GearDoor`: one slot per peg, then one per pile spot, each holding a gear kind or nothing. Placing, taking and restoring change slots; which gears turn and whether the gate is open follow from the slots on every machine |
| Placing is the item's own use | Each gear is a `PlaceableItem` with `GearPegPlacement` ([Placeables.md](Placeables.md)): the rule finds the free peg nearest the aim point (`GearDoor.TryFindPeg`, within `pegSearchRadius`) and `ServerSeat`s it. The item is spent only if it went on |
| Taking is a press on the gear | `GearDoorGrip` (a trigger box over a slot, on while a gear lies there): RMB asks the server (`GearDoorTake`), which puts the item in the presser's inventory and empties the slot. Pile gears and pegged gears alike; a pegged gear is locked in once the gate is open |
| A covered peg is hidden | `GearDoor.ShowPosts` (every solve): a bare peg whose centre lies under a standing gear's disc hides its model (`Peg.post`), so its stem does not poke through the gear; it shows again when that gear is taken. Such a peg is unusable anyway (`It would hit the gear beside it`) |
| Readable at a glance (GDC-L1-UX-0004) | Carryable gears are brass and copper with size dots; fixed ones iron and rust. The drive turns from the start, so the first gear that meshes visibly turns (GDC-L1-FEEL-0004: seat clunk, jam buzz, lock click, gate swing) |
| Where it would go, before the press (GDC-L1-UX-0003) | `GearPegPlacement.Preview` (the `PlacementRule` preview hook, [Placeables.md](Placeables.md)): while the local player holds a gear and aims near a free peg, `GearDoor.ShowGhost` draws a see-through copy of it there, green if it fits and red if refused, in `PlacementTint`'s colours with an outline (`TintMaterials.Translucent`), teeth set into the gear it would mesh with. Local only |
| A newly turning gear runs in its neighbour's teeth | `GearTrain.MeshedAngle`, from each model's tooth phase (`toothDegrees`, measured by the builder off the rim's furthest vertex) |

## Key types

| Type | Role |
|---|---|
| `GearTrain` / `GearGeometry` / `GearSpec` | Pure solver: `Between`, `Drive`, `MeshedAngle`. Tested without a scene |
| `GearDoor` | A door: slots, solving, the leaves' swing, the pile's grounding, the messages. `TryFindPeg` (static, all live doors), `ServerSeat`, `RequestTake`, `CanTake`, `Restore`, `ShowGhost`/`HideGhost`, `PlaceModel` (shared with the builder), `IsGate` / `GateOf(root)` (the gate among several), `IsLeafPart` |
| `GearDoorGrip` | `IInteractable` + `IInteractionReadout` + `IInteractionMoment` over one slot |
| `GearPegPlacement` | `PlacementRule` on each gear item |
| `GearDoorSaveable` | key `"gearDoor"`: slot name -> item id, only slots that differ from the start, for every door on the entity. The gate's slots are named bare (`Peg_05`), as saves from before the vault have them; another door's carry its `saveId` (`Vault/Peg_03`) |
| `GearDoorBuilder` (editor) | Called by `WorkshopPrefabBuilder.Compose`: `Attach` (the gate) and `AttachVault`. Shuts each door's leaves (the hinges whose `door` names it), gives them a hull, puts pegs, drive, lock and pile on in the door's plane, makes the gear items (asset, prefab, icon) |
| `GearDoorLayout` (editor) | A layout file as classes, `Load(path)`. Door-plane coordinates: `u` along the shut door (the player's right facing it; prefab +z on the gate), `v` up |

## Flows

- **Model to game.** `workshop_export.py` ships `Gate_Door_N`/`_S` (meshes carrying `spin_axis`,
  `closed_angle_deg`, `open_angle_deg`) as **hinge** units: own node in `workshop_moving.fbx` at the modelled (open)
  pose, one `COLT_Gate_Door_*` hull each, and `layout.hinges` (`axis`, `shut` degrees, Unity; `door`, the leaf's
  `gear_door` property: empty for the gate, `Vault` for `Vault_Door_Leaf`). The vault's plane comes from the layout's
  `marks` (`VaultDoor_Face`: -Y out of the door; `VaultDoor_CogTray` is still exported but no longer read). `gear_kit_export.py`
  writes `Props/Gears/gear_{xs,s,m,l,xl,flywheel,skeleton,fixed_s,fixed_m,peg}.fbx`. **Rebuild Workshop Prefab** then logs
  `gear door: 46 pegs, 3 gears on them, 6 loose gears, 2 leaves shut; vault door: 16 pegs, 0 gears on them, 5 loose gears, 1 leaves shut`.
- **A placement.** LMB with a gear → `PlaceableItem.OnRequestUse` (owner aims, `GearPegPlacement.CanPlace` refuses
  with a hint such as `It would hit the gear beside it`) → server `Use` → `Place` → `GearDoor.ServerSeat` → slot set,
  solved, `GearDoorState` to all → item spent.
- **A take.** RMB on a gear → `GearDoorGrip.Interact` → `RequestTake` → `GearDoorTake` (Target = the presser) →
  server: `MayActFor`, `IPlayerInventory.TryAddItem`, slot emptied, `GearDoorState` to all.
- **The opening.** The solve that first finds the lock turning plays the unlock click; after `unlockSeconds` (1.5)
  the leaves turn `openDegrees` about their hinge over `swingSeconds` (8), outward, back to the pose they are modelled
  in. A restore or a joiner's answer that finds it solved stands it open at once.

## Multiplayer

On the building's channel (the settlement wrapper, [Workshop.md](Workshop.md)): `GearDoorTake` 133 (taker → server;
A = slot, or -1 to ask; B = the door) and `GearDoorState` 134 (server → all; A = slot, B = kind + 1, bit 8 = land in it,
bits 9 up = the door). The door is its place among the entity's gear doors (`NetChannel.IndexOf`), the way a station's
number is: every machine runs the same prefab. A late
joiner asks once spawned and is answered with every slot that differs from the start. Placing rides the gear item's
own `UseItem`. Turning gears are each machine's own clock; their speeds come from the slots, so they agree.
**Not run on a client.** Everything was run on an offline host only.

## Persistence

`GearDoorSaveable` on the workshop root (`SaveablePolicy.EnsureWorldInteractables`: an `IPersistentEntity` with a
`GearDoor` below it): `slots` by name (`Peg_05`, `Pile_09`) → item id, `""` for an emptied pile spot. Null at the
authored start. A gear in someone's hotbar or dropped in the sand is the item system's to save. Verified 2026-10-11
in Play through `SaveableEntity.Capture`/`Restore` (10/10 slots back, the gate open again); **not** through a save
file and a reload, since `WorkshopTest` cannot save ([Scenes.md](Scenes.md)).

## Gotchas

- **Edit the builder and the layout, never `Workshop.prefab`.** Rebuild rewrites the gate with the rest.
- **Find a door's leaves by its `leaves` field, not by name**: the LOD pass hangs a `Gate_Door_*_LOD1` under each leaf.
- **Two doors share the building's channel.** Every message carries the door's index; before the vault a slot number
  alone named a slot, and a second door would have answered the first one's messages.
- **The vault's chain of five only fits winding, with the drive and lock low and apart.** With them level and 1.6 m
  apart no chain longer than three fitted inside `FACE_R` 1.8; at (-1.0, 1.72) and (0.94, 2.14) with `WANDER` 1.7 the
  first seed finds a unique five (found by sweeping drive/lock spots in a scratch script, 2026-10-12). Only XS, S and M
  fit the face. If the door is remodelled, measure the face and the centre again (`Vault.CENTRE`, `FACE_R`) and re-run it.
- **The vault's loose cogs lie inside `Vault.FLOOR`**, a polygon of clear floor in front of the door (out, along)
  measured off `workshop.blend` by ray casts: flat within 3 cm, 2.4 m clear above, the portal posts, threshold, cog bench
  and rocks left out. **Blender's "along" is the player's LEFT** (right-handed, Z up): measured there, the polygon has to
  be mirrored for Unity, or the cogs land in the bench. They ground from 0.5 m above (`groundProbeHeight`), under the
  tunnel's overhangs.
- **`gear_fixed_s/m.fbx` shipped 2.3 and 4.6 m off their origin until 2026-10-12**: `Coll_Gear_Fixed_S/M` in
  `gear_kit.blend` had `instance_offset` (0, 0, 0) while their parts stand at the kit sheet's S and M spots, and
  `export_collections` moves a collection home by that offset. The vault's drive and lock were drawn metres off the
  door. Fixed in the .blend; a new gear collection needs its offset on its seat.
- **A seat made under a leaf inherits its scale 100** (it is an FBX node): pegs and gears came out 300 m across.
  `OnLeaf` makes each at metre scale and parents it keeping that.
- **The shut leaves leave a 0.7 m gap at the seam** (each leaf's mesh starts 0.35 m outside its hinge). No peg may
  stand within 0.6 m of it (`SEAM_CLEAR` in the generator); the builder stops with `no gate leaf stands at u = ...`.
- **The leaves swing outward over the ground, 14.6 m long.** The pile lies out of both sweeps; a gear in a sweep
  would be cut through. `ThePileLiesOutOfBothLeavesSweep` measures it off the built prefab.
- **A NetworkObject added by script is saved with another prefab's hash** (three of the six gears shared one), and
  its `prefabId` empty. `StampOnDisk` re-saves the asset and reads the file back; Rebuild throws if either is wrong.
- **The gear item prefabs are made once and kept.** Saved again from a new object, a root gets a new file id and
  the network prefab list keeps naming the old one (`RegisteredPrefabs_AllStillExist`: a null entry). Delete a
  prefab to have Rebuild make it again, then Sync Network Prefabs.
- **`ItemWorldPresence.Apply` only completes networking that is there**: add `NetworkObject` and `NetRelay` first.
- **The aim's target is no use on the server**: it resolves to the building's NetworkObject. The peg is found again
  from the aim point.
- **A peg is free, not reachable**: `TryFindPeg` takes the nearest peg to the hit point, so aiming at a seated gear's
  rim can pick a peg behind it. Aim at the peg.
- **Placed on natural ground, the gate is buried.** The lane runs at the main room's floor level while the ground under
  a placed mountain rises: in `Chunk_4_3` the terrain stood 0.2 to 1.6 m over the lane floor and 2 m in front of the
  gate. **Dig The Ground Out Of Placed Workshops** (`WorkshopGroundCutter`, with the workshop's chunk scenes and the next
  chunk open) lowers it 0.25 m under the lane, levels a 30 m apron flush with the lane's mouth and blends back over
  10 m (`SettlementTerrainSculptor.Shape`); then re-bake the world NavMesh. Done for `Chunk_4_3`/`Chunk_5_3` 2026-10-11.
- **The gate's NavMesh obstacles carve** the lane while shut; the ship cannot come in through it until it is solved.
- **Pegs stand on the brass bands' face**, a few centimetres proud of the plate between them.
- **Verified 2026-10-11, offline host, Play in `WorkshopTest`, driven by script through the real paths**
  (`GearDoorGrip.Interact` with the player's Interactor, `GearPegPlacement.CanPlace`/`Place`), on the layout before
  the start gears: a pile gear went into the hotbar; an XL was refused beside a gear; the full chain opened the gate,
  locked the pegged gears and swung both leaves open; `Capture`/`Restore` brought back 10/10 slots. With the three
  start gears (edit mode, the built prefab): they stand on solution pegs 4, 6 and 8, the pile is the six missing,
  the gate starts shut and opens with all nine; green and red ghosts render. 11 EditMode tests green. **Not run:** a real crosshair press, a gear
  carried in the hands (hold pose untuned), a client, a save file and a reload, the world NavMesh bake.
- **The vault's seven-gear layout (2026-10-12) was checked in edit mode only**: Rebuild, the layout tests (unique way
  to the lock, pile = the five), its loose cogs against the floor survey, and preview-scene renders of the built prefab
  (drive and lock on their seats, the five on the floor, the solution drawn on the face). **Not run:** Play, a press,
  a client, a save and reload. **A save made on the old ten-peg vault** restores its `Vault/Peg_NN` names onto
  different pegs of the new face; only dev saves from 2026-10-11 can hold one.

## Extending

- **A different puzzle:** change the constants of the door's class (`Gate`, `Vault`) in `tools/gear_door_layout.py`
  (deterministic: unchanged, it writes the shipped files byte for byte), write the layout with it
  (`gear_door_layout.py [vault] <out>`), Rebuild, and keep `GearDoorLayoutTests` green (it runs for both files).
- **Another door:** give its leaves `spin_axis`/`closed_angle_deg`/`open_angle_deg` and a `gear_door` name in the .blend
  (the export ships any such mesh as a hinge), a face marker, a door class in the generator, and a builder call like
  `AttachVault` with its own `saveId`. A door outside the workshop also needs a saveable root above it.
- **Another gear size:** a row in `GearDoorBuilder.Items` and a collection in `gear_kit_export.py`.
