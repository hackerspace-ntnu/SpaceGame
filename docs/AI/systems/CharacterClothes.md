---
system: CharacterClothes
layer: characters
summary: "One prefab per garment, skinned by bone name to whichever Raxy wears it, and the outfits made of them"
paths:
  - Assets/Game/Scripts/Presentation/Appearance/SkinnedGarment.cs
  - Assets/Game/Editor/Agents/DrifterClothes.cs
  - Assets/Game/Prefabs/agents/Characters/Raxy/Clothes
  - Assets/Game/Art/Materials/Characters/Raxy
symptoms:
  - "the clothes show a noise pattern in game"
  - "a garment dropped onto a character does not follow the body"
  - "the character it is on has no bone, so the garment cannot be worn by it"
  - "an outfit prefab is missing a piece of clothing"
  - "the body pokes through the poncho mid-stride"
  - "a clothes prefab shows nothing when opened or dropped into a scene"
  - "every piece of clothing wears the character's skin texture"
  - "a garment sits fine at rest and shears into a giant sheet the moment the rig poses"
  - "a garment's sleeve or hem stays frozen in place while the rest of it moves"
  - "a duplicated garment has vertex GROUPS with the right bone names but does nothing when posed"
  - "a belt or pouch near the hip gets dragged along by a swinging arm"
  - "a renamed garment object leaves its old garment prefab rendering nothing, still nested in an outfit"
reads_with: [ArtPipeline, AgentSystem, TalkingMouth]
updated: 2026-09-26
---

# Character Clothes

Clothes modelled on a character in Blender, shipped as one prefab per garment and worn by a character prefab as nested instances bound to its skeleton.

**Scope:** [SkinnedGarment.cs](Assets/Game/Scripts/Presentation/Appearance/SkinnedGarment.cs), [DrifterClothes.cs](Assets/Game/Editor/Agents/DrifterClothes.cs), the garment prefabs in [Raxy/Clothes/](Assets/Game/Prefabs/agents/Characters/Raxy/Clothes), their materials in [Materials/Characters/Raxy/](Assets/Game/Art/Materials/Characters/Raxy). Used by [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs) and [DrifterRigSync.cs](Assets/Game/Editor/Agents/DrifterRigSync.cs).
**Related:** [ArtPipeline.md](ArtPipeline.md) (the .blend, the rig, the export) · [AgentSystem.md](AgentSystem.md) (the drifters) · [TalkingMouth.md](TalkingMouth.md)

## Model

