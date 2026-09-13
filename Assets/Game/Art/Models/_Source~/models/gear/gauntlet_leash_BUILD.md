# Gauntlet Leash — build record

`models/gear/gauntlet_leash.blend`, built 2026-09-03 by `gauntlet_leash.py`;
shipped by `gauntlet_leash_export.py` to `Assets/Game/Art/Models/Items/gauntlet_leash.fbx`.
One collection, `Coll_GauntletLeash`. The tether launcher worn on the forearm,
authored against the deck of the bracer worn on it.

The previous `leash_gauntlet.py` was rejected for reading as "all metal brace".
Here the brace is the worn bracer's, and the device is rope gear: a big spool of
wound hemp is the hero mass.

## The bracer is not in this model (2026-09-04)

Every gauntlet built before this date appended
`components/props/gauntlet_base.blend` and shipped a copy of the bracer inside
its own FBX. It does not any more: the player wears the Mount variation on both
forearms permanently (`gauntlet_base_export.py` ships it, Unity's
`ForearmBracers` seats it), and a gauntlet is only the device that stands on its
hardpoint deck. `strip_bracer.py` took the ten `Mesh_GauntletBase_*` objects out
of this file; the device was not touched, and the generator lost its
`append_base()` call in the same change, so a regeneration lands here too —
proven by a control diff of every object's vertex count and bounds.

**Everything below still measures the device against the bracer's deck and
shells, and all of it still holds** — the bracer is in exactly the same place
relative to the arm, it is simply worn rather than carried. Only the counts and
bounds that summed the two are restated.

## Built twice — the second cut is 2x linear

The first cut of this device (same day) was correct but read too small on the
astronaut: "quite visible; do not be afraid of size". Every constant was
**re-derived at double size rather than the mesh being scaled**, because three
families of number must not double:

- **embeds** stay 2-4 mm (a scaled embed becomes 8 mm and the part looks sunk);
- **bevels** stay `BEVEL_W` 3 mm (a 6 mm chamfer reads as melted);
- **rope gauge** stays 12 mm — the winding went from ten turns to **twenty**
  at the same pitch instead of doubling into a hawser.

The growth went up and forward over the back of the hand. Two things changed
shape rather than size:

- **The fairlead moved off the deck onto a forward bracket.** At r 0.148 the
  spool overhangs the deck's front edge, so an eye standing up off the plate
  would have been *inside* the drum — `rope_tangent_point()` now raises rather
  than returning a bogus tangent when the eye is inside the winding circle. The
  pedestal on the plate carries a bracket reaching forward to the eye at
  y 0.020, ahead of the wrist.
- **The cradle plate did not grow in x.** The deck is the base's and unchanged,
  and its 10 mm edge bevel caps the plate at x ±0.060; the device's width is
  carried by the bearing feet, which reach x ±0.192 above the deck plane.

## Reuse

| What | From | How |
|---|---|---|
| `tube_path` | `components/mechanical/panel_control.py` | the rope lead |

`components/props/leash_device.blend` was looked at and **not** reused: its
`Coll_Leash_Spool` is one fused mesh (grip, receiver and drum in a single
object) so the drum cannot be lifted out on its own, and at 0.040 m radius it
is a thimble against a 0.296 m spool. Nothing in that file was touched.

## Decomposition (`Mesh_Leash_<Part>`, all origins at the wrist unless noted)

