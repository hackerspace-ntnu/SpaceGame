# Strider barges — plan

**Goal:** the walking city gains three crewed dune barges (one `DuneBarge`, one `DuneBargeCompact`, one
`DuneBargeLookout`) that travel in the column and whose crew step off at stops, like the houses.

**User decisions (2026-09-25):** "add the dune barges to the moving settlement" → 1 of each (3), not 2
(size); **simple formation mover** (NPC travel only, players do not drive them); **crewed, step off at
stops**.

**Constraints:**
- The barge art prefabs, `DuneBargeBuilder.cs` and `DuneBarge.md` belong to another session and are
  uncommitted work in progress. Never edit them. Strider barges are **prefab variants** of
  `Assets/Game/Prefabs/Vehicles/DuneBarge/{DuneBarge,DuneBargeCompact,DuneBargeLookout}.prefab`, exactly
  as the habitat is a variant of RigWalker.
- Variants live under `Assets/Game/Prefabs/Agents/Vehicles/Ground/` (RagdollWiring decides by folder).
- Every lesson of the walking city applies: `KeepSceneMigrationSync` is the builder's last write,
  identities are preserved across rebuilds, `WireStriderCity` stays idempotent, the persistentScene
  `starterVehicle:` block must survive every save, the editor lock + play-mode check protocol.

## Design

**Mover — `TrackedHullMotor`** (`Assets/Game/Scripts/Vehicles/Motors/`), an `IMovementMotor` for
big transform-driven hulls:
- server-authoritative (a NetAuthority simulation driver); it writes the transform, and the barge's own
  `ClientNetworkTransform` replicates it;
- steers along a NavMesh path with the shared `NavPathFollower` (`NavPathFollowerSettings route`);
- turns on the spot at `turnRate`, accelerates and brakes to `cruiseSpeed`, which is slow;
- sits on the ground with a ground probe: height from the probe, and optional pitch/roll to the ground
  under the hull's footprint, smoothed;
- no Rigidbody physics. If the barge prefab has a Rigidbody, the variant makes it kinematic;
- the walkable deck keeps working through the barge's existing carrier, because players walk on board
  while it moves;
- the pure maths (heading step, speed step, footprint tilt) goes in a static class with EditMode tests.

**Strider variants — `StriderBargeBuilder`** (`Assets/Game/Editor/Vehicles/`):
- per variant: AgentController + `TrackedHullMotor`, EntityFaction Striders (through `EntityFactionWiring`),
  FormationModule (Social, `holdSlotAtRest`), `CrewShift` + `VesselSeats` + `ChairPose` + gangway;
- crew posts are placed on the walkable deck, measured from the deck colliders the same way
  `StriderCityBuilder` places house posts. Reuse that code by extracting it, not by copying it;
- `CrewPosts` per variant comes from its layout;
- no player helm, no MountModule;
- registered, saveable (the SaveablePolicy rules for group members apply), and
  `KeepSceneMigrationSync` is the last write.

**City — `RosterAuthoring.WireStriderCity`:**
- order: houses, crawlers, crabs, **barges (3 fixed prefabs, crew: false)**, scouts, then crew;
- crew total = the sum of every carrier's posts, split between the Warrior and Scout roles as today;
- `CityShape` spacing must fit the largest footprint (barges ~35 m). Size rows/lanes from measured
  collider footprints, with a test like `ItsFormation_IsSizedForTheWheels_NotForPeopleOnFoot`;
- `CityFarthestSlot` grows, so the monowheel `RegroupDistance` (derived) needs Build Strider Monowheels
  re-run. Check that the crab regroup still covers its slot;
- the departure gate (`CrewShift.AllAboard(formationId)`) must include barge crews. Verify, don't assume.

## Tasks

1. **TrackedHullMotor + tests** (pure maths + component). Read the barge prefab first: Rigidbody?
   colliders? carrier? network components?
2. **StriderBargeBuilder**: three variants; extract the shared deck-post placement from
   `StriderCityBuilder`; tests (like `StriderHabitatWalkerTests`: faction, motor wiring, posts on deck,
   gangway on ground, identity, migration sync, registration, no ragdoll, no helm).
3. **City wiring**: template members, crew total, CityShape sized to footprints, monowheel rebuild,
   tests (`StriderCityTemplateTests` + footprint test + regroup test), idempotence, `starterVehicle` block.
4. **Docs**: Striders.md (members, barges, motor), Vehicles.md (TrackedHullMotor row), a one-line
   pointer in DuneBarge.md's Related list **only if** its owner session agrees; otherwise ours point to it.
   the-systems.md Striders line.
5. **Review + user playtest**: barges hold formation over dunes without sinking or floating, crew step
   off and board at stops, players can walk on a moving barge deck, client view, save/reload mid-stop,
   profile.
