# Dune Barge in Unity: design

**Date:** 2026-09-24
**Status:** awaiting approval
**Source:** `Assets/Game/Art/Models/_Source~/models/vehicles/dune_barge.blend`, collection **Collection 2**, which is the user's rebuild. It is organised into `Interior` and `Exterior` sub-collections.

## What the user asked for

| Asked | Decided with the user (2026-09-24) |
|---|---|
| Export the barge to Unity | Yes, as one prefab. |
| One version for the exterior only, one for when players go inside | **One prefab, two render groups.** The interior's renderers switch on only on the machine of a player who is inside it or at an opening. From outside, only the exterior is drawn. |
| The barge will move | Its interior is walked **in place** in the world scene, not as an additive interior scene. That is the precedent set by the PlayerShip and by `Vehicles.md` ("walkable deck → `WalkerPlatformCarrier` + carry volume"). Interior scenes restore you to a *fixed* return point, so they cannot follow a hull. |
| Ladders work | Every ladder gets a real `Ladder` volume, using the `LAD_` marker convention. |
| Players can enter the hatches | **Both fixes:** the model is exported at **×1.6**, so the hatches become 1.9 m and can be crouched through (crouch = 1.8 m). The hatches also get **press-E climb-through**. |

**Driving is out of scope.** Another session owns vehicle movement. The prefab is built so it can be driven later: networked, carrier-ready, saveable. Track animation and a driver seat come later.

## Why ×1.6

The player is 3.0 m tall and 1.0 m wide, and 1.8 m crouched; there is no prone stance. Measured at ×1:

| Space | ×1 | ×1.6 |
|---|---|---|
| Hatch | 1.18 m | 1.89 m, crouch through |
| Cockpit headroom | 2.21 m | 3.54 m |
| Doorways | 2.9 m | 4.6 m |
| Stair headroom | ~2.2 m | ~3.5 m |
| Hold (floor to roof) | 3.36 m | 5.4 m |
| Length | ~22 m | ~35 m |

The `.blend` is not rescaled. The scale is one constant in the export script, applied to the in-memory copy.

## 1. Export (Blender): `models/vehicles/dune_barge_export.py`

The export uses `_exportlib.export(..., keep_collection="Collection 2", keep_empties=True, fix_inverted=True, prepare=...)`. It writes `Assets/Game/Art/Models/Vehicles/DuneBarge/dune_barge.fbx`. The `.blend` is never saved; everything below is done in memory by `prepare()`:

1. **Scale.** Every root is scaled ×`EXPORT_SCALE` (1.6) about the world origin, then transforms are applied, so the FBX carries unit scale.
2. **Groups.** Two empties, `GRP_Exterior` and `GRP_Interior`, are added. Every mesh is re-parented under one of them by its collection membership, keeping world transforms. The Unity builder finds the groups by name.
3. **Ladders.** Each ladder mesh (`Mesh_Stair_Ladder_*`) gets a `LAD_<Name>_01` empty at its foot, on the rung line, with `_Top` and `_Exit` children. These are measured from the mesh: the foot is the lowest rung centre, and the top is the upper end. The exit point is 0.5 m past the top, over the surface the ladder leads onto, which is found by raycast. There are five ladders:
   - two from the ground onto the fenders
   - two from the fenders onto the roof
   - one inside, from the hold floor to the starboard hatch

   The ladder meshes stay as they are. They are already scaled, so their rung spacing is cosmetic; the climb is the `Ladder` maths.
4. **Collision (`COL_` islands).** These follow `ArtPipeline.md` and are separate objects, never merged:
   - **Floors, roofs, bulkheads, stern, collar, cockpit floor and cockpit roof:** a box or a convex hull each. Each bulkhead is split into boxes around its doorway (left, right and header). The stern is split around the gun port.
   - **Side walls:** convex hulls in segments, split at the hatch so the hatch stays open: fore of the hatch, aft of it, below the sill and above the head.
   - **Neck:** convex hulls in 30° arch sectors, so the tunnel stays open.
   - **Fender tops:** thin boxes at the walking surface.
   - **Stair:** an invisible **32° ramp** from the deck to the cockpit floor edge. The capsule has no step offset, and 32° is the angle the player ship's boarding ramp walks at.
   - **Large furniture:** one box each from its bounds (bunks, lockers, engines, crates, the gun base, the trestle). Small props get no collision.
   - **Glass:** no structural collider. It gets an `InteractionBlocker` box instead.
