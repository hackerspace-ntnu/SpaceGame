# Sprayer kit — build record

The shared parts of the nine-item **clean issued equipment** set. Built
2026-09-07 alongside the first four items that use it: the flamethrower, the
foam gun and the cryo sprayer.

| File | |
|---|---|
| `sprayer_kit.py` | **live module** — imported by every model in the family, *and* the generator for the `.blend` |
| `sprayer_kit.blend` | **source of truth** for the built variations; never re-run `main()` over it |
| `sprayer_kit_BUILD.md` | this record |

Nothing here exports on its own. The kit's job is to be reused, and it is
reused two ways.

## Why this file exists at all

Nine hand-held items, three modelling agents, one look. Without a kit the third
crate problem arrives immediately: nine tanks with nine slightly different
domes, nine grips at nine rakes, and nine gauge plates the Unity builder has to
be told about one at a time. `GDC-L1-CONTENT-0003` is the principle — a
convention that is *enforced by a shared artefact* scales; one that lives in
nine authors' heads does not.

It follows the pattern `docs/AI/systems/ArtPipeline.md` already documents for
this library: "family kits sit beside their components … imported, not copied",
as `_console_kit.py` and `models/gear/_gauntlet.py` do.

## Two ways to use it

1. **Import the builders.** `tank`, `gauge_plate`, `grip_moulded`,
   `trigger_collar`, `nozzle_bell`, `iris_vanes`, `nozzle_finned`,
   `nozzle_fan`, `hose`, `arc`, `clamp_band`, `shell`, `rounded_rect`,
   `marker`. Each takes a `TrackedPart` and material **indices**, so a model
   builds a kit part in its own accent colour inside its own object. This is
   what the four sprayers do, and it is what makes an orange fuel bottle and a
   blue cryogen bottle the same part rather than two.
2. **Append the built variations** from `sprayer_kit.blend` when a model wants
   a part exactly as issued.

## The material contract

Every model in the family opens its table with `KIT_MATS` — the same eight
entries in the same order — and puts its function colour at index 8:

| # | Name | Material | Used for |
|---|---|---|---|
| 0 | `DARK` | `Mat_Metal_Steel_Dark` | frames, barrels, plugs — **and every bevel** |
| 1 | `SHELL` | `Mat_Paint_White_Arctic` | the issued moulded shell |
| 2 | `GREY` | `Mat_Neutral_Panel_Grey` | secondary mouldings, guards, bosses |
| 3 | `BLACK` | `Mat_Neutral_Black_Matte` | gauge bezel, seals, recessed slots |
| 4 | `RUBBER` | `Mat_Plastic_Rubber_Black` | hoses, grip cores, boots, feet |
| 5 | `CHROME` | `Mat_Metal_Chrome_Scuffed` | collars, bezels, clamp bands, lips |
| 6 | `CRT` | `Mat_Emissive_Green_CRT` | the gauge's lit contents strip — **nothing else** |
| 7 | `WORN` | `Mat_Metal_Steel_Worn` | bare-steel reservoirs and clamps |
| 8 | `ACCENT` | *per model* | the function colour |

Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
creates with material index 0 — the `_buildlib` trap. **No material was added
to the palette**; the whole family is existing entries.

Function colours in use so far:

| Item | Accent | Why |
|---|---|---|
| Flamethrower | `Mat_Paint_Safety_Orange` (+ `Mat_Paint_Warn_Red` muzzle band, `Mat_Emissive_Amber` pilot) | fire |
| Foam gun | `Mat_Plastic_Safety_Yellow` | the only bright moulded plastic in the palette |
| Cryo sprayer | `Mat_Paint_Blue_Station` | the palette's cold enamel |

The remaining five items should take an unused hue rather than a fifth shade of
one already here.

## The gauge is the reason the kit is worth having

`OxygenGearBuilder` finds a supply gauge by **material, not by submesh index**:
it looks for `Mat_Emissive_Green_CRT` on a named mesh, measures the lit slab's
own vertices, and lays a track and fill bar over it
(`docs/AI/systems/SupplyGauge.md`). `gauge_plate` builds exactly what that
expects, once:

```
bezel  -0.0040 .. +0.0040 from the skin, along `out`   (BLACK, half sunk in)
well   +0.0020 .. +0.0070                              (DARK, 2 mm into the bezel)
strip  +0.0045 .. +0.0085                              (CRT, 2.5 mm into the well)
```

Two things are load-bearing and neither is aesthetic:

- **The strip shares the plate's centre in both in-plane axes.** The builder
  mirrors the measured rect about the *gauge mesh's* middle, and a strip sitting
  off-centre produces a bar that is too long and visibly skewed — the bottle's
  36% error recorded in `SupplyGauge.md`.
- **Nothing meets on a plane.** Three plates stacked outward, each embedded in
  the one under it.

`gauge_plate` takes the bar direction as a **vector**, not an axis letter, which
is why a gauge can run vertically up a can flank with no other
change.

## Variations in `sprayer_kit.blend`

Ten objects in nine collections, 9 300 tris. All are built about the origin —
the library's norm for a component file; use `_preview.py --spread` to browse
them, and note that `_zverify` on this file is meaningless for the same reason
(`ArtPipeline.md` records that gotcha).

