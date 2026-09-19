# Sky soldier — build record

A long-coated Sky Tribe rifleman from a concept image (2026-09-17): flat-topped officer's
cap, goggles over a face-high collar, an orange scarf streaming behind, a stiff teal slab
coat edged in orange, a layered pauldron, thin armoured legs and heavy boots. No rifle
(decided with the user) and not the small walker robot from the same image.

`models/characters/sky_soldier/sky_soldier.blend`, from `sky_soldier.py`.

## The body: `components/organic/human_mannequin.blend`

The general human template, built first and kept as its own component.

- **Skeleton: the Nomads'.** A copy of `models/characters/nomad/nomad.blend`'s live armature
  (`Armature.001`, 65 `mixamorig:` bones, A-pose, object scale 0.01), only moved: hips over
  x = 0 and soles on z = 0 (the Nomad's soles are at z = -0.587). Same proportions as the
  characters in game - about 3.45 units from sole to crown - and the Nomads' animations and
  humanoid avatar apply without retargeting.
- **Stylized jointed mannequin**, not an anatomical sculpt: head, neck, chest, abdomen,
  pelvis, clavicles, upper arms, forearms, hands, thumbs, thighs, shins, feet, and a dark
  ball at every joint, each its own object. Each is bound **rigidly** to one bone - an
  Armature modifier plus one full-weight vertex group, the way the Nomad's parts are bound -
  so it animates as a skinned mesh and stays a separate part. No object parenting, so every
  transform is scale 1.
- **Three builds**, one collection and armature each: `Coll_Mannequin_Standard` (x = 0),
  `Coll_Mannequin_Heavy` (x = +2.8, torso x1.28, limbs x1.22), `Coll_Mannequin_Slim`
  (x = -2.8, torso x0.84, limbs x0.70). The soldier needed Slim; Standard and Heavy are built
  ahead for other characters. ~9k triangles each.
- Materials: `Mat_Plastic_Cream_Aged` shell, `Mat_Neutral_Black_Matte` joints.

## Decomposition

Every piece of clothing is a component in the new `components/apparel/` category, modelled
**in place** on the Slim body standing at the origin (`components/apparel/_fit.py` reads the
Slim armature), so an assembled character takes each piece exactly where it was built. Each
object records the bone it rides in a `bind_bone` custom property; `sky_soldier.py` binds it
to `Arm_SkySoldier` with that bone, rigidly, like the body.

| Component | Worn | Built ahead | Bone(s) |
|---|---|---|---|
| `officer_cap` | `Coll_Cap_FlatTop` — band, flared crown, dark top, orange rim, stub visor | `Kepi`, `Peaked` | Head |
| `goggle_mask` | `Coll_Mask_TwinGoggles` — dark hood band, two tinted lenses in black rims | `SlitVisor` | Head |
| `high_collar` | `Coll_Collar_FaceWrap` — collar to just under the eyes, orange top and base trims | `Folded` | Spine2 |
| `trail_scarf` | `Coll_Scarf_Streaming` — loop over the collar, a long tail posed blown out behind-left | `Hanging` | Spine2 |
| `slab_coat` | `Coll_Coat_Slab` — back panel, great right-front slab to the shins, narrow left front hung 3 cm inside it, orange edge trims, dark tunic shells | `Tails` | Spine2; tunic on Spine1 / Spine / Hips |
| `shoulder_guard` | `Coll_Guard_Layered` — three lapping plates on the left shoulder, orange-edged | `Round` | LeftArm |
| `limb_armour` | Greaves + knee cops, cuisses, sleeves, vambraces, boots, gloves, both sides | — | the limb bones |
| `belt_kit` | `Coll_Belt_RadioPack` — belt, buckle, radio pack with antenna, pouch | `Pouches` | Hips |

Why the cuts fall where they do:

- **Coat as panels, not a wrap.** The arms have to come out of the sides, and the reference's
  front is one stiff slab over a narrower panel. Panels hang on one elliptical line; the
  overlapping left front hangs on a smaller radius so no two faces are coplanar. Trims are
  separate objects lying 2 mm proud of the cloth.
- **Tunic under the coat** is the mannequin's torso stations grown 12 %, so the side gaps
  show dark cloth instead of the template's cream shell.
- **The goggles' hood is a closed band**: without it the head's cream showed between the
  collar and the cap.
- **Scarf posed, not simulated** — the silhouette the wind leaves; cloth simulation in Unity
  can take over from the same mesh.

## Materials

`Mat_Fabric_Tarp_Azure` (coat, collar, cap band, pauldron — within reach of the reference's
teal), `Mat_Paint_Safety_Orange` (every trim), `Mat_Fabric_Sail_Orange` (scarf),
`Mat_Neutral_Slate_Dark` (cap top, visor, hood band, boots, gloves, belt),
`Mat_Neutral_Black_Matte` (goggle rims, radio), `Mat_Glass_Canopy_Tinted` (lenses),
`Mat_Metal_Steel_Dark` (buckle, antenna), and one addition:

- **`Mat_Paint_Teal_Deep` `#2B6F8E`** — the armour plate and tunic. `palette.py check` found
  nothing near it (the closest were `Mat_Hide_Slate_Teal`, a desaturated creature hide, and
  `Mat_Neutral_Slate_Dark`, near-black).

## Shared library change

`_buildlib.Part` gained `segment` (a lofted limb/torso shell between two arbitrary points,
domed ends) and `sheet` (a thickened grid surface, optionally closed), lifted from the
mannequin script so the apparel scripts share them. The mannequin script was switched to
`Part.segment` and re-run to a scratch file: its per-object report matched the saved
`.blend` line for line.

## Verified

- Renders front, side, back and three-quarter at each step; a raycast sweep from the side for
  any template cream showing through (found the thigh tops, fixed by starting the cuisses at
  the hip).
- Posing `LeftForeArm` 60° moves exactly the left forearm, hand, thumb, wrist ball, vambrace
  and glove.
- Every mesh is bound to `Arm_SkySoldier`, every transform is scale 1, no local materials,
  the only library is `palette.blend`.

## Not done / judgement calls

- **Not exported to Unity.** No FBX, no prefab yet; the Nomad export (`nomad_export.py`) is
  the pattern when it is wanted.
- **Rigid binding** means joints hinge rather than bend: the coat panels swing with the chest
  and the long slab will cross the thighs in a big stride.
- ~22k triangles for the dressed soldier including the hidden body underneath; the body
  parts fully covered by apparel could be hidden or deleted for the game version.