5. **Door pivots.** The two bulkhead door leaves are exported as their own objects with their origin on the hinge, which the library already does. This lets the builder make them `ArticulatedPart`s.

## 2. Prefab builder (Unity): `Assets/Game/Editor/Vehicles/DuneBargeBuilder.cs`

The menu item is **Tools ▸ Vehicles ▸ Build Dune Barge Prefab**. It is idempotent and ends in `Verify()`. It is modelled on `DesertCrawlerBuilder` (transform-driven, carrier-ridden, networked) and `SkyCityBuilder` (`COL_` and `LAD_` gathering).

- **Root, `DuneBarge.prefab`:** a `NetworkObject`, identity and save wiring, and the persistence set for a transform-driven hull (`IPersistentEntity`, `ITeleportAware`, carrier). It is registered in `DefaultNetworkPrefabs` with a non-zero `GlobalObjectIdHash` and a stamped save `prefabId`. It must be built out of Play mode.
- **Model:** the FBX nested under a clean identity root. The builder checks that forward is +Z.
- **Colliders:** `COL_*` become a `BoxCollider` when the island is an 8-corner axis-aligned box, and a convex `MeshCollider` otherwise. Their renderers are removed.
- **Riding:** a `WalkerPlatformCarrier` with a carry volume that covers the fenders, roof and whole interior, so riders are carried once the hull moves.
- **Ladders:** a `Ladder` on each `LAD_` marker, set up with `Configure(top, exit)`.
- **Doors:** each bulkhead leaf gets an `ArticulatedPart` (a 100° hinge) and an `ArticulatedPartInteraction`. Doors open and close with E, replicated, and are saved by `ArticulatedPartsSaveable`.
- **Hatches:** a new `HatchPassage` component on each hatch coaming (see below).
- **Interior reveal:** a new `InteriorReveal` component (see below) holding the interior volumes and the `GRP_Interior` renderers.
- **Shelter:** a `SandstormShelter` volume over the interior, so the storm neither damages nor fogs you inside. This follows the player-ship precedent.

## 3. New runtime components

**`InteriorReveal`** (presentation only, with nothing on the network):
- Each machine turns the interior renderers on while its *own* camera is inside one of the interior volumes, or within `revealRadius` (tunable, about 4 m) of an opening: a hatch, the gun port, or the neck bulkhead door.
- Otherwise they are off, and only `GRP_Exterior` draws.
- Colliders are never toggled; collision is identical on every machine.
- It polls a point against a few `Bounds`, like `SandstormShelter`. There are no triggers, so it cannot interfere with the carrier or with streaming.
- Pure maths (`ShouldReveal(point, volumes, openings, radius)`) is unit tested.

**`HatchPassage`** (an `IInteractable`):
- The prompt reads "Climb in" or "Climb out", depending on which side the interactor is on.
- On Interact, the **interactor's own machine** moves its own body, because player bodies are owner-authoritative. The move goes through the project's existing teleport path (`SaveTeleport.Move` or the equivalent owner-side move), from the hatch's outer mark to its inner mark or back.
- The marks are two child transforms set by the builder: the outer one on the fender top, the inner one on the hold floor beside the inside ladder.
- If the landing spot is blocked, it refuses and logs one warning.
- The pure choice of destination is unit tested.

## 4. Multiplayer

- **Doors:** replicated through `ArticulatedPartInteraction`'s existing channel.
- **Hatch passage:** owner-side, so nothing new goes on the wire.
- **Interior reveal:** local presentation only.
- **Ladders:** per-machine maths over a static registry.
- **Riding:** handled by the existing carrier.
- **Verification** happens on a real client as well as the host: board through a hatch, climb every ladder, open the doors, walk the stair into the cockpit, and see the interior reveal on the client's own screen only.

