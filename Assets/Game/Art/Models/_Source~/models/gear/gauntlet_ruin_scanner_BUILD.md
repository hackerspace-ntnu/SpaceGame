# Gauntlet Ruin Scanner — build record

`models/gear/gauntlet_ruin_scanner.blend` → `Assets/Game/Art/Models/Items/gauntlet_ruin_scanner.fbx`
→ the Ruin Scanner artifact, worn on the forearm.

Built 2026-09-02 by `gauntlet_ruin_scanner.py` on the gauntlet base's Mount
variation: a ground-penetrating pulse emitter — a dark-steel housing on the
hardpoint deck with a big emitter horn out of its front, its mouth reaching
over the back of the hand. Supersedes `ruin_scanner.blend` (the webbing-cuff
version), which stays as a historical record.

**Rebuilt at 2x on 2026-09-03.** The first build on the base was sized off
the brief's original envelope (z ≤ 0.42) and the user's verdict on the whole
gauntlet family was that the devices read too small — "quite visible; do not
be afraid of size". Every constant in the script was re-derived at double,
not the mesh scaled: embeds are still 2-4 mm and `BEVEL_W` is still 4 mm,
both of which a mesh scale would have doubled into mush. The growth went up
and forward; the elbow end did not move.

| Part | Where it comes from |
|---|---|
| `Mesh_RuinScanner_Bed` | machined bed on the deck: pedestal under the housing, front step under the horn |
| `Mesh_RuinScanner_Housing` | dark-steel box flared out over the bed, roof corners r 60 mm |
| `Mesh_RuinScanner_Horn` | worn-steel truncated cone, mouth −Y, dished 32 mm for the lens |
| `Mesh_RuinScanner_Bezel` | chrome torus round the mouth lip |
| `Mesh_RuinScanner_Lens` | amber disc, face 2 mm inside the mouth |
| `Mesh_RuinScanner_Stripe` | hazard-red conical band round the horn |
| `Mesh_RuinScanner_Boot` | rubber torus where the horn enters the housing |
| `Mesh_RuinScanner_Panel` | safety-orange plate on the roof — the one suit-armour accent |
| `Mesh_RuinScanner_Lamps` | two amber ready lamps on the panel |
| `Mesh_RuinScanner_SightFrame` | folding rear sight: two posts, crossbar, chrome hinge pin |
| `Mesh_RuinScanner_SightPost` | front sight post with a chrome bead |
| `Emitter` | empty at the lens centre on the mouth plane — the prefab's `muzzle` |

**3,076 triangles**, which is the whole model now (budget 6,000). The bracer's
3,924 are worn, not carried, and are counted once for the player rather than
once per gauntlet. Export: `gauntlet_ruin_scanner_export.py`
(`keep_armature=False`, `keep_empties=True`, `describe(worn_scale=1.0)`).

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

The arm is the bracer, worn permanently — nothing of the sleeve is authored
here, and since 2026-09-04 none of it is in this file either.
From the old `ruin_scanner.py` the *idea* is kept (housing + horn + lens +
lamps + arming stripe) and nothing else: its cuff, spine and clamp bands are
the legacy generation `_gauntlet.py` says not to use.

## Decomposition

Every logical part is its own object. The split follows what someone would
want to select: the horn can be reshaped without touching the bezel or lens,
the lens can take a Unity shader without touching the horn, the panel can be
recoloured without touching the lamps, the sight frame can be folded on its
own origin. Fasteners live inside the part they fasten: the hinge pin is in
the sight frame, the bead is in the post. Nothing is joined.

**The bed is one object, not a pedestal plus a cradle.** At 1x the housing
fitted inside the deck and stood on it directly, with a separate cradle block
in front holding the horn. At 2x the housing is wider than the deck, so the
load path had to become an explicit pedestal — and a pedestal and a cradle
that meet on the deck share a plane wherever the seam is drawn (side faces if
they overlap in y, end faces if they abut, bottom faces either way). It is
one machined piece in reality, so it is one object here: a stepped block
extruded across the arm by `prism(axis='X')`, 30 mm tall under the housing
and 60 mm tall under the horn.

## Materials

