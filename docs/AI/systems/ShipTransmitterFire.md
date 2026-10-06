---
system: ShipTransmitterFire
layer: items
summary: "The lander's burnt-out transmitter: sparks, catches fire after landing, put out with the extinguisher"
paths:
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/BrokenShipPart.cs
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFire.cs
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFireRules.cs
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFireState.cs
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFireTuning.cs
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFirePhase.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/ShipPartFireSaveable.cs
  - Assets/Game/Scripts/Items/Artifacts/FireExtinguisher
  - Assets/Game/Scripts/Items/Artifacts/SprayerItem.cs
  - Assets/Game/Prefabs/Items/ShipParts/TransmitterHusk.prefab
  - Assets/Game/Scripts/Items/Artifacts/ShipParts/FizzlingHusk.cs
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/FireExtinguisher.prefab
  - Assets/Game/Prefabs/Items/ShipParts/TransmitterSparks.prefab
  - "Assets/Game/Art/Models/_Source~/models/gear/fire_extinguisher_export.py"
symptoms:
  - "the burnt-out transmitter is back in the ship's cradle after I took it off and reloaded"
  - "I cannot fit the working transmitter: the cradle in the ship will not take it"
  - "the ship says airworthy with the burnt-out transmitter still in the cradle"
  - "the transmitter fire lights again after it was put out and the world reloaded"
  - "the fire in the ship is only on the host's screen, or a client still sees it after it is out"
  - "the extinguisher sprays at the fire and nothing happens"
  - "the extinguisher never runs out, or comes back full after a reload"
  - "AddComponent returns null for a MonoBehaviour defined in an editor test file"
  - "the transmitter is on fire but there are no flames, on the host or a client"
  - "the burnt-out transmitter can be pulled out before it has ever caught fire"
  - "the transmitter never catches fire"
reads_with: [PlayerShip, SatelliteDish, ShipSignal, Flamethrower, Backpack, Persistence, Multiplayer]
updated: 2026-10-06
---

# Ship transmitter fire

The lander arrives with its long-range transmitter burnt out. The dead unit sits in a wall cradle in
the aft room, sparking and jammed in its socket. A while after the crew get the oxygen plant back in
its mount and running ([Oxygen.md](Oxygen.md)) it catches fire; a fire extinguisher hangs in a bracket
a couple of metres away. Put the fire out and the unit comes free: pull it and it pops onto the floor,
fizzles and is gone. The socket is then free for the working transmitter from the satellite dish
([SatelliteDish.md](SatelliteDish.md)).

**Scope:** the broken unit, its fire and the extinguisher. The socket and the module rule are
[PlayerShip.md](PlayerShip.md)'s; the flames and the harm are the flamethrower's ([Flamethrower.md](Flamethrower.md)).

## Model

- **A broken unit is a rack state, not an object.** `ShipPartRack` keeps a second mask beside the installed one:
  `authoredBrokenMask` (bit 11, the transmitter socket, on `PlayerShip.prefab`) and the live `BrokenMask`.
  A broken socket is occupied (`Accepts` refuses everything) but not fitted, so `IsComplete` stays false.
  `ShipPartSocket.SetState(installed, broken)` shows the `brokenUnit` child exactly while it is broken and
  paints no ghost; `ShipPartHighlighter` skips broken sockets. A working part wins if both bits are ever set.
- **Taking it off** is [`BrokenShipPart`](Assets/Game/Scripts/Items/Artifacts/ShipParts/BrokenShipPart.cs) on the unit
  (its `BoxCollider` is the aim target): an `IInteractable` + `IInteractionReadout`. **It comes out only after its
  fire** (`ShipPartFireRules.MayRemove`, phase `Out`): before, "Jammed in its socket" and a refusal sound; while it
  burns, "On fire: put it out first" with a FIRE bar; after, "Needs replacing.  RMB: take it off". The server
  spawns a `TransmitterHusk` at `DropPoint`, then clears the bit. It never goes into an inventory, and nothing seats
  a broken unit again.
- **The husk** ([`FizzlingHusk`](Assets/Game/Scripts/Items/Artifacts/ShipParts/FizzlingHusk.cs), `TransmitterHusk.prefab`,
  the old `BrokenTransmitter` prefab moved and stripped, GUID kept so its network registration carried over): a
  kinematic body the server drops to the highest floor below it in 0.35 s, sparks fading with `SparkShare` (full for a
  third of its life, nothing at the end), despawned after `lifetime` (15 s). The end of its life is a server instant
  replicated for late joiners. It has no persistent marker and a kinematic body, so `SaveablePolicy` never opts it in:
  **a save during its 15 s simply does not have it** (the socket is saved empty), and it never returns.
