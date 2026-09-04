# Gauntlet Item Scanner — build record

`gauntlet_item_scanner.blend`, one collection `Coll_GauntletItemScanner`.
Generated 2026-09-03 by `gauntlet_item_scanner.py`; exported by
`gauntlet_item_scanner_export.py` to `Assets/Game/Art/Models/Items/gauntlet_item_scanner.fbx`.

The wrist-top radar console for the item scanner, on the armoured
`gauntlet_base` (Mount variation). Replaces `item_scanner.blend`, which carried
`handheld_terminal`'s Scanner case on the webbing cuff.

**Rebuilt at double size the same day.** The first cut read too small on the
astronaut. Every constant was re-derived, not the mesh scaled: embeds stayed
2-4 mm and the bevels stayed at 3-5 mm, so the machined corners did not turn to
soap and no joint opened into an 8 mm gap. The growth went up and forward over
the back of the hand; the elbow end did not move, because the arm folds. The
base is untouched, and this build appends the fixed `gauntlet_base.blend`
(undersleeve wrist rim at y 0.022).

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

## Reuse

| From | What |
|---|---|
| `Mesh_ItemScanner_Bracket` | was `Mesh_GauntletBase_Deck_Mount`. The hand edit rotated it onto the flank WITH the console, making it that console's bracket rather than the arm's hardpoint, so `strip_bracer.py` kept and renamed it when the rest of the bracer left |
| `components/props/handheld_terminal.py` | `planar_uv` — the screen plate's 0..1 UV layer |
| `components/mechanical/panel_control.py` | `tube_path` — the tapered whip |

Nothing from `handheld_terminal.blend` is appended. Its screen, dial and
antenna meshes were sized for a hand-held case in cream plastic; at this deck,
at this size, they would have needed rescaling, re-materialing and
re-origining. The three objects are built fresh under the terminal's exact
names, so the prefab's serialized references survive.

## Decomposition

| Object | Material slots (index: material) | Tris |
|---|---|---|
| `Mesh_ItemScanner_Plinth` | 0 Steel_Dark | 108 |
| `Mesh_ItemScanner_Housing` | 0 Steel_Dark, 2 Chrome (bolt heads) | 384 |
| `Mesh_ItemScanner_Bezel` | 0 Steel_Worn | 432 |
| `Mesh_Terminal_Scanner_Screen` | **0 Emissive_Green_CRT (front face only)**, 1 Steel_Dark (rim) | 12 |
| `Mesh_Terminal_Scanner_Dial` | 0 Steel_Dark skirt, 2 Chrome pointer, 3 Safety_Orange cap, 5 Rubber body | 252 |
| `Mesh_Terminal_Scanner_Antenna` | 2 Chrome tip, 5 Rubber base and whip | 114 |
| `Mesh_ItemScanner_Lamps` | 2 Chrome rings, 4 Emissive_Amber | 176 |

Device total **1,586** tris (budget 6,000), which is the whole model now — 1,478
as the device was counted before, plus the 108 of the bracket it inherited from
the deck. The bracer's own 3,924 are worn rather than carried.

The housing is one side profile (front face, apron, 25 degree slope, roof
strip, back face) extruded across the arm, plus the antenna lug on the −X flank
and four chrome bolt heads on the apron — fasteners live in the part they
fasten. The bezel and screen are built in a slope-local frame and placed with
`_gauntlet.place`, so their transforms are applied and their origins sit at the
screen centre.

## The plinth, and why the body does not touch the deck

The body is 0.264 m across, twice the deck's own 0.140. A foot at ±0.132 sunk
into the deck plane would put geometry below z = 0.250 far outside the deck
footprint, over the dorsal shell, which the hardpoint contract forbids. So the
only part on the deck is `Mesh_ItemScanner_Plinth` — x ±0.062, y 0.106..0.316,
z 0.246..0.262 — sunk 4 mm, inside the deck's 10 mm top bevel, swallowing all
four bolt bosses (top z 0.254). The body's underside is a flat 0.258 for its
whole length: 4 mm into the plinth, 8 mm clear of the deck plane at the sides,
about 22 mm clear of the shell where it cantilevers past the wrist. Verified
from the mesh: **0 device vertices below z = 0.250 outside the deck footprint.**

