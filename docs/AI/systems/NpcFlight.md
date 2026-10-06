---
system: NpcFlight
layer: vehicles
summary: "Sky nomads flying the NPC ornithopter: NpcFlightModule decides, NpcAviator flies and lands; never saved"
paths:
  - Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/
  - Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs
  - Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs
  - Assets/Game/Scripts/agents/entity/IAirborneCarrier.cs
  - Assets/Game/Scripts/agents/entity/LootAwaitingGround.cs
  - Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs
  - Assets/Game/Prefabs/Agents/Vehicles/Aircraft/NpcOrnithopter.prefab
  - Assets/Game/Editor/Tests/NpcAviatorTests.cs
  - Assets/Game/Editor/Tests/NpcOrnithopterPrefabTests.cs
  - Assets/Game/Editor/Tests/NpcOrnithopterFlightTests.cs
  - Assets/Game/Editor/Tests/NpcFlightModuleTests.cs
symptoms:
  - "an EditMode test that simulates the NPC craft into a rock never crashes it"
  - "a Sky nomad shot down in flight drops its wing pack in mid-air, far from where its body lands"
  - "a Sky nomad walks a two-kilometre leg instead of flying"
  - "Sky nomads pile up on the ground under the city"
  - "a Sky nomad saved mid-flight is back on the ground where it took off after loading"
  - "a Sky nomad killed in flight is reloaded still wearing its wing pack, which never drops"
reads_with: [Ornithopter, SkyTribe, Vehicles, Persistence, AgentSystem]
updated: 2026-10-06
---

# NpcFlight

An NPC flying the wing pack's ornithopter: seated in the cradle, flown to a goal on a simple motor and set down near it — none of the player's energy model.
**Scope:** [`Vehicles/Ornithopter/NpcFlight/`](Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight) (plan + wings), [NpcAviator.cs](Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs), [NpcFlightModule.cs](Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs) (when a nomad flies), [NpcOrnithopterBuilder.cs](Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs), [NpcOrnithopter.prefab](Assets/Game/Prefabs/Agents/Vehicles/Aircraft/NpcOrnithopter.prefab).
**Related:** [Ornithopter.md](Ornithopter.md) · [Vehicles.md](Vehicles.md) (`VesselSeats`) · [SkyTribe.md](SkyTribe.md) · [Persistence.md](Persistence.md)

## Model

- NPC pilots ride `NpcOrnithopter` — a variant of the player's craft with the player's motor, mount and savers removed, flown by `FlyingRigidbodyMotor` + `NpcAviator`/`NpcFlightPlan`; the player's motor and energy model are untouched (user decision 2026-10-06). No `MountModule`, so no player can take an NPC's craft.
- The plan (`NpcFlightPlan`) is pure: position, ground height, landing point and cruise height in, a target point and a speed fraction out. `NpcAviator` turns each step into a `MoveIntent`; `FlyingRigidbodyMotor` (banking and pitch-along-path on) flies it. The wings are read off the motion by `NpcOrnithopterWings`.
- Seating is `VesselSeats`' (one marker, `SEAT_Cradle`): feet off, brain on, so a pilot can shoot from the cradle; a dead pilot is dropped straight down.

## Key types

