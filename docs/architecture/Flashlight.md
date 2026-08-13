# Flashlight

Toggleable spot light on the player camera. Press **L** to toggle.

Lives on the `Flashlight` child of [Main Camera.prefab](Assets/Game/Prefabs/Camera/Main Camera.prefab).

## Three pieces

| Piece | What it does | File |
|---|---|---|
| **URP spot light** | Near-field lighting + shadows (short range, ~40m) | Light component on the prefab |
| **Long-throw layer** | Reaches far surfaces (URP falls off too fast). Uses `flashlightReach` on the script, *not* `Light.range` | [Flashlight.cs](Assets/Game/Scripts/Player/Flashlight.cs) pushes globals → [Flashlight.hlsl](Assets/Game/Art/Shaders/Flashlight.hlsl) |
| **Visible beam** | Dust cone in the air. Cone mesh is rebuilt each frame from raycasts so it ends at the actual surface. Radial falloff is normalized to the cone's local radius at each axial slice — that's why it reads as a cone, not a spike | [FlashlightBeam.shader](Assets/Game/Art/Shaders/FlashlightBeam.shader) |

Consumers: [CaveTriplanar.shader](Assets/Game/Art/Shaders/caves/CaveTriplanar.shader), [StylizedTerrain.shader](Assets/Game/Art/Shaders/StylizedTerrain.shader). Both add `lit += SampleFlashlight(posWS, N, wrap);` on top of URP's normal additional-lights loop.

**URP `Light.range` is intentionally short** (~40m). The long-throw layer handles distance via `flashlightReach` on the Flashlight script. This split is why intensity tuning works: URP only has to cover near-field, so reasonable intensities (~25) don't blow out objects 1m from the camera.

## Tweaking

**"Doesn't reach far enough" (surfaces)** → raise `flashlightReach` and/or `longThrowIntensity` on the Flashlight script. To slow the long-distance falloff, lower `longThrowFalloff` (0.008 default). Note: only shaders that include `Flashlight.hlsl` benefit. Other surfaces only see URP's spot light — capped at `Light.range` (~40m).

**"Near surfaces blow out"** → lower URP Light `Intensity` (default 25). Long-distance reach comes from the long-throw layer, not the URP light, so you can keep this modest.

**"Cone shape too obvious / too narrow"** → widen `Spot Angle` / `Inner Spot Angle` on the Light component.

**"Beam doesn't reach the ground / hovers past surfaces"** → the beam now raycasts to find the surface each frame. If it still misses, check `beamHitMask` (must include your ground layer) and `beamProbeRays` (more rays catch tighter geometry).

**"Beam jitters as length changes"** → lower `beamLengthSmoothing` on the script (0.5 default; lower = smoother but laggier).

**"Beam mesh too short on open spaces"** → raise `beamMaxLength` on the script (default 120m, clamped to `flashlightReach`).

**"Can't see the beam in the air"** → raise `_Density` or `_Intensity` on [FlashlightBeam.mat](Assets/Game/Art/Materials/FlashlightBeam.mat).

**"Beam core too tight / too washed out"** → `_AxisFalloff` on the beam material. Higher = tighter bright axis, lower = more uniformly lit cone.

**"Beam looks chunky / banded"** → raise `_StepCount` (24 default, up to 64). Cost scales linearly.

**"Beam clips into walls weirdly"** → the beam clips against the scene depth buffer automatically. If you want a softer transition, raise `_SoftIntersect`. The beam *will* stop at solid geometry — that's the point.

**"Rays don't reach the ground"** → enable `debugDrawRays` on the Flashlight component to see the raycasts in the Scene view. Green = center hit, magenta = probe hit, yellow/grey = miss. Common causes: `beamHitMask` excludes the ground layer; the ground has no collider; you're pointing into empty space past `beamMaxLength`.

## Adding flashlight response to a new shader

```hlsl
#include "Assets/Game/Art/Shaders/Flashlight.hlsl"
// in fragment, after your normal lighting:
lit += SampleFlashlight(positionWS, N, wrap); // wrap = 0 if you don't have a wrapped-diffuse term
```

## Gotchas

- Only one flashlight supported (globals are singular).
- Long-throw layer doesn't sample shadows — that's what keeps it cheap. URP layer handles shadows.
- Beam mesh is built ONCE in `Awake()` as a generous bounding volume at `beamMaxLength × beamWidthScale`. It is **not** resized per frame. The shader ray-marches inside this bounding mesh and clips against `_FlashlightBeamEnd` (the per-frame raycast distance pushed from C#) plus the scene depth buffer. If you change `beamMaxLength`, `beamWidthScale`, `flashlightReach`, or the Light's `Spot Angle` at runtime, the mesh won't update — restart play mode.
- The shader reads `_CameraDepthTexture`. URP's "Depth Texture" must be enabled on the active URP asset (it is on `PC_RPAsset`; check Mobile if you're targeting it).
- Raycast layer mask excludes the Player layer (6) by default. If your player uses a different layer, update `beamHitMask` or rays will self-hit and the beam collapses to zero length.
- Toggle through the script's `SetEnabled`, not the Light directly, or the beam-renderer desyncs.
