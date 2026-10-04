---
system: DuneBarge
layer: vehicles
summary: "Walkable tracked barge: interior drawn on entry, ladders, crawl-through hatches, hinged doors"
paths:
  - Assets/Game/Editor/Vehicles/DuneBargeBuilder.cs
  - Assets/Game/Editor/Support/ModelMarkerImport.cs
  - Assets/Game/Editor/Traversal/PlayerTraversalWiring.cs
  - Assets/Game/Scripts/Vehicles/Interior/
  - Assets/Game/Scripts/Characters/Player/Movement/HatchCrawler.cs
  - Assets/Game/Editor/Tests/DuneBargePrefabTests.cs
  - Assets/Game/Editor/Tests/InteriorRevealTests.cs
  - Assets/Game/Editor/Tests/HatchPassageTests.cs
  - Assets/Game/Art/Models/Vehicles/DuneBarge/
  - "Assets/Game/Art/Models/_Source~/models/vehicles/dune_barge_export.py"
  - "Assets/Game/Art/Models/_Source~/models/vehicles/dune_barge_rig.py"
symptoms:
  - "the barge's interior is missing when I walk inside"
  - "I can see the barge's interior through its walls from outside"
  - "the hatch does nothing when I interact with it"
  - "I crawl through a hatch and end up back outside, or deeper inside"
  - "I get stuck in a hatch with gravity off"
  - "I slide back down the barge's stair"
  - "a barge ladder can't be grabbed, or drops me in the wrong place at the top"
  - "a barge door or hatch lid swings into the room the wrong way"
  - "an invisible wall fills the barge's hold or cockpit"
  - "I can't walk from the barge's stair into the cockpit"
  - "getting off a barge ladder or out of a hatch crawl shoves me sideways or through the hull"
  - "the dune barge export fails with 'no room for the player's body'"
  - "DuneBargePrefabTests says a ladder exit or hatch mark is inside LeafCollider"
reads_with: [Vehicles, Ladders, PlayerCharacter, SceneTransitions, ArtPipeline]
updated: 2026-10-04
---

# Dune barge

**What it is:** tracked barges, about 35 m long, that players walk on, climb, and go inside. There are three variants, each one hand-built collection of `dune_barge.blend`, and each becomes its own prefab under `Assets/Game/Prefabs/Vehicles/DuneBarge/`, built by [DuneBargeBuilder.cs](Assets/Game/Editor/Vehicles/DuneBargeBuilder.cs):

| Prefab | Collection | What it has |
|---|---|---|
| `DuneBarge` | Collection 2 | Cockpit, neck stair, hold, stern castle |
| `DuneBargeCompact` | Collection 3 | Rear hull only (no cockpit, neck or front pods), with a lookout cabin on the castle |
| `DuneBargeLookout` | Collection 4 | The full barge, plus the lookout cabin |

**Scope:**
- driving is **not** here, because another system owns vehicle movement
- the hulls are built ready to be driven: networked, carrier-ridden, and saved as transform-driven hulls

**Related:**
- [Vehicles.md](Vehicles.md): walkable decks and the carrier
- [Ladders.md](Ladders.md)
- [PlayerCharacter.md](PlayerCharacter.md): crouch and the 3 m body
- [SceneTransitions.md](SceneTransitions.md): why this is *not* an interior scene
- [ArtPipeline.md](ArtPipeline.md)

Spec: [2026-09-24-dune-barge-unity-design.md](../../superpowers/specs/2026-09-24-dune-barge-unity-design.md).

## Model

**Walked in place, like the player ship.**
- Interior scenes restore their visitors to a fixed return point, so they can't follow a moving hull.
- The interior therefore lives in the same prefab and the same scene as the exterior.

**The exterior-only look comes from one prefab and two render groups.**
- The export writes `dune_barge_groups.json`, which lists every mesh in the `.blend`'s `Interior` and `Exterior` collections.
- `InteriorReveal` switches the interior renderers off and turns them on only on a machine whose own camera is:
  - inside the hold or cockpit volumes, or
  - within `revealRadius` of a hatch sill or the gun port.
- Colliders are never toggled.

