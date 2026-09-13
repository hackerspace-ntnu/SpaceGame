# Dragon Bazooka — build record

A shoulder-fired recoilless launcher with a Chinese temple dragon cast over its muzzle. The
rocket leaves through the dragon's teeth.

Written as the decomposition was decided, not proposed for approval. It exists so a later
reader can see *why* the model is cut this way without reverse-engineering it from geometry.

## Reused from the library

Nothing existed that served. The nearest neighbour was
`components/props/gravel_blaster.blend`, and it was rejected deliberately: it is a *pipe
shotgun* — two short rusted barrels on a wooden stock, a scavenged-junk read — where this
weapon is a lacquered ceremonial object. Sharing the part would have made both worse.

The shared **palette** is reused throughout, and two entries were added to it (below).

## New components

| Component | Why it is separate |
| --- | --- |
| `components/organic/dragon_head.blend` | The ornament is the whole idea of the weapon and has obvious life beyond it — a shrine fitting, a prow, a gate guardian, a boss head. It is also the only *organic* part of an otherwise mechanical assembly, so it belongs in a different category, not just a different file. |
| `components/mechanical/launch_tube.blend` | A barrel has no reason to change when the thing holding it does. The same tube would serve a scavenged pipe launcher or a mounted gun. |
| `components/mechanical/weapon_grip.blend` | A grip is the single most reusable part of any weapon, and it was buried inside the gravel blaster's mesh where nothing else could reach it. Splitting it out is the highest-value part of this build for future work. |
| `components/props/dragon_rocket.blend` | The ammunition is a separate object in the game — it flies, it is instantiated per shot, and it has its own prefab. It could not be geometry on the launcher even if reuse were not a consideration. |

### Variations built

Bold is what this model actually needed; the rest are built ahead, per the skill's
overproduction rule. All differ in silhouette or structure, not only colour.

- **dragon_head** — **Roaring** (jaws wide, tall horns, whiskers, flame mane), Snarling
  (jaws barely parted, horns raked low, no mane — a watchful static fitting), Whelp (half
  scale, stub horns, no whiskers — cheap enough to fly on a projectile).
- **launch_tube** — **Banded** (0.95 m, five bands, deep venturi), Vented (0.78 m, fatter,
  ring of gas vents behind the muzzle), Twin (two 0.86 m tubes on a common yoke).
- **weapon_grip** — **Pistol**, **Fore**, **Saddle**, Spade (twin handles for a mounted gun).
- **dragon_rocket** — **Firework** (0.30 m, four canted fins), **Whelp** (0.15 m, three fins),
  Spent (burnt casing, split nozzle, one fin folded — loot and set dressing).

## How it assembles

Everything is written in **tube space** — the launch tube's origin, on the bore axis at the
breech face — and the assembly is re-origined onto the grip at the end. Measuring the
assembly from the barrel and the hold pose from the hand keeps a change to either from
silently moving the other.

```
                 horns, mane
                     ↑
  [ dragon head ]═════[ launch tube ]══════════[ venturi ]
   y −0.96, z −0.015          y 0                 y +0.098
        ↑ throat              │
        Marker_Muzzle    ┌────┴────┬──────────┐
        y −1.199         fore     pistol    saddle
                       y −0.64   y −0.30    y −0.14
```

- The head drops by `HEAD_BORE_Z × HEAD_SCALE` so its **gullet sits on the tube's bore axis**.
  Getting this wrong does not look wrong; it just fires the rocket through the dragon's chin.
- `HEAD_SCALE = 1.22`. At 1:1 the head's 152 mm jowl was barely wider than the tube's 110 mm
  and the assembly read as a mask taped to a pipe. A figurehead has to be the widest thing on
  the weapon.
- The saddle is turned 180° so its trough opens **downward**: the component is authored as a
  rest something sits in, and here the thing it cradles is the gunner, not the gun.

Unique to this model: the muzzle **tassel** (the one soft thing on an assembly of hard lacquered
shells) and two **sling loops**.

## Articulation — and why there is no armature

The lower jaw is the only moving part. It ships as **its own object with its pivot on the hinge
axis**, so a single local X rotation is the whole roar and the FBX hands Unity a plain transform
the artifact drives directly. An armature for one rigid part turning about one axis would be
dead weight — the same call, for the same reason, as `sucker_puncher.py`'s ram.

## Palette

Two materials added; `palette.py check` confirmed nothing existing was within tolerance of
either.

