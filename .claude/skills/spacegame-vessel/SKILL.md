---
name: spacegame-vessel
description: Use when turning a static model into an NPC-flown transport vehicle in SpaceGame — a new sky/air vessel that carries a group of NPCs to a destination, lands or hovers, drops them off, and flies home. Also use when an existing transport flies stern-first or sideways, drops a passenger in mid-air onto another passenger's head, starts unloading while still sliding across its landing site, leaves NPCs frozen in place after it despawns, or its "NO DECK" build warning fires.
---

# SpaceGame NPC transport vessels

> **Design check:** an unpiloted vehicle carrying NPCs to the player is a pacing and encounter-design
> choice before it is code — read the `FEEL`, `DESIGN` and `MP` principles in
> `docs/game-development-constitution/INDEX.md` (arrival telegraphing, threat readability) before
> tuning cruise speed, landing generosity or capacity.

## Overview

A transport vessel is a `NetworkObject` flown entirely by the server (`VesselPilot`) that carries
seated NPC passengers (`VesselSeats`/`NpcSeating`, shared with mounted caravan riders) to a moving
quarry, finds a place to set them down (`LandingSiteFinder`), unloads them, and flies itself home.
**Nobody pilots it** — no player seat, camera or controls; players can still shoot it down.

The Sky Tribe's `SkySkiffTransport`/`SkyFreighterTransport` are the worked example throughout:
[SkyVesselBuilder.cs](../../../Assets/Game/Editor/Vehicles/SkyVesselBuilder.cs). Full model, the
war-party flow that drives one, and its gotchas: [SkyTribe.md](../../../docs/AI/systems/SkyTribe.md)
and [Vehicles.md](../../../docs/AI/systems/Vehicles.md) (the vessel component API lives there,
since it is generic vehicle machinery, not Sky-specific). This skill assumes the flight/landing
math (`VesselMission`, `LandingSiteFinder`, `VesselFlightMath`) and the pilot/seats components
already exist — it is the checklist for building a **new prefab** on top of them, not for changing
that math.

## When NOT to use

- A vehicle a **player** flies or drives → the mount/station stack in [Vehicles.md](../../../docs/AI/systems/Vehicles.md) Extending, not this skill.
- A new tribe's roster, faction or war-party template → **spacegame-tribe** (its §5 war-party
  template is what actually launches a vessel built with this skill).
- The creature/NPC riding inside the vessel → **spacegame-agent**.
- Save/load for the vessel itself → nothing to do; see Persistence below.

## 1. Find the model's bow, never trust its mesh names

