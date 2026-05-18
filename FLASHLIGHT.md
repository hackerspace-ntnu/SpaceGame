# Flashlight

Toggleable spot light on the player camera. Press **L** to toggle.

Lives on the `Flashlight` child of [Main Camera.prefab](Assets/Prefabs/Camera/Main Camera.prefab).

## Three pieces

| Piece | What it does | File |
|---|---|---|
| **URP spot light** | Near-field lighting + shadows | Light component on the prefab |
| **Long-throw layer** | Reaches far surfaces (URP falls off too fast) | [Flashlight.cs](Assets/Scripts/Player/Flashlight.cs) pushes globals → [Flashlight.hlsl](Assets/Shaders/Flashlight.hlsl) |
| **Visible beam** | The dust cone you see in the air | [FlashlightBeam.shader](Assets/Shaders/FlashlightBeam.shader) on a procedural cone mesh |

Consumers: [CaveTriplanar.shader](Assets/Shaders/caves/CaveTriplanar.shader), [StylizedTerrain.shader](Assets/Shaders/StylizedTerrain.shader). Both add `lit += SampleFlashlight(posWS, N, wrap);` on top of URP's normal additional-lights loop.

## Tweaking

**"Doesn't reach far enough"** → lower `longThrowFalloff` on the Flashlight script (0.012 default; try 0.006). Or raise `longThrowIntensity`.

**"Cone shape too obvious / too narrow"** → widen `Spot Angle` / `Inner Spot Angle` on the Light component. Cookie texture is intentionally off — re-enabling it brings back the projected shape.

**"Can't see the beam in the air"** → raise `_Intensity` on [FlashlightBeam.mat](Assets/Materials/FlashlightBeam.mat). Or `beamLengthScale` / `beamWidthScale` on the script (requires play-mode restart — mesh is baked in `Awake()`).

**"Too smoky / too bright"** → `_EdgeFalloff` on the beam material. Higher = thinner edge glow.

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
