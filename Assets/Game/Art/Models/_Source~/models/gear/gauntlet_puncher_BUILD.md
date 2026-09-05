# Sucker Puncher gauntlet — build record

`models/gear/gauntlet_puncher.blend`, built 2026-09-03 by `gauntlet_puncher.py`,
one collection `Coll_GauntletPuncher`. Exported by `gauntlet_puncher_export.py`
to `Assets/Game/Art/Models/Items/gauntlet_puncher.fbx` (`keep_empties=True`).

The steam-driven punching ram, **at double the first pass's device size** — the
gauntlets read too small on the astronaut, so every part of the machine is twice
the linear size it was. Its two rails come out of `gauntlet_base`'s **Rail**
variation through `_gauntlet.append_rails`, which renames them into the
puncher's own namespace; they did not double either, and nothing else of the
bracer is in this file (see below).

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

## Frame and scale

Gauntlet family frame (`_gauntlet.py`): arm +Y, wrist joint at y = 0, elbow +Y,
forward −Y, dorsal +Z, thumb +X on a right forearm. `_exportlib` maps Blender
`(x, y, z)` onto Unity `(−x, z, −y)`. True suit scale, origin at the wrist bone,
worn at scale 1.

## The stroke is 0.168 m, and 0.34 is not reachable

Two independent bounds, both re-derived from the constants by `audit()`, which
refuses to save if either is broken:

1. **The rails.** They are 0.240 m long (y 0.090..0.330) and the sled has to
   stay on them: `stroke ≤ 0.240 − shoe − end margins`. With a 0.064 m shoe and
   4 mm at each end that is **0.168**. 0.34 m of travel would need a sled of
   negative length — no sled geometry can buy it, and the rails belong to the
   base, which does not change.
2. **The in-line cylinder chain.** Between the fist's forward limit (y = −0.24)
   and the elbow limit (y = 0.36) everything has to fit at rest: the block
   (0.152 deep), the head plate and the rod's pin and clevis (0.048), the piston
   and its engagement, the shell (≥ stroke + those) and the shell's own rear
   steam stub — and the moving half of that list sits one stroke further back at
   rest, so the stroke is counted twice. It collapses to `2·stroke ≤ 0.345`,
   i.e. **stroke ≤ 0.172**, whatever the rails do.

Bound 1 binds at 0.168, bound 2 at 0.172, so 0.168 is the geometric maximum for
a single in-line ram on this arm. What did grow is the fist's **reach**: the
strike face travels from y −0.070 to y −0.238 (against −0.040..−0.210 at half
size), on a head with four times the frontal area.

Ways to beat 0.172 that were considered and rejected: twin cylinders outboard of
the block (pins can then sit at the block's *front*, lifting bound 2 to ~0.25 —
but the pair is 0.23 m wide against a 0.21 limit, and bound 1 still caps at
0.19); a cylinder mounted high over the beam with a forward-reaching top lug
(also ~0.24, but it puts a 0.10 m shell at z 0.58 and the machine becomes a
tower); and a device-carried forward rail extension, which the brief rules out —
the sled rides the base's bars.

## Reuse

| Component | Object | How |
|---|---|---|
| `components/props/gauntlet_base.blend` | `Mesh_GauntletBase_Rail{Left,Right}_Rail` (2 objects) | `append_rails(coll)`, which renames them `Mesh_SuckerPuncher_Rail{Left,Right}` — they are the puncher's track, not a mount, and no other gauntlet has used them |
| `components/mechanical/ram_slide.blend` | `Mesh_RamSlide_Cylinder` | scaled (2, 0.87, 2), anchor at (0, 0.330, 0.306) |
| `components/mechanical/ram_slide.blend` | `Mesh_RamSlide_Rod` | scaled (2, 1.06, 2), clevis pin at (0, 0.098, 0.306), re-origined to `RAM_PIVOT` |
| `components/mechanical/knuckle_block.blend` | `Mesh_KnuckleBlock_Segmented` | scaled 2, mounting face at (0, 0.082, 0.360), re-origined to `RAM_PIVOT` |
| `components/mechanical/panel_control.py` | `tube_path` | the steam line |
| `models/gear/sucker_puncher.py` | `place(obj, matrix, origin)` | copied as `place_at`, plus a per-axis `scale` |

**Non-uniform scale on the ram_slide parts is deliberate.** They are doubled
radially — a fist this size needs a big bore — but not along the arm: a stroke
the rails cannot give does not need a 0.46 m shell, and the elbow limit has no
room for one. The barrel's cross-section is circular about Y, so scaling Y
independently leaves it a cylinder; the result is a short big-bore shell, which
is the honest shape for the force it is meant to make. The rod's 1.06 is the
number that keeps the piston 11 mm inside the gland at full stroke without its
rear face reaching the barrel's end at rest — at the authored length it broke
out of the gland by 0.9 mm, which `audit()` caught.