**The model is exported 1:1 and scaled ×`ModelScale` (1.6) in the prefab.**
- The `.blend` is sized for 2 m crew; the player is 3 m tall, 1 m wide and 1.8 m crouched.
- At ×1.6 the hatches are 1.9 m (crouchable), the cockpit has 3.5 m of headroom, and the doorways are 4.6 m.
- Collision is built at scale 1 and scales with the model.

**A stern castle stands over the base's end.** It is the taller rear section with a crenellated crown (`Mesh_RearHull_Castle*`, `Mesh_RearHull_Crown*`).
- It is one more reveal volume (castle floor to castle roof).
- The stern wall and gun port are at its end.

**The rig** is built by `dune_barge_rig.py`: one armature per variant (`Arm_DuneBarge2`/`3`/`4`), with rigid parts bone-parented to it. Every bone is found by what is in the variant, not by name:
- `Bone_Pod_L`/`R`: pod steering
- `Bone_Wheel_<Unit>_<Kind><n>`: wheel spin
- `Bone_Door_<n>`: each bulkhead door. The hinge is measured in the leaf's own mesh space, so a moved, scaled or turned copy (the lookout-cabin door) hinges right.
- `Bone_Hatch_R`/`L`: the side-hatch lids, shut at rest
- `Bone_Lid_<n>`: any other hatch, such as the stern hatch
- `Bone_Gun_<n>_Yaw`/`Pitch`: each heavy gun

The track links are not boned. Stepping them along the belt is a runtime job, as on the original barge.

## Key types

| Type | Role |
|---|---|
| `DuneBargeBuilder` | Editor. Builds the prefab from the export's markers. Idempotent, and refuses to run in Play mode. |
| `ModelMarkerImport` | Editor, shared with `SkyCityBuilder`. `COL_*` islands become box or convex colliders, and `LAD_*` markers become `Ladder`s. |
| `InteriorReveal` | Runtime, presentation only. `ShouldReveal(point, volumes, openings, radius)` is pure. |
| `HatchPassage` | An `IInteractable` on each hatch lid. It opens the lid through its switch, then hands the interactor's body to their `HatchCrawler`. |
| `HatchCrawler` | Player component, owner only. It steers the body along outer → sill → inner (or the reverse), crouched, with gravity off, ignoring the hull's colliders, then finishes with `SaveTeleport.Move`. |
| `PlayerStance.HoldCrouch(bool)` | A counted hold that keeps the player crouched regardless of input. |
| `PlayerTraversalWiring` | Puts `LadderClimber` and `HatchCrawler` on the player prefab. |

## Flows

**Export.** `dune_barge_export.py` does the following for each variant, in memory only:
1. It writes `<stem>_groups.json`.
2. It measures the rooms (`VOL_*`) with upward rays from every floor-like piece.
3. It cuts 110–175 `COL_DuneBarge_####` islands. Every opening, room and the stairwell stays open:
   - floors are kept whole
   - bulkheads are split around their doorways
   - the stern is split around the gun port
   - walls are split around the hatches
   - the neck is split into six arch sectors
   - an invisible stair ramp is pinned to the cockpit floor edge and capped at 32°
   - every other structure piece is `carve`d: hulled only in the parts beyond each face of any room or stairwell it intrudes into, recursively
   - the belly is kept under the floors, in segments
   - track fenders are included, with running-gear boxes trimmed under them
   - furniture gets one box per cluster of touching parts within one collection
4. It places `HATCH_<R|L>_Outer/Sill/Inner`: the outer mark on the fender outboard of the coaming, the sill in the opening, and the inner mark on the hold floor.
5. It places the `LAD_*` markers. Each has `_Top` and an `_Exit` 0.6 m past the top. The hold's ladder up to a hatch sill gets none, because it is the crawl's inside stair.

   Every set-down spot (ladder exits and both crawl ends) is slid, up to 2 m, to the nearest spot on the same floor where the player's standing body fits among the islands. If no such spot exists, the export fails. It then ships the collection with the rig and the empties. `-- --src snapshot.blend` exports a `save_as_mainfile(copy=True)` snapshot, so an export never waits on, or touches, the file the user has open.

