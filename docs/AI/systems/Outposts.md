---
system: Outposts
layer: world
summary: "Hand-built outposts as prefabs of decoration pieces, and the Outpost component that settles people at one"
paths:
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Biomes/Outpost.cs
  - Assets/Game/Editor/Terrain/OutpostEditor.cs
  - Assets/Game/Editor/Terrain/Outposts/
  - Assets/Game/Prefabs/Environment/Structures/Outpost/
  - Assets/Game/Art/Models/Environment/Structures/Outpost/OutpostLayouts.json
  - Assets/Game/Art/Models/_Source~/models/buildings/raxy_outposts_export.py
  - Assets/Game/Editor/Tests/OutpostPrefabTests.cs
symptoms:
  - "an Outpost generates but nobody moves in, with a clean console"
  - "Outpost: Generate says No config assigned"
  - "residents stand at the foot of an outpost ladder and never climb it"
  - "residents never sit on the outpost's seats or only stand beside them"
  - "an outpost prefab I hand-edited lost its changes"
  - "the outpost's second copy appears when I raise the settlement size"
  - "a bell frame's deck cannot be walked on and its ladder leads nowhere"
  - "an outpost prefab renders at the wrong place or mirrored compared to the .blend"
reads_with: [Residents, Seats, Ladders, Errands, TerrainGeneration, NavMeshSystem, ArtPipeline]
updated: 2026-10-04
---

# Outposts

An outpost is a camp of tents, scaffolds, towers and fires that people live at. The prefab is the place; the [`Outpost`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Biomes/Outpost.cs)
component settles a list of character prefabs at it. It is a [`Settlement`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/Settlement.cs)
([TerrainGeneration.md](TerrainGeneration.md)), so everything residents do ([Residents.md](Residents.md)) they do here.

## Model

| Idea | Mechanism |
|---|---|
| One component | `Outpost` on an empty GameObject: `outpostPrefab`, `residents` (prefab, count, chance), `culture` (new components get `OutpostCulture`, below), ground knobs. Its inspector is [OutpostEditor](Assets/Game/Editor/Terrain/OutpostEditor.cs): the settlement buttons (Generate, Generate + Bake World NavMesh, Clear) under its own fields |
| It is its own config | `Settlement.Config` is `protected virtual`; `Outpost` returns a `SettlementConfig` made from its fields (one building, the residents, the culture), thrown away on `OnValidate`. `sizeMultiplier` is hidden: at 2 it would place the outpost twice |
| Turn it | The building's yaw is the GameObject's own yaw (`GetSpawnRotation` for the one building); every other settlement ignores the transform's rotation |
| Its culture | [OutpostCulture.asset](Assets/Game/ScriptableObjects/Residents/OutpostCulture.asset) is a copy of `NomadCulture` with **no expedition profile**: with `NomadCulture` Generate demands a war-band muster spot and 6 Warriors and the validator errors. Set as the script's default reference (`Outpost.cs.meta`), applied when the component is added in the editor |
| No beds | An outpost has no `Dwelling`, so every listed copy moves in, in seeded order, and sleeps where it stands (a camp sleeper, [Residents.md](Residents.md)). Population = the list, not a bed count |
| A prefab is a layout of pieces | Every piece is an instance of its own `Deco_<Kind>` prefab, so an outpost brings those prefabs' work posts, seats, fixtures and colliders. `raxy_outposts_export.py` reads the three .blend files and writes [OutpostLayouts.json](Assets/Game/Art/Models/Environment/Structures/Outpost/OutpostLayouts.json) |
| What the pieces lack is added | [OutpostPrefabBuilder](Assets/Game/Editor/Terrain/Outposts/OutpostPrefabBuilder.cs) adds ladders, walkable collision, sit spots and props (below) |

Eleven outposts: `Outpost_Raxy_01` (Raxy_outpost.blend), `Outpost_Raxy_02`..`10` (the nine variants in Raxy_outpost_2.blend, reading order of the sheet), `Outpost_Scavenger_01`.

## Key types