**Not reused, deliberately:** `Mesh_RamSlide_Rails` (the base has rails) and
`Mesh_RamSlide_Carriage`, whose round bushings are bored for 5 mm round rails.
The carriage is rebuilt here as a gantry sled and keeps the component's name,
because `SuckerPuncherArtifact` finds it by name.

## Layout — the three clearances that decided the shape

- **The sled is a gantry** straddling a cylinder that lies between the rails.
  Its shoes are **stepped, not blocks**: a lower jaw (x 0.046..0.069,
  z 0.262..0.273) wrapping the rail's top-outer corner and an upper jaw
  (x 0.056..0.069, z 0.273..0.296) set 10 mm further out. A solid block put its
  inner-top corner 3 mm inside the shell's gland and end-cap rings — caught by
  the audit on the first build of this pass.
- **The pillars** (x 0.056..0.086) stand outboard of the gland ring (0.0512),
  because the sled passes over the gland at full stroke; the bridge
  (z 0.364..0.400) clears the gland's crown (0.357) by 7 mm and the beam
  (z 0.362) by 5 mm.
- **The boiler's bracket arms cross UNDER the shoe path** (z 0.251..0.258
  against a shoe bottom of 0.262). That is the only route out to the flank: the
  sled sweeps the whole length of the deck, so no arm can cross at deck height
  anywhere without being run over. Their feet sink 3 mm into the deck's outer
  margin at y 0.180 and 0.270, clear of the bolt bosses at y 0.107 and 0.313.

The rod pins to a lug hanging **below** the head plate on the bore's axis, so
its clevis sits in open air between the lug and the gland face; a lug on the
plate's centre would need the clevis inside the shell. The gusset behind the
plate stops at y 0.124, 6 mm forward of the gland face — a longer one runs into
the barrel.

## Objects and origins (Blender / Unity)

| Object | Origin (Blender) | Origin (Unity) | Bounds at rest (Blender) | Tris |
|---|---|---|---|---|
| `Mesh_RamSlide_Carriage` **ram** | (0, 0.294, 0.261) | (0, 0.261, −0.294) | x ±0.092, y 0.262..0.326, z 0.262..0.408 | 368 |
| `Mesh_SuckerPuncher_RamArm` **ram** | (0, 0.294, 0.261) | (0, 0.261, −0.294) | x ±0.100, y 0.080..0.268, z 0.286..0.450 | 332 |
| `Mesh_KnuckleBlock_Segmented` **ram** | (0, 0.294, 0.261) | (0, 0.261, −0.294) | x ±0.134, y −0.070..0.082, z 0.245..0.479 | 1468 |
| `Mesh_RamSlide_Rod` **ram** | (0, 0.294, 0.261) | (0, 0.261, −0.294) | x ±0.037, y 0.093..0.326, z 0.269..0.343 | 324 |
| `Mesh_RamSlide_Cylinder` | (0, 0.330, 0.306) | (0, 0.306, −0.330) | x ±0.050, y 0.130..0.357, z 0.255..0.397 | 324 |
| `Mesh_SuckerPuncher_Cradle` | (0, 0, 0) | (0, 0, 0) | x ±0.030, y 0.152..0.268, z 0.245..0.274 | 144 |
| `Mesh_SuckerPuncher_Boiler` | (0, 0, 0) | (0, 0, 0) | x −0.202..−0.098, y 0.116..0.331, z 0.288..0.430 | 376 |
| `Mesh_SuckerPuncher_BoilerBracket` | (0, 0, 0) | (0, 0, 0) | x −0.164..−0.048, y 0.164..0.286, z 0.247..0.302 | 200 |
| `Mesh_SuckerPuncher_SteamLine` | (0, 0, 0) | (0, 0, 0) | x −0.158..0.002, y 0.321..0.359, z 0.334..0.391 | 100 |
| `Marker_Grip` (empty) | (0, 0, 0) | (0, 0, 0) | GripPoint | — |
| `Marker_Vent` (empty) | (−0.150, 0.288, 0.430) | (0.150, 0.430, −0.288) | the safety valve's crown | — |
| 12 × `Mesh_GauntletBase_*_Rail` | (0, 0, 0) | (0, 0, 0) | the base, unchanged | 4012 |

**`RAM_PIVOT` = Blender (0, 0.294, 0.261) = Unity (0, 0.261, −0.294)**: on the
rail axis (midway between the rails, at rail mid-height) under the sled's
centre. The build asserts all four ram objects share it. **Stroke 0.168 m**
along Blender −Y = Unity +Z (`ramAxis = Vector3.forward`, `ramThrow = 0.168` —
it was 0.17 in the prefab and must be updated).