| Collection | Object | Dimensions (m) | Tris |
|---|---|---|---|
| `Coll_SprayerKit_TankSquat` | `Mesh_SprayerTank_Squat` | 0.099 × 0.179 × 0.099 | 1340 |
| `Coll_SprayerKit_TankCartridge` | `Mesh_SprayerTank_Cartridge` | 0.079 × 0.130 × 0.079 | 1104 |
| `Coll_SprayerKit_TankLance` | `Mesh_SprayerTank_Lance` | 0.091 × 0.323 × 0.091 | 1416 |
| `Coll_SprayerKit_GaugePlate` | `Mesh_SprayerGauge_Plate` | 0.090 × 0.013 × 0.030 | 228 |
| `Coll_SprayerKit_GripMoulded` | `Mesh_SprayerGrip_Moulded` | 0.050 × 0.132 × 0.132 | 956 |
| `Coll_SprayerKit_TriggerCollar` | `Mesh_SprayerTrigger_Collar` | 0.117 × 0.139 × 0.057 | 668 |
| `Coll_SprayerKit_NozzleBell` | `Mesh_SprayerNozzle_Bell` | 0.180 × 0.116 × 0.180 | 1120 |
| | `Mesh_SprayerNozzle_Iris` | 0.159 × 0.033 × 0.167 | 648 |
| `Coll_SprayerKit_NozzleFinned` | `Mesh_SprayerNozzle_Finned` | 0.080 × 0.120 × 0.081 | 1484 |
| `Coll_SprayerKit_NozzleFan` | `Mesh_SprayerNozzle_Fan` | 0.086 × 0.049 × 0.047 | 336 |

Three tanks, three nozzles and two hand interfaces is deliberate overshoot: the
four items built so far need one tank shape each and three of the nozzles. The
finned nozzle at a second size, the lance tank and the trigger collar are all
already there for the five items still to come.

## Reuse considered and rejected

**`components/mechanical/weapon_grip.blend` → `Coll_WeaponGrip_Pistol` /
`Coll_WeaponGrip_Fore`.** The library's existing hand interfaces, appended by
`dragon_bazooka.blend`, and the obvious thing to reuse. Both were built into the
flamethrower and looked at. They are rejected, and the reason is visible in one
render rather than arguable: the pistol grip has **ply cheeks** and the foregrip
a **canvas wrap** — a scavenged weapon's language. On a white issued shell they
read as parts off a different model. `grip_moulded` is the kit's answer: the
same one-loft-with-raked-stations construction (including the failure
`weapon_grip.pistol` records, where a stack of rotated boxes turns into
confetti), rubber core, moulded shell panels, kit materials.

**`components/props/gas_bottle.blend`, `oxygen_tank.blend`, `power_cell.blend`.**
Read first, as the brief asked. All three are the wrong size class: the oxygen
tank is a 0.387 m bottle a player carries in two hands, the power cell a 0.29 m
slab, and `gas_bottle`'s three variations are 0.11–0.12 m *scene dressing*
already scaled for a shelf. Nothing in the family wants a 0.12 m bottle with its
own valve furniture bolted to a 0.5 m gun. `tank` builds the shape at the size
each item needs and shares the *construction*, which is the part worth sharing.

## Traps this file already walked into

- **A capped loft is a solid.** `nozzle_bell` first built the bell as a capped
  loft, which rendered as a white disc filling the mouth and hid the shutter
  behind it. It is now a closed loop of rings — outer surface, rim, inner
  surface, throat annulus — the same trick as
  `gauntlet_flashlight.reflector`. `cap=False` alone would leave a one-sided
  surface that shows backfaces the moment the item swings past the camera.
- **A canted plate's far corner is not at `mid + halflen`.** It is at
  `sqrt((mid + halflen)² + halfwidth²)`. Iris vanes sized against the bell's
  mouth radius poked visibly out through the lip. `iris_vanes` takes an `outer`
  fraction and the vanes stop at 0.90 of the mouth.
- **A stack of straight blocks cannot follow a rake.** The grip's side panels
  were three overlapping boxes and came out as a visible staircase down the
  side of the grip; they are one loft down the same raked stations as the core.
  They also start at `t = 0`, flush with the body — starting at 0.10 left 13 mm
  of bare dark core between the white panel and the white shell, which read as a
  grip floating clear of the gun.
- **`Part.torus` cannot follow a curve.** It takes an axis letter and no
  rotation, so a corrugated hose cannot be made of tori threaded along a path.
  `hose(ribbed=True)` alternates the swept cylinder's radius instead.
- **Grip numbers do not scale with the item.** A 0.9 m lance and a 0.5 m pistol
  are held by the same hand (`GDC-L1-UX-0005`), so `GRIP_LEN` and `GRIP_RAKE`
  are the human hand's numbers, taken from `weapon_grip.pistol`, and are
  constants rather than fractions of anything.

## Extending

A new item in the family: `MATS = kit.KIT_MATS + ["<your accent>"]`, build the
body with `kit.shell`, the reservoir with `kit.tank`, the hand with
`kit.grip_moulded` or `kit.trigger_collar`, the working end with one of the
three nozzles, and the instrument with `kit.gauge_plate`. Then place
`Marker_Muzzle`, `Marker_Grip` and `Marker_Gauge` with `kit.marker` — the
wave-2 assets agent reads those three names on every item in the set.

A genuinely new shared shape goes **here**, not into the model file, and gets a
`Coll_SprayerKit_*` variation so it is browsable.