- **The fire** is [`ShipPartFire`](Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFire.cs) on the ship root.
  Its rules are pure ([`ShipPartFireRules`](Assets/Game/Scripts/Items/Artifacts/ShipParts/ShipPartFireRules.cs)):
  Dormant → Burning → Out, Out is permanent. The ignition clock runs only while the fire is **armed**
  (`ShipPartFire.Armed`): the crew are down, the unit is seated, and the ship's `OxygenPlantMount` is `Running` (in
  its mount, its cracks soldered and powered). At `igniteDelay` (**3 s**, 2026-10-06; was 75) it catches at `startStrength` (0.15) and grows to 1 over
  `growSeconds` (45 s). A douse knocks strength off; at 0 it is out for good. Because the unit cannot leave before
  its fire, every world gets this fire exactly once.
- **Not a new fire system.** The flames are `Resources/Effects/BodyFire` (the burning-body shell) under the unit's
  `FirePoint`, held world-upright, scaled by strength (0.45 → 1.5 m wide, using `BurningVisual.PrefabWidth`), and
  started on the edge through `FlameLayers` exactly as a burning body starts them. The harm is
  `Ignition.Light`, the ground-fire patch's announcement, every 0.5 s on whoever stands within 0.9 → 2 m
  (`BurningStatus` bills it). The ship's own hierarchy is skipped: the cabin fire never sets the hull alight.
  Sparks are `TransmitterSparks.prefab` (stretched embers, 6/s with probabilistic crackle bursts, world collision);
  burning multiplies their captured rate by 4. The loop sound is a stand-in (`WeaponBallLightningChargeLoop`).
- **The extinguisher** is [`FireExtinguisherArtifact`](Assets/Game/Scripts/Items/Artifacts/FireExtinguisher/FireExtinguisherArtifact.cs),
  a [`SprayerItem`](Assets/Game/Scripts/Items/Artifacts/SprayerItem.cs). The base was lifted out of the cryo sprayer:
  hold stream, valve, timeout, `SupplyReservoir` tank and `CryoSprayerNozzle` plume are shared, the verb (`Land`) is
  each gun's own. Range 5 m, cone 20° (widened by the fire's reach), douse 0.4/s, tank 0.17/s (about 6 s), no refill.
  On the authority it calls `ShipPartFire.Douse` for each burning fire it covers in sight.
- **Where:** cradle on the port recess between the map projector and the oxygen plant, device centre (-2.77, 5.65,
  -0.90) ship-local, 2.67 m above the deck. Since the 2026-10-06 1.5x resize (cradle 2.19 x 1.0 m, device 1.32 m,
  plate 0.0375 thick) the part's outboard face stands 0.17 m clear of the empty oxygen mount and of `PlantDock`'s aim
  volume, both at x -2.34, and the standing terminal is over 6 m away. Bracket on the repair station's front at (-1.14, 3.72, -1.75), 2.4 m from
  the fire (outside its full 2 m reach), a `WallInventory` whose one shelf face accepts only the extinguisher.

## Key types

| Type | Role |
| --- | --- |
| `ShipPartRack` (broken mask) | `IsBroken`, `TryRemoveBroken`, `RestoreMasks`, `AuthoredBrokenMask`; a second `NetworkVariable<int>` |
| `BrokenShipPart` | Readout, the take request (`NetMsg.ShipPartTakeBroken` 129), server `TakeFor` |
| `ShipPartFire` | `NetworkVariable` phase + strength, server clock, flames, sparks, harm; `Douse`, `Restore`, `Owns(collider)` |
| `ShipPartFireRules` / `State` / `Tuning` / `Phase` | Pure rules, the saved state, the serialized tunables, the phase enum |
| `ShipPartFireSaveable` | Key `partFire`: `{ phase, strength, armedSeconds }`, null while dormant with no clock |
| `SprayerItem` / `FireExtinguisherArtifact` | Shared sprayer plumbing / the dousing verb |

## Flows

1. **Crash.** The unit sparks, jammed in its socket. The oxygen plant is out of the ship ([Oxygen.md](Oxygen.md)).
2. **The plant is carried home, soldered and powered.** The fire is armed; 3 s later it **ignites**: flames, brighter sparks,
   the loop; people in reach catch.
3. **Grab the extinguisher** off the bracket (the wall's crosshair take), hold Use on the flames.
4. **Out.** Strength 0 → `Out`. RMB pulls the unit: the husk pops onto the floor and fizzles out in 15 s. The socket
   is empty and ghosts for a held transmitter.
5. **Fit** the working transmitter from the dish with the stock `ShipPartItem` use. The terminal comes out of
   static, the COMMS tab appears and the ship hears the signal ([ShipSignal.md](ShipSignal.md)); the objective
   `fit-transmitter` walks the crew through steps 2-5 and hands on to `answer-signal`.

## Multiplayer

| Path | Carrier | Authority |
| --- | --- | --- |
| Broken unit seated | `ShipPartRack.networkBroken` | Server; read on spawn by late joiners |
| Take | `ShipPartTakeBroken` on the ship's channel, `A` = socket index | Server re-checks broken + burned and out; spawns the husk |
| Husk | `TransmitterHusk` NetworkObject (`NetworkTransform`), end-of-life instant | Server drops and despawns it |
| Fire phase/strength | `ShipPartFire` NetworkVariables | Server steps the rules and applies douses |
| Harm | `Ignition.Light` from every machine | Billed once by `StatusReceiver` where the body simulates |
| Spray | The item hold stream (`SprayerItem`) | Douse on the authority only |
| Extinguisher on the bracket | `WallInventoryNetwork` | Server |

## Persistence

| State | Saver | Key |
| --- | --- | --- |
| Broken unit seated | `ShipPartsSaveable` (`broken` field, appended) | `shipparts` |
| Fire | `ShipPartFireSaveable` (auto-attached by `SaveablePolicy` beside `ShipPartFire`) | `partFire` |
| Extinguisher on its bracket | `WallInventorySaveable`, `recordName` `extinguisherBracket` | `wallInventory.extinguisherBracket` |
| Extinguisher charge | `SupplyReservoir` through the slot's item state | (the slot's bag) |