## Where it sits (Blender frame: arm +Y, wrist y = 0, dorsal +Z, thumb +X)

| | x | y | z |
|---|---|---|---|
| Plinth | ±0.062 | 0.106..0.316 | 0.246..0.262 |
| Housing (lug to −0.168) | ±0.132 | −0.090..0.314 | 0.258..0.418 |
| Bezel | ±0.1155 | 0.115..0.278 | 0.338..0.422 |
| Screen plate | ±0.098 | 0.131..0.264 | 0.344..0.413 |
| Dial | 0.045..0.099 | −0.141..−0.087 | 0.270..0.324 |
| Antenna | −0.161..−0.139 | 0.271..0.353 | 0.388..0.579 |
| Lamps | −0.111..−0.041 | 0.005..0.035 | 0.332..0.344 |

Envelope check against the relaxed limits: forward −0.141 (limit −0.24, needing
z ≥ 0.20 and |x| ≤ 0.20 — the dial is at z 0.27..0.324, |x| ≤ 0.099);
elbow 0.353 (limit 0.36); height 0.579 (limit 0.64); width 0.168 (limit 0.21).
The elbow limit is what caps the antenna: a 20 degree lean makes 0.200 m of
whip land at y 0.353.

Every contact is embedded 2-4 mm: plinth into the deck (4), body into the
plinth (4), bezel and screen into the slope (3), dial skirt into the front face
(3), lamp rings and bolt heads into the apron (4 and 3), antenna base into the
lug (2), lug into the flank (4). The screen plate keeps 1.5 mm clearance inside
the bezel aperture.

## Pivots (no empties)

Values below are **as the file stands after the 2026-09-03 hand edits** — see
"The hand edits". The generated pose they replaced is in this file's history.

| Object | Blender | Unity (−x, z, −y) |
|---|---|---|
| `Mesh_Terminal_Scanner_Screen` | (−0.2937, 0.0926, 0.0266) | (0.2937, 0.0266, −0.0926) |
| `Mesh_Terminal_Scanner_Dial` | (−0.2148, 0.3310, −0.0454) | (0.2148, −0.0454, −0.3310) |
| `Mesh_Terminal_Scanner_Antenna` | (−0.3098, −0.0410, 0.1766) | (0.3098, 0.1766, 0.0410) |
| `Mesh_ItemScanner_Bezel` | (−0.2964, 0.0879, 0.0276) | (0.2964, 0.0276, −0.0879) |
| `Mesh_ItemScanner_Housing`, `_Plinth` | (0.0822, 0.2410, 0.0266) | (−0.0822, 0.0266, −0.2410) |
| `Mesh_ItemScanner_Lamps` | (0.0822, 0.2759, 0.0266) | (−0.0822, 0.0266, −0.2759) |

The dial's origin is on its axle where the knob meets the front face, and the
antenna's at its root on the lug. Neither is at rotation 0 any more, but the
axle is still the object's own local axis — a hand rotation moved the node, not
the mesh inside it — so Unity's `Euler(0, 0, angle)` and `Euler(x, 0, z)` still
spin and sway them in place **provided they are composed onto the node's rest
rotation**. `ItemScannerArtifact` does that as of 2026-09-03; it used to assign
`localRotation` outright, which flattened both parts to identity on the first
frame the set was switched on.

## Bounds (measured by the export, whole model)

Re-measured 2026-09-04, after the bracer left the model. The figures that
included it are in this file's history; the console itself did not move.

- Blender: min (−0.4971, −0.1123, −0.1054) max (−0.1638, 0.3820, 0.1946)
- Unity: min (0.1638, −0.1054, −0.3820) max (0.4971, 0.1946, 0.1123)
- Size (0.3334, 0.4943, 0.3000) — longest 0.4943 (Blender Y, along the arm).
  `holdSize` at 1.0x wear = 0.4943, down from 0.6871: that was Unity X across
  the arm, and it was the BRACER's width, not the console's.

