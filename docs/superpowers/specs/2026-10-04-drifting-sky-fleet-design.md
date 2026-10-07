# Drifting Sky Fleet — design

Date: 2026-10-04 · Status: decided (user unavailable; defaults chosen here and listed at the end)

## Request

"The sky city was never put correctly into the scene, so put it into the scene correctly. Make it
drift slowly. Add black smoke particles (same as the dust particles, just black) to the motors on
the sky city and its vehicles. Make the smaller vehicles drift a bit independent of the flagship so
that it looks good, and you may even add in a few more of the smaller skyship prefabs."

Hard constraints: every moving vehicle moves through the agent stack (`AgentController` +
`IBehaviourModule` brain driving an existing `IMovementMotor`), never a one-off transform mover; the
smoke reuses `DustCloudRecipe` and is driven by a small runtime component, like
`MonowheelPresentation` drives its dust.

## What "not correctly in the scene" was

1. **It is not in the scene at all.** Main's cleanup (8e7ef62a) moved `SkyCityFleet.prefab` into
   `Prefabs/Environment/Structures/SkyFleet/` and deleted its `persistentScene` instance; the merge
   kept the deletion. No Sky City means no Sky residents, and `sky-war-party` has no origin.
2. **Where it stood, it stood inside a rock spire.** The deleted instance was at (3704, 228, 1182).
   The world NavMesh has walkable ground at y = 251–308 at (3700–3800, 1000–1200): terrain reaching
   ~80 m above the city's pivot, through its decks.
3. **Every builder pointed at the old paths** (`SkyCityBuilder.PrefabPath`/`HullsPath`,
   `SkyFleetBuilder.FleetPrefabPath`), so a rebuild would have written fresh prefabs (new GUIDs)
   beside the moved ones.
4. **It was static scenery**: batching/occluder-static flags on every renderer, no `NetworkObject`,
   no saver. Static batching alone would leave every mesh behind the moment it moved.

## Design

### Motor — `FlyingRigidbodyMotor`, extended with a kinematic-hull mode

The existing free-3D-flight motor (blimps, drones). It drives a *dynamic* body through
`linearVelocity`, which a hull this size cannot have: the city carries 13 non-convex mesh colliders,
and PhysX refuses a non-convex mesh collider on a non-kinematic body. `VesselPilot` (what the sky
transports use) is not an `IMovementMotor` but a mission state machine, and `TrackedHullMotor`
follows the ground. So the flying motor gains `kinematicHull`: it keeps its own velocity (the same
`MoveTowards` ramps, same facing logic) and integrates it with `MovePosition`/`MoveRotation` on the
physics clock. It also starts honouring the intent's facing channel (`OverrideFacing`), which the
escorts use to hold the flagship's heading instead of turning toward every wander point.

### Brains — two new movement modules

- **`DriftRouteModule`** (flagship): a closed loop of world waypoints. Under way it issues
  `MoveTo(route[leg])`; on arriving (inside `arriveRadius` and slowed below `mooredBelowSpeed`) it
  moors for `mooredSeconds`, then sails for the next waypoint. State: leg, under way, moor time left.
- **`FleetEscortModule`** (each escort): station = flagship pose × local `station` offset + a slow
  three-axis sine wander (`wanderAmplitude`, `wanderPeriod`, per-instance `wanderPhase`), led by the
  flagship's velocity × `leadSeconds`; speed proportional to distance (`catchUpGain`); facing =
  flagship heading.

### Route and speed (GDC-L1-LEVEL-0002, GDC-L1-LEVEL-0006)