A new world starts with the unit seated and the clock at 0. **A save from before this feature** has no `broken`
field: it reads as the authored mask, so the burnt-out unit is seated (unless that save already has a
transmitter fitted, where there is nothing to burn), and with no `partFire` record its clock starts once the
oxygen plant is running — for a world already past its crash the plant counts as in its mount, so the 3 s start
on load if it is powered, or when its cell goes in.
A fire saved burning reloads burning at its strength; one put out reloads out and never relights.

## Gotchas

- **The fire follows the plant almost at once (2026-10-06, the user's call).** `igniteDelay` went 75 → 3 s so the plant
  coming online and the overloaded unit catching read as cause and effect; it drops the rest beat GDC-L1-LEVEL-0003 argues
  for after a peak. The field kept its old value on the prefab until `OxygenPlantRecoveryAuthoring` wrote it
  (INVARIANTS: a serialized field keeps its old value); `BrokenTransmitterTests.TheFireCatchesAlmostAsSoonAsThePlantComesOnline`
  reads the asset. A world loaded with the plant already running and the unit unburnt ignites about 3 s after the load
  (sooner if its saved clock had already passed 3 s).

- **`BodyFire`'s emitters do not play on awake.** A burning body starts them through `FlameLayers.SetEmitting`;
  the first version of this fire only instantiated the prefab and made it active, and burned with NO flames on any
  machine, which looked like a networking or culling fault. Start them on the edge (`Play` on a playing system
  restarts it), and hold the flames world-upright: the socket faces out of the cabin wall, the flame layers simulate
  in local space, and a fire that took the socket's rotation burns sideways into the room.
  `BrokenTransmitterTests.ABurningUnitShowsUprightFlames`.
- **Testing a fire in EditMode: drive `PresentFlames`, not `Present`.** `Present` also starts the burn loop, and
  FMOD has no runtime outside play mode.
- **`PlayerShipBuilder` is gone; these pieces are hand-authored on `PlayerShip.prefab`** (`Part_Transmitter_A` with
  `Cradle`, `Model`, `BrokenUnit`; `ExtinguisherBracket` as the LAST child so the gear wall keeps `WallIndex` 0).
- **The bracket's face is horizontal on purpose.** A display copy keeps its own up along the face's normal, so an
  upright extinguisher on a vertical face would stick straight out of the wall. The shelf face (3 x 6 cells,
  `displayScale` 0.776 so the copy is its true 0.684 m) stands it upright on the bracket's foot.
- **The aim line was measured, not assumed.** From a standing eye in the aisle the first collider hit is
  `BrokenUnit` and the terminal is 3.4 m or more away; head-on the line clears the map projector and tank dock by
  0.99 m, from the forward aisle it passes 0.31 m beside the tank dock. From behind the repair station it hits the
  station, which is not interactable.
- **A MonoBehaviour test double must be NESTED.** `AddComponent` returns null for a top-level `MonoBehaviour` in an
  editor folder, because its MonoScript is classed as an editor script; a nested type has no MonoScript. The shared
  hotbar double is `TestDoubles.HotbarBehaviour` for that reason.
- **`SprayerItem` keeps the cryo sprayer's serialized field names** (`tank`, `nozzle`, `holdTimeout`,
  `sweepsPerSecond`), so the prefab kept its values across the extraction. Its `Awake`/`Update`/`OnDisable`/`OnValidate`
  are `protected virtual`: `CryoFreezeTests` invokes them by reflection on the derived type, which cannot see a
  base class's private methods.

## Extending

- **Retune the beat** on `ShipPartFire.tuning` (delay, start, growth) and the extinguisher's `dousePerSecond` and
  tank drain. Spraying must out-pace growth (0.4/s against 1/45 per second).
- **Another broken module:** set its bit in `authoredBrokenMask`, give its socket a `brokenUnit` with a
  `BrokenShipPart`, and add a `ShipPartFire` for it if it should burn (one per socket).
- **A real fire sound** replaces `burnLoop` on the prefab; the bank has no fire loop yet.