| Type | File | Role |
|---|---|---|
| `NpcFlightPlan` / `NpcFlightSettings` | [NpcFlight/NpcFlightPlan.cs](Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcFlightPlan.cs) | Pure NPC flight steering, not the energy model. Phases: EnRoute (climb or sink to cruise over the ground), Approach (straight, flare, or spiral when too steep), Wreck (spiral at `WreckSlope`). Outputs a point plus a speed fraction for `FlyingRigidbodyMotor`. |
| `NpcOrnithopterWings` / `NpcWings` (+ `NpcWingSettings`, `NpcWingState`) | [NpcFlight/NpcOrnithopterWings.cs](Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcOrnithopterWings.cs) + [NpcWings.cs](Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcWings.cs) | The measured `IOrnithopterFlightState` for NPC craft: velocity, turn rate and roll read off the transform each `Update` on every machine (nothing sent) — measured across every frame since the pose last changed (a dynamic body moves only on physics steps, so per-frame differencing reads zero then double; past `maxSampleGap` it reads parked) and low-passed by `velocitySmoothingSeconds`; then pure `NpcWings.Step` derives spread (shut when parked, `WreckSpread` when sinking past `WreckSinkSpeed`), beat effort (glide + climb) and phase. `PitchInput` 0, never stalled. |
| `NpcFlightModule` | [agents/Modules/Movement/NpcFlightModule.cs](Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs) | On the nomad, priority Override, claims only the deploy frame. Decides **when** to fly and launches: spawns `craftPrefab` with `CraftDeployment.LaunchPosition` (seat read off the prefab), `NpcAviator.Fly`, `SaveScopeHold` on the nomad. `InFlight`, `OnSortie`, `Aviator`. Tunables: `minFlightDistance` 250, `relaunchCooldown` 20, `cruiseHeight` 60, `landingSampleDistance` 6, `takeoffLift` 3, `minLaunchClearance` 12, `sortieTask`, `sortieChance` 0.05, `sortieLifetime` 300, `fallDeploySeconds` 0.5. |
| `NpcAviator` | [NpcAviation/NpcAviator.cs](Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs) | `BehaviourModuleBase` (priority Override) on the NPC craft. `Fly(pilot, destination, cruiseHeight, landingSampleDistance)` seats the pilot in seat 0 (`VesselSeats`, `SEAT_Cradle`) and begins the plan; `Tick` turns each `NpcFlightStep` into a `MoveIntent` for `FlyingRigidbodyMotor`. Picks a `LandingSiteFinder` site near the goal (`CraftLanding`: rings 3–14 m) once the ground has streamed in, else lands at the goal projected onto the ground. Owns the endings (touchdown/crash, pilot death, pilot taken away). An `IStowsTorsoGear`: the seated pilot's pack folds away on every machine. `Pilot`, `HasSite`, `Wrecked`, `Goal`, `LandingPoint`, `Phase`, `PilotReleased(npc, alive)`. |
| `NpcOrnithopterBuilder` | [Editor/Vehicles/NpcOrnithopterBuilder.cs](Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs) | **Tools ▸ SpaceGame ▸ Vehicles ▸ Build NPC Ornithopter**: writes `NpcOrnithopter.prefab` as a variant of `DuneOrnithopter` (savers, `SaveableEntity`, `MountNetworkSync`, `SteerModule`, `MountModule`, `OrnithopterFlightMotor` removed; `FlyingRigidbodyMotor` at `CruiseSpeed` 25 / `Acceleration` 8 with banking, `NpcOrnithopterWings`, one-seat `VesselSeats`, `NpcAviator` added), registers it, then `Verify()` reads it back and throws on anything that did not stick. Never writes `DuneOrnithopter.prefab`. |

## Flows

