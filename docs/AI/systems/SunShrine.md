---
system: SunShrine
layer: world
summary: "A ruin whose vault opens when players bounce the moving sun off three cranked mirrors onto its seal"
paths:
  - Assets/Game/Scripts/Gameplay/Puzzles/SunShrine
  - Assets/Game/Scripts/Core/Persistence/Adapters/SunSealSaveable.cs
  - Assets/Game/Editor/Tests/SunShrineTests.cs
  - Assets/Game/Prefabs/Environment/SunShrine
  - Assets/Game/Prefabs/Puzzles/SunShrine.prefab
  - Assets/Game/Art/Models/Environment/SunShrine
  - "Assets/Game/Art/Models/_Source~/models/buildings/sun_shrine.blend"
  - "Assets/Game/Art/Models/_Source~/models/buildings/textures/sun_shrine_ruin_BaseColor.jpg"
  - Assets/Game/Art/Materials/Environment/SunShaft.mat
  - Assets/Game/Art/Materials/Environment/SunMirrorSilver.mat
  - Assets/Game/Art/Textures/Effects/SunShaftProfile.png
symptoms:
  - "the sunbeam is there but invisible against the sand"
  - "the sunbeam is brightest up in the sky and fades out at the mirrors"
  - "the beam visibly misses a mirror but the light still counts as reaching it"
  - "the mirrors are right but the seal never fills"
  - "the puzzle cannot be solved at some hours"
  - "the vault is sealed again after loading a save"
  - "the mirrors look like flat gold or grey discs, not mirrors"
  - "every mirror shows the same big dark wedge"
  - "a mirror arrangement that solves the puzzle on paper never fills the seal in game"
  - "the shrine's sand skirt and entrance stairs are buried in the ground"
  - "the prefab and the scene disagree about the shrine after an edit to one SunShrine prefab"
reads_with: [WeathervanePuzzle, Environment, InteractionSystem, Multiplayer, Persistence, ArtPipeline]
updated: 2026-10-19
---

# Sun Shrine

A half-buried sun temple. Its vault door is sealed by a gold sun glyph that wakes only when sunlight
strikes it face-on for `holdSeconds` (5 s). The door is in a shaded alcove, so the light has to be
bounced in: sunlight falls on mirror 1, which the players turn and tilt to send it to mirror 2, then
mirror 3, then the seal. **The sun keeps moving**, so a working arrangement drifts off within about an
in-game hour, and the players have to keep up. Once opened, the door stays open.