A moving landmark works against the mental map (LEVEL-0002 asks for stable landmarks); the user
asked for drift, so the cost is contained: slow (2 m/s, the city's own length in ~100 s), a fixed
loop the player can learn, moorings that make it a destination for minutes at a time, and the map
marker following it. The loop stays in view of the spawn (LEVEL-0006, show before you go) over the
low basin east of the western mountains, every leg clear of the spires at x≈2900 and x≈3750:

| # | Waypoint (x, z) | Note |
|---|---|---|
| 0 | (3350, 1400) | ~470 m from the spawn; start of a new world |
| 1 | (2550, 1400) | |
| 2 | (2550, 550) | |
| 3 | (3100, 300) | |

Cruise altitude 280 m (pivot): the plain is 100–130 m, the tallest ground under any leg's corridor
is ~200 m, the city's keel hangs 25 m below the pivot and the lowest escort ~45 m. ~3.5 km loop,
~29 min sailing + 4 × 2 min moored.

### Residents and the NavMesh — parked while under way

A `NavMeshAgent` cannot ride a moving NavMesh: Unity has no API to move a `NavMeshDataInstance`,
and re-adding it every frame resets every agent's path. The codebase's precedent for "crew on a
moving machine" is the Strider house (`VesselSeats`/`CrewShift`): crew are carried while it walks
and walk ashore at stops. The city does the same with a new **`SettlementDeck`** (server only):

- **Departing:** every Sky resident standing inside the deck volume is parked where it stands —
  `NpcSeating.Suppress` (feet off, `RidesAsPassenger`, brain on, so it still shoots) and
  `NpcSeating.Attach` under the flagship's `NetworkObject` (replicated parenting). The city's
  NavMesh is withdrawn (`StaticNavMeshData` disabled) and `SettlementPopulation` is suspended, so
  nobody spawns onto a mesh that is no longer under the deck.
- **Under way:** re-scans every 0.5 s, so a resident restored by a load (or arriving late) is parked
  too; refreshes the `WorldSiteMarker` every few seconds.
- **Moored:** the NavMesh is re-laid at the city's current pose, residents are unparked
  (`Detach` + `Restore`, which warps the agent onto the new mesh) and the population resumes.
- A teleport (save restore) while moored re-lays the NavMesh.

Players are carried by a `WalkerPlatformCarrier` with an explicit carry volume over the decks (and
one per escort).

### Netcode

Flagship and every escort: in-scene `NetworkObject` + `NetRelay` + `NetAuthority` + server-authority
`NetworkTransform` (the sky transports' exact stack, `SkyVesselBuilder.AddNetworking`), kinematic
body. `NetAuthority` switches the controller and motor off on clients; they interpolate. Parking
parents residents under the flagship's `NetworkObject`. Smoke runs on every machine from the
transform it sees.

### Persistence

Flagship and escorts are authored objects in `persistentScene` with **baked** `SaveableEntity`
identities (`sky-city-flagship`, `sky-escort-N`), so their pose is saved by `TransformSaveable` and
re-applied through `SaveTeleport`. The route state (leg, under way, moor time left) is
`DriftRouteSaveable` (key `driftRoute`), auto-added by `SaveablePolicy` for a `DriftRouteModule`.
The escorts' wander phase is a pure function of time and their serialized phase: nothing to save.
Parked residents are saved like any resident and re-parked after a load.

### Smoke (GDC-L1-FEEL-0004, GDC-L1-TECH-0002)

`DustCloudRecipe.Cloud` with a sooty mid-grey `DustCloudRecipe.Material(..., softFade: 0)` (tint
0.30/0.29/0.27, alpha 0.45 — the first, near-black version read as ink), one per duct (measured from
each `SternGear` assembly: two ducted fans each), blasted astern as a jet: the builder overrides the
recipe's thrown-sand speed/cone/drag with 12–18 m/s × √(duct scale), a 10° cone and a constant 4.5 m/s² × √(scale) braking (trail ~25 m astern, ~50 m on the city), white particle colour (JetSmoke multiplies its tint by it, which squared the first grey back to near-black), because
hulls doing 2–6 m/s otherwise piled their smoke up in a ball at the nozzle (playtest feedback). **`EngineSmoke`**
(every machine) drives `rateOverTime` from measured speed (`MonowheelPresentationMath`), idle when
moored, more under way, faded out by camera distance; caps from `DustCloudRecipe.CapFor`.

### More escorts

Seven escorts instead of three: two freighters, three skiffs, two tugs, at stations clear of the
city and of each other by more than their wander.

## Defaults the user may want to change

Cruise speed 2 m/s · escort top speed 6 m/s · moored 120 s per waypoint · cruise altitude 280 m ·
the four waypoints above · seven escorts and their stations · wander ±14 m horizontal / ±5 m
vertical over 40–75 s · smoke grey at alpha 0.45, jet 12–18 m/s (24–36 on the city), 1 puff/s per duct idle, 4 under way, faded out between 600 and
1500 m · residents parked (standing still, still able to shoot) while under way.
