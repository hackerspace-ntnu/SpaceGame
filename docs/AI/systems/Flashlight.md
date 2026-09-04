---
system: Flashlight
layer: characters
summary: "The torch: a worn forearm gauntlet whose lamp is a URP spot, long-throw shader globals and a beam volume"
paths:
  - Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs
  - Assets/Game/Scripts/Items/Artifacts/Gadgets/FlashlightGauntletArtifact.cs
  - Assets/Game/Editor/AssetPipeline/FlashlightGauntletBuilder.cs
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlashlightGauntlet.prefab
  - "Assets/Game/Art/Models/_Source~/models/gear/gauntlet_flashlight.py"
  - Assets/Game/Scripts/Characters/Player/Combat/PlayerAimRig.cs
  - Assets/Game/Art/Shaders/Effects/Flashlight.hlsl
  - Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader
  - Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab
symptoms:
  - "L does nothing and there is no torch at all"
  - "the beam points somewhere other than the crosshair"
  - "the beam whips around whenever I fire a gauntlet"
  - "the torch arm hangs at my side and lights my boots"
  - "the arm holding the torch drops whenever I pick something up"
  - "the arm stays up after the torch is switched off"
  - "the bulb glows while the torch is switched off"
  - "taking the gauntlet off leaves a light burning on someone's arm"
  - "the torch dies at about 40 m and distant terrain stays black"
  - "a lit shaft hangs in the air with the lamp switched off"
  - "the beam cone collapses to zero length or stops at nothing"
  - "another player disconnecting blacks out my long-throw lighting"
  - "the torch is off after a load, or a remote player's torch state is wrong"
  - "the torch came back on after a reload but the world is still dark for everyone else"
  - "changing beam length or spot angle in the Inspector does nothing at runtime"
reads_with: [PlayerCharacter, BodyEquipment, Artifacts, Multiplayer, Persistence, Environment]
updated: 2026-09-03
---

# Flashlight

A player torch in three layers: a URP spot light, a global-uniform long-throw contribution for shaders that opt in, and a screen-space beam volume. **Worn on a forearm as the Flashlight Gauntlet** and switched with that arm's key (Q or E) — there is no helmet lamp and no L key.
**Scope:** [Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs), [FlashlightGauntletArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/FlashlightGauntletArtifact.cs), [Flashlight.hlsl](Assets/Game/Art/Shaders/Effects/Flashlight.hlsl), [FlashlightBeam.shader](Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader).
**Related:** [BodyEquipment.md](BodyEquipment.md) · [Artifacts.md](Artifacts.md) · [PlayerCharacter.md](PlayerCharacter.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Environment.md](Environment.md)

## Model

