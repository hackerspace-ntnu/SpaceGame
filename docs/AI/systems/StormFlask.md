---
system: StormFlask
layer: items
reads_with: [Artifacts, Multiplayer, Environment]
summary: "A thrown flask stands a storm for 30 s: a raymarched cloud, a rain column you stand in, bolts on the tallest"
paths:
  - Assets/Game/Scripts/Items/Artifacts/StormFlask
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/StormFlask.prefab
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/StormCloud.prefab
  - Assets/Game/Resources/Items/Artifacts/StormFlask.asset
  - Assets/Game/Art/Shaders/Artifacts/StormCloud.shader
  - Assets/Game/Art/Shaders/Artifacts/StormVeil.shader
  - Assets/Game/Art/Shaders/Artifacts/StormCloudVolume.hlsl
  - Assets/Game/Art/Shaders/Artifacts/Materials/Mat_StormCloud.mat
  - Assets/Game/Art/Shaders/Artifacts/Materials/Mat_StormVeil.mat
  - Assets/Game/Art/Models/Props/storm_cloud.fbx
  - Assets/Game/Art/Models/Items/storm_flask.fbx
symptoms:
  - "the storm cloud is a flat magenta cylinder"
  - "a shader compiles clean, reports isSupported and zero messages, and still draws as the magenta error shader"
  - "a volumetric effect renders on some frames and is completely absent on others"
  - "the storm renders as a smooth grey flying saucer with no churn"
  - "there is a bright band of open sky between the cloud and the top of its own rain"
  - "the rain is a wall of grey cubes instead of falling water"
  - "the cloud's underside is as bright as its top"
  - "the raymarched storm disappears when the camera does not produce a depth texture"
  - "the storm vanishes when standing directly underneath it"
updated: 2026-09-09
---

# Storm flask

The design brief is [Artifacts/StormFlask.md](Artifacts/StormFlask.md); read
[Artifacts.md](Artifacts.md) first. This page governs the code.

Uncork the flask at a patch of ground and a storm gathers 15 m above it, stands for 30 s, rains,
and every 2.5 s throws the Lightning Spell's bolt at the tallest body underneath — including the
player who threw it.

## Model

| Thing | What it is |
| --- | --- |
| `StormFlaskArtifact` | A `ToolItem`. Owner reads the aim, **server** spawns the cloud, every machine plays the sound. The charge is spent by `Deplete()` on the path that actually put a storm in the world, so a click at open sky costs nothing. |
| `StormCloud` | The spawned `NetworkBehaviour`. Holds `brokeAt` / `lifetime` (server-written) and one `StormStrike` (point + ordinal). Every machine derives gather, dispersal and age from that clock. |
| `StormCloudTargets` | The sweep. Colliders resolve to their `StatusReceiver`, and the receiver's own root position decides "tallest" — so a limb under the rim is not a claim. |
| `StormCloudField` | The per-machine registry and the live-storm budget. Registered everywhere, evicted only by the server. |
| `StormCloudLook` | The **only** place this artifact talks to a shader. Paints `_Form`, `_Flash` and `_BoltPoint` through a `MaterialPropertyBlock`. |

## Flows

1. **Use.** Owner puts the aimed point in the message; server spawns `StormCloud.prefab` at that
   point plus `height`, then calls `Begin()`, which stamps the clock and resolves the owning chunk.
2. **Rain tick** (server, `rainInterval`): sweep the column, clear `Burning`, refresh the `Wet`
   [SurfaceCoat](Artifacts/SurfaceCoat.md).
3. **Bolt** (server, `boltInterval`): tallest receiver takes `RadiusDamage`; the strike point and an
   ordinal go on the wire.
4. **Draw** (every machine): `OnStrikeChanged` instantiates the Lightning VFX locally and calls
   `StormCloudLook.Flash(point)`, which lights the cloud volume from that world point.

## How it is drawn

Two raymarched volumes over one FBX (`storm_cloud.fbx`), sharing
`StormCloudVolume.hlsl` — read that
file's header before touching either shader; it holds the frame, the bounds and the two guards.