## The hand edits (2026-09-03)

The lead corrected the seating in Blender. **The .blend is the source of truth
and `gauntlet_item_scanner.py` no longer reproduces it** — that script is
history, `start()` refuses to overwrite the file, and the refusal is load
bearing. Measured from the file:

- **The whole console is rotated onto the flank.** Every scanner object carries
  Blender XYZ euler `(−180, 90, −360)` — that is `Ry(90) · Rx(180)`. The base
  is untouched apart from `Mesh_GauntletBase_Deck_Mount` at `(0, −90, 0)`.
- **The screen and bezel are stretched and individually raked.** Screen scale
  `(1, 1.88455, 1.02647)` at rake `−177.694`; bezel `(1, 1.68246, 1.02682)` at
  `−178.391`. Everything else stays at scale 1. Non-unit scales to five
  decimals are the classic fingerprint of a hand edit and are exactly why this
  file must not be regenerated.
- The export applies no transforms, so **all of that ships into Unity as node
  rotation and node scale**, not baked into the meshes.

## Screen, verified from the data

Re-measured from the hand-edited file, in world space, through the largest
triangle in UV space:

- Plate is **0.1960 m per unit `u` by 0.2502 m per unit `v` — true aspect
  0.7833**, portrait. It was 1.3803 landscape before the hand edit; the 1.88
  Y-scale on the node is the difference.
- `u+` = Blender (0, 0, 1) = Unity **+Y**, the dorsal/up direction on the arm.
- `v+` = Blender (0.285, 0.959, 0) = Unity **(−0.285, 0, −0.959)**, toward the elbow.
- Face normal = Blender (−0.959, 0.285, 0) = Unity **(0.959, 0, −0.285)**: the
  screen looks out over the little-finger flank. (Worn frame: Unity +Z is
  toward the hand, +Y dorsal, +X the little-finger side.)
- `ItemScannerScreen.materialIndex` = **0**.
- **`_FlipX` is no longer authored.** `ItemScannerScreen` measures the plate's
  handedness from these same UV gradients against the face normal and pushes
  the result, so the material's slider is a preview default only. That closes
  the "confirm on a real contact before trusting it" note both this build
  record and the hand-held one carried unresolved.

## Decisions the lead might want reversed

1. **The console stands on a plinth**, rather than the body being a wide foot
   on the deck. Forced by the contract: see above. It also reads as a mount.
2. **Nose reaches y −0.090 (dial front −0.141), 90 mm past the wrist**, at
   z ≥ 0.258 — inside the relaxed forward envelope with 100 mm to spare. Pull
   `Y0` back if it fouls the glove on the rig.
3. **Top at z 0.418 (bezel rim 0.422)**, 168 mm above the deck. Set by the
   25 degree slope over its 0.176 m run plus an 86 mm front face; the face has
   to carry a 54 mm knob with margins.
4. **Antenna is 0.200 m**, as asked. The elbow limit (y ≤ 0.36) is what caps
   it, not height: the tip is at y 0.353, z 0.579. Rooted on a flank lug on the
   little-finger side at the elbow end.
5. **Bezel is `Mat_Metal_Steel_Worn`** against the dark housing, as the one
   tonal step on the console; the orange accent is the dial cap.
6. **The lamps are one object** (`Mesh_ItemScanner_Lamps`), the way the base
   ships its four bosses as one.
7. **Detail did not grow with the size.** Doubling a plain console shows more
   empty surface; the apron carries only two lamps and four bolts. If it reads
   bare on the rig, the apron is where a vent or a stencil should go.

## Renders

`scratchpad/gb/item_scanner_{threequarter,top,side,front}.png` from the lead's
render script and `item_scanner_dev_*.png` with the same script re-targeted on
the console.
