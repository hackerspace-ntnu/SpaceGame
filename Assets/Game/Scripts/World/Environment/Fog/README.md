# Fog volumes and clouds

Air you can be inside of. A fog volume is a body of air placed in a scene that has a shape, a
colour and a direction; a cloud layer is the same idea at a scale of kilometres, overhead.

Both are built on `VolumetricCore.hlsl`, which is the physics the sandstorm proved — see
[the sandstorm's README](../Sandstorm/README.md) for where each piece of that came from and what it
looked like when it was missing.

## The one thing to know

Drop a `FogVolume` in a scene and it renders. There is nothing to register, nothing to wire, and no
manager to add. `FogVolumes` finds it, `FogRenderFeature` draws it, and both do nothing at all in a
scene that has none.

The same goes for the sky: one `CloudLayer` component anywhere in the scene, and there are clouds.

## Authoring a fog volume

| Field group | What it decides |
|---|---|
| **Shape** | `Ellipsoid` for outdoor banks, `Box` for rooms and corridors, `Cylinder` for columns and vents, `GroundLayer` for low mist. `size` is half-extents in metres *before* the transform's scale. |
| **Look** | `color` is what the fog is; `emission` is light it gives off by itself. `extinction` is how far you can see — 0.1 loses the view at roughly 30 m. |
| **Detail** | `noiseScale` is the size of the lumps: 8 m churns, 60 m rolls. `erosion` tears them apart. |
| **Motion** | `windSpeed` slides the mass; `churn` stirs it in place. |

The transform does the rest. Rotation and non-uniform scale apply to every shape, so a cylinder can
be tipped over into a sideways vent and a box can be stood on one corner, with no extra shape kind
and no code.

Three things are worth knowing because they are not guessable:

- **`churn` is what makes fog look lived in.** Drift alone slides the whole body past you like a
  texture on a conveyor; real air also turns over where it stands. A volume with `churn` at zero
  looks fine in a screenshot and looks like a scrolling texture the moment you stand still.
- **`color` is a surface colour, not a light.** A volume with a colour set and nothing shining on
  it is black. That is what `ambient` (sky light) and `FogLight` (lamps) are for.
- **A `GroundLayer` ignores its own top face.** It decays exponentially upward instead, because a
  hard ceiling overhead is the single thing that gives a box of ground mist away. Its gizmo draws
  where the fog actually thins out rather than where the box ends.

## Lamps

Put a `FogLight` beside a `Light` and that lamp lights the air as well as the walls. It is opt-in
because reading every light in a scene would put a fog cost on scenes that never asked for one, and
would get quietly slower every time an artist added a lamp.

Eight lamps reach the shader, nearest first. There is no shadow march toward them: what sells a lamp
in mist is the falloff and the forward scattering, and paying a second march per light per sample to
model the metre of fog in between would cost more than the rest of the shader together.

## Overlap

Up to eight volumes are uploaded, chosen by distance to their surfaces rather than to their centres —
so a large volume you are standing in never loses its slot to a small one parked slightly nearer.

All eight are marched **together, in one pass**. At each sample every volume's look is averaged
weighted by its own density, so two overlapping fogs read as one body of air whose colour is
somewhere between them. Density itself is summed, because two overlapping fogs genuinely are thicker
than either alone. `FogGallery.unity` has a crimson and an azure ellipsoid overlapping in the middle
of the plaza for exactly this reason: it is the one behaviour that cannot be checked on a single
volume.

One march rather than one per volume is also why there is no sorting problem. Everything a ray
passes through is integrated in depth order by construction.

## Why it is a fullscreen pass

The same reason the sandstorm's shell is drawn on back faces with the depth test off: one fullscreen
ray, clipped by scene depth, serves the view from outside a volume, the view from inside it, and the
moment of walking through its edge, with no special case for any of them. Per-volume meshes would
need front faces outside, back faces inside, a correct sort between overlapping volumes, and would
still crack at the near plane.

## Rendering notes

The march runs at reduced resolution and is composited back with a **depth-aware upsample**. Two
details there are load-bearing and both were visible defects before they were fixed:

- The march jitters **once per texel it writes**, against `_FogTexelSize`, not against
  `_ScreenParams`. A half-resolution pass scaled by the full-resolution size advances the dither two
  steps per texel, which lands the pattern at a frequency no upsample can filter — thin fog comes out
  covered in fixed-pattern stipple.
- The upsample is **3×3**, not 2×2. A 2×2 kernel spans exactly one march texel per output pixel, so
  the per-texel jitter survives it intact. Nine taps average a full neighbourhood of offsets away.
  The depth weight is what keeps the result off the silhouette of anything standing in front.

Quality tiers, live-settable through `FogRenderFeature.Quality` and
`VolumetricCloudsRenderFeature.Quality`:

| Tier | Fog steps | Fog light steps | Cloud steps | Resolution |
|---|---|---|---|---|
| Low | 8 | 1 | 16 | quarter |
| Medium | 16 | 2 | 32 | half |
| High | 32 | 4 | 64 | half |
| Ultra | 48 | 6 | 96 | full |

**The cost has not been measured.** Profile before trusting any of it. The first thing to drop is the
resolution tier, then the step count; the light steps are already as low as they can usefully go,
because they run once per view sample *per volume* and so cost is multiplied by both.

The pass is not enqueued at all when no volume is within `maxDistance`, which is what keeps fog off
the frame budget of every scene that does not have any.

## The sky

`CloudLayer` marches a **spherical shell** around a centre placed far below the camera, not a pair of
horizontal planes. That single choice is what makes the horizon work: on a sphere the layer curves
away and the clouds crowd together and thin out as they approach the horizon, while between two
planes they stretch to infinity and read as a textured ceiling. The centre follows the camera in XZ,
so you can never walk to the edge of the sky, while the weather map that decides *where* the clouds
are is sampled in absolute world space — so flying a kilometre puts you under different clouds.

Three details in the cloud march are there because their absence was a visible bug, and all three
only show up at shallow viewing angles:

- **A sky pixel is never clamped by scene depth.** Its reconstructed "scene position" lies on the
  camera's far *plane*, and a plane is nearest along the forward axis: at a 3 km far clip and a 62°
  field of view that distance is 3000 m at the centre of the screen and 4745 m in the corner. Using
  it as the march's far bound therefore clipped the layer away hardest exactly where the player was
  looking — a cloud seen out of the corner of your eye vanished the moment you turned to face it —
  and it removed everything below about 20° of elevation outright. Geometry still occludes clouds;
  nothing does when there is no geometry. The fog shader has the same guard for the same reason.
- **The marched span is capped, not the step.** A horizon ray crosses tens of kilometres of the
  shell, and dividing that by the step count gives steps several times longer than a billow, so the
  march walks over the clouds and the deck dissolves precisely where a real one looks densest. The
  cap is derived from the billow size and the step count, which keeps the step under three quarters
  of a billow at every quality tier without a new dial.
- **The eroding octave fades out with march distance.** The step grows with distance while the
  detail octave carves features a fraction of its size, so far cloud gets point-sampled far below
  its own frequency and breaks into speckle. Fading the detail leaves the smooth base shape, which
  the step can still resolve. This would be a mip bias if the noise volume had a mip chain; it does
  not.

The weather map is sampled as **two octaves at scales that are not a whole-number ratio**. One
octave repeats every `weatherScale` metres, which is invisible overhead — one tile fills the view —
and unmissable near the horizon, where you look along tens of kilometres of it and the repeat reads
as a grid of identical puffs marching into the distance.

While a `CloudLayer` is enabled it pushes `_VolumetricCloudFade`, and `DesertSkybox` uses it to stand
down its own painted 2D dust bands. The two are competing answers to the same question and running
both gives flat bands sitting in front of volumes with real depth. A scene with no cloud layer never
sets the global, an unset global is zero, and that sky is exactly what it always was.

## Setup

`SpaceGame ▸ Environment ▸ Install Volumetric Render Features` adds both features to every renderer
under `Assets/` and switches on Depth Texture, which both of them march against. It is idempotent, so
it is also the repair tool. It deliberately skips URP's own renderer inside the package — writing
there appears to work and is reverted the next time the package resolves.

`SpaceGame ▸ Environment ▸ Build Fog Gallery Scene` writes `Assets/Game/Scenes/Tests/FogGallery.unity`
and runs the installer for you. The camera is a free-fly `SpectatorCamera`: press Play and walk into
things, because every claim this system makes is a claim about a viewing angle.

## How it survives multiplayer and save/load

Neither needs any code, and that is by design rather than by omission.

**Multiplayer:** there is nothing to replicate. Volumes and cloud layers are authored in scenes, so
every machine already has them, and their only moving parts — drift and churn — are pure functions of
`Sandstorms.Now`, the clock every machine in a session already agrees on. Two machines evaluating the
same function against the same number see the same fog in the same place, with no traffic. Nothing is
spawned at runtime, so there is no network prefab to register and no ownership question to get wrong.

**Persistence:** there is no runtime state. No field on either component changes while the game runs,
so a reloaded world rebuilds identical fog from the scene plus the weather clock anchor the save
system already restores.

If either of those stops being true — a gadget that spawns a fog cloud, a volume that thickens as a
reactor fails — it stops being true in exactly the way the sandstorm already handles, and
`StormInstance` is the model to copy.

## Not in this version

Fog does not affect gameplay: nothing queries it for AI sight, audio muffling, or damage, and there
is no CPU mirror of the shape function. Adding one is a new file that reads the same `FogVolume`
fields, plus a determinism test — `Sandstorms.IntensityAt` and `SandstormTests` are the shape to
follow. Also absent: shadow marching from local lights, temporal reprojection, and any coupling
between a fog volume and the `WindField`.