**Scope:** [Gameplay/Puzzles/SunShrine/](Assets/Game/Scripts/Gameplay/Puzzles/SunShrine), the shared
[Gameplay/Puzzles/Dials/](Assets/Game/Scripts/Gameplay/Puzzles/Dials) ([WeathervanePuzzle.md](WeathervanePuzzle.md)).
**Placed in:** `Tests/Marius test scene.unity` as `SunShrine` at (157.9, 1.7, −60), with a `Sun`
prefab added (the scene's static light is disabled). Not in any world chunk yet. **There are two
prefabs:** the scene and the network prefab list use [Prefabs/Puzzles/SunShrine.prefab](Assets/Game/Prefabs/Puzzles/SunShrine.prefab)
(the one with the vault reward); `SunShrineTests` read [Prefabs/Environment/SunShrine/SunShrine.prefab](Assets/Game/Prefabs/Environment/SunShrine/SunShrine.prefab),
a copy made in the same commit that carries the **same `prefabId`**.

## Model

- **Nothing about the light crosses the wire.** The sun is a pure function of the shared clock
  ([Environment.md](Environment.md): `DayNightCycle`), storms are too, and the mirror arrangement is
  `DialSet` state every machine already holds. Every machine traces the beam itself each frame.
  **Only the verdict is the server's:** its own trace fills the seal and opens the door.
- **The sun turns about world X** (`Sun.prefab`, `rotationAxis` (1,0,0), `cycleDuration` **1600 s**,
  not the 2400 s once quoted). Its light always arrives in the world YZ plane, rising from −Z, so only
  its *elevation* drifts. That is why **only mirror 1 tilts**; the relays (2, 3) only turn. If they
  tilted, mirror 1's tilt error would compound down the chain.
- **The trace is order-free:** reflect off whichever mirror face comes next, up to `maxBounces`. Any
  path the light can really take counts. It is bounded by two rules: a **recess** (the seal accepts
  light only within `sealAcceptDegrees` (15°) of face-on, which stops mirror 1 shining straight at it)
  and **occlusion** (a physics raycast per hop on `occluders`, so walls block beams, and so does a
  player standing in one).
- **Waking window:** the sun must be within `oculusHalfAngle` (60°) of the `Oculus` transform's
  forward, which is about a third of the day. The ruin's south wall has a round oculus for the low
  morning sun and is low enough for the higher sun to pass over it; the cone is the rule and the
  geometry only illustrates it.
- **Storms dim it:** strength = 1 − storm intensity / `stormCutoff` at mirror 1. The fill rate scales
  with strength, and at the cutoff the beam is gone.
- Legibility (`GDC-L1-SYS-0006`, `GDC-L1-DESIGN-0006`): a visible shaft, each crank saying "in the
  sunbeam", the seal's fill as the crank readout's 0–1 bar, the seal glowing as it fills, and a
  sundial outside with the waking third of its rim inlaid in gold.
- **The beam is drawn exactly as wide as it hits** (`2 × beamRadius`, set in code, with no width of
  its own in the prefab). A beam that visibly grazes a mirror's rim counts, and one that visibly
  misses does not. It is two renderers: `skyShaft`, the sunlight falling onto mirror 1 and fading in
  from the sky (shown whenever the sun reaches the mirror, even one turned away), and `beam`, the
  reflected path, which keeps a uniform strength and rounds its corners at each bounce.
- **The panes are real mirrors**: silver, reflecting the ruin and the sky of the current hour through
  the `MirrorReflections` probe ([Environment.md](Environment.md): `TimedReflectionProbe`). Nothing
  crosses the pane's face; the frame holds it on two short pins.
- **Game scale is 2× the first sketch** (rebuilt 2026-10-19): a ~30 m platform on a sand skirt, 3.2 m
  mirror centres, a 4.6 × 6.8 m door. Every distance and radius doubled together, and the puzzle is
  angles only, so its solutions and drift are exactly those of the sketch. The gold sun motifs are kept
  to the puzzle (medallion under mirror 1, oculus ring, lunette and attic suns, seal, mirror crowns,
  sundial arc) so they lead the eye (`GDC-L1-LEVEL-0001`).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `SunShrine` | [SunShrine.cs](Assets/Game/Scripts/Gameplay/Puzzles/SunShrine/SunShrine.cs) | A `DialSet`: two dials per mirror (turn 2i, tilt 2i+1), packed mixed-radix. Traces, draws the `skyShaft` and `beam` `LineRenderer`s, fills and glows the seal. `LightsSeal(arrangement, toSun)` is the pure query the tests use |
| `SunMirror` | [SunMirror.cs](Assets/Game/Scripts/Gameplay/Puzzles/SunShrine/SunMirror.cs) | Presentation + geometry: poses `YawPivot`/`TiltPivot`, spins the cranks. `Normal` follows the swing so the beam sweeps with it; `NormalAt(steps)` is pure. The `Mirror_Pane` renderer wears `SunMirrorSilver.mat`, not the FBX's material |
| `SunSeal` | [SunSeal.cs](Assets/Game/Scripts/Gameplay/Puzzles/SunShrine/SunSeal.cs) | One-way `NetLatch` (`ILatchHost`, `IPersistentEntity`); sinks the door `sinkDepth` (6.9 m, into a slot in the floor) |
| `DialCrank` | [DialCrank.cs](Assets/Game/Scripts/Gameplay/Puzzles/Dials/DialCrank.cs) | "Mirror crank", "RMB: turn" / "RMB: tilt"; bar = `SunShrine.Progress` |
| `SunSealSaveable` / `DialSetSaveable` | [Adapters/](Assets/Game/Scripts/Core/Persistence/Adapters/SunSealSaveable.cs) | Keys `sunSeal` `{ open }` and `dials` `{ arrangement }` |

| Tunable | Default | Where |
| --- | --- | --- |
| turn steps / tilt steps × degrees | 12 (30°) / 15 × 5° (mirror 1 only) | `SunMirror` |
| mirror `radius` / `beamRadius` | 1.4 m / 0.9 m | mirror / shrine |
| `oculusHalfAngle` / `stormCutoff` | 60° / 0.65 | shrine |
| `sealRadius` / `sealAcceptDegrees` | 1.4 m / 15° | shrine |
| `holdSeconds` / `drainSeconds` | 5 s / 3 s | shrine |
| beam look | `beamColour` (alpha-blended gold); cross-section `SunShaftProfile.png` on `SunShaft.mat` | shrine / material |
| mirror reflections | 256 px, box-projected (36.8 × 11.8 × 28.8 m), re-rendered every 10 s, captured 2.6 m above the mirror tops | `MirrorReflections` probe |

## Flows

1. **Crank:** `DialCrank` → `DialSet.RequestTurn` → server → `DialState` to all → `SunMirror.Show` swings (0.5 s a step).
2. **Every frame, every machine:** `SunStrength` (live `DayNightCycle`, window, storm) → `Trace` → draw the beam → `Fill`.
3. **Server:** `fill` reaches 1 → `SunSeal.Open()` → `NetLatch.Request(true)` → door sinks on every machine, for good.

Layout (shrine-local metres; mirror 1 is turned 180° so its turn step 0 faces −Z): mirror 1 (0,0,0),
mirror 2 (0,0,−6), mirror 3 (6.928,0,−2), seal (6.928,3.2,6.88) facing −Z, panes 3.2 m up, so the
reflected beams run level at 3.2 m. Oculus (0,6.8,−11.8); vault altar top (6.928,1.22,11.9). The solution at sun angle
θ (degrees up from the −Z horizon) is mirror 1 turn 0, tilt θ/10; mirror 2 turn 1; mirror 3 turn 10.
A few extra routes open around noon.

## Multiplayer

- Mirrors ride `DialTurn`/`DialState` (see [WeathervanePuzzle.md](WeathervanePuzzle.md)); the door rides `LatchSet`/`LatchState`. The root carries `NetworkObject` + `NetRelay`.
- A client's fill is presentation and can lead or lag the server's by its clock offset. It never opens anything: the gate is `Network.Decides`.
- **Verified offline in play mode only** (host-of-none): a real crank round trip, the sun anchored at 60°, the beam traced mirror 1 → 2 → 3 → seal, the seal filled and the door opened. **Not yet verified on a real client.**

## Persistence

- `DialSetSaveable` (mirrors) and `SunSealSaveable` (door) are auto-attached by `SaveablePolicy.EnsureWorldInteractables`, and are also on the prefab. Time of day is already saved (`DayNightSaveable`, key `sky`).
- The open door is **not** derivable from the mirrors: the sun has moved on by load time.
- **Not yet verified by a save/reload**: the test scene runs no save system.
- The 2026-10-19 rebuild added no runtime state: it changed meshes, materials and serialized distances only.

## Gotchas

- **The shrine must stand square to the world axes.** The puzzle is solvable only because the sun's plane lines up with the 30° turn grid. `SunShrineTests` reads the prefab at identity. A rotated instance can be unsolvable, and nothing warns.
- **An additive shaft is invisible in daylight** against pale sand. `SunShaft.mat` is alpha-blended for that reason.
- **The shaft's texture must vary only across the ribbon.** A `LineRenderer` in Stretch mode spreads its texture's U over the *whole* line. `SunShaft.mat` used Unity's round default-particle blob, so the beam was brightest halfway up the 40 m sky shaft and almost gone at the mirrors and the seal, where players look. `SunShaftProfile.png` is a 4×64 strip: bright core, soft edges, constant along its length.
- **The visible width and `beamRadius` must not be tuned separately.** The beam used to be drawn 0.45 m wide while the hit test padded each disc by 0.45 m (a 0.9 m column), so light visibly passed beside a mirror and still counted. `DrawBeam` now sets the width from `beamRadius` every frame; a width typed into the prefab is overwritten.
- **The pane's silver lives on the prefab, not in the FBX.** The FBX import loses `metallic` ([ArtPipeline.md](ArtPipeline.md)), so the palette's `Mat_Metal_Chrome_Scuffed` on the pane in Blender arrives as matte grey. `SunMirror.prefab`'s `Mirror_Pane` renderer is assigned `SunMirrorSilver.mat` (metallic 1, smoothness 0.96). Re-exporting keeps it, because the renderer references the mesh by name.
- **The probe must capture from above the mirrors.** Captured from among them, the neighbouring mirrors and stands filled half its cubemap, and every pane showed the same big dark wedge. `MirrorReflections` sits 2.6 m above the mirror tops, with box projection on so the reflections still line up with the walls.
- **The scale lives in two places that must move together:** the meshes (2× in the `.blend`) and the prefabs' numbers (mirror/door/oculus/sundial/probe positions, every collider, `radius`, `beamRadius`, `sealRadius`, `sinkDepth`, the reward's spot on the altar). Scaling one alone keeps the tests green and leaves the beam visibly off the art.
- **The tests never test occlusion; the ruin's colliders can block every solution.** `LightsSeal` is pure geometry. Each ruin node except `SunShrine_Rubble` carries a MeshCollider on the `occluders` layer, so a wall, column or fallen block in a beam stops it in game. After moving any of them, trace every waking degree's solutions through the colliders: instantiate the prefab in a preview scene and raycast each hop of `Trace`'s path in its `PhysicsScene`, with the same 0.3 m skin. On 2026-10-19 all 119 waking degrees had a clear solution; the alcove jamb blocks 22 oblique alternative routes, which is the recess doing its job.
- **The platform is 1.8 m deep, and its origin is the floor.** Stand the instance 1.7 m above flat ground, so the sand skirt's rim sits just under it and the stairs reach it. At the old 0.5 m height, the skirt and four of the five steps are buried. **The entrance stairs are only for show.** The player has no step offset and their risers are 0.36 m, so a `StairsRamp` box tilted 23° over the nosings, on both prefabs, carries the walk from the floor to the sand.
- **The two `SunShrine.prefab`s are full copies, not a variant, and share one `prefabId`.** Edit both. The same `prefabId` on two prefabs is a persistence hazard, and `VaultReward_SmallMotor` logs a nested-`SaveableEntity` warning on every import; neither is fixed.
- **`maxBounces` and the recess are the only limits on emergent routes.** Widening `sealAcceptDegrees` lets mirror 1 light the seal alone at some hours.
- The mirror's reflecting face is the `PaneFace` empty, not the FBX pane node: FBX nodes carry the axis conversion, so their `forward` is not the face.