## 5. Persistence

- **Hull:** its transform through the transform-driven hull saver.
- **Doors:** `ArticulatedPartsSaveable`, keyed by hierarchy path.
- **Hatch and reveal:** no state, by design.
- **Verification:** save, quit and reload with a door open, and check that the door state appears in the save JSON.

## 6. Documentation and tests

- **Documentation:** a new `docs/AI/systems/DuneBarge.md`, an entry in `docs/Human/the-systems.md`, a regenerated index, and `dune_barge_BUILD.md` updated.
- **EditMode tests:**
  - `InteriorReveal` maths
  - the `HatchPassage` side and destination choice
  - prefab structure: the network id and prefab id, every `LAD_` has a `Ladder`, both doors are articulated, no `COL_` has a renderer
- **Route check in `Verify()`:** a 3 m capsule probe through each doorway and up the stair ramp, and a 1.8 m crouched probe through each hatch.

## Amendments (2026-09-25, during implementation)

- **Scale is applied in Unity, not in the export.** The FBX stays 1:1 with the `.blend`, and `DuneBargeBuilder` scales the model child ×1.6 after building collision. Reasons: the rig and pivots are left untouched, the FBX matches the source, and `SkyCityBuilder` already does it this way (×1.5). §1.1 is superseded.
- **Hatches are shut by default** (the user's request). Interacting opens the lid through its replicated switch, and the player **crawls through automatically**. `HatchCrawler` is owner-side and modelled on `LadderClimber`: gravity off, crouch held through `PlayerStance.HoldCrouch`, and the hull's colliders ignored for the crawl. This replaces the teleport in §3.
- **No hardcoded keys.** Interaction is whatever the Input System's Interact action is bound to, and prompts say "Climb in" or "Climb out" without naming a key.
- **The rig** (the user's request): `Arm_DuneBarge2`, 43 bones, built on Collection 2:
  - chassis
  - steering pods
  - 30 wheel spin bones
  - door hinges
  - hatch-lid hinges
  - yaw and pitch for the three guns

  The lids' rest pose is shut. The builder puts the `ArticulatedPart`s on the door and lid hinge bones.
- **The groups are a JSON manifest** (`dune_barge_groups.json`) rather than `GRP_` empties, because rigged parts can't be re-parented under a group without breaking their bones.
- **The stair ramp is capped at 32°.** The user's stair is 34°, so the ramp's foot starts slightly behind it.

- **Three variants, 2026-09-25.** The user built Collections 3 (compact) and 4 (lookout cabin). The export, rig and builder became variant-generic:
  - `dune_barge_export.py` finds parts by group, base name and shape.
  - `dune_barge_rig.py` builds one armature per variant.
  - `DuneBargeBuilder` builds the prefabs `DuneBarge`, `DuneBargeCompact` and `DuneBargeLookout`.
  - Rooms are measured with upward rays (`VOL_*` markers).
  - Door leaves swing toward the side with fewer of the barge's own colliders.
  - Any door is also a reveal opening.
  - Each room gets its own `SandstormShelter`.

## Risks

- **Ladders on a moving hull are unproven.** `LadderClimber` turns gravity off and knows nothing of carriers. That doesn't matter while the barge is parked. Once it drives, a climber may slide off, and that has to be tested then.
- **×1.6 applies to everything, props included.** The bunks, gun and crates are placed at crew scale ×1.2–1.5 inside a room that is now ×1.6. They will read a little large. The alternative, rescaling only the architecture in Blender, is the user's choice to make later.
- **Walkability pass, 2026-09-25.** The route checks exposed convex hulls filling the hold and cockpit, a slab across the stair's top, 0.85 m-thick lid boxes over the fender, and a crawl landing inside cargo. The fixes:
  - collision is carved against every room and the stairwell
  - leaf and lid colliders are oriented boxes
  - every set-down spot is slid to where the body fits
  - **the hold ladder is the crawl's stair, not a `Ladder`**, so each variant has four ladders

  The step-off at its top would have set a 3 m body into the 1.9 m hatch.