| Type | Role |
|---|---|
| `Outpost` (World) | the component above |
| `OutpostLayoutFile` / `OutpostLayout` / `OutpostPiece` | the JSON: kind, position, rotation (quaternion), scale, in Unity axes relative to the outpost's footprint centre on the ground |
| `OutpostPrefabBuilder` | menu **Tools/SpaceGame/Outposts/Build Missing Outpost Prefabs** (keeps existing prefabs) and **Rebuild All** (rewrites them in place, GUIDs kept) |
| `OutpostLadders` | a `Ladder` for each `BellFrame` and `Scaffold_Tower` piece (their meshes carry a ladder but their prefabs no `Ladder`), measured off the .blend |
| `OutpostCollision` | makes pieces walkable-in (below): stand-in colliders for `BellFrame` and `Scaffold_Walkway`, and the bake-ignored layer for poles, roofs and anything up on a roof |
| `OutpostWalkability` | menu **Check Walkability Of Selected Prefab**: bakes the settlement's throwaway NavMesh over one prefab in a scratch scene and reports each ladder's two ends and every spot |
| `OutpostPosts` | a `Workshop` post for `PumpJack`, `TransformerBox`, `SignalDish_Array`, `Pipe_Manifold`, `ToolRack`, `Salvage_ChainHoist` and a `Kitchen` post for `Cauldron`, `CookingHearth_Spit` (they bring none), a metre in front of the piece's bounds |
| `OutpostSeating` | a sit spot for every loose `Seat_*` piece: seats within 6 m of a fire (`CookingHearth_*`, `Cauldron`) are one circle (`HearthSeat`, group `fire`), the rest `Seat` |
| `OutpostProps` / `OutpostItemModels` | `Carry_Bucket_Metal` and `Carry_Tank_Oil` pieces become `SettlementProp`s resting beside the nearest `GoodsPile` stop; every stop gets two floor `PropRest`s. Other `Carry_*`/`Tool_*` pieces are scenery models of the item |

## Flows
- **Build.** Export (Blender, read-only) → JSON → builder: for each outpost `LoadPrefabContents`, destroy the children, place the pieces (`Pieces`), then `Ladders`, `Collision`, `Spots`, `Props` containers → `SaveAsPrefabAsset` → read back. A prefab open in Prefab Mode is skipped and named.
- **Place.** Put the `Outpost` in a **chunk scene**, assign the prefab and characters, press **Generate + Bake World NavMesh**: the ground is flattened under the footprint, the people stand on any NavMesh surface within `outskirts`, residents are assigned, the world NavMesh is re-baked.
- **At runtime** nothing is outpost-specific: the society gathers the prefab's spots, residents sit (`ResidentSeating`), climb (`Ladder` links), work and haul ([Errands.md](Errands.md)).

## Multiplayer
Scene content, like any settlement. A prop makes `SettlementNetworking.Wrap` stand a networked wrapper round the outpost, whose hash is written when the chunk scene is saved after Generate; Generate an outpost in an unsaved scene and its props act per machine. **Not run on a client.**

## Persistence
Nothing generated is saved ([TerrainGeneration.md](TerrainGeneration.md)); residents, props and doors save as for any settlement. The prop list is `ResidentTuning.carryItems`: `Carry_Tank_Oil` was appended to it (append-only).