- [Flashlight.prefab](Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab) is nested on the **`Emitter`** of [FlashlightGauntlet.prefab](Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlashlightGauntlet.prefab), at the mouth of the lamp's horn, at an identity local pose. Until 2026-09-03 it hung under [Main Camera.prefab](Assets/Game/Prefabs/Camera/Main%20Camera.prefab) instead; that instance is gone.
- **The beam therefore follows the ARM, not the camera.** That was chosen deliberately over a camera-aimed cone: the lamp is diegetic and you have to point it. The cost is real and is the thing to remember before "fixing" it — the light and the crosshair are different directions, and the `Point {Right,Both} {Down,Level,Up}` clips that raise a firing arm swing the beam with them.
- **No gauntlet worn, no light of any kind.** The torch is an item now; a player who drops it is in the dark. The Flashlight Gauntlet is in `startingBody` slot 2 (the right forearm) on [PlayerCharacterNetworked.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab) so a fresh player still has one.
- **While the torch is LIT the body takes its ordinary held-item pose**, and that is the whole mechanism — the flashlight has no pose of its own. Without it the arm hangs at the player's side and the torch lights their boots. `FlashlightGauntletArtifact` calls `PlayerAimRig.SetTorchStyle(litPose)` off `Flashlight.Switched`, which writes the same `HoldStyle` parameter a held item does; switching off puts the arm down, so the pose reads the lamp's state across a room. Anything actually in the hands wins, for free — `PlayerAimRig.EffectiveStyle` prefers `heldStyle`, and there is only one parameter, so the two can never both be on.
- The exporter maps the model's Blender −Y (out of the horn) onto Unity **+Z**, the axis a spot light shines down, so the nested lamp needs no rotation offset. Anything typed there is a second opinion about which way the horn faces.
- **URP `Light.range` is intentionally short (~40 m).** Distance comes from the long-throw layer via `flashlightReach` (120 m). The split is why intensity tuning works: URP only covers near-field, so ~25 does not blow out a wall 1 m away.
- **Long-throw is a set of global shader uniforms** — `_FlashlightPos/Dir/Color/Params/Falloff/BeamEnd` — so there is exactly **one** writer per frame: the local player's lamp (`Network.Owns(this)`).
- Falloff is inverse-linear, `1 / (1 + k·d)` with `k = longThrowFalloff` (0.012), plus a soft range fade from `longThrowRangeFadeStart` (0.85) of `flashlightReach`. It samples **no shadows** — that is what keeps it cheap.
- The beam is **not ray-marched**. For each fragment it finds the closest point on the view ray to the light axis, then shades by radial distance (a `_CoreWidth` core + `_HaloWidth` halo) and axial distance. Brightness therefore depends on the cone's world position, not on how long the camera ray spends inside it — looking down the axis does not brighten the screen.
- The beam **mesh is a bounding volume built once in `Awake`** at `min(beamMaxLength, flashlightReach)` and `tan(halfAngle)·len·beamWidthScale` radius. It is never resized. Per-frame length comes from raycasts, pushed as `_FlashlightBeamEnd` and clipped in the shader.

## Key types

| Type | File | Role |
|---|---|---|
| `Flashlight` | [Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs) | The lamp: `IsOn`, `Switch(bool)`, the `Switched` event, `SetEnabled`, beam mesh, shader globals. Reads no input |
| `FlashlightGauntletArtifact` | [FlashlightGauntletArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/FlashlightGauntletArtifact.cs) | The switch, the bulb, and the saved bit. `UseAuthority.Owner` |
| `SampleFlashlight` | [Flashlight.hlsl](Assets/Game/Art/Shaders/Effects/Flashlight.hlsl) | `float3 SampleFlashlight(posWS, N, wrap)` — the long-throw contribution |
| beam shader | [FlashlightBeam.shader](Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader) | `Blend One One`, `ZWrite Off`, `Cull Off`. Material: [FlashlightBeam.mat](Assets/Game/Art/Materials/Effects/FlashlightBeam.mat) |
| `PlayerViewNetwork` | [PlayerViewNetwork.cs](Assets/Game/Scripts/Characters/Player/Core/PlayerViewNetwork.cs) | `NetworkVariable<bool> netTorch`, `TorchOn`, `SetTorch`/`ClearTorch` |
| `FlashlightGauntletBuilder` | [FlashlightGauntletBuilder.cs](Assets/Game/Editor/AssetPipeline/FlashlightGauntletBuilder.cs) | *Tools ▸ SpaceGame ▸ Items ▸ Build Flashlight Gauntlet*. Owns the prefab and the item asset |
| `PlayerAimRig.SetTorchStyle` | [PlayerAimRig.cs](Assets/Game/Scripts/Characters/Player/Combat/PlayerAimRig.cs) | The pose a lit torch asks for; `EffectiveStyle` lets a held item override it |

Consumers of the long-throw layer: [StylizedTerrain.shader](Assets/Game/Art/Shaders/Terrain/StylizedTerrain.shader), [CaveTriplanar.shader](Assets/Game/Art/Shaders/caves/CaveTriplanar.shader), [AlgaeRock.shader](Assets/Game/Art/Shaders/caves/AlgaeRock.shader) — each does `lit += SampleFlashlight(IN.positionWS, N, wrap);` after its normal lighting.

