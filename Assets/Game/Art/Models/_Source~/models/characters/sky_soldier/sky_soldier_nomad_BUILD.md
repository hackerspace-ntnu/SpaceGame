# Sky soldier (Nomad body, helmet) — build record

The long-coated Sky Tribe soldier from the concept image, built on a real Nomad rather than a
primitive mannequin (the first attempt, `sky_soldier.blend`, did not match the concept or the game's
art style). This is the **helmet** version; a flat-cap version is to follow as its own model.

`models/characters/sky_soldier/sky_soldier_nomad.blend`, from `sky_soldier_nomad.py`.

## Reused — both sources only read

| Source | Taken | Left behind |
|---|---|---|
| `~/Documents/Blender/sand_nogs.blend`, Nomad **Maroon** | `Maroon_Suit`, `Maroon_Gloves`, `Maroon_Boots`, `Maroon_Dome_01` (head, seen only through the visor) and their parent `Armature_NomadMaroon` — the Nomads' 65-bone Mixamo rig | hood, brass breathing mask, long cloth strip, pole, rod, packs, pouches, straps, buckles, rings, sash, bands, scarves, shawl |
| `models/characters/nomad/nomad.blend` | the robot helmet, `Robot Helmet Parts`: shell, face, lens, brow, jaw, ear pods, top rail, neck rim, studs | everything else |

The user chose Maroon's body (better than the Nomad's) and the Nomad's helmet.

## Changes made to the copies

- **Moved** to stand at the origin: hips over x = 0, soles on z = 0 (Maroon's soles were at z = −1.589).
- **Suit smoothed** at the user's request: 35 passes of vertex averaging take out the sculpted creases
  (chest dent, crumpled sleeves, crotch and knee wrinkles) and keep the suit's shape. Compared at 0,
  8, 25 Laplacian (no visible change), 30 and 90 averaging passes (90 thins the limbs to sticks).
- **Helmet fitted** onto Maroon's head at its original size (the head-width ratio, 1.26, made it dwarf
  the shoulders), nudged 7 cm back and 5 cm down onto the collar, baked into its meshes (scale 1) and
  bound rigidly to `mixamorig:Head`.
- **Every material replaced from the palette**; the sources' local materials and orphaned meshes are
  purged.

## Garments — sculpt style, small folds

Modelled, not simulated: a cloth simulation was tried first and gave a flat apron in front and a
twisted strip behind. Each garment is a grid laid off the smoothed suit's measured surface (rays from
outside toward the body axis), thickened outward, subdivided once and smooth-shaded.

- **Coat** — `Coat_FrontRight` (204–268°), `Coat_FrontLeft` (272–336°), `Coat_Back` (24–156°), open at the
  sides for the arms. Each row sits 3.5 cm off the suit and never moves inward on the way down, so the
  coat falls from the chest in an A-line. Small vertical folds (≤ 1.1 cm) fade in below the chest; the
  hem waves slightly. Orange trims run down the front opening and along each hem, lifted 1.6 cm off
  the cloth. A ray hit further than 0.42 m from the axis is treated as an arm, not the torso — without
  that, the edges flared out to arm's width and kept it to the hem.
- **Cowl** — a thick collar round the neck and shoulders under the helmet's neck rim.
- **Scarf** — a thick loop over the cowl and a tail blown out behind to the left, drooping, rippling,
  twisting and narrowing (rigid on `Spine2`).

Materials: coat and cowl `Mat_Fabric_Tarp_Azure`, trims `Mat_Paint_Safety_Orange`, scarf
`Mat_Fabric_Sail_Orange`, suit `Mat_Paint_Teal_Deep` (added to the palette for the first attempt), gloves
and boots `Mat_Neutral_Slate_Dark`, head `Mat_Neutral_Black_Matte`, helmet as the Nomad reads: dark shell,
orange face, amber visor, steel rails, brass studs, teal ear pods.

## Rigging

Body parts keep Maroon's skin weights. Garments are weighted **by height** between neighbouring spine
bones (Hips → Spine → Spine1 → Spine2 → Neck). Copying weights from the suit was tried twice: the coat
took the arms' and legs' weights, and with those filtered out a bent spine tore it across the waist.

Verified with Spine1 bent 25°, LeftArm raised 40° and RightUpLeg swung 35°: the largest change in any
coat edge is 2 cm (it was 57 cm with copied weights), trims stay on the cloth, the raised leg moves
under the hem. Every mesh is bound to `Arm_SkySoldierN`; no local materials remain.

## Not done / judgement calls

- **Not exported to Unity** — no FBX or prefab yet.
- ~230k triangles, mostly Maroon's suit (43k vertices) and the subdivided coat; far above a game
  character's budget until decimated for export.
- The coat does not follow the legs: a long stride will pass a knee through the front panels.
- The cowl is rigid on the chest, so a hard head turn can bring the helmet's jaw into it.
- No shoulder guard yet (the concept has one).

## Poncho (live edit, 2026-09-17)

The user reworked the coat toward a poncho by hand (panels stretched over the left arm, scarf
tail dropped, cowl turned orange, a waist piece `Mesh_SkySoldierN_Cowl.001`, a khaki suit
material) because the three-panel coat did not read like the concept. On their request the poncho
was then built in the live session, in `Coll_SkySoldierN_Poncho`, keeping every one of those edits:

- **`Mesh_SkySoldierN_Poncho`** — one piece from the front-right opening (205°) round the front, over
  the left arm and across the back (160°), open only on the right where the rifle arm comes out.
  A shoulder yoke runs from under the cowl to a broad, high shoulder line (2.6 m, dropping toward
  the opening); below it each column falls straight, so it reads as the concept's stiff slab. Column
  radii are measured off the suit (arm hits beyond 0.62 m ignored, so the forearm and hand come out
  under it) and smoothed. The hem slopes to its lowest corner at the left front. 3.5 cm thick,
  subdivided, near-flat (folds ≤ 4 mm).
- **Piping** — `..._Piping_OpeningFront`, `..._OpeningBack`, `..._Hem`: orange strips inset two grid
  steps from each edge, raised off the outer face, as in the painting.
- **Rig** — all four weighted by height between spine bones, Armature modifier first so the shell
  thickens the deformed surface.
- The earlier `Coat_*` panels and trims are **hidden, not deleted**, for comparison.

## To Unity (2026-09-17)

- **Rig fixes** before export, in the live file: `Helmet_EarPods.001` and `Scarf_Tail.001` (the user's
  duplicates) had Armature modifiers pointing at no armature, and the waist piece `Cowl.001` had no
  Armature modifier and the collar's Spine2/Neck weights; it is now weighted by height (Hips/Spine).
  Posed (spine bent and twisted, head turned, arm lowered, leg lifted) every visible piece follows.
- **Export**: `sky_soldier_nomad_export.py` → `Assets/Game/Art/Models/Characters/Nomad/sky_soldier.fbx`,
  flags as `sand_nogs_export.py`, 65 bones, the visible rigged meshes only. The scarf pieces go out as
  `Cloth_SkySoldier_Scarf_*` so the prefab builder dyes them and gives them wind. The rig is slid to the
  origin together with the unparented meshes (helmet, poncho, scarf, waist piece) - moved alone, those
  would have landed 9 cm off their bones.
- **Prefab**: `NomadPrefabBuilder.SkySoldier`, built by `Tools/SpaceGame/Agents/Build Sky Soldier NPC` into
  `Prefabs/Agents/Characters/SkyTribe/SkySoldier.prefab`; fielded through `SkyTribePeople` by the Sky roster
  (war parties) and the Sky City population.