**Using a hatch:**
1. Interact with the lid.
2. `HatchPassage` asks the lid's `ArticulatedPartInteraction` to open. This is replicated and saved like any door.
3. It waits until the lid is `openEnough`.
4. `HatchCrawler.TryCrawl` moves the owner's body along the route.
5. When the crawl finishes, the lid is closed again.

## Multiplayer

**Doors and lids:** `ArticulatedPartInteraction` over `NetRelay`. The root carries a `NetworkObject` (DontDestroyWithOwner) and a `ClientNetworkTransform`.

**Hatch crawl:** owner-side body movement only. The player's transform is owner-authoritative, so nothing is added to the wire.

**Reveal:** each machine reads its own `Camera.main`. A remote player standing inside never reveals anything on this machine.

**Ladders:** per-machine maths over a static registry.

## Persistence

- **Hull:** its transform, through `SaveableEntity` + `TransformSaveable`, stamped by `SaveableWiring`.
- **Door and lid state:** `ArticulatedPartsSaveable`, keyed by hierarchy path.
- **Crawl, reveal and ladders:** no saved state, by design. A player loaded mid-crawl is standing where they were.
- **Build out of Play mode.** A prefab saved in Play mode ships with no save id.

## Gotchas

- **Nothing inside the barge draws until the camera enters.** In the Scene view the interior looks missing. That is `InteriorReveal`, not a broken import.
- **A hatch that "does nothing":**
  - the player prefab has no `HatchCrawler`: run Tools ▸ SpaceGame ▸ Player ▸ Wire Hatch Crawler
  - or the lid can't open because it is blocked
- **Probe only walkable surfaces in the export.** A BVH over every mesh in the barge (~300k tris) ran headless Blender out of memory, and the export failed with an unrelated "nothing to step onto" error. `WALKABLE` lists the surfaces the probes need.
- **Openings are found as a contiguous run of ray misses, not min..max of all misses.** Past a plate's edge also misses, so min..max over-sized the gun port to the top of the stern.
- **The stair itself is 34°.** The ramp is capped at 32° (the angle the player ship's ramp walks at, just under where friction lets go), so its foot starts a little behind the first tread.
- **A convex hull of a concave shell fills whatever it wraps.** The hull body under the cockpit (`Cube.001`/`.015`) is a tub. Hulled whole, it filled the cockpit. Hulled around one room only, its "above the nook" part was the ring of vertices at the walls' knee, which hulled into a slab across the cockpit. `carve` cuts against *every* room and the stairwell, on the boxes' exact faces, so it always terminates. The belly has its own rule and is skipped by the general loop; carved as well, it filled the hold.
- **The stairwell needs a landing.** On `DuneBarge` the measured cockpit room stops 0.35 m short of the stair's top edge, and a slab of hull stood in that gap. The clear box runs `STAIR_LANDING` past the edge.
- **Leaf and lid colliders are convex `MeshCollider`s of the leaf mesh, on a `LeafCollider` child.** That hull is exactly the shape `dune_barge_export.py`'s `Obstacles` slides every set-down spot clear of, so Blender and Unity share one model of each lid. Until 2026-10-04 they were oriented boxes fitted in the hinge frame, and a box around a curved lid is bigger than its hull: two ladder exits and two hatch outer marks per variant sat a few cm inside a shut lid's box. (Boxes in the mesh's own axes were worse still: the tilted side-hatch lids came out 0.85 m thick over the fender.) The interactable is found by `GetComponentInParent`, like the `Interactor`.
- **Furniture boxes never span collections.** An engine box merged with the jerrican beside it and walled off the corner between them, which was the only floor under the left hatch.
- **The Compact's lookout-cabin door has a 0.2 m lip.** The cabin floor sits below the castle roof outside, and the player has no step offset. This is a model issue.
- **The coaming ring is visual only.** The user enlarged it, so only about 0.5 m of fender top is left outboard of it, which is why the crawl starts 0.3 m out.

## Extending

- **New mesh:** put it in the `.blend`'s `Interior` or `Exterior` collection. The export refuses a mesh in neither.
- **New ladder:** a `Mesh_Stair_Ladder_*` object in the variant's collection. Its marker is measured, not typed. One whose foot is inboard and whose top reaches a hatch sill is treated as the crawl's stair.
- **Refresh:** re-export, then re-run the builder. Never edit the prefab.
