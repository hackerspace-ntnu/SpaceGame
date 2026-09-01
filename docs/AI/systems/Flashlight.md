---
system: Flashlight
layer: characters
summary: "Player torch in three layers - URP spot, global long-throw shader uniforms, screen-space beam volume"
paths:
  - Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs
  - Assets/Game/Art/Shaders/Effects/Flashlight.hlsl
  - Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader
  - Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab
  - Assets/Game/Scripts/Core/Persistence/Adapters/FlashlightSaveable.cs
symptoms:
  - "pressing L toggles every player's torch on this machine"
  - "the torch dies at about 40 m and distant terrain stays black"
  - "a lit shaft hangs in the air with the lamp switched off"
  - "the beam cone collapses to zero length or stops at nothing"
  - "another player disconnecting blacks out my long-throw lighting"
  - "the torch is off after a load, or a remote player's torch state is wrong"
  - "changing beam length or spot angle in the Inspector does nothing at runtime"
reads_with: [PlayerCharacter, Multiplayer, Persistence, Environment]
updated: 2026-09-01
---

# Flashlight

A player torch in three layers: a URP spot light, a global-uniform long-throw contribution for shaders that opt in, and a screen-space beam volume. Toggled with **L**.
**Scope:** [Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs), [Flashlight.hlsl](Assets/Game/Art/Shaders/Effects/Flashlight.hlsl), [FlashlightBeam.shader](Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader).
**Related:** [PlayerCharacter.md](PlayerCharacter.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Environment.md](Environment.md)

## Model

- [Flashlight.prefab](Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab) is nested inside [Main Camera.prefab](Assets/Game/Prefabs/Camera/Main%20Camera.prefab), which is itself under the player.
- **URP `Light.range` is intentionally short (~40 m).** Distance comes from the long-throw layer via `flashlightReach` (120 m). The split is why intensity tuning works: URP only covers near-field, so ~25 does not blow out a wall 1 m away.
- **Long-throw is a set of global shader uniforms** — `_FlashlightPos/Dir/Color/Params/Falloff/BeamEnd` — so there is exactly **one** writer per frame: the local player's lamp (`Network.Owns(this)`).
- Falloff is inverse-linear, `1 / (1 + k·d)` with `k = longThrowFalloff` (0.012), plus a soft range fade from `longThrowRangeFadeStart` (0.85) of `flashlightReach`. It samples **no shadows** — that is what keeps it cheap.
- The beam is **not ray-marched**. For each fragment it finds the closest point on the view ray to the light axis, then shades by radial distance (a `_CoreWidth` core + `_HaloWidth` halo) and axial distance. Brightness therefore depends on the cone's world position, not on how long the camera ray spends inside it — looking down the axis does not brighten the screen.
- The beam **mesh is a bounding volume built once in `Awake`** at `min(beamMaxLength, flashlightReach)` and `tan(halfAngle)·len·beamWidthScale` radius. It is never resized. Per-frame length comes from raycasts, pushed as `_FlashlightBeamEnd` and clipped in the shader.

## Key types

| Type | File | Role |
|---|---|---|
| `Flashlight` | [Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs) | The whole component: L key, `IsOn`, `RestoreOn(bool)`, `SetEnabled`, beam mesh, shader globals |
| `SampleFlashlight` | [Flashlight.hlsl](Assets/Game/Art/Shaders/Effects/Flashlight.hlsl) | `float3 SampleFlashlight(posWS, N, wrap)` — the long-throw contribution |
| beam shader | [FlashlightBeam.shader](Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader) | `Blend One One`, `ZWrite Off`, `Cull Off`. Material: [FlashlightBeam.mat](Assets/Game/Art/Materials/Effects/FlashlightBeam.mat) |
| `FlashlightSaveable` | [FlashlightSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/FlashlightSaveable.cs) | Key `flashlight`. On the player root of [PlayerCharacter.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab) |
| `PlayerViewNetwork` | [PlayerViewNetwork.cs](Assets/Game/Scripts/Characters/Player/Core/PlayerViewNetwork.cs) | `NetworkVariable<bool> netTorch`, `TorchOn`; reparents a remote lamp onto `AimPivot` |