| Added | Why nothing served |
| --- | --- |
| `Mat_Paint_Lacquer_Vermilion` `#C1272D` r 0.28 | The Paint family's reds were `Mat_Paint_Warn_Red` (#8E2B22, a matte stencilled hazard band at roughness 0.55) and `Mat_Paint_Coral_Faded` (a sun-killed hull tone). Neither reads as the wet ceremonial lacquer a dragon is finished in. This is the only glossy painted surface in the palette, and the low roughness is the point. |
| `Mat_Metal_Gold_Leaf` `#E0B33A` r 0.26 m 1.0 | `Mat_Metal_Brass_Tarnished` (#9C7B3F, roughness 0.45) is scavenged machine brass — dull, dark, deliberately cheap — and using it made the dragon read as plumbing. Gold has to out-shine the vermilion it sits on or the ornament disappears at arm's length. |

Everything else came from the existing palette: `Mat_Hide_Ivory_Spine` (teeth),
`Mat_Metal_Copper_Oxide` (jade collar band), `Mat_Emissive_Amber` (the lit eye),
`Mat_Metal_Steel_Worn` / `_Dark`, `Mat_Neutral_Black_Matte`, `Mat_Plastic_Rubber_Black`,
`Mat_Wood_Ply_Worn`, `Mat_Fabric_Canvas_Faded`, `Mat_Fabric_Rope_Hemp`,
`Mat_Fabric_Flag_Bleached`, `Mat_Metal_Rust_Heavy`.

## Faults found and fixed while building

Worth recording, because each looked fine in code and only failed on inspection:

1. **The boolean deleted two thirds of the head.** Blender's exact solver wants a closed
   manifold; the finished head is a dozen interpenetrating shells sharing one mesh. Pointed at
   the whole thing it reported success and silently removed the horns, teeth, eyes and mane
   (2560 → 788 tris). Now only the cranium loft is bored, and the ornaments are merged in after.
2. **`Part.box(rot=…)` turns a box about its OWN centre.** The pistol grip was built as a stack
   of rotated slabs meant to rake backward; each tilted in place instead and the grip came out
   as loose confetti. Rake belongs in loft station offsets, where the geometry is continuous by
   construction.
3. **`matrix_world` reads stale right after an append.** A freshly appended object reports the
   identity matrix until the view layer updates, which collapsed the jaw's hinge onto the head's
   origin. A jaw that rotates about the wrong point does not fail — it chews sideways.
4. **The eye's taper flipped per side.** `cyl` measures `radius_top` toward +X regardless of
   where the part sits, so one eye domed outward and the other funnelled outward and read as a
   black box glued to the cheek.
5. Whiskers reached 150 mm past the nose of a 260 mm head — 150 mm of gold wire hanging in the
   player's aim. Now they flick forward, roll outboard and curl back over the brow.
6. Fin trim strips were offset in world space rather than through the fin's rotation, so they
   floated beside the canted fins as four gold bars.

## Shipping

`models/gear/dragon_bazooka_export.py` writes three FBXs, because they are three separate
things in the game:

| FBX | From |
| --- | --- |
| `Assets/Game/Art/Models/Items/dragon_bazooka.fbx` | this model file |
| `Assets/Game/Art/Models/Items/dragon_rocket.fbx` | `dragon_rocket.blend`, `keep=` Firework |
| `Assets/Game/Art/Models/Items/dragon_rocket_whelp.fbx` | `dragon_rocket.blend`, `keep=` Whelp |

The two rounds come out of the shared **component** file, which is why `_exportlib.export`
gained a `keep=` filter: a component file holds every variation stacked at the origin, and
exported whole it arrives in Unity as one interpenetrating lump.

Markers carried across the FBX for `DragonBazookaBuilder`: `Marker_Muzzle` (0, −1.199, 0),
`Marker_Grip` (0, −0.290, −0.132), `Marker_Breech` (0, 0.098, 0), `Marker_JawHinge`
(0, −0.9917, −0.0073).

## Decisions you may want made differently

- **`HEAD_SCALE = 1.22`** is a judgement call about presence, not a measurement. Drop it to 1.0
  for a more restrained, military read.
- **The gullet constraint no longer holds, deliberately.** The rocket component is authored
  at 40 mm across to clear the head's 44 mm gullet, but `DragonBazookaBuilder` now scales the
  hero round to **1.95x** (0.585 m long, ~78 mm across) because the flight *is* the effect and
  the original read as a dart. It is spawned ahead of the muzzle on the aim ray rather than
  passed through the mouth, so nothing intersects — but if you ever animate the round actually
  travelling up the bore, the head has to grow or the round has to shrink.
- The **Snarling** and **Spade** variations exist only because the skill asks for overproduction.
  Nothing references them yet.
