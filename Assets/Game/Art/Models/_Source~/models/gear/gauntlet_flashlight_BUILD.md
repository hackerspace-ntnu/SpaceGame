# Gauntlet Flashlight — build record

`models/gear/gauntlet_flashlight.blend` → `Assets/Game/Art/Models/Items/gauntlet_flashlight.fbx`
→ the Flashlight Gauntlet artifact, worn on the forearm. It carries the game's
only light: the URP spot, its long-throw layer and its beam volume all hang on
this model's `Emitter`.

Built 2026-09-03 by `gauntlet_flashlight.py` on the gauntlet base's **Mount**
variation. The user's brief: "reuses the ruin scanner model — some tweaks can
be done… a minimal model pass."

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

## Reuse — this is the Ruin Scanner's machine

The bed, housing, horn, bezel, boot, roof panel and lamps are the Ruin
Scanner's, **at exactly the same numbers**. That device is already a housing
with an emitter horn out of the front, and its dimensions were not chosen —
they were fitted the hard way against the glove's puffy cuff, the fold
envelope and the horn's throat radius (`gauntlet_ruin_scanner_BUILD.md`).
Re-deriving them for a lamp would have been a second answer to a question that
has one.

| Part | Where it comes from |
|---|---|
| `Mesh_Flashlight_Bed` | the Ruin Scanner's stepped bed, unchanged |
| `Mesh_Flashlight_Housing` | the Ruin Scanner's flared box, unchanged |
| `Mesh_Flashlight_Horn` | the Ruin Scanner's dished cone, unchanged |
| `Mesh_Flashlight_Bezel` | the Ruin Scanner's chrome mouth ring, unchanged |
| `Mesh_Flashlight_Boot` | the Ruin Scanner's rubber boot, unchanged |
| `Mesh_Flashlight_Panel` | the Ruin Scanner's safety-orange roof plate, unchanged |
| `Mesh_Flashlight_Lamps` | the Ruin Scanner's two amber lamps — read as charge lamps here |
| `Mesh_Flashlight_Reflector` | **new** — a chrome paraboloid shell in the dish, holed at its throat |
| `Mesh_Flashlight_Bulb` | **new** — the emitter element, through that hole |
| `Emitter` | empty on the dish axis at the mouth plane; where Unity hangs the lamp |

Dropped: `Stripe` (an arming band says *this fires*), `SightFrame` and
`SightPost` (a scanner is aimed; a torch is not). That is the silhouette
decision — the roof reads flat and the eye goes to the dish. Peak height fell
from **z 0.594 to z 0.530**.

## The one real decision: no cover glass

The first cut put a warm-white emissive disc across the mouth. It read as a
torch instantly, and it hid the reflector completely — 896 triangles of nothing.

Glass would not have rescued it. `Mat_Glass_Canopy_Tinted` carries its
transparency as Principled **transmission**, and the FBX exporter does not
carry transmission; the gauntlet family imports with `materialLocation: 1`
(embedded), so it arrives in Unity opaque. No material choice makes the dish
visible through a disc.

So the disc went and **the dish is the face**. Chrome catches the world's
light when the lamp is off, the bulb is the only emissive part, and
`FlashlightGauntletArtifact` dims that bulb through a `MaterialPropertyBlock`
when the torch is switched off — so the model tells the truth about its own
state, which the glowing plate never could. The generated icon
(`Assets/Game/Art/Sprites/Items/FlashlightGauntlet.png`) is the check: it reads
as a lamp at 256 px.

## Decomposition

Every logical part is its own object; nothing is joined. The reflector and the
bulb are separate because they are separate things and because the artifact
only ever writes the bulb — a joined dish would mean repainting chrome every
time the torch was switched.

**The reflector is a closed shell, not a dished surface.** A loft of one
surface is one-sided, and a one-sided reflector shows its backfaces the moment
the arm swings past the camera. The loop runs front face (rim → throat),
across the throat, back face (throat → rim), across the rim, and closes;
`cap=False`, because the loop already closes itself. Its whole back half is
invisible and exists only to make the part solid.

## Materials

**No palette additions.** Seven, all in use elsewhere: `Mat_Metal_Steel_Dark`
(bed, housing), `Mat_Metal_Steel_Worn` (horn), `Mat_Metal_Chrome_Scuffed`
(bezel, reflector), `Mat_Paint_Safety_Orange` (roof panel), `Mat_Emissive_Amber`
(charge lamps), `Mat_Emissive_Cabin_Warm` (bulb), `Mat_Plastic_Rubber_Black`
(boot).

