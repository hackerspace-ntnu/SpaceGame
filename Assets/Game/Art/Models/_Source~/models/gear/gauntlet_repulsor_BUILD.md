# Repulsor Gauntlet — build record

`models/gear/gauntlet_repulsor.blend`, built by `gauntlet_repulsor.py`; shipped by
`gauntlet_repulsor_export.py` to `Assets/Game/Art/Models/Items/gauntlet_repulsor.fbx`.
The concussive air-blast emitter, first real model — it replaces a greybox of
Unity primitives. One collection, `Coll_GauntletRepulsor`.

**Rebuilt at DOUBLE the device size on 2026-09-03.** The first cut (2026-09-02)
was sized to sit politely on the deck and read as a wristwatch on a 3 m
astronaut. The numbers were re-derived at the new size, not multiplied: embeds
are still 2–4 mm and bevels did NOT scale (a doubled chamfer reads as soap, a
doubled embed floats a part inside its neighbour). The base is untouched —
this build appends the rebuilt `gauntlet_base.blend` (undersleeve wrist rim
moved 0.030 → 0.022) unchanged.

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

- `_gauntlet.py`'s hardpoint constants (`BASE_DECK_Z` 0.250, `BASE_DECK_Y0/Y1`).
- `components/mechanical/panel_control.tube_path` for the two bent conduits.
- `_buildlib` / `_tracked.TrackedPart`: `tube` (coil, stripe, glow ring), `cyl`
  (drums, caps, studs, backplate, hub, bolts, collars), `torus` (cradle
  collar), `slab`/`box` (bracket, throat, cover, strap, bus bar, vanes). The
  ball is `bmesh.ops.create_uvsphere` through `TrackedPart._tag` — the library
  has no sphere primitive.

## Decomposition (17 device objects; every logical part its own object)

| Object | Material(s) | Where (Blender) | Tris |
|---|---|---|---|
| `Mesh_Repulsor_Bracket` | Steel_Worn | stepped deck plate: sunk core x ±0.066, y 0.104..0.316, z 0.246..0.262 (4 mm into the deck, over all four bosses) + table x ±0.150, y 0.116..0.304, z 0.256..0.272 | 88 |
| `Mesh_Repulsor_Throat` | Steel_Dark, Chrome bolts | x ±0.088, y −0.086..0.044, z 0.300..0.470 | 220 |
| `Mesh_Repulsor_Cover` | Safety_Orange | throat top plate, 4 mm sunk, 8 mm proud — the one accent | 44 |
| `Mesh_Repulsor_Ring` | Steel_Dark | the coil: annulus axis Y, centre (0, −0.128, 0.410), r 0.130..0.194, y −0.176..−0.080, rims bevelled 10 mm | 672 |
| `Mesh_Repulsor_Stripe` | Warn_Red | band r 0.190..0.198, 28 mm wide, mid-depth on the coil | 224 |
| `Mesh_Repulsor_Backplate` | Steel_Dark | disc r 0.134, y −0.092..−0.076, closing the bore's rear | 108 |
| `Mesh_Repulsor_Vanes` | Steel_Dark | four vanes in an X + hub r 0.028, at y −0.156..−0.124 in the bore | 92 |
| `Mesh_Repulsor_Glow` | Emissive_Amber | ring r 0.122..0.134, y −0.172..−0.160 — 4 mm inside the mouth | 224 |
| `Mesh_Repulsor_CapLeft/Mid/Right` | Steel_Worn, Brass caps + studs | r 0.052 at x −0.100 / 0 / +0.100, y 0.020..0.320, axis z 0.320; 4 mm overlap between neighbours | 180 ea |
| `Mesh_Repulsor_Strap` | Steel_Dark | clamp x ±0.148, y 0.290..0.314, z 0.358..0.386 | 44 |
| `Mesh_Repulsor_BusBar` | Brass | x ±0.116, y 0.342..0.358, joining the rear studs | 12 |
| `Mesh_Repulsor_Cradle` | Steel_Dark pedestal, Brass collar | pedestal x ±0.046, y 0.110..0.270, z 0.354..0.418; torus at z 0.418, major = the ball's own section radius there (0.1201) | 460 |
| `Mesh_Repulsor_Capacitor` | Emissive_Amber | the glass ball, **Ø 0.280**, **origin at its own centre** (0, 0.190, 0.490) | 624 |
| `Mesh_Repulsor_ConduitLeft/Right` | Steel_Worn pipe, Brass collars | out of the outer drum's top, round the flank at x ±0.172 / z 0.404, into the coil's rear rim at r 0.156 | 252 ea |

Two empties, both identity rotation: `Marker_Emitter` at the mouth centre,
`Marker_Grip` at the wrist joint (the origin).

The first cut's separate `Mesh_Repulsor_Foot` is **gone**: at this size the
foot and the bracket were the same slab on the same deck, so they are one
stepped part.