- **Source:** `raxy.blend`'s `Clothes` collection, skinned meshes named `Clothes_*` on the body's rig. The original eight (2026-09-25): `Poncho`, `Pants`, `Shorts`, `Armor`, `ArmorStrap`, `Belt.poncho` (renamed from `Belt` 2026-09-26, see Gotchas), `Backpack`, `Bracelets` (the user's eight bracelet rings, joined into one object). A further thirteen added 2026-09-26: `Belt.standard`, `Buff_Armor_standard`, `Poncho_Beige`, `Poncho_green`, `Sweater_green001`, `Sweater_purple`, `Sweather_red`, `TankTop` (colour/style variants of the originals), and `skirt`, `skirt_Male.001`, `skirt_workBelt`, `Work_belt`, `Work_Belt_Flask` (a new waist outfit). All are skinned by copying the body's weights from the nearest body face — the originals unrigged from the start, the 2026-09-26 batch because a Blender duplicate/append had carried vertex GROUPS (the right bone names) without ever getting a paint pass, which looks identical to a real weight until the rig poses (see Gotchas). Finger and toe bones fold into the hand and foot; a nearest-face search for a waist-level garment is scoped to torso/hip/leg bones only, or it bleeds `LeftHand`/`RightHand` weight from the resting hand (see Gotchas). Smoothed twice, at most 4 bones a vertex.
- **One prefab per garment** in `Raxy/Clothes/Clothes_<Name>.prefab`: a `SkinnedMeshRenderer` on the FBX's mesh, a `RestPose` child drawing the same mesh on a plain `MeshRenderer`, and a `SkinnedGarment` that carries its bones **by name**. The prefab is saved unworn: skin off, rest pose on, so it shows when opened, previewed or dropped in a scene alone. Put on someone, the skin switches on and the rest pose switches off in the editor. In play the rest pose is deleted, before the ragdoll (which measures every `MeshFilter` at death, inactive ones included) could count it as body. A prefab cannot reference bones outside itself, so the garment binds to the skeleton of whatever it is parented under, in play and in the editor alike (`[ExecuteAlways]`). A binding already saved in a character prefab is only checked.
- **An outfit is which garment prefabs a character wears**, as nested instances under its `Model`, bound to its bones. `SculptRecipe.Outfit` names the set a new build is dressed in: `Drifter_RaxyPoncho` (poncho, pants, belt, backpack, bracelets — what the user left showing in Blender) and `Drifter_RaxyArmor` (armour, armour strap, shorts — what they left hidden beside it). The plain Raxy and its Slate/Sage/Ash/Mauve skins wear nothing. After the build the outfit is the prefab's own: add or remove garments in Prefab Mode.
- **Colours are materials, one per look**, at `Materials/Characters/Raxy/Raxy_<Look>.mat` (URP/Lit): Poncho, Pants, Shorts, Armor, Leather (belt, armour strap), Rust (backpack, one bracelet), Bronze, Brass, Mouth for the inside of the mouth, and, added 2026-09-26, LeatherLight, PonchoBeige, PonchoBeigeTrim, PonchoGreen, SweaterGreen, SweaterPurple, SweaterRed, TankTop, White (both skirt variants share it), Flask. The FBX importer's external object map points each `raxy_<look>` material at its `.mat`, so every instance of the FBX, and every garment prefab made from it, wears them. Base colours are Blender's, converted linear → gamma with the standard sRGB OETF (`1.055·L^(1/2.4) − 0.055`, matching what the original set was converted with). Metallic is Blender's only on armour and bracelets: Blender had metallic 1 on the cloth and the skin too, and in URP a metallic cloth has no diffuse and goes near-black. A blend material whose colour is byte-identical to an existing one (a Blender duplicate that never got recoloured, e.g. `raxy_leather.002/.003` → `raxy_leather`) is remapped straight to the existing `.mat` instead of getting a new one.
- **Shaded smooth**, with edges sharper than 60° kept hard (the rims of the belt, straps and bracelets, and the deepest fold-overs).

## Key types

| Type | File | Role |
|---|---|---|
| `SkinnedGarment` | [SkinnedGarment.cs](Assets/Game/Scripts/Presentation/Appearance/SkinnedGarment.cs) | `boneNames`, `rootBoneName`; `Bind()` on `OnEnable` / `OnTransformParentChanged`; logs a bone the wearer lacks |
| `DrifterClothes` | [DrifterClothes.cs](Assets/Game/Editor/Agents/DrifterClothes.cs) | `Prefix` (`Clothes_`), `IsGarment`, `FolderFor(characterPrefab)` = `<its folder>/Clothes`, `EnsurePrefabs(fbx, folder)`, `Dress(model, outfit, folder)` |
| `SculptRecipe.Outfit` | [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs) | Garment names a new build wears (`RaxyOutfit(...)`) |

## Flows

1. **Model** a garment in `raxy.blend`, named `Clothes_<Name>`, in the `Clothes` collection, parented to `Human_Rig` with an Armature modifier and weights; shade it smooth.
2. **Export** `raxy.fbx` (see [ArtPipeline.md](ArtPipeline.md)). A garment with a new material needs a `Raxy_<Look>.mat` and a remap on the FBX's import settings, or it ships the FBX's embedded material.
3. **Garment prefabs:** `DrifterClothes.EnsurePrefabs` makes a missing one and keeps mesh, bone names and bounds of an existing one in step. It sets materials only when making one. Run by *Build Drifter NPCs* and by *Sync Drifter Rigs From FBX*.
4. **Dress:** a new build → `DrifterClothes.Dress` removes the FBX's own garment copies (a fresh model instance carries them all), then instantiates the outfit's prefabs and binds them. By hand: drag a garment prefab onto a Raxy's `Model` in Prefab Mode; it binds on the spot.

## Multiplayer

Nothing to replicate. Clothes are part of the character prefab, so every machine spawns the same ones, and binding by name gives the same result everywhere. The outfit prefabs are registered in both network prefab lists like every drifter. Changing clothes during play is not supported: that would be state to replicate.

## Persistence

None beyond the prefab: a saved drifter respawns from its prefab id, wearing what its prefab wears.

## Gotchas

- **A skinned garment on its own draws nothing.** With no wearer its renderer has 0 bones against the mesh's 67 bind poses, and Unity renders nothing and says nothing. The first garment prefabs shipped like that and looked empty. The `RestPose` view is the fix. Do not delete it from a garment prefab.
- **A flat-shaded garment reads as a noise pattern in game.** The user modelled every garment flat (`use_smooth` off on all 1280 poncho faces), and a crumpled low-poly surface lit per triangle looks like speckle. In Unity the tell is ~3 vertices per triangle on the mesh; smooth, the poncho drops from 3557 to 832. Fix it in Blender: shade smooth, sharp by angle.
- **`ApplySkin` paints every EMBEDDED slot with the skin.** Garment and mouth slots survive only because the importer remaps them to project materials (`AssetDatabase.IsSubAsset` false). A new garment material with no remap gets the Raxy's skin texture.
- **The builder unpacks the FBX, garments and all.** `Dress` strips every `Clothes_*` child that is not an instance of a prefab in the clothes folder. Do not rename a garment's GameObject inside a character; the prefix is how it is found.
- **Clothes are measured out of a character's height.** `TryMeasure` and `AlignSoleToRoot` skip garments, so every outfit of one body is scaled to the same 3 m and stands on its soles, not its trouser hems.
- **Nearest-face weights do not stop the body poking through.** Mid-stride, the swinging arm and the hip show through the poncho's side, and a thigh through the pants. The garment follows the skin it was nearest to at rest, not the limb that moves into it. Fix per garment in Blender (weight the poncho's side to the upper arm, or inflate it). Hiding the covered body is not built.
- **A vertex GROUP existing is not the same as a vertex being WEIGHTED, and the rest pose cannot tell them apart.** Six of the eight garments added 2026-09-26 (`Buff_Armor_standard`, `Poncho_Beige`, `Poncho_green`, `Sweater_green001`, `Sweater_purple`, `Sweather_red`) each carried a ~84-vertex patch with zero total weight in any group — the rest of the mesh had real paint (up to 0.99 in a group), so `len(vertex_groups) > 0` and even "does this vertex have a `groups` entry" both say yes right up until you sum the weights. At rest those vertices just don't move, same as everything else, so nothing looks wrong until the rig poses and that patch stays glued in place while its neighbours swing. Check by summing `vertex.groups[*].weight` per vertex, not by counting groups.
- **A garment's own object transform has to be identity before the Armature modifier deforms it correctly — a non-uniform scale or an extra rotation shears it once posed, even though the mesh looks perfect at rest.** The 2026-09-26 batch had garments duplicated from elsewhere and left with real loc/rot/scale on the object instead of on the mesh, unlike every working garment (Poncho, Armor, TankTop), which sits at identity. At rest the deform is a no-op, so this is invisible until something moves — then it reads as a giant sheared/inflated blob. Fix by baking `matrix_basis` into the vertex coordinates and resetting the object to identity; if the baked matrix has a negative determinant (two of the batch did, both belt-family pieces), the mesh comes out mirrored/inside-out and needs its faces flipped too, in that order.
- **A waist-level garment's nearest-face weight search bleeds hand/arm weight, because the rest pose has the hands right next to the hips.** A belt or pouch sized and placed at the hip will find `LeftHand`/`RightHand`/`LowerArm` as its "nearest" body geometry for some vertices, since a resting hand and a hip occupy the same neighbourhood in space. The tell is a belt with a `LeftHand` vertex group. Scope the candidate search to torso/hip/leg bones (`Hips`, `Spine`, `Chest`, the four leg bones) for anything worn at or below the waist.
- **Renaming a `Clothes_*` object in the `.blend` orphans its old garment prefab — nothing re-links it.** The user's original belt (`Clothes_Belt`) became `Clothes_Belt.poncho` on 2026-09-26 (to sit alongside a new `Clothes_Belt.standard`). `DrifterRigSync` matched the new name and made `Clothes_Belt.poncho.prefab`, but the outfit prefab nesting the *old* `Clothes_Belt.prefab` kept pointing at it, and that prefab's `SkinnedMeshRenderer` went to a `NULL` mesh on reimport — invisible belt, no console error. Nothing checks for this. After any rename, grep the old prefab's GUID across `Assets/` for outfit prefabs still nesting it, swap the nested instance for the new one (`PrefabUtility.InstantiatePrefab` in place of the old child, matched transform, re-`Bind()`), and delete the orphaned prefab.

## Extending

1. **A new garment:** model it on the body in the `Clothes` collection, rig it (Armature modifier + weights), shade smooth, add its `.mat` and remap if it has a new colour, re-export, then *Sync Drifter Rigs From FBX* (or *Build Drifter NPCs*) makes its prefab.
2. **A new outfit prefab:** a `RaxyOutfit("<Name>", "Clothes_…", …)` recipe, then *Build Drifter NPCs*. Or duplicate an outfit prefab and swap garments by hand. That prefab has no recipe, so the builder does not know it; register it with *Tools ▸ SpaceGame ▸ Multiplayer ▸ Sync Network Prefabs*.
3. **Colour variants of a garment:** a second `.mat`, set on a copy of the garment prefab. The mesh stays shared.
4. **Clothes for another drifter** work the same way, provided its garments are skinned to its own skeleton in its own `.blend`. A garment binds only to a rig with every bone it names.
