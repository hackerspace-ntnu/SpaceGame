# Gauntlet Base — build record

`components/props/gauntlet_base.blend`, built 2026-09-02 by `gauntlet_base.py`.
The armoured forearm sleeve, at true suit scale. The user asked for one base
that "looks great on the astronaut, fits the suit, is bulky, realistic in shape
but simple in detail", with every gauntlet built on it.

## It is worn, not built into anything (2026-09-04)

For two days every gauntlet appended a variation of this file, so the game
shipped seven copies of the same armour and taking a gauntlet off left a bare
sleeve. Now **the player wears the Mount variation on both forearms
permanently** and a gauntlet is only the device that stands on its deck:

- `models/gear/gauntlet_base_export.py` ships Mount as
  `Assets/Game/Art/Models/Items/gauntlet_base.fbx`.
- Unity's `ForearmBracerBuilder` makes `ForearmBracer.prefab` from it, and
  `ForearmBracers` on the player seats one per arm through the same
  `ForearmSeat.Apply` a gauntlet goes through — which is why a device authored
  against `BASE_DECK_Z` lands on the deck rather than near it.
- `models/gear/strip_bracer.py` is what took this file's objects back out of the
  seven gauntlets built before the change.

Nothing here was remodelled. The bracer is in the same place relative to the
arm; only who owns it changed.

## Variations

| Collection | Objects (`Mesh_GauntletBase_<Part>_<Variant>`) | For |
|---|---|---|
| `Coll_GauntletBase_Mount` | Plain + Deck, Bosses | **the bracer the player wears.** The only variation anything ships |
| `Coll_GauntletBase_Plain` | Undersleeve, DorsalShell, VentralShell, Collar, HingeFront/Rear, LatchFront/Rear | the same shells with no hardpoint. Nothing uses it since the gear screen's ghost stopped being a bracer; kept as the record of what Mount is Plain *plus* |
| `Coll_GauntletBase_Rail`  | Mount + RailLeft, RailRight | only the two rails are still used, and they go to the Sucker Puncher as its own track through `_gauntlet.append_rails` — the arm never wears them |

11,608 triangles for all three; ~3,600 per variation. No armature, no
empties. Materials: `Mat_Neutral_Panel_Grey` (shells), `Mat_Paint_Safety_Orange`
(collar), `Mat_Metal_Steel_Dark` (deck, rails, plates), `Mat_Metal_Chrome_Scuffed`
(bosses, hinge pins), `Mat_Neutral_Black_Matte` (latch slots),
`Mat_Plastic_Rubber_Black` (undersleeve). **No palette additions.**

## Sized off the rig, not guessed

The first cut used forearm radii of 0.105-0.135 m from an older bind-pose
measurement and vanished inside the suit. The numbers in the script come from
baking the skinned suit in `PlayerCharacter.prefab` and keeping every vertex
bound at least half to the forearm or hand bone, binned 3 cm along the arm and
15 degrees around it, both arms unioned:

| from the bone | sides | palm side | back |
|---|---|---|---|
| wrist (y 0.03) | 0.15-0.18 | 0.18 | 0.17 |
| mid (y 0.15-0.27) | 0.13-0.18 | 0.12-0.14 | 0.20-0.22 |
| elbow end (y 0.33) | 0.15-0.18 | 0.13 | 0.22 |

The bone runs near the palm side of the padded sleeve, so the shell's section
centre drifts 4.5 cm toward the back of the arm from wrist to elbow. Every
station is that envelope plus 12 mm; the back line is held flat at z = 0.236
from the first band on so the hardpoint is one flat plane.

## Frame — and the bug it exposed

Family frame from `models/gear/_gauntlet.py`: arm +Y, wrist at y = 0, elbow
+Y, forward −Y, dorsal +Z, **Blender +X = thumb side of a right forearm**.
The export maps Blender `(x, y, z)` onto Unity `(−x, z, −y)`; the left arm is
the same model at a negative X scale.

Staging this on the rig showed `BodyEquipmentController.WearOnForearm` had
its dorsal cross product the wrong way round on both arms — the deck sat on
the palm side, which the folded rest pose hides. Fixed in the same change
(operand order swapped); verified by a camera on the computed dorsal side
seeing knuckles rather than curled fingers, and by the proximal phalanges
flexing away from it. The measured outline was then mirrored into the
corrected frame.

## Hardpoint contract

| Name | Value | Meaning |
|---|---|---|
| `DECK_Z` | 0.250 | flat deck top, the plane devices mount on |
| `DECK_HX` | 0.070 | deck half-width |
| `DECK_Y0..DECK_Y1` | 0.100..0.320 | deck extent along the arm |
| `BOSS_INSET` | 0.014 | bolt bosses at the four corners, 4 mm proud |
| `RAIL_X`, `RAIL_Z` | ±0.048, 0.272 | Rail variant: rail centres and rail top |
| `RAIL_Y0..RAIL_Y1` | 0.090..0.330 | rail extent |

Nothing on the base crosses y < 0.03 (the collar's wrist edge) or rises above
`DECK_Z` except bosses and rails. `profile()` / `station()` give the shell
outline at any y for devices that wrap the arm.

## Why a clamshell

The first shape — a closed tapered tube with strap rings — rendered as a pipe
coupling. Bracers are two shells over a sleeve, so that is what this is: dark
rubber undersleeve, thick dorsal and ventral shells with rounded rims, a gap
down each side where the sleeve shows, hinge plates with chrome pins on the
thumb side and latch plates with slots on the little-finger side. Squircle
section (superellipse 2.5), wider than tall.

## Verification

- Rendered headless from four angles (shape), then staged on both forearms of
  `PlayerCharacter.prefab` through the editor bridge with the corrected
  seating math at scale 1: no suit poking through, deck on the back of the
  arm, latches outward on a folded arm.
- Every part is its own object; all embeds are 3-5 mm, no coplanar faces.
- Origins at the wrist joint (the family origin), transforms applied.