A builder script instantiates the raw FBX **unrotated** (identity transform, as exported) and
turns it so the vessel's own bow points along the **root's +Z**, because that is the heading
`VesselFlightMath`/`VesselPilot` fly and steer toward. There is one number for this,
`Transport.ModelYawCorrection` (degrees about the model's own centre) — never a shared constant
across vessels, because each hull was hand-placed in its source file at whatever heading the
artist happened to use.

**Measure it, do not assume it:**
1. Instantiate the FBX with identity rotation and read the renderer bounds of its most
   bow-identifying parts (a cockpit house, engine pylons, a mast — whatever is asymmetric front to
   back) along local Z.
2. **Mesh names lie.** `SkySkiffTransport`'s engine pylons and `SkyFreighterTransport`'s rotor
   nacelles are both named `Mesh_SkyCity_SternGear` — inherited from an unrelated flagship part
   library — yet one pair is the stern and the other the bow. A part named `Prow` on the freighter
   turned out to be the aft end. Only the geometry's actual position along Z, cross-checked with a
   playtest observation of which end visibly leads, is reliable.
3. Once you know which end is the bow and at what Z, `ModelYawCorrection` is the rotation that
   puts it at +Z. The skiff and freighter both needed 0° (they were exported bow-first already);
   do not copy that value onto a new hull without re-measuring — a fixed 180° here is exactly the
   bug this step exists to prevent (a transport that flies stern-first).
4. Seats and `Ramp`/`Drop` need **no separate correction**: seat markers are placed with
   `model.TransformPoint`, in the model's own space, so they turn with whatever yaw is applied;
   `Ramp` is placed from the model's *rotated* bounds, so it automatically tracks the new astern
   end.

## 2. Prefab shape

Built fresh from the FBX each time — **never from a static scenery prefab** of the same model
(a static prop is marked static/batched for baking and carries no `NetworkObject`; nothing about
it survives being flown).

**Root** (pivot at the keel — `VesselPilot` sets *this point* down on the landing site; prow at
local +Z):
- `NetworkObject` (add first — everything below looks it up), `NetRelay` (carries damage
  requests), `NetAuthority`.
- `NetworkTransform`: `AuthorityMode = Server`, sync every position and rotation axis, no scale,
  world space, interpolated. The pilot runs only on the server; every other machine watches.
- Kinematic `Rigidbody`, no gravity, no interpolation (an interpolated body would drag the hull
  back behind where the pilot and the `NetworkTransform` put it).
- `HealthComponent` + `NetworkedHealthComponent`, `EntityFaction` (the tribe's faction +
  `GlobalRelationships.asset`).
- `ChairPose`, `VesselSeats` (seat list + `chairPose`), `VesselPilot` (`footprintRadius`, `ramp`,
  `drop`).

**Children:**
- `Model` — the FBX, yawed by `ModelYawCorrection`, lifted so its lowest renderer sits at the
  root; apply the same collider-fit rules (solid, not triggers) and cull-LOD group your static
  escort/scenery builder already uses for this model, so the two do not diverge.
- `Seats/Seat_i` — one marker per seat, on the deck, facing outboard from the deck's centre line.
  **Deck points are hand-measured** (raycast the model in the editor, or read renderer bounds) in
  the model's own pre-rotation space, one array entry per seat.
- `Ramp` — astern of the hull (its bounds' far edge along −Z plus a stand-off, e.g. 2 m), at keel
  height: where a **landed** party walks off.
- `Drop` — at the keel: where a **hovering** vessel drops its party straight down.

**Deck validation is not optional.** After placing seats, raycast straight down from a small
distance above each seat marker and confirm the model's own colliders answer within a tight
tolerance (e.g. 0.5 m). Log an error and flag the build report (`NO DECK under Seat_i`) rather than
silently shipping a floating chair — this caught a freighter deck re-measurement in the field. Run
`Physics.SyncTransforms()` first, or freshly parented colliders will not have moved yet.

## 3. Capacity and footprint

Pick `FootprintRadius` (used by `LandingSiteFinder`'s headroom/slope check — see
[Vehicles.md](../../../docs/AI/systems/Vehicles.md)) and seat count as *design* numbers, not
whatever the deck happens to fit: `SkySkiffTransport` is 4 seats / 12 m, `SkyFreighterTransport` is
8 seats / 18 m, chosen so a tribe's roster tiers (`spacegame-tribe` §3) map cleanly onto "fits the
small vessel" vs. "needs the large one" (`NpcGroupTransport.VesselFor(riderCount)` picks by
`VesselSeats.Capacity`). Serialize `MaxHealth` per vessel size too — do not hardcode a single
number across a small and a large hull.

## 4. Network registration

End the builder with `NetworkPrefabRegistrar.Sync(out _, out _)` — **never `SyncMenu()`**, which
opens a modal "OK" dialog that would park a chained build (savers, ragdolls, a following tribe
builder) until a human clicks it. After registering, `AssetDatabase.ForceReserializeAssets` the
built prefab paths: a `NetworkObject` created by script ships `GlobalObjectIdHash` 0 until
`OnValidate` runs against the *saved* asset, and the hash only reaches the YAML on reserialize.

## 5. Landing tuning

The vessel does not choose *where* it lands beyond its own `footprintRadius` and the shared
`LandingSettings` on `VesselPilot` (ring distance, slope/spread tolerance, hover height, clearance
— all serialized, tunable per prefab). Read [Vehicles.md](../../../docs/AI/systems/Vehicles.md)'s
`LandingSiteFinder`/`VesselMission` rows before touching these: slope and height spread are judged
against a least-squares plane through the footprint, not the raw min/max, specifically so landing
stays generous on rough ground; a level hull set down on a sloped Land site is expected to float on
the downhill side.

## 6. NpcSeating, not a copy of it

Passengers board through [`VesselSeats`](../../../Assets/Game/Scripts/Vehicles/SkyVessel/VesselSeats.cs),
which is a thin per-machine presentation layer (poses a rider with `ChairPose`, ignores its
collisions with the hull) over [`NpcSeating`](../../../Assets/Game/Scripts/agents/Modules/Riding/NpcSeating.cs)
— the same suppress/restore mechanics a mounted caravan rider uses (`NpcPassenger`). Never
reimplement seating: `NpcSeating.Suppress`/`Restore`/`Abandon` are what take an NPC's feet without
touching its brain (it keeps targeting and firing from the seat) and what hand them back only to a
still-living NPC. `VesselSeats.OnDestroy` → `AbandonAll` is the backstop that must run before the
hull despawns, or a seated passenger is orphaned mid-air when Netcode lifts it to the scene root.

## 7. Wiring a war party to fly one

A vessel built with this skill does nothing until a tribe's war-party template points at it: give
the `NpcGroupTemplate` an `NpcGroupTransport` block (`smallVessel`, `largeVessel`, `travelSpeed`
matched to the vessel's own `VesselPilot.cruiseSpeed`, `homeSiteName` naming a registered
`WorldSiteMarker`). See **spacegame-tribe** §5 for the template itself and
[SkyTribe.md](../../../docs/AI/systems/SkyTribe.md) Flows for the full board/launch/deliver/retry
sequence this then drives.

## 8. Tests

Add a fixture beside
[SkyTransportPrefabTests.cs](../../../Assets/Game/Editor/Tests/SkyTransportPrefabTests.cs), one
per new vessel type. Cover, per the Sky fixture:

- The prefab is a registered network prefab (`NetworkPrefabRegistrar` / `NetworkManager`'s list).
- `VesselSeats.Capacity` matches the seat markers built; every seat has deck within tolerance
  under it (or the build already logged and this test catches a regression).
- Seating/unseating gives back exactly what `NpcSeating.Suppress` took (agent re-enabled, body
  gravity/interpolation restored) — for a living NPC, and gives a dead one nothing.
- A seat whose occupant is destroyed while seated gives up its claim and can be filled again.
- `VesselSeats.AbandonAll()` (or the equivalent teardown) leaves nobody `RidesAsPassenger` and no
  `CarriedBody` hold outstanding.

Run headlessly and expect `FAILED=0` before trusting a new vessel.

## 9. Verification checklist — host **and** a client

A vessel is not done until every line below has been seen, not merely built:

- [ ] It flies bow-first — watch it approach from the side; a stern-first flight is a
  `ModelYawCorrection` bug, not a flight-math bug.
- [ ] It lands on flat-enough ground with the keel on the site, or hovers over rough ground with
  the party dropping onto the NavMesh below — both seen once each.
- [ ] Passengers step off one at a time at the ramp foot (landed) or straight down (hovering),
  each ending on the NavMesh with its agent re-enabled — checked on a **client**, not just the
  host (seated presentation and the dismount are both local-machine reads of replicated state).
- [ ] Shoot it down mid-flight: every passenger drops straight down where it was, never teleported
  to a landing site the vessel had already picked.
- [ ] Despawn it with passengers still seated (fold it out of view, or via the war-party flow):
  nobody is left standing frozen where the hull was.
- [ ] Two vessels of the same prefab do not park on top of each other if your tribe's template
  reuses parked hulls (see `NpcGroupTransport.DockPoint` in [SkyTribe.md](../../../docs/AI/systems/SkyTribe.md)).

## Related

- [SkyTribe.md](../../../docs/AI/systems/SkyTribe.md) — the worked end-to-end example: faction,
  roster, city and the war-party flow that launches these vessels.
- [Vehicles.md](../../../docs/AI/systems/Vehicles.md) — the vessel component API
  (`VesselPilot`/`VesselMission`/`LandingSiteFinder`/`VesselSeats`), its tunables and gotchas.
- **spacegame-tribe** — the war-party template that flies a vessel; §9 for a home settlement.
- **spacegame-multiplayer** — network prefab registration in general, ownership/authority.
- **spacegame-persistence** — why a vessel and its passengers are deliberately not saved
  individually (the group record rebuilds them).