## Flows

1. **Wear.** `BodyEquipmentController` instantiates the gauntlet on the forearm bone **on every machine** (derived from replicated body-slot state, never sent). `OnEquipped` hands the lamp to `PlayerViewNetwork.SetTorch`, which on a peer applies `netTorch` immediately. `OnUnequipped` calls `ClearTorch`.
2. **Toggle.** Q or E → `UseChannel` → `FlashlightGauntletArtifact.Use()` on the **owner only** → `lamp.Switch(!lamp.IsOn)`. There is no message: `PlayerViewNetwork.Publish` sees `IsOn` change and writes `netTorch`, and every peer's `ApplyTorch` calls `Switch` on its own copy. A late joiner reads the current value in `OnNetworkSpawn`.
3. **Beam length.** `UpdateBeamGeometry` casts a centre ray plus `beamProbeRays` (6) around a ring at 0.7 of the outer half-angle, takes the shortest **axial** distance, and lerps `currentBeamLength` by `beamLengthSmoothing`.
4. **Push.** `PushShaderGlobals` every frame, owner only.
5. **Pose and bulb.** `Flashlight.Switched` fires on whichever machine changed, and the artifact repaints `Mesh_Flashlight_Bulb`'s emission through a `MaterialPropertyBlock` — so the model reads on/off correctly for peers too, and without touching the shared palette material.

## Multiplayer

The lamp no longer needs the input gate it once did — it reads no keyboard, and the gauntlet's own key already arrives through `UseChannel` on the owner alone. What remains owner-gated is the single-slot effects. The **URP spot light** is switched for everyone (it is a real light and lights the world correctly for all viewers); the **long-throw layer and the beam mesh** run only for the lamp that owns the single shader slot. Giving every torch those too means an *array* in `Flashlight.hlsl` — a rendering change, not a netcode one.

## Persistence

One bool, in the **item's own state bag** under key `on` — `FlashlightGauntletArtifact.CaptureItemState`/`RestoreItemState`. `FlashlightSaveable` and its `flashlight` player-record key are **gone**: the lamp is part of an item now, so the bit travels with the gauntlet (into a chest, onto another player) instead of with the body that happened to be wearing it. Off is the default and is not written, so an unlit gauntlet adds no key.

Body-slot bags are saved by `BodyEquipmentSaveable` through `GearSaveCodec.CaptureStates`. Old saves carrying the retired `flashlight` key are ignored.

## Gotchas