**No palette additions.** Seven, all in use elsewhere: `Mat_Metal_Steel_Dark`
(bed, housing, sight frame, post — the base's own hardware colour),
`Mat_Metal_Steel_Worn` (horn), `Mat_Metal_Chrome_Scuffed` (bezel, hinge pin,
bead), `Mat_Paint_Safety_Orange` (roof panel), `Mat_Paint_Warn_Red` (stripe),
`Mat_Emissive_Amber` (lens, lamps), `Mat_Plastic_Rubber_Black` (boot).
Material index 0 of every part is dark steel, because `bmesh.ops.bevel`
stamps its new faces with index 0. The stripe and panel are not bevelled for
that reason — a bevel would edge the paint in steel.

## Frame and the measured numbers

Family frame: arm along +Y, wrist joint at y = 0, elbow +Y, forward −Y,
dorsal +Z, +X the thumb side of a right forearm. Export maps Blender
(x, y, z) → Unity (−x, z, −y). Origin at the wrist bone, worn at scale 1.

Measured on the exported FBX by `describe()`:

```
                       Blender                          Unity
whole model  min   (-0.2090, -0.0730, -0.1916)     (-0.1900, -0.1916, -0.3600)
             max   ( 0.1900,  0.3600,  0.5940)     ( 0.2090,  0.5940,  0.0730)
size               (0.3990, 0.4330, 0.7856) — longest 0.7856 (Unity Y, dorsal)
device only  x ±0.1505   y -0.0730..0.3160   z 0.2295..0.5940
             Unity: x ∓0.1505   y 0.2295..0.5940   z -0.3160..0.0730
```

Re-measured 2026-09-04, after the bracer left the model: Blender min
(−0.1505, −0.0730, 0.2295) max (0.1505, 0.3160, 0.5940), size
(0.3010, 0.3890, 0.3645). `holdSize` at 1x wear = **0.3890**.

It was 0.7856 while the bracer was in the file, and dorsal rather than along
the arm, because the bracer's ventral shell reached z −0.19. With that worn
instead of carried the dorsal span is the device's own, z 0.2295 to 0.594, and
the longest axis is Blender Y across the arm.

### Pivots

| Object | Blender (x, y, z) | Unity (x, y, z) | Rotation |
|---|---|---|---|
| `Emitter` | (0, −0.0600, 0.3800) | (0, 0.3800, 0.0600) | identity |
| `Mesh_RuinScanner_Lens` | (0, −0.0600, 0.3800) | (0, 0.3800, 0.0600) | identity |
| `Mesh_RuinScanner_SightFrame` | (0, 0.2940, 0.4900) | (0, 0.4900, −0.2940) | identity (the hinge-pin axis) |
| everything else | (0, 0, 0) | (0, 0, 0) | identity |

The `Emitter` is the lens centre on the mouth plane: **60 mm forward of the
wrist joint** (it was 60 mm *behind* it at 1x — the horn now reaches past the
hand) and 380 mm dorsal of it. Identity rotation on purpose — the exporter
carries Blender −Y onto Unity +Z, so the empty's Unity forward is out of the
horn. `RuinScannerPulse` roots the cone at `muzzle.position` and follows it
each frame; direction comes from the camera.

## Layout

Read down the arm from the elbow; metres in the Blender frame.

```
 y 0.320  deck ends
 y 0.316  back of the housing
 y 0.312  back of the bed
 y 0.294  rear sight frame on its pin (posts x ±0.060, top z 0.594)
 y 0.278..0.150  orange panel (x ±0.070, z 0.480..0.488); lamps at y 0.240, x ±0.044
 y 0.140  front sight post (bead top z 0.590)
 y 0.132  horn throat, r 0.075, buried 20 mm in the housing
 y 0.120  the bed's step riser
 y 0.116  rubber boot (y 0.102..0.130), straddling the housing front (y 0.112)
 y 0.102  front of the bed (deck's front edge is 0.100)
 y 0.035..0.005  arming stripe, 3 mm proud
 y −0.028 lens recess floor
 y −0.058 lens face
 y −0.060 mouth plane, r 0.150 — the Emitter
 y −0.073 front of the chrome bezel
```

Housing: x ±0.115, y 0.112..0.316, z 0.272..0.484. Bed: x ±0.066,
y 0.102..0.312, z 0.246..0.276 with the front step to 0.306. Horn axis
z 0.380.

## The envelope, and how it is enforced

The relaxed envelope for the 2x device: |x| ≤ 0.21, y ≤ 0.36 at the elbow,
z ≤ 0.64, forward to y = −0.24 provided z ≥ 0.20 and |x| ≤ 0.20 there, feet
on the deck plane sunk 2-4 mm, nothing below the deck outside its footprint.

`check()` in the generator now **asserts** all six and raises `SystemExit`
on a breach, rather than printing numbers for a reader to compare. Measured:

| Limit | Value | Measured |
|---|---|---|
| width | \|x\| ≤ 0.210 | 0.1505 (the bezel) |
| elbow end | y ≤ 0.360 | 0.3160 (the housing) |
| forward reach | y ≥ −0.240 | −0.0730 (the bezel) |
| height | z ≤ 0.640 | 0.5940 (the sight frame) |
| over the glove (y < 0.03) | z ≥ 0.200 | 0.2295 (the mouth's floor) |
| over the glove (y < 0.03) | \|x\| ≤ 0.200 | 0.1505 |

**The horn's numbers are a fit.** A 0.30 m mouth on an axis at z 0.380 has
its floor at 0.230 — 30 mm clear of the glove limit; at the axis the brief
suggested (0.30) it would be at 0.15, inside the glove. The throat (r 0.075)
must pass under the housing roof, which is why the roof is at 0.484. The
mouth clears the base's collar (top 0.2165) by 70 mm and the dorsal shell
(0.236) by 17 mm at its nearest.

**The housing is flared and stands on the bed.** x ±0.115 against the deck's
±0.070; its underside is at z 0.272, entirely above the deck plane, so
nothing of the device is below the deck plane outside the deck footprint —
only the bed is, and the bed fits inside the footprint with 4 mm to spare on
each side. The bed covers all four bolt bosses, which the hardpoint contract
explicitly sanctions ("sink its foot 2 mm into the deck rather than sit on a
boss").

**The panel is 0.070 half-wide, not 0.115.** The roof rounds off from
x ±0.055; a plate as wide as the housing lands tangent to the shoulder with
0.2 mm of clearance, which flickers. At ±0.070 its edges sink 2.1 mm into
the shoulder and its centre 4 mm into the flat.

## Z-fighting

Checked pairwise on every axis-aligned face in the file (scratch script,
0.5 mm tolerance): **zero coplanar overlapping pairs in the whole file** —
device against device, device against base, and base against base. The 26
pairs the 1x build reported inside the base itself (`Collar_Mount` against
`Undersleeve_Mount` at y = 0.030) are gone: the base was rebuilt with the
sleeve's wrist rim moved to y 0.022, and this build appends that copy.

Designed out on the device:

- bed sunk 4 mm into the deck; housing 4 mm into the bed; the bed's rear face
  is 4 mm forward of the housing's so the two do not share a plane;
- the bed's front face is 2 mm inside the deck's front edge;
- the panel is 4 mm into the roof at its centre and 2.1 at its edges; the
  lamps are 3 mm into the panel and, deliberately, 1 mm above the roof plane;
- the sight frame and post are 4 mm into the roof, the pin 6 mm;
- the horn's throat is 20 mm inside the housing; the bed's step rises 3-13 mm
  into the horn's underside; the boot's top is 5 mm under the roof; the
  stripe is 4 mm into the horn wall; the lens is 1 mm into the recess wall
  and 4 mm into its floor; the bezel's inside edge is 0.5 mm inside the
  lens's rim.

## Rotation verification

Rendered from four angles (scratch renders `ruin_scanner_{threequarter,top,side,front}.png`):
the horn faces −Y (in the side view from +X, forward is screen-left and the
horn is on the left), the lens faces the front camera square-on, the panel
and lamps read from above, and the sight line from the rear frame (z 0.594 at
y 0.294) over the front bead (z 0.590 at y 0.140) passes 40 mm above the
mouth's top (0.530). All object rotations are identity and all transforms
applied — every part is built in place; the two non-zero origins are set
through `finish(origin=...)`.

## Decisions the lead might want reversed

- **Horn axis 0.380, mouth 0.300 across** — the axis is set by the glove
  clearance, not by taste; a lower axis needs a smaller dish.
- **Housing roof at 0.484** — set by the throat radius, not chosen.
- **The bed replaces the 1x build's separate plinth and cradle** — see
  Decomposition. If the lead wants the cradle back as its own object, the
  seam has to be a deliberate 2 mm gap on every shared face.
- **Housing 0.230 wide (x ±0.115)** — it overhangs the base's shell by
  ~36 mm of air on each side. That is the flare that makes it read as a
  machine rather than a box; narrowing it to the bed's width would lose it.
- **`BEVEL_W` and all embeds were NOT scaled.** The brief's instruction; it
  is also what keeps the edges crisp at this size.
- **Sighting is a rear frame plus a front bead post**, both simple; the
  frame's origin is on its hinge pin so it could be folded later, but no
  armature — nothing in the game folds it.
- **No readout screen and no conduit** — the two lamps carry the "armed"
  read, and the base has nothing for a cable to plug into.
- **`holdSize` is 0.3890** as of 2026-09-04 (0.7856 while the bracer was in the
  model, 0.6066 at 1x device size before that). It is not typed on the prefab:
  `GauntletPrefab.HoldSize` is 0, which means "keep the size the artist built".

## Not done here

- `RuinScanner.prefab` still wears the old cuff model: the mesh swap,
  `muzzle` → `Emitter`, `holdSize` 0.79 and re-seating on `GauntletFit` at
  scale 1 are the prefab session's.
- `LIBRARY.md` / `library_index.json` were **not** regenerated — the lead
  regenerates them once for the whole gauntlet batch.
- Not yet seen on the rig.
- The .blend has been deleted and regenerated three times (twice at 1x for
  lamp/roof coplanarity, once for this 2x rebuild), each time within minutes
  and before the file had ever been opened in Blender. From here it is the
  source of truth.