`Mat_Emissive_Cabin_Warm` (#FFF2D8) rather than a new white: it is the
palette's warm lamp, and it is within a hair of the colour the URP light is
actually authored at on `Flashlight.prefab` (r 1, g 0.96, b 0.85 ≈ #FFF5D9).
The bulb and the beam are the same light.

Material index 0 of every part is dark steel, because `bmesh.ops.bevel` stamps
its new faces with index 0. The panel is not bevelled for that reason.

## Frame and the measured numbers

Family frame: arm along +Y, wrist joint at y = 0, elbow +Y, forward −Y,
dorsal +Z, +X the thumb side of a right forearm. Export maps Blender
(x, y, z) → Unity (−x, z, −y). Origin at the wrist bone, worn at scale 1.

Measured on the exported FBX by `describe()`:

```
                       Blender                          Unity
whole model  min   (-0.2090, -0.0730, -0.1916)     (-0.1900, -0.1916, -0.3600)
             max   ( 0.1900,  0.3600,  0.5305)     ( 0.2090,  0.5305,  0.0730)
size               (0.3990, 0.4330, 0.7221) — longest 0.7221
device only  x ±0.1505   y -0.0730..0.3160   z 0.2295..0.5305
```

**3,220 triangles**, which is the whole model now (budget 6,000). The bracer's
3,924 are worn, not carried, and are counted once for the player rather than
once per gauntlet. Export: `gauntlet_flashlight_export.py`
(`keep_armature=False`, `keep_empties=True`, `describe(worn_scale=1.0)`).

`holdSize` and `packSize` are both **0** — the family's, from `GauntletPrefab`.
Zero means "keep the size the artist built", which since 2026-09-04 is the
device's own 0.3890 rather than the 0.7221 it measured with the bracer in it.
`packSize` stopped being 0.54 in the same change: that was a deliberate shrink
for a bracer too bulky to lie on the pack mat, and the bracer is worn now.

### Pivots

| Object | Blender (x, y, z) | Unity (x, y, z) | Rotation |
|---|---|---|---|
| `Emitter` | (0, −0.0600, 0.3800) | (0, 0.3800, 0.0600) | identity |
| everything else | (0, 0, 0) | (0, 0, 0) | identity |

`Emitter` is the dish axis on the mouth plane: 60 mm forward of the wrist joint
and 380 mm dorsal of it. **Identity rotation on purpose** — the exporter carries
Blender −Y onto Unity +Z, which is the axis a spot light shines down, so
`Flashlight.prefab` nested there at an identity local pose points straight out
of the horn. `FlashlightGauntletBuilder` asserts that pose.

## Layout of the lamp inside the dish

The horn is untouched, so the cavity is fixed: the recess is 32 mm deep (mouth
plane y −0.060, floor y −0.028) and 0.240 m across. Front to back, in metres:

```
 y −0.060  mouth plane, r 0.150 — the Emitter
 y −0.046  reflector rim, front face — 14 mm back inside the mouth, so the
           horn's own lip shades it instead of it ending flush with the bezel
 y −0.044  bulb tip
 y −0.042  reflector rim, back face (shell 4 mm thick along the axis)
 y −0.028  the recess floor the reflector's vertex passes through
 y −0.024  reflector vertex, front face — 4 mm INTO the solid horn
 y −0.020  reflector vertex, back face, and the bulb's tail
```

The paraboloid is `y = −0.024 − 0.022·t²` with `r = 0.121·t`. `REFL_R` is
0.121 against the recess wall's 0.120, so the rim **embeds 1 mm** rather than
floating with a gap that would show as a black ring. The bulb is r 0.017
through a hole of r 0.015 — 2 mm of interference, so the shell closes on it
instead of leaving a ring you can see the dark through.

## Z-fighting

Checked pairwise on every planar face in the file against every other object's
(0.5 mm plane tolerance, 2D overlap in the plane's own frame):
**zero coplanar overlapping pairs involving any device part** — device against
device and device against base alike.

One pair remains inside the appended base itself,
`Mesh_GauntletBase_Collar_Mount` against `Mesh_GauntletBase_Undersleeve_Mount`
(1 face pair). It is **pre-existing and shared**: `gauntlet_ruin_scanner.blend`
reports exactly the same pair, and it comes from `gauntlet_base.blend`. Not
touched here — fixing it means editing the base every gauntlet appends, which
is a change to six other models and belongs in its own pass.

Designed out on the device: the reflector's vertex is sunk 4 mm past the recess
floor so no face of it shares the floor's plane; its rim embeds 1 mm into the
recess wall; the bulb interferes 2 mm with the reflector's throat and runs on
into the solid horn. Everything inherited keeps the Ruin Scanner's embeds.

## Rotation verification

Rendered orthographically from four angles (scratch renders). Side view from
+X: the horn's mouth is on **screen-left**, which is Blender −Y = forward —
the lamp points out over the back of the hand, and after the export's axis
conversion that is Unity +Z. Front view down the axis: the dish is square-on
and concentric with the bezel, the bulb on the centre line. Top view: the
orange panel and both lamps read. All object rotations are identity and all
transforms applied; the one non-zero origin is the `Emitter` empty's.

## Decisions the lead might want reversed

- **No cover glass.** See above. If a glazed face is wanted, it has to be
  `Mat_Glass_Canopy_Tinted` *and* the material has to be made genuinely
  transparent on the Unity side (alpha, not transmission) — otherwise the
  reflector is dead geometry again and should be deleted with it.
- **The device is the Ruin Scanner's, dimension for dimension.** The two
  gauntlets read as siblings from behind; only the dish and the missing sights
  tell them apart. That is deliberate — one family, one machine shop — but if
  they need to be distinguishable at silhouette distance, the housing is the
  part to change, not the horn.
- **The bulb is emissive geometry, not a light.** It glows at whatever the
  artifact last painted, so a gauntlet lying on the ground shows the prefab
  default. A second Light would be more honest and more expensive.
- **The charge lamps are decorative.** Nothing drives them; there is no charge.

## Not done here

- `LIBRARY.md` / `library_index.json` regenerated for this file only.
- Not yet seen on the rig in play mode, and the beam origin has moved from the
  eye to the wrist — `_NearFade` on `FlashlightBeam.mat` may need retuning.
- The .blend was generated twice, minutes apart and before the file had ever
  been opened in Blender (the second time to drop the cover glass). From here
  it is the source of truth; never re-run `gauntlet_flashlight.py` over it.
