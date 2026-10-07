# Sandstorms

A sandstorm is a real place in the world: it has a shape, it sits somewhere, it moves, it blinds
you and it kills you. This system exists so that all of those are the *same* storm — one shape
function drives the damage, the AI, the audio and every pixel of the rendering.

## The one thing to know

Everything talks to `Sandstorms`, and nothing talks to anything else:

```csharp
float intensity  = Sandstorms.IntensityAt(pos);   // 0..1 sand density, ignoring cover
float exposure   = Sandstorms.ExposureAt(pos);    // after shelter — this is what hurts
float visibility = Sandstorms.VisibilityAt(pos);  // metres
float sight      = Sandstorms.SightFactorAt(pos); // multiplier for an AI's acquisition range

if (Sandstorms.TrySample(pos, out StormSample s)) { /* s.Profile, s.WindDirection, ... */ }
```

Adding a new way for a storm to affect the world is a new file that calls one of these. Every one
of them answers sensibly when there is no manager, no session and no storm, so a scene without
weather is not a broken scene.

## Scene setup

| Where | What | Why |
|---|---|---|
| On the **NetworkGameManager** object | `SandstormManager` + a `SandstormCatalog` asset | It needs the NetworkObject that is already there. A second scene NetworkObject in this project is a liability. |
| Anywhere | `SandstormDirector` | Rolls roaming storms on an interval. Omit it and only placed zones exist. |
| Anywhere | `SandstormVisuals` + the wall material | All three render layers. Omit it on a dedicated server: storms still exist and still hurt. |
| Anywhere | `SandstormAudio` | 2D loop, filtered by shelter. |
| On the URP renderer | `SandstormRenderFeature` + the fog material | The interior fog. **Requires Depth Texture enabled on the URP asset.** |
| On the player and NPCs | `SandstormVictim` | Anything without it is immune. |
| On the ShipRV, caves, interiors | `SandstormShelter` | A trigger volume, plus the doors that must be shut. |
| On gear prefabs | `SandstormProtection` | One float. That is the whole authoring story for storm equipment. |
| In a scene | `SandstormZone` | A permanently parked storm — the hazard-region case. |

Materials: create one from `SpaceGame/Sandstorm` for the render feature and one from
`SpaceGame/SandstormWall` for `SandstormVisuals`. Assign them; do not rely on `Shader.Find`, which
would let both shaders be stripped from a build.

## Authoring a storm

One `SandstormProfile` asset per *kind* of storm. Duplicating an asset is how you get a new kind;
there is no code involved.

- **Shape** — `Cell` has edges you can walk around; `Wall` is a slab you cannot flank. `radius` is
  a radius for a cell and a half-thickness for a wall; `lateralExtent` of 0 makes a wall span the
  map. `height` is what makes a storm tower — 600–1500 m reads as enormous.
- **Motion** — `travelSpeed` of 0 parks it. At 11 m/s a storm crosses the 4 km world in about six
  minutes.
- **Lifetime** — `duration` of 0 means forever, which is how a zone is authored. Otherwise
  `intensityOverLife` is sampled across the duration: the ramp in is the player's warning.
- **Gameplay** — `damagePerSecond` at full exposure, `visibilityAtFullIntensity` in metres, and
  `aiSightFactorAtFullIntensity` for how blind the robots go.
- **Look**, **Audio**, **Near detail** — colours, noise, the loop, and an optional VFX prefab.

`SandstormZone` draws its footprint, height and heading as gizmos, and will show the real
silhouette in the scene view if you give it the wall material. The preview object is
`HideAndDontSave`, so authoring a storm never leaves anything in a scene file.

## How it survives multiplayer

The server writes one ~30-byte `StormInstance` record when a storm is born and never touches it
again. Position and intensity are *recomputed* on every machine from that record plus
`NetworkManager.ServerTime` — no per-frame traffic, and a late joiner gets the record from the
`NetworkList` and lands in the same weather as everyone else.

That only works because `StormInstance.Evaluate` is exactly deterministic, which is why storms use
`StormNoise` rather than `Mathf.PerlinNoise` (no cross-platform promise) and why the determinism
test in `Assets/Game/Editor/Tests/SandstormTests.cs` is the most important test in the folder.

Damage is applied by the server only. Everything else — fog, silhouettes, audio, the near detail —
is client-side and authoritative over nothing.

## Rendering

The sand is an actual volume, following the Nubis/Decima recipe at a scale a desert game can
afford. `SandstormVolume.hlsl` holds the whole model and both shaders include it, so the storm
cannot look like one thing from outside and another from within:

- **A tiling Perlin-Worley 3D texture** (`SandstormNoiseGenerator`, `Tools ▸ World ▸ Bake
  Sandstorm Noise`) supplies the billows. Baked rather than evaluated in the shader because a
  raymarch takes dozens of samples per pixel and one filtered fetch replaces about two dozen
  hash-and-lerp operations.
- **The analytic shape is a coverage mask, never the density.** The low-frequency noise is
  remapped against coverage, so the noise decides the shape and coverage only decides how much of
  it survives. Using the shape as density directly is what made the first version render as a box
  with a fuzzy rim.
- **Beer-Lambert integration** along the view ray, with a short march toward the sun for
  self-shadowing. That sun march sums **three Beer terms with decreasing extinction** — a cheap
  stand-in for multiple scattering. A single term is wrong in a way you can see immediately: half
  a kilometre of dust has an optical depth in the hundreds, so the storm renders as a black hole.
- **Henyey-Greenstein** forward scattering, a **powder term** for the crumbly lit edges, and a
  height-graded sky term so the top is lit and the base sits in its own shadow.
- The noise is **squashed vertically** (`windStretch`), which stretches the billows horizontally.
  Without it the storm reads as smoke sitting still rather than as sand being driven.

The three layers:

1. **`SandstormWall`** — a closed bounding shell per storm, drawn on back faces with the depth
   test off and the march clipped by scene depth instead. That combination lets one shell serve
   the view from outside, the view from inside, and the walk through the edge, with no special
   cases. The mesh only says *where on screen* the storm might be; every pixel intersects the real
   analytic shape.
2. **`SandstormRenderFeature`** — the interior, at reduced resolution, using the same volumetric
   core with extinction derived from the profile's gameplay visibility, then a full-resolution
   composite that adds the screen warp and grain. **The pass is not enqueued at all unless the
   camera is in a storm.**
3. **`SandstormVfx`** — the profile's near-detail prefab at the camera, above a threshold.

Quality tiers, live-settable through `SandstormRenderFeature.Quality`:

| Tier | Shell steps | Fog steps | Light steps | Fog resolution |
|---|---|---|---|---|
| Low | 16 | 4 | 2 | quarter |
| Medium | 36 | 8 | 4 | half |
| High | 64 | 16 | 6 | full |

**The cost has not been measured.** The original 1–2 ms target predates the volumetric rewrite and
should be treated as unverified — profile before trusting it, and drop the near detail first, then
the shell step count.

Tuning notes, in rough order of how much they change the look: `erosion` (how torn the mass is),
`wallNoiseScale` (must be at *storm* scale — hundreds of metres — since the noise now defines the
shape), `wallExtinction`, `windStretch`, `ambient`.

## Not in this version

Wind push on the player and vehicles, `WindField` coupling (so no sail or cape reaction), lighting
and `DayNightCycle` changes, HUD warnings, and stamina/oxygen or gadget degradation. Each of those
is a consumer of `Sandstorms`, so each is a new file rather than a change to this one.