Consumers of the long-throw layer: [StylizedTerrain.shader](Assets/Game/Art/Shaders/Terrain/StylizedTerrain.shader), [CaveTriplanar.shader](Assets/Game/Art/Shaders/caves/CaveTriplanar.shader), [AlgaeRock.shader](Assets/Game/Art/Shaders/caves/AlgaeRock.shader) — each does `lit += SampleFlashlight(IN.positionWS, N, wrap);` after its normal lighting.

## Flows

1. **Toggle.** `Update` reads `Keyboard.current[toggleKey]` directly, gated by `GameplayMenuScope.AcceptsGameplayInput` **and** `Network.Owns(this)`, then `SetEnabled(!enabled)`.
2. **Replicate.** `PlayerViewNetwork` publishes `IsOn` into `netTorch` and calls `RestoreOn` on every remote copy.
3. **Beam length.** `UpdateBeamGeometry` casts a centre ray plus `beamProbeRays` (6) around a ring at 0.7 of the outer half-angle, takes the shortest **axial** distance, and lerps `currentBeamLength` by `beamLengthSmoothing`.
4. **Push.** `PushShaderGlobals` every frame, owner only.

## Multiplayer

Owner-gated on both counts. A keyboard is not per-player: without the ownership gate one press of L toggled every player's lamp on this machine. The **URP spot light** is switched for everyone (it is a real light and lights the world correctly for all viewers); the **long-throw layer and the beam mesh** run only for the lamp that owns the single shader slot. Giving every torch those too means an *array* in `Flashlight.hlsl` — a rendering change, not a netcode one.

## Persistence

`FlashlightSaveable`, key `flashlight`, one bool. `CaptureState` returns `null` when off, so a fresh player record carries no key. Restores through `RestoreOn`, the same path `PlayerViewNetwork` uses — an authority stating the truth, never a flip.

## Gotchas

- **`FlashlightSaveable` uses `GetComponentInChildren`**, because the lamp is nested under `Main Camera`; a `GetComponent` on the root answers null on every player.
- **`PlayerViewNetwork` reparents the remote lamp *before* applying `netTorch`.** Reparenting out of a switched-off camera *activates* it, which runs `Flashlight.Awake`, which switches the light off.
- **`SetEnabled` is the only correct toggle** — it switches the beam renderer with the light. Touching the `Light` directly leaves a lit shaft with no lamp.
- **Beam renderer is `on && OwnsSingleSlotEffects`.** A remote lamp drawing the mesh would cut it to the *local* player's beam length — a cone of light stopping at nothing.
- **`OnDestroy` clears the shader slot only if it owned it.** Otherwise a remote player disconnecting blacked out the local long-throw layer.
- **`beamHitMask` excludes layer 6 (Player) by default.** Include it and the rays self-hit and the beam collapses to zero length. Set `debugDrawRays` to see them: green = centre hit, yellow = centre miss, magenta = probe hit.
- **Changing `beamMaxLength`, `beamWidthScale`, `flashlightReach` or `Spot Angle` at runtime does not rebuild the mesh** — restart play mode.
- **The beam shader reads `_CameraDepthTexture`.** URP "Depth Texture" must be on for the active URP asset.
- **One flashlight only** (the globals are singular), and only shaders that `#include` `Flashlight.hlsl` see the long throw — everything else is capped at `Light.range`.

## Extending

1. New shader response: `#include "Assets/Game/Art/Shaders/Effects/Flashlight.hlsl"`, then `lit += SampleFlashlight(positionWS, N, wrap);` after your lighting (`wrap = 0` with no wrapped-diffuse term).
2. Reaches too short → raise `flashlightReach` / `longThrowIntensity`, or lower `longThrowFalloff`. Near surfaces blow out → lower the URP `Light` intensity, not the long throw.
3. Beam look → `_Intensity`, `_CoreWidth`/`_CoreStrength`, `_HaloWidth`/`_HaloStrength`/`_HaloPow`, `_EndFadePow`, `_NearFade` on [FlashlightBeam.mat](Assets/Game/Art/Materials/Effects/FlashlightBeam.mat). Beam missing geometry → widen `beamHitMask` or raise `beamProbeRays`; jittery → lower `beamLengthSmoothing`.
4. Per-player long throw would need the globals replaced by an array plus a loop in `SampleFlashlight`, and `OwnsSingleSlotEffects` dropped.