## Gotchas
- **Rebuild All overwrites hand edits**, so build once and edit the prefabs, or put changes in the builder ([INVARIANTS.md](../INVARIANTS.md): edit the builder, never the asset it writes). Build Missing never touches an existing prefab.
- **A piece is the whole decoration unless the layout says otherwise.** The author deletes parts of pieces in the .blend (a bell frame keeps only its tower: no roof, bell or rope; roofs lose their weights); the `Deco_` prefab still has them. The export lists each piece's remaining mesh parts and the builder switches the rest off on the instance (`HideRemovedParts`), only the piece's own FBX meshes, never a decoration nested in it (the stool under a cushion pile stays). A prefab whose model is one mesh is left whole. `APieceShowsExactlyTheMeshesTheBlendKeptUnderIt` pins it.
- **The layout is only as current as the .blend.** Re-run `raxy_outposts_export.py` after editing a layout, then Rebuild. A `Root_` empty with no mesh under it (20 in Raxy_outpost_2) is a leftover and is left out; so is anything 150 m from the origin.
- **An outpost names its own heart.** The settlement guesses its walkable heart from the biggest group of places that walk to one another, and counts every surface under each place: an outpost's decks hold as many places as its ground, so on `Outpost_Raxy_02` the guess landed on a deck and all 13 ground places read "an island" (`Generate` would have reported them and the planner skipped them). Every outpost prefab has a `Heart` (`SettlementHeart`) on open ground 2 m past its footprint, which Generate takes over the guess.
- **Some outposts were built off the ground plane**: `Outpost_Raxy_10` 2.4 m up on a platform, `Outpost_Raxy_05` half a metre down. The export puts each outpost's floor (where most of its pieces stand) at y = 0; without it the whole camp floated and no spot had mesh.
- **Pieces carry their author's scale**: the first BellFrame of `Outpost_Raxy_01` is 1.2x, a walkway 1.3x long. Anything added in a piece's frame must use its `lossyScale` (the collision container does; the ladder uses `TransformPoint`).
- **Item prefabs cannot be scenery.** `Carry_*`/`Tool_*` prefabs are networked world items (pickups, rigidbodies, savers); `OutpostItemModels` copies their meshes into a plain model prefab. An item's origin is where a hand holds it, so the model is raised to stand on its floor point and the instance lowered by the same amount.
- **The agent is wider than the author's decks.** The bake erodes every edge by two voxels (0.67 m: radius 0.5 rounded up at voxelSize 0.33) and drops regions under 2 m². A 1.4 m walkway between 1.15 m rails leaves nothing; a 2.4 m bell-frame deck leaves 1 m2; tent poles cut the ground into islands. So inside an outpost `OutpostCollision` (a) gives `BellFrame` (3 m) and `Scaffold_Walkway` (2.6 m) a deck wider than the model, so a resident on the centre line is still on the model's deck, walled in at the model's edge, (b) moves rails, corner posts, thin poles (capsule-only objects under 0.3 m), roofs and anything whose origin is over 2 m up onto the `Ignore Raycast` layer, which `WorldNavMesh.asset`'s layer mask leaves out and the physics matrix still collides. A player is stopped as before; shots and the crosshair pass through those colliders; a resident brushes past them.
- **A resident's deck is reached by ladder only if both link ends have mesh.** `Scaffold_Tower`'s top deck is 2.5 x 1.4 m, too small: its ladder is `navigable = false` (the player climbs it); without that the link retries for 30 s and warns on every load. Run **Check Walkability** after changing a prefab: it counts each ladder's exit floor.
- **A listed character stays out where its role has no place** (a farmer with no farm bed, a drover with no pen): the summary counts only those that moved in, so `4/4 characters` can follow a list of six. Give a prefab with a role a post of that role, or list roleless `Raxy_Casual_*` characters.
- **Generate's throwaway bake has no ladder links**, so a spot on a deck is reported unusable ("an island") there although the world NavMesh at runtime has the link ([Ladders.md](Ladders.md)).
- **Verified 2026-10-04, edit mode only** (scratch scene, flat ground, the settlement's throwaway bake): all eleven prefabs build and `OutpostPrefabTests` pass; **Check Walkability** finds both ends of 13 of the 14 ladders on mesh (floors of 10-26 m2; the 14th is the non-navigable scaffold tower) and 86 of 89 spots usable, 3 of them up a ladder, the other three being the grindstone of `Outpost_Raxy_04` and a seat and a cauldron post of `Outpost_Raxy_07` in tight clutter; a real `Generate` of `Outpost_Raxy_01` with Raxy residents ran clean (1/1 building, 4/4 characters as residents, no problem listed). **Not run:** Play with residents on an outpost, a client, a save/reload, or the world NavMesh bake with an outpost placed in a chunk scene.
- **A new `.cs` can be dropped from the compile** and the console says nothing: check `Library/ScriptAssemblies` or `~/Library/Logs/Unity/Editor.log`.

## Extending
- **A new outpost:** build it in Blender from `Coll_Deco_*` pieces (each `Root_<Kind>` empty with its meshes parented), add the file to `SOURCES` in the export script, run it, then Build Missing.
- **A piece without a `Deco_` prefab:** make the prefab ([ArtPipeline.md](ArtPipeline.md)); an item stood about as scenery needs only its `InventoryItem` asset in `Resources/Items/Tools`.
- **A ladder or deck fix for another piece:** a row in `OutpostLadders.Specs` / `OutpostCollision`, measured off the .blend in the piece's Unity axes (x, y, z) = (-x, z, -y).
- **A new prop:** its name in `OutpostProps.Carried`, if its item is hand-held and has a hold pose ([HandTools.md](HandTools.md)).