0. **When a nomad flies** (`NpcFlightModule.Tick`, where the nomad is simulated). Wing pack worn (torso item's prefab is a `WingPackItem`) → **falling** (`FlightLaunch.IsAirborne` for `fallDeploySeconds` running — a hop is not a fall) deploys to land at once, fight or not, toward its goal or straight down; otherwise no target (D2: a nomad in a fight fights on foot) → its goal, or for a formation follower its leader's (`FormationModule.LeaderOf`; D5: each flies alone to the shared goal), is `minFlightDistance` away flat → room to launch (airborne, or `PhysicsGroundProbe.IsClear` of `minLaunchClearance` above `takeoffLift`; no ledge needed, the NPC craft just climbs) → spawn, `SaveScopeHold.Hold`, `Fly`. Refused: retry after `retryInterval`. `PilotReleased` → `relaunchCooldown`, then `GoalTravelModule` walks the last metres. **Sortie:** every `sortieCheckInterval`, a resident with no goal of its own standing inside an airborne Home site (the Sky City) takes a `sortieChance` roll and resolves `sortieTask` (`NpcTaskPlanner.ResolveDestination`, ground sites only); only a launch that succeeds sets that as its goal and `OnSortie` — a refused one leaves it home with no goal.
1. **NPC flight** (`NpcAviator`, server only). `Fly` → `VesselSeats.Seat(0)` → plan steps (climb or sink to cruise height, cruise, approach or spiral down, flare at `FlareSpeed`) → **touchdown** (plan says down) or **crash** (`OnCollisionEnter` → `OnContact` past `launchGraceSeconds`, only with the world: a `crashMask` layer and no body or a kinematic one — never a person or creature (health without `VesselSeats`), a projectile, ragdoll, other craft or the dead pilot's own body; priced on the velocity of the tick before) → price the arrival (`OrnithopterCrash.ImpactDamage` on the closing speed), `Unseat(0, ground, landingSampleDistance)` onto NavMesh (the one sample; a failed unseat logs an error, keeps the pilot aboard and retries), `NetDamage`, `PilotReleased(npc, alive)` → `CraftDeployment.Retire`. **The pilot dies:** `VesselSeats` has already dropped the body; the plan spirals the craft in as a wreck (`Wrecked`, Phase `Wreck`) and it is retired when it reaches the ground, or once it has fallen `noGroundDepth` below where the pilot died with still no ground under it. **The pilot is taken away** (its group folded): retired at once. Measured on the real prefab (`NpcOrnithopterFlightTests`, 2026-10-06): off the ground 600 m → down 2.7 m from the goal in 27.7 s; off 280 m, 2000 m → 2.7 m in 84.3 s; both touching down at 1.3 m/s, unhurt.

## Multiplayer

- **The NPC craft is server-owned:** `NpcAviator` decides only where `Network.Simulates`; `NetAuthority` disables `FlyingRigidbodyMotor` on clients (`SimulationDrivers` collects motors, never the `AgentController`, which gates its own module ticks through `AgentAuthority`), so clients follow the replicated transform; the wings are derived per machine by `NpcOrnithopterWings` off that transform. Seating, parenting and the despawn replicate through `NpcSeating`/`WorldService`. No message. Its own registered network prefab (distinct hash from the player's craft).

## Persistence

- **The NPC craft is never saved:** `SaveablePolicy.NeedsSaving` refuses any root carrying `NpcAviator`, and the builder strips every saver and the `SaveableEntity` it inherits. The flier's group record (or nothing, for a sortie) is what comes back after a load.
- **Neither is the flier (D4).** `NpcFlightModule` holds a world-scope nomad for the flight (`SaveScopeHold` → `WorldSaveStore.Withhold`): its record is **dropped** and no capture writes another, and an authored one is tombstoned too, so a mid-flight save reloads neither at its take-off spot nor at its authored one. `PilotReleased` gives it back (`Return`, tombstone lifted); a group member is `External` already and never touched (its group record would load it a second time). A pilot killed aloft is given back only once `LootAwaitingGround.WhenDropped` says its loot is down; a sortie flier never is. Tests: `NpcFlightModuleTests` and `SaveScopeHoldTests`, all on what the real `WorldSaveStore.Dehydrate` captures.

## Gotchas

- **An unsaved prefab is not enough to keep a runtime spawn out of the save.** `WorldService.Spawn` runs `SaveablePolicy.EnsureSpawned` on every runtime spawn, and the NPC craft's non-kinematic body and `AgentController` each qualify on their own — the `NpcAviator` clause in `NeedsSaving` is what stops a craft caught mid-flight by a save being wired at spawn and restored with nobody aboard.
- **`PilotReleased` fires on every ending that had a pilot aboard, including the craft being destroyed from outside** (`OnDestroy`, off the pilot cached at `Fly` — `VesselSeats.OnDestroy` may already have emptied the seat). `NpcFlightModule` releases its `SaveScopeHold` on it, so an ending that skips the event leaks the hold. Fired at most once per flight. A listener may be handed a destroyed pilot (the craft was destroyed with the pilot in it, `alive` false): compare with `==`, never dereference it.
- **A pilot killed in flight drops its loot where its body lands, not where it died (D8).** `NpcAviator` is an [`IAirborneCarrier`](Assets/Game/Scripts/agents/entity/IAirborneCarrier.cs); `EntityLootTable.Drop`, seeing its body still seated under one, hands the whole drop (bag, worn pack, rolls) to [`LootAwaitingGround`](Assets/Game/Scripts/agents/entity/LootAwaitingGround.cs) on the corpse, which drops it once the body's root is within `landedHeight` of solid ground (dynamic bodies, like the wreck, are not ground; a kinematic deck is) and its ragdoll `IsSettled`, then removes itself. **`IsSettled`, never `IsAtRest`:** a corpse on the drifting Sky City deck or a dune slope never reads at rest, and `IsAtRest` has no ceiling, so the pack was held until `Remains` took the body, which it never does while a player is watching: the looter held it hostage. Despawned in the fall, it drops at the last ground seen below, never at altitude; never while quitting or during a network shutdown. "Seated aloft" is a latch (`EntityLootTable.OnTransformParentChanged`, kept through a death), not a parent read at `OnDeath`, so it does not depend on `VesselSeats`' let-go handler running after the loot table's. The waiter is not saved, so a corpse saved during the fall would reload still wearing the pack and never drop it (`IsRestoring`): `NpcFlightModule` therefore keeps the corpse out of the save until the waiter has dropped (`WhenDropped`) — releasing at `PilotReleased` is too early. `WhenDropped` does not depend on `OnDeath` handler order: asked before the loot table has heard the death, it reads the table's latch (`DropsOnceDown`) and makes the waiter for `Begin` to fill. A save in those seconds loses corpse and pack together, as D4 loses any flier. Tests: `EntityBodyEquipmentPersistenceTests`, `NpcFlightModuleTests`.
- **A seated flier runs its side-effect modules only** (`AgentController.RidesAsPassenger`), so `NpcFlightModule` stops ticking the moment the pilot is seated: flight-time logic lives on the craft (`NpcAviator`), never on the nomad.
- **A sortie flier is unsaved for good and taken away unseen.** It stays disowned after landing and `NpcSpawn.Remove`s itself once `sortieLifetime` is up and no player is within `sortieUnseenDistance` (`UnseenRemoval.IsDue`) — otherwise the city's refills pile people up on the ground beneath it.
- **Flipping `SaveScope` alone does not take an entity out of the save.** The world store keeps a runtime record whose id is still live (`DropVanishedRuntime`), so a capture that merely skipped the flier left the record from before take-off, and the reload put it back there. `WorldSaveStore.Withhold` drops the record; that is also why the hold no longer calls `DisownToExternal` (refused outside play mode anyway).
- **The stand-in ground depth is for the plan only.** With no ground under the craft, `Tick` feeds the plan `position.y - noGroundDepth`; a landing there stands the pilot under the cradle (`position - TouchdownHeight`), never at that depth.
- **`OnCollisionEnter` is never sent in Edit Mode, even under `PhysicsScene.Simulate`,** so the contact rules are tested through `OnContact` with real colliders, not by simulating a collision.
- **A wreck ends as Phase `Landed` with `Touchdown`, exactly like a landing** — `NpcFlightPlan` does not tell them apart, so `NpcAviator` tracks `Wrecked` itself and releases nobody at a wreck's touchdown.
- **The NPC craft's crash grace and site checks run on `Time.time`.** EditMode tests never advance it, so in `NpcOrnithopterFlightTests` collisions are always inside the grace and the site is looked for once; the real craft's flight there never touches anything (its colliders live in a preview scene, the ground in the default one).
- **An NPC craft cannot land until it has flown.** `NpcFlightPlan` latches `airborne` once it is `TakeOffClearance` above the touchdown band; a craft resting at its start is already inside the band, so without the latch EnRoute flips to Approach and "lands" on step one (ground hop, or a Sky City platform launch). `Begin()` clears the latch.

## Extending

1. **Retune NPC flight:** `NpcFlightSettings` on the prefab's `NpcAviator` (slopes, flare, spiral, touchdown band) and the builder's `CruiseSpeed`/`Acceleration` consts — `NpcFlightPlanTests` fly the plan against exactly those two, and `NpcOrnithopterFlightTests` fly the real prefab; keep both green.
2. **Change the craft:** edit `NpcOrnithopterBuilder` and re-run **Tools ▸ SpaceGame ▸ Vehicles ▸ Build NPC Ornithopter** — never hand-edit the variant, and never touch `DuneOrnithopter.prefab` for NPC needs. Model and rig edits to the player's craft flow into the variant.