| Object | What | Tris |
|---|---|---|
| `Cradle`   | plate sunk 3 mm into the deck (x ±0.060, y 0.112..0.292, inside the deck's edge bevel), a 10 mm bearing foot under each upright reaching x ±0.192 above the deck plane, two stadium uprights (x ±0.148..0.176, y 0.134..0.278) whose semicircle is centred on the axle | 276 |
| `Spool`    | two flanges r 0.148, 28 mm thick at abs(x) 0.116..0.144; barrel faces orange | 184 |
| `Winding`  | the hemp: a loft along X, x ±0.120, radius alternating 0.1205 / 0.124 every 6 mm — twenty 12 mm turns | 1,636 |
| `Axle`     | chrome, r 0.022, x −0.200..0.192, through both uprights into the boss and the hub | 44 |
| `Ratchet`  | 12-gon wheel r 0.040 at x −0.183 (2 mm into the upright) and a hex boss r 0.034 at x −0.199 closing the axle | 64 |
| `Pawl`     | bar from its chrome pin at (−0.183, 0.262, 0.470) — inside the upright's stadium — down onto the wheel, 6 mm into its rim | 72 |
| `Lever`    | hub r 0.040 at x 0.186, hex nut (x 0.209, the widest point), arm 130 mm leaning 35° back toward the elbow, rubber grip | 160 |
| `Fairlead` | pedestal on the plate (y 0.114..0.150), a bracket reaching forward to y 0.026, and a torus eye major 0.042 minor 0.018, axis along the arm. The bracket meets the ring at its **bottom**, 6 mm below the hole, not through its centre — a neck run to the eye's axis would plug the hole the rope goes through | 408 |
| `RopeLead` | r 0.006 hemp from inside the winding, off it at the computed tangent point (0, 0.191, 0.309), through the eye, a sag point, into the hook's ferrule | 196 |
| `Hook`     | **origin at its eye (0, −0.070, 0.276)**; built in a local frame (eye at origin, shank +Y, mouth +X) and seated by `hook_frame()`, 35° below horizontal pointing −Y; ferrule, eye torus, shank, 210° arc (own partial-torus helper), squared tip, gate bar | 436 |
| `muzzle`   | empty, identity rotation, at the eye's centre | — |

Device total **3,476 triangles** (budget 6,000), which is the whole file now:
the bracer's 3,924 are worn rather than carried.

## Materials (all palette, linked; nothing added)

`Mat_Metal_Steel_Dark` (cradle, flanges, hook, lever arm, boss),
`Mat_Metal_Steel_Worn` (eye, ratchet, pawl, hub), `Mat_Metal_Chrome_Scuffed`
(axle, pins, nut), `Mat_Paint_Safety_Orange` (flange rims — the one accent),
`Mat_Fabric_Rope_Hemp` (winding, rope lead), `Mat_Plastic_Rubber_Black`
(lever grip). Index 0 is dark steel, so bevel faces land on a metal.

## Measured

Frame: arm +Y, wrist at y 0, elbow +Y, forward −Y, dorsal +Z, +X thumb side.
Export maps Blender (x, y, z) → Unity (−x, z, −y).

| | Blender | Unity |
|---|---|---|
| FBX bounds min | (−0.2090, −0.1622, −0.1916) | (−0.2090, −0.1916, −0.3600) |
| FBX bounds max | (0.2090, 0.3600, 0.5780) | (0.2090, 0.5780, 0.1622) |
| size | (0.418, 0.522, 0.770) | (0.418, 0.770, 0.522) — longest **0.770** on Unity Y |
| `muzzle` | (0, 0.0200, 0.3300), rot 0 | (0, 0.3300, −0.0200); +Z forward through the eye toward the hand |
| `Mesh_Leash_Hook` origin | (0, −0.0700, 0.2760) | (0, 0.2760, 0.0700) |

Device-only bounds (no base): min (−0.208, −0.162, 0.208), max (0.209, 0.354, 0.578).

Relaxed envelope, checked against the printed bounds — the generator prints
this line on every build:

| Limit | Value | Worst part |
|---|---|---|
| abs(x) ≤ 0.210 | **0.209** | lever nut |
| y ≥ −0.240 (forward) | **−0.162** | hook tip |
| y ≤ 0.360 (elbow) | **0.354** | spool flange |
| z ≤ 0.640 | **0.578** | spool top |
| z ≥ 0.200 forward of the wrist | **0.208** | hook tip |

Only the cradle plate goes below the deck plane (z 0.247) and it is inside the
deck footprint; everything else stands at z ≥ 0.270 except the hook and rope
lead, which hang forward over the hand under the forward rule.

## Geometry rules

Every part is its own object; nothing joined. Embeds are 2-4 mm: plate 3 mm
into the deck, feet 3 mm into the plate, uprights 3 mm into the feet, winding
caps 4 mm inside the flanges, ratchet/hub 2 mm into the uprights, boss 2 mm
into the wheel, nut 1 mm into the hub, bracket 30 mm into the pedestal and
12 mm into the eye's lower tube, both rope ends buried. Flange rims clear the
feet by 2 mm and the uprights by 4 mm. Rotations verified from the printed
bounds: lever max y 0.348 / z 0.539 (leans to the elbow and up), pawl spans
y 0.229..0.270 / z 0.442..0.480 (from its pin down onto the wheel), hook
min y −0.162 / z 0.208 (down-forward over the hand), and the close-up renders
confirm the reading.

## Renders

Lead's script: `scratchpad/gb/leash_{threequarter,top,side,front}.png` — the
threequarter frames the whole device on the arm. Close-ups aimed at the
device: `scratchpad/gb/leash_dev_{threequarter,thumbside,littleside,front,rear}.png`
from `scratchpad/gb/render_leash_device.py` (its cameras were pulled back for
the doubled model).

## Decisions the lead might reverse

- **Orange is on the flange rims**, not the lever grip (which is rubber): two
  296 mm orange discs seen edge-on read from any angle.
- **Spool axle at z 0.430** (top 0.578) — 62 mm under the height limit. There
  is room to go bigger still; the binding constraints are width (0.209 of 0.210
  at the lever nut) and the elbow (0.354 of 0.360 at the flange).
- **The rope leaves the underside of the winding**, at the lower tangent to
  the eye. The top tangent would have looked like it was being wound on.
- **Hook mouth opens to the thumb side** (+X). On the left arm's mirrored
  model it opens to the little-finger side.
- **Ratchet is a plain 12-gon**, no teeth: teeth at this size would be
  greebles, and the brief asks for chunky readable masses.
- `arc_tube` (a partial torus) and the flange-rim tagging call
  `TrackedPart._absorb` / `_tag` directly; if a bent tube is wanted elsewhere
  it belongs in `_buildlib`.
- No armature: the spool and lever could turn, but nothing in Unity drives
  them and `LeashArtifact` only reads `muzzle`.
