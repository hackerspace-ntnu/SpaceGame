# Dune Barge in Unity: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Collection 2 of `dune_barge.blend` as one networked, saveable, walkable prefab. It has an exterior-only look from outside and reveals the interior on entry, with working ladders, hatches you can climb through, and doors you can open.

**Architecture:**
- A Blender export script scales the model ×1.6 in memory and adds these helpers:
  - `GRP_Exterior` and `GRP_Interior` groups
  - `LAD_` ladder markers
  - `COL_` collision islands
  - door leaves with their origin on the hinge
- An editor builder turns that FBX into `DuneBarge.prefab` using existing systems (carrier, ladders, articulated doors, sandstorm shelter, netcode and save wiring).
- Two small runtime components:
  - `InteriorReveal`, a local presentation toggle
  - `HatchPassage`, which moves the interactor's own body through a hatch

**Tech stack:** Blender 5.2 (bpy, `_exportlib`), Unity 6 C# (NGO, EditMode tests via Unity MCP), `tools/typecheck.py`.

**Spec:** `docs/superpowers/specs/2026-09-24-dune-barge-unity-design.md`

## Global constraints

- `EXPORT_SCALE = 1.6`, and the `.blend` is never written. The user must save the live file before the export.
- No hardcoded input keys. Interaction goes through `IInteractable` and the Interactor's Input System action. Prompts never name a key.
- **Every** `COL_` island is convex, separate, and never merged. Openings (doorways, hatches, gun port, neck tunnel) stay open.
- The prefab is built out of Play mode, by the builder only. It is never hand-edited.
- No magic numbers: tunables are serialized fields or named constants.

## Review focus

1. **A ladder whose `_Exit` sits above its foot.** `Ladder` logs a warning and gives the ladder no climb side. Exits must be offset horizontally from the rung line.
2. **Collision closes an opening.** A doorway, hatch or tunnel becomes impassable. `Verify()` capsule-probes every opening.
3. **The stair ramp misses a floor.** The ramp stops short of either the deck or the cockpit floor. The builder checks both ends are within 5 cm.
4. **Reveal on a client.** Remote players' positions must never reveal the interior on this machine. `InteriorReveal` only ever reads the local camera.
5. **The hatch lands inside geometry.** `HatchPassage` refuses when the landing spot overlaps a collider.

---

### Task 1: The export script

**Files:** create `Assets/Game/Art/Models/_Source~/models/vehicles/dune_barge_c2_export.py`

`prepare()` runs, in order:
1. **Scale:** ×1.6 about the origin; transforms are applied.
2. **Door leaves:** their origin moves onto the hinge. The hinge is the leaf's vertical edge nearest the right jamb, and the axis is world Z.
3. **Ladders:** `LAD_<name>` markers, measured from each `Mesh_Stair_Ladder_*`. Each has `_Top` and `_Exit` children, with the exit 0.6 m past the top, over the surface found by raycast.
4. **Collision:** `COL_DuneBarge_####` islands, as listed in spec §1.4. They are linked into Collection 2 so the `keep_collection` filter ships them.
5. **Groups:** `GRP_Exterior` and `GRP_Interior`; every mesh is re-parented into one of them.

**Verify:** run headless; the FBX appears under `Assets/Game/Art/Models/Vehicles/DuneBarge/`, and `describe()` lists five `LAD_` and N `COL_`.

### Task 2: A shared importer for islands and ladders

**Files:** create `Assets/Game/Editor/Art/ModelMarkerImport.cs`, which holds `BuildIslandColliders` and `GatherLadders`, moved out of `SkyCityBuilder`. Modify `SkyCityBuilder.cs` to call it.

**Test:** the existing sky-city tests still pass; the typecheck is clean.

### Task 3: `InteriorReveal`

**Files:** create `Assets/Game/Scripts/Vehicles/Interior/InteriorReveal.cs`, with a pure `static bool ShouldReveal(Vector3 point, IReadOnlyList<Bounds> localVolumes, IReadOnlyList<Vector3> localOpenings, float radius)`. Add tests in `Assets/Game/Editor/Tests/InteriorRevealTests.cs`.

**Tests:** point inside → true. Near an opening but outside the volumes → true. Far outside → false. Exactly at the radius → false.

### Task 4: `HatchPassage`

**Files:** create `Assets/Game/Scripts/Vehicles/Interior/HatchPassage.cs`, implementing `IInteractable` and `IInteractionReadout`. It has a pure `static bool IsOutside(Vector3 point, Vector3 outer, Vector3 inner)` and a `static Vector3 Destination(...)`. Add tests in `Assets/Game/Editor/Tests/HatchPassageTests.cs`.

- The move is `SaveTeleport.Move(body, dest, rot)`, and only on the machine that owns the interactor's body.
- **Tests:** the side choice (the nearer mark decides) and the destination (the opposite mark).

### Task 5: `DuneBargeBuilder`

**Files:** create `Assets/Game/Editor/Vehicles/DuneBargeBuilder.cs` and `Assets/Game/Editor/Tests/DuneBargePrefabTests.cs`.

The builder does:
- the model under an identity root, checked to face +Z
- `COL_` into colliders, through `ModelMarkerImport`
- a kinematic Rigidbody and a `WalkerPlatformCarrier` with a carry volume
- `LAD_` into `Ladder`s
- doors as `ArticulatedPart` (Rotate about Z, 100°) plus `ArticulatedPartInteraction`
- `HatchPassage` with its outer and inner marks
- `InteriorReveal`, holding the `GRP_Interior` renderers, the volumes and the openings
- `SandstormShelter`
- netcode and save wiring, as in `DesertCrawlerBuilder.WireNetworkAndPersistence`, then `NetworkPrefabRegistrar.Sync` and `SaveableWiring.TryWirePrefabs`
- `Verify()`, which runs the route probes

**Tests:**
- the prefab exists, has a NetworkObject with a non-zero hash, and has a SaveableEntity with a prefabId
- ladder count is 5
- 2 doors are articulated
- no `COL_` renderer is left
- `InteriorReveal` holds more than 0 renderers

### Task 6: Documentation

`docs/AI/systems/DuneBarge.md`, an entry in `docs/Human/the-systems.md`, `dune_barge_BUILD.md`, and `python tools/docs_check.py --index`.

### Task 7: Play verification

- On the host and a client:
  - climb all five ladders
  - use both hatches (interact, and crouch through)
  - open both doors
  - walk the stair into the cockpit
  - check the reveal happens only locally
- Save/quit/load with a door open; the door state must be in the save JSON.