## Measured bounds (from `describe()`, whole model including the base)

- Blender: min (−0.209, −0.070, −0.192) max (0.190, 0.360, 0.479)
- Unity: min (−0.190, −0.192, −0.360) max (0.209, 0.479, 0.070)
- Size, re-measured 2026-09-04 after the bracer left the model: Blender min
  (−0.2020, −0.0696, 0.2450) max (0.1340, 0.3594, 0.4786), size
  (0.3360, **0.4290 longest**, 0.2336); `holdSize` at 1.0x wear = 0.4290. It
  read 0.670 on the dorsal axis while the bracer was in the file, because the
  bracer's ventral shell was the far end of it.

## Triangles

**3,724** (budget 6,000), which is the whole model now: knuckle block 1,468,
carriage 368, boiler 376, ram arm 332, cylinder 324, rod 324, bracket 200,
cradle 144, steam line 100, and the two rails' 88 — those used to be counted
with the bracer and are the puncher's own since 2026-09-04. The bracer's
remaining 3,924 are worn rather than carried.

## Materials (palette only, none added)

`Mat_Metal_Steel_Worn` (index 0 — bevel stamps 0) sled bridge and pillars, beam,
gusset, lug, cradle, brackets; `Mat_Metal_Steel_Dark` shoes, beam collar and
rib, head plate, boiler drum; `Mat_Metal_Chrome_Scuffed` bolt heads and rivets;
`Mat_Metal_Brass_Tarnished` boiler hoops, gauge bezel, safety valve, outlet
union, cradle bolts; `Mat_Paint_Safety_Orange` once — the sled's bridge plate,
the moving part, echoing the base's collar; `Mat_Emissive_Amber` gauge face;
`Mat_Plastic_Rubber_Black` steam line. `Mat_Paint_Warn_Red` arrives on the
knuckle block, which carries its own arming stripe across its top. The appended
components bring their own palette slots (chrome rod, brass gland).

## Verification

- `audit()` runs before save and raises: the four ram origins equal
  `RAM_PIVOT`; the stroke is within both bounds above; every device vertex, at
  rest AND shifted −0.168 in y, stays inside y ≤ 0.360, z ≤ 0.640, |x| ≤ 0.210;
  anything forward of the base's wrist edge is above z 0.200, within |x| ≤ 0.200
  and no further than y = −0.240; nothing under y 0.090 comes within 4 mm of the
  collar crown (0.2165); no ram part but the rod enters the cylinder (radial
  test — the shell's AABB is 83 mm too tall because of its rear steam stub, and
  the beam legitimately passes over the gland); the piston stays 11 mm inside
  the gland at full stroke.
- Rendered headless at rest and at full stroke from the lead's four angles plus
  a rear quarter, and from a −X flank view: the rod visibly leaves the gland,
  the sled runs to the front of the rails, the fist clears the collar.
- Every part its own object; no coplanar faces between objects (shoe jaws 1 mm
  and 15 mm off the rail's faces, head plate 2 mm into the block's backing
  plate, beam 6 mm into the bridge, orange plate 2 mm sunk and 6 mm proud, feet
  3 mm into the deck, risers 10 mm into the tank).
- Transforms applied on everything; the appended components are re-origined and
  scaled through `place_at`, which bakes the transform into the mesh, so every
  object still ships at scale 1 and rotation 0.

## Decisions the lead may want reversed

1. **Stroke 0.168, not 0.34.** Derivation above; both bounds are asserted in the
   build. The Unity prefab's `ramThrow` must come down from 0.17 to 0.168, and
   `holdSize` up from 0.541 to 0.670.
2. **The ram_slide parts are scaled non-uniformly** (2x bore, 0.87 and 1.06
   along the arm). Uniform 2x would be a 0.46 m shell that does not fit between
   the fist and the elbow.
3. **The boiler moved outboard and grew** (drum now 0.096 across at x −0.150,
   |x| up to 0.202 of the 0.210 allowed) and is carried on two outriggers that
   duck under the sled. Mirror `BOILER_X` and the bracket X constants for the
   thumb side.
4. **The fist sits high** — block top at z 0.479, centre 0.360 — because the
   beam has to clear the cylinder that lies under it. It is well inside the
   0.640 ceiling, and it is what makes the device read at size.
5. **The cradle holds only the barrel's bottom third** (|x| ≤ 0.030): the shoes
   sweep the deck at |x| 0.045..0.069, so nothing standing between them can be
   wider than that.
6. The steam line's rear rim reaches y 0.359 of the 0.360 allowed; it ends at
   the shell's hose knuckle rather than its tip for exactly that reason.
7. Boiler and cradle bands are 12-sided, and the pillars, rib, lug and bracket
   feet are unbevelled — cheap where nothing reads, since the knuckle block
   component alone is 40 % of the device's triangles.