**Materials, all from the palette, none added:** `Mat_Metal_Steel_Worn`
(index 0 — bevel stamps it), `Mat_Metal_Steel_Dark`, `Mat_Metal_Brass_Tarnished`,
`Mat_Metal_Chrome_Scuffed`, `Mat_Paint_Safety_Orange`, `Mat_Paint_Warn_Red`,
`Mat_Emissive_Amber`; plus the base's own six.

## Measured

Triangles: **3,856**, which is the whole model now (limit 6,000). The bracer's
3,924 are worn rather than carried.

| | Blender (x, y, z) | Unity (−x, z, −y) |
|---|---|---|
| Device min | (−0.198, −0.176, 0.212) | — |
| Device max | (0.198, 0.358, 0.630) | — |
| Whole FBX min | (−0.2090, −0.1760, −0.1916) | (−0.1980, −0.1916, −0.3600) |
| Whole FBX max | (0.1980, 0.3600, 0.6300) | (0.2090, 0.6300, 0.1760) |
| Size | (0.4070, 0.5360, 0.8216) | (0.4070, 0.8216, 0.5360) — longest **0.8216** (Unity Y) |
| `Marker_Emitter` | (0, −0.1760, 0.4100) | (0, 0.4100, 0.1760) |
| `Marker_Grip` | (0, 0, 0) | (0, 0, 0) |
| `Mesh_Repulsor_Capacitor` origin | (0, 0.1900, 0.4900) | (0, 0.4900, −0.1900) |
| Capacitor **diameter** | 0.280 m (r 0.140, measured off the mesh) | same |

Unity +Z on `Marker_Emitter` is Blender −Y: out of the coil, past the hand.
`holdSize` at 1.0× wear = 0.8216.

## Envelope compliance (measured off the built mesh, not intended)

| Rule | Limit | Measured |
|---|---|---|
| forward reach | y ≥ −0.24 | −0.176 |
| elbow end | y ≤ 0.36 | 0.358 |
| height | z ≤ 0.64 | 0.630 |
| width | \|x\| ≤ 0.21 | 0.198 |
| forward of the wrist: floor | z ≥ 0.20 | 0.212 |
| forward of the wrist: width | \|x\| ≤ 0.20 | 0.198 |
| below the deck plane off its footprint | none | only the coil and its stripe, entirely in the permitted forward zone (y −0.176..−0.080) |
| feet sunk into the deck | 2–4 mm | 4 mm (bracket core, z 0.246) |

## Geometry rules honoured

No joins. An automated audit (`scratchpad/gb/check_rep.py`) compared every
pair of touching parts' face planes at 0.5 mm: the only coincidences are the
three identical drums sharing end and tangent planes, which is what three
identical cylinders side by side necessarily do — no two *faces* overlap.
Vane rotations are placed along the same rotated vector they are rotated by,
so position and rotation cannot disagree; verified in the front render (an X
with a hub). Rendered from five angles, headless.

The ball's seat is the one deliberate deep interpenetration: it clears the two
OUTER drums by ~5 mm and nests 22 mm into the MIDDLE one, inside the channel
the outer drums and the cradle pedestal hide. That is forced — the height
budget is deck (0.250) + drum (0.104) + ball (0.280) = 0.634 against a 0.64
ceiling, so a ball sitting clear on top of the bank would put its crown at
0.68. Documented because Unity scales this object: scaled up it grows further
into the middle drum, scaled down it retreats into the socket.

## Decisions the lead might want reversed

1. **Coil centre z 0.410.** Bounded below by the forward floor (a 0.198 m
   radius needs a centre ≥ 0.398 to keep its rim above z 0.20) and above by
   the fold ceiling (≤ 0.442). `RING_Z` — nothing else moves with it.
2. **The bank grew in girth (0.052 → 0.104 Ø) but only 1.9× in length**
   (0.300 vs a true double of 0.340): the fold limit at y 0.36 is a hard wall
   and the drums already reach y 0.352 with their studs. Fat short drums read
   as capacitors; long thin ones read as pipes.
3. **The `Foot` object is gone**, folded into a stepped `Bracket`. The step
   exists because the bank (0.304 wide) is wider than the deck (0.140) and
   nothing may drop below the deck plane off its footprint.
4. **The coil is an annulus (`tube`, bevelled), not a torus** — at 0.40 m a
   round-section torus reads as a tyre. The cradle collar is the torus.
5. **Bevels did not scale**: 4 mm on plate, 10 mm on the coil's rims, 3 mm on
   the strap and cover. Doubling them was the obvious move and the wrong one.
6. **A backplate closes the bore** (not in the brief) — without it the throat
   block is visible through the mouth.
7. **Conduits are Steel_Worn pipe** with brass collars, not rubber hose.
8. **Empties keep identity rotation**, per the brief; if the prefab wants +Z
   along the arm instead, rotate in the prefab, not here.

## Not done

- Not staged on the rig. The device now stands well proud of the base by
  design; the numbers above are the check.
- `LIBRARY.md` / `library_index.json` not regenerated (the lead does it once).