| Shader | Draws | Bound it marches |
| --- | --- | --- |
| `StormCloud.shader` | The body: a rotating, domain-warped, coverage-eroded disc, banded onto five measured palette entries. | Cylinder, radius `_BodyRadius`, y `0 .. _BodyTop` |
| `StormVeil.shader` | The rain column — **and the inside of the storm**, because the march simply starts at the camera when the camera is in it. | Cylinder, radius `_VeilRadius`, y `-1 .. 0` |

Both draw `Cull Front / ZTest Always / ZWrite Off`, so the fragment is always the volume's far side
and the march always has the whole volume in front of it, inside or outside. Depth is settled by the
depth **texture**, not the depth test.

There is no interior render feature, no fullscreen pass and no camera-parented quad. A volume
marched from the camera is its own interior; the sandstorm needs a fullscreen pass only because its
interior is a kilometre across ([Environment](Environment.md)).

**Violence comes from four terms on the sample point**, not from fading anything: differential
rotation (`_Spin`, `_SpinCore` — the core outruns the rim, so the field shears into spirals), a
domain warp (`_Warp`), an updraft scroll (`_Updraft`), and coverage erosion (`_Coverage`) that cuts
the volume away so the silhouette itself is ragged and moving. The rain adds travelling gust sheets
(`_Gust`) and per-column weights (`_ColumnBite`) so the curtain surges instead of sitting still.

## Multiplayer

Nothing about the drawing is replicated. `_Form` comes from the shared clock and `_BoltPoint` from
the strike that was already on the wire, so two machines watching one storm paint the same numbers.
`StormCloud.prefab` **is** registered in `DefaultNetworkPrefabs.asset` (checked 2026-09-09).

## Persistence

Not saved, deliberately: 30 s of world state, and the prefab must keep out of
`SaveablePolicy.NeedsSaving` — no non-kinematic Rigidbody, no `HealthComponent`, no
`PickupableItem`, no `NavMeshAgent`, no `SceneTracked`. The cloud watches the streaming grid itself
and ends when its chunk unloads.

## Gotchas

- **A property named `_Wind` makes the material draw as Unity's magenta error shader.** It collides
  with a legacy built-in global. The shader compiles, `isSupported` is true, `ShaderHasError` is
  false and `GetShaderMessages` is empty — there is no diagnostic at all. The property is
  `_RainWind` for that reason and nothing else.
- **An intersection helper with `out` parameters must have a SINGLE EXIT.** The first
  `StormCloudRayCylinder` returned early from three places while writing `tNear`/`tFar`; it also
  compiled clean, reported no messages, and drew as the magenta error shader on most frames and as
  nothing at all on the rest. Rewritten straight-line it behaves. Both helpers in
  `StormCloudVolume.hlsl` are written that way now; keep them that way.
- **The scene-depth clamp must treat BOTH ends of the depth range as "no information".** An unbound
  `_CameraDepthTexture` samples as white, which under reversed Z is the NEAR plane, so the march is
  cut to about 30 cm and the storm silently disappears. Any camera that draws the storm without
  having asked for a depth texture does this — it is not only an editor problem.
- **The body's bound is a cylinder, not the lens-shaped ellipsoid it looks like.** An ellipsoid's
  radius goes to zero at its base, so the cloud pinches to a point exactly where the rain leaves it
  and a bright band of sky opens between the cloud and its own veil. For the same reason the body's
  density has **no feather at its base**.
- **A cell-indexed rain column must be shaded as a soft rod, not as its cell.** Constant density
  across the whole cell in both horizontal axes and along the ray marches as a wall of cubes.
- **The belly cannot be darkened by the sun march alone.** The body is ~3.4 m thick, so a march long
  enough to leave it accumulates almost no depth and the underside comes out as bright as the crown.
  `_BellyShadow` is the term that makes a flat cloud sit in its own shadow.
- **Edit-mode `Camera.Render()` lies about these shaders.** The first render after an import is
  Unity's magenta placeholder, and a camera without `requiresDepthTexture` truncates the march. Warm
  up with a throwaway render and check for magenta before believing a capture.

## Extending

A new storm effect is a new file that reads `StormCloudField` or the cloud's own clock — never an
edit to `StormCloud`. A new shader over the same meshes includes `StormCloudVolume.hlsl` and gets
the frame, the bounds and both guards for free.