## Extending

1. **Retune the drift:** change `tiltStepDegrees`, `beamRadius` or mirror `radius`, then run `SunShrineTests`. They check it is solvable at every waking degree, that no arrangement holds for more than two in-game hours, and that some arrangement holds for at least 20 s.
2. **Place it in the world:** copy `SunShrine` into a chunk scene at rotation 0. Put the reward in the vault; the test scene uses a SmallMotor hull module.
3. **Models:** `models/buildings/sun_shrine.blend` holds `Coll_SunShrine_Ruin`, `Coll_SunMirror`, `Coll_SunSealDoor` and `Coll_SunDial` at game scale; re-export with `_exportlib.export_collections(..., triangulate=True)` to `Environment/SunShrine/`. The ruin's nodes: `SunShrine_Ground` (platform, paving, medallion, stairs), `_Sand` (skirt and drifts), `_Wall{South,East,West,North}` (the west one holds the gate piers), `_Gateway` (facade, vault, altar), `_Columns`, `_Debris` and `_Rubble`. The pivots are the prefab's: mirror `Plinth` 0, cranks (±1.06, 0.6), `Turntable`/`Yoke` 1.2, `Frame`/`Pane` 3.2; door 0, glyph 3.2. The frame's ray crown is on its upper half only, so no tilt swings it into the yoke's crossbar. Keep anything off the pane's face.
4. **Textures:** each model is one local `Mat_SunShrine_<Ruin|Ground|Sand|Mirror|Door|Sundial>` sampling a baked atlas (4096/4096/2048/2048/2048/1024). The masters are `_Source~/models/buildings/textures/sun_shrine_*_BaseColor.jpg`; the export copies them into `<model>.fbm/`, which Unity binds. Repaint a JPG and re-export. A geometry edit needs a new UV unwrap and bake ([ArtPipeline.md](ArtPipeline.md) Gotchas, the baked-atlas models). The ruin and ground atlases are raised to 4096 on their texture importers; Unity's default is 2048. The glyph and pane keep palette materials, because the prefab overrides both.
