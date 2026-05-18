# Flashlight

Toggleable spot light on the player camera. Press **L** to toggle.

Lives on the `Flashlight` child of [Main Camera.prefab](Assets/Prefabs/Camera/Main Camera.prefab).

## Three pieces

| Piece | What it does | File |
|---|---|---|
| **URP spot light** | Near-field lighting + shadows | Light component on the prefab |
| **Long-throw layer** | Reaches far surfaces (URP falls off too fast) | [Flashlight.cs](Assets/Scripts/Player/Flashlight.cs) pushes globals → [Flashlight.hlsl](Assets/Shaders/Flashlight.hlsl) |
| **Visible beam** | Dust cone in the air. Analytical ray-vs-cone math in fragment shader — cone mesh is just a bounding volume | [FlashlightBeam.shader](Assets/Shaders/FlashlightBeam.shader) |

Consumers: [CaveTriplanar.shader](Assets/Shaders/caves/CaveTriplanar.shader), [StylizedTerrain.shader](Assets/Shaders/StylizedTerrain.shader). Both add `lit += SampleFlashlight(posWS, N, wrap);` on top of URP's normal additional-lights loop.

## Tweaking

**"Doesn't reach far enough"** → lower `longThrowFalloff` on the Flashlight script (0.012 default; try 0.006). Or raise `longThrowIntensity`.

**"Cone shape too obvious / too narrow"** → widen `Spot Angle` / `Inner Spot Angle` on the Light component. Cookie texture is intentionally off — re-enabling it brings back the projected shape.

**"Can't see the beam in the air"** → raise `_Intensity` on [FlashlightBeam.mat](Assets/Materials/FlashlightBeam.mat). The cone mesh is just a bounding volume — brightness is computed analytically. If the mesh isn't big enough, the visible beam will get clipped: raise `beamLengthScale` / `beamWidthScale` on the script (requires play-mode restart).

**"Beam looks too solid / too thin"** → `_RadialDensity` on the beam material. Lower = wider/softer halo, higher = thin laser-like core.

**"Beam falls off too fast along its length"** → `_AxialFalloff` on the beam material. Lower = carries further.

**"Beam clips into walls weirdly"** → `_SoftIntersect` on the beam material. Higher = smoother fade where the beam meets surfaces.

## Adding flashlight response to a new shader

```hlsl
#include "Assets/Shaders/Flashlight.hlsl"
// in fragment, after your normal lighting:
lit += SampleFlashlight(positionWS, N, wrap); // wrap = 0 if you don't have a wrapped-diffuse term
```

## Gotchas

- Only one flashlight supported (globals are singular).
- Long-throw layer doesn't sample shadows — that's what keeps it cheap. URP layer handles shadows.
- Beam cone mesh is baked at `Awake()`. Light range/angle and beam scale changes need a play-mode restart.
- Toggle through the script's `SetEnabled`, not the Light directly, or the beam mesh desyncs.
