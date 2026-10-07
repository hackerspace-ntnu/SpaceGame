# Strider monowheels — scouts and war-party convoys

**Date:** 2026-09-24 · **Branch:** `Feat/create-factions` · **Status:** design approved, plan next
**Parent:** [Striders walking city](2026-09-23-striders-walking-city-design.md) (sub-project 1). This is
**sub-project 2**; it runs after the walking-city plan and changes that plan's war-party Rider role.

## 1. What we are building

The Striders' fast machines: rim-driven **monowheels** (the models exported under
`Assets/Game/Art/Models/Vehicles/Monowheel/`). Players and NPCs both drive them, through one shared
physics motor. The walking city keeps **8 scouts** on monowheels: 6 ride with the column as a fast
escort ring while **2 at a time** sweep a loop around the city, then trade places. Strider **war
parties become monowheel convoys**; the two-wheeled doubles carry gunners.

### Decisions (user, 2026-09-24)

| Question | Decision |
| --- | --- |
| Who drives | **Players and NPCs.** Players mount through `MountModule` + `SteerModule`, NPC riders through `NpcPassenger` — the robot-horse pattern. |
| Motor | **One shared physics motor** (`MonowheelMotor`, Rigidbody): momentum, lean, slip on sand (ring spin and particles belong to the separate `MonowheelPresentation`). NPCs feed it by following a NavMesh path; no motor swap. |
| Scouts at home | **Ride with the column** in formation as an escort ring. |
| Scouting | **8 scouts, 2 out at a time**, sweeping a loop around the city, then the next pair. |
| War parties | **All monowheels.** Singles carry a rider; doubles carry a driver and up to 3 gunners. Crabs leave war parties and stay as the city's flank escort. |
| Double seating | **Driver + rear passenger + one gunner inside each wheel** (the `.blend`'s side seats) — 4 aboard. |
| Seating animation | The seated pose is the existing one for now; a proper seated/gunner animation is backlog **ANIM-04**. |
| Out of scope | Player gunners (a second player in a gunner seat), a city-wide alarm from a scout's sighting, killable city walkers, real cargo on the haulers. |

## 2. The monowheel

### 2.1 `MonowheelMotor`

A new `IMovementMotor` on a non-kinematic Rigidbody (`Assets/Game/Scripts/Vehicles/Monowheel/`).

- **Drive model:** throttle accelerates toward `topSpeed` along the heading; steering changes the
  heading at a rate that falls with speed (`turnRate` at a crawl, `turnRateAtTop` at full speed); the
  body **leans into the turn** (roll proportional to lateral acceleration, capped `maxLean`); sideways
  velocity is bled off by `lateralGrip`, low enough on sand to slide a little. Ground contact by a
  probe under the ring; gravity does the rest. All of these are serialized.
- **Presentation (every machine) is not ours.** A parallel piece of work
  ([monowheel presentation spec](2026-09-24-monowheel-presentation-design.md)) owns the ring spin
  (`Bone_Ring`/`Bone_RingL`/`Bone_RingR`) and the sand/dust/smoke particles in
  `MonowheelPresentation`, reading only the root transform's motion, on art prefabs
  `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<Variant>.prefab`. **The motor never spins the
  rings.** Our prefabs nest that art prefab as their `Body` child. **Chassis lean is ours**
  (confirmed with that session 2026-09-24: its component only spins rings and drives particles): a
  roll on the `Body` child only — never on the physics root. The rings spin about their own bones and
  the emitters sit under the art prefab, so both tilt with the lean. **Never yaw the `Body` child**:
  `MonowheelPresentation` reads speed along the root/Body forward axis (+Z), so the art's forward must
  stay the driving direction. It treats implied speed above its `maxPlausibleSpeed` (50 m/s) as a snap.
- **Player input:** `SteerModule` → the motor's rider channel (throttle, steer, brake), exactly how the
  robot horse's legged driver takes a rider.
- **NPC input:** `MoveIntent`s (MoveTo / StopAndFace / Idle) → the motor follows a
  `NavMesh.CalculatePath` corner list the way `LeggedDriver.SteerAlongPath` does: steer at the next
  corner, ease speed for the turn angle ahead, stop at `StopDistance`. When a player drives, it may
  leave the NavMesh freely; an NPC driver re-paths from wherever it is.
- **Speeds:** singles `topSpeed` ≈ 20 m/s, doubles ≈ 16 m/s; NPC convoy/sweep cruise ≈ 14–16 m/s
  (tunables; the plan's first task measures what the models and ground handle).
- **Constraint carried from sub-project 1:** a formation follower can only keep up with a leader that
  cruises below the follower's top speed — cruise must stay under `topSpeed` for convoys.

### 2.2 Occupants

- **Driver:** `NpcPassenger` in the rider socket (`Socket_Rider_*`) — spawn on start, dismount when
  hurt, stamped into the group (`GroupMembership.StampRider`). A player uses the same saddle through
  `MountModule`.
- **Gunners (doubles only):** `VesselSeats` with 3 seats — rear passenger seat and the two side seats
  inside the wheels. A new **`MountedGunners`** component (server) spawns a Strider gunner into each
  seat when the monowheel spawns (as `NpcPassenger` spawns its rider), stamps them into the group, and
  lets `VesselSeats` handle a gunner's death (seat freed). Gunners keep their own targeting and fire
  from their seats. Gunner seats are NPC-only in this pass.
- **Teardown order:** occupants despawn before the monowheel (seated NPCs are parented under its
  `NetworkObject`) — the same rule as the city houses, via the same reverse-order despawn.

### 2.3 Prefabs

One builder (`StriderMonowheelBuilder`) makes, around the five `Monowheel_<Variant>` art prefabs (nested as `Body`):
`StriderMonowheel_Runner`, `_Hauler`, `_Patched` (single, 1 rider) and `StriderMonowheel_Double`,
`_DoubleWide` (driver + 3 gunners). Each: Rigidbody + `MonowheelMotor` + `AgentController` +
`MountModule` + `SteerModule` + `NpcPassenger` (rider = a Strider nomad prefab, faction baked) +
health + `EntityFaction` = Striders + `FormationModule` + `GoalTravelModule` + netcode stack + savers;
doubles add `VesselSeats` + `ChairPose` + `MountedGunners`. The mount itself never attacks.
A riderless monowheel (its rider killed or dismounted) is a vehicle a player can take.

## 3. Scouts at the city

- **8 scout monowheels** join the `strider-city` template (fixed prefabs — the three singles — each
  with a Strider rider), listed after the houses so formation order stays sensible. They hold slots in
  the column's formation as the fast escort ring.
- **`ScoutRota`** (server, on the lead house): keeps exactly **2 scouts out**. It picks the pair home
  longest, deactivates their `FormationModule`, and gives each a sweep — a loop of waypoints on a ring
  `sweepRadius` (400–800 m) around the city, written into `AgentGoal` one after another
  (`GoalTravelModule` drives). On completing the loop, it re-activates their formation (they ride
  back to their slots) and sends the next pair. A scout that is fighting finishes its fight first; a
  dead scout drops out of the rota.
- **Sightings:** out scouts keep their normal perception and `AlertBroadcaster` — allies within the
  usual alert radius are warned. No long-range city alarm in this pass.
- **Persistence:** scouts are group members and fold/restore with the city. The rota's "who is out"
  is not saved: after a reload every scout is back in formation and the rota starts a fresh sweep.
  Nothing new in the save.

## 4. War parties

- The roster's **Rider** role becomes the monowheels: the three singles (weight 3 each) and the two
  doubles (weight 1 each) — roughly one draw in four is a double. The crab outrider leaves the Rider
  role (it stays in the city template as a fixed prefab).
- **Tiers:** 0 = 2 Riders, 1 = 3 Riders, 2 = 5 Riders. This approximates "2 singles / 2 singles + a
  double / 3 singles + 2 doubles" by weighted draw rather than exact counts — exact counts would need
  a new roster role, not worth it now.
- Convoys travel at the monowheel cruise speed when spawned; riders and gunners dismount to fight as
  every rider does (gunners on a hurt double stay seated and fire).

## 5. Multiplayer

Server-authoritative Rigidbody with `NetworkTransform` while an NPC drives; `MountModule`'s existing
ownership handoff gives a player driver authority over the motor, as on every mount. Occupants seat
through `NpcSeating` under the monowheel's `NetworkObject` (replicated parenting, no new message);
seated poses are derived per machine. `ScoutRota` and `MountedGunners` decide on the server only. New
prefabs registered via `NetworkPrefabRegistrar.Sync(out _, out _)`.

**Verify on a client:** watch a scout pair sweep and return; a convoy arrives with gunners in the
wheels; a client player drives a monowheel (smooth, owner-authoritative) and another client sees it.

## 6. Persistence

Monowheels as city/war-party members are never saved individually — the group record decides (as
for all group members). A **player-taken** monowheel is a free-standing vehicle and needs
`SaveableEntity` + transform/mount savers so it stays where it was parked — verify by reloading and
grepping the save JSON.

## 7. Performance

The city grows by 8 scouts × (mount + rider) = +16 agents → about 50 when loaded, and a tier-2 convoy
can carry up to 13 people. Physics motors on 8–13 Rigidbodies near the player. Profile beside the city
and beside a convoy (**GDC-L1-PERF-0004 / 0001**); scout count and double weight are the tunables.

## 8. Testing

Pure logic first: `MonowheelDrive` maths (heading/lean/slip step), `NavPathSteering` corner/speed
choice, `ScoutRotaLogic` (who goes next, 2 out invariant, dead scout dropped). Prefab read-back tests
per builder output (components, faction, seats, netcode hash, savers). Roster tier test update. Play
checks: player drive feel, NPC path following at speed, sweep, convoy, host + client, reload.

## 9. Design principles consulted

- **GDC-L1-PERF-0004 / 0001** — the agent/physics budget in §7 is measured.
- **GDC-L1-LEVEL-0001** — fast red machines circling the city draw the eye to it from afar.
- **GDC-L1-PROD-0002** — player gunners, a city-wide alarm and exact tier counts are deliberate cuts.

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| A fast physics vehicle driven by AI overshoots corners / flips | Speed eased by upcoming turn angle; lean capped; first plan task is a drive spike on real terrain. |
| Ring-spin presentation disagrees across machines | Driven only from replicated velocity, never from input. |
| Player ownership handoff on a physics mount jitters | Same `MountModule` path as other mounts; client check in the plan. |
| Scouts never get back into formation after a sweep | Formation regroup + a rota timeout that re-activates formation. |
| Agent/physics cost | Profile; scout count and double weight are tunables. |