- **`RestoreItemState` is owner-only, and the guard is load-bearing.** A peer's copy of a worn slot arrives with an **empty** bag — `BodyEquipmentNetwork` replicates the item id and clears the state on every machine — so an ungated restore runs a frame after `SetTorch` applied `netTorch` and switches a lit torch back off. `LateUpdate` puts it right again, which is exactly the one-frame flicker nobody can reproduce on purpose.
- **`PlayerViewNetwork` is HANDED the lamp; it does not look for one.** A worn gauntlet is instantiated and parented inside one call, and any search of the player for a `Flashlight` run earlier than that — in `Awake`, in `OnNetworkSpawn` — finds nothing and never looks again.
- **`ClearTorch` does not publish false.** `Publish` reads `torch != null && torch.IsOn` every frame and sends it on the next one; doing it twice is how the published value and the variable get to disagree.
- **The torch pose is the held-item pose, not one of its own.** A bespoke three-clip set (elbow closed at 90°, blended on `AimPitch`) was authored for it first and thrown away: the forearm ends up in the same place either way, and the hold pose already exists, already blends and already has somewhere to lose to. Do not re-add `TorchArm` — the pose belongs in `HoldStyle`, which is what makes "a held item wins" free rather than a rule.
- **The firing raise still outranks both.** `ArmRaise != 0` blocks every hold transition, so firing any gauntlet drops the torch pose for the raise's duration and then returns to it. That is the existing behaviour for held items and it is left alone.
- **The beam now starts at the wrist, not the eye.** It is much closer to the camera than it was, so `_NearFade` on [FlashlightBeam.mat](Assets/Game/Art/Materials/Effects/FlashlightBeam.mat) matters more than it used to.
- **`SetEnabled` is the only correct toggle** — it switches the beam renderer with the light. Touching the `Light` directly leaves a lit shaft with no lamp.
- **Beam renderer is `on && OwnsSingleSlotEffects`.** A remote lamp drawing the mesh would cut it to the *local* player's beam length — a cone of light stopping at nothing.
- **`OnDestroy` clears the shader slot only if it owned it.** Otherwise a remote player disconnecting blacked out the local long-throw layer.
- **`beamHitMask` excludes layer 6 (Player) by default.** Include it and the rays self-hit and the beam collapses to zero length. Set `debugDrawRays` to see them: green = centre hit, yellow = centre miss, magenta = probe hit.
- **Changing `beamMaxLength`, `beamWidthScale`, `flashlightReach` or `Spot Angle` at runtime does not rebuild the mesh** — restart play mode.
- **The beam shader reads `_CameraDepthTexture`.** URP "Depth Texture" must be on for the active URP asset.
- **One flashlight only** (the globals are singular), and only shaders that `#include` `Flashlight.hlsl` see the long throw — everything else is capped at `Light.range`.
- **Two flashlight gauntlets, one on each arm, is legal and half-broken.** Both URP spots light the world; only the owner's *last* `Update` writes the long-throw globals, so which arm gets the 120 m reach is arbitrary. Nothing forbids it today.
- **The bulb is emissive geometry, not a light.** It is dimmed by a property block, so a gauntlet lying on the ground unowned shows whatever the artifact last painted — its prefab default, `bulbDark`.
- **Do not hand-edit `FlashlightGauntlet.prefab`.** `FlashlightGauntletBuilder` rebuilds it wholesale from the Ruin Scanner's prefab; tuning belongs in that builder's constants or on `Flashlight.prefab`.

## Extending

1. New shader response: `#include "Assets/Game/Art/Shaders/Effects/Flashlight.hlsl"`, then `lit += SampleFlashlight(positionWS, N, wrap);` after your lighting (`wrap = 0` with no wrapped-diffuse term).
2. Reaches too short → raise `flashlightReach` / `longThrowIntensity`, or lower `longThrowFalloff`. Near surfaces blow out → lower the URP `Light` intensity, not the long throw.
3. Beam look → `_Intensity`, `_CoreWidth`/`_CoreStrength`, `_HaloWidth`/`_HaloStrength`/`_HaloPow`, `_EndFadePow`, `_NearFade` on [FlashlightBeam.mat](Assets/Game/Art/Materials/Effects/FlashlightBeam.mat). Beam missing geometry → widen `beamHitMask` or raise `beamProbeRays`; jittery → lower `beamLengthSmoothing`.
4. Per-player long throw would need the globals replaced by an array plus a loop in `SampleFlashlight`, and `OwnsSingleSlotEffects` dropped.
5. Changing the lamp's shape or where it points: edit `gauntlet_flashlight.py`'s constants, re-export with `gauntlet_flashlight_export.py`, then re-run *Tools ▸ SpaceGame ▸ Items ▸ Build Flashlight Gauntlet*. The builder re-finds `Emitter` and `Mesh_Flashlight_Bulb` by name, so renaming either in the model silently unwires it — the builder's `VERIFY` lines say so.
6. Changing the pose: `litPose` on the prefab's `FlashlightGauntletArtifact` — `OneHanded` is the ordinary item pose, `Relaxed` carries it lower. Changing what those poses *are* changes them for every held item too; that is [Artifacts.md](Artifacts.md)'s `HoldStyle`, not this system's.
7. A second torch (a lantern, a vehicle lamp) is a new item carrying its own `Flashlight`; only one may own the long-throw slot, and today that is whichever the owner's `PlayerViewNetwork` was handed last.
