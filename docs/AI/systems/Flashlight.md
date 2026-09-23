---
system: Flashlight
layer: characters
summary: "The torch: a worn forearm gauntlet whose lamp is a URP spot, long-throw shader globals and a beam volume"
paths:
  - Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs
  - Assets/Game/Scripts/Items/Artifacts/Gadgets/FlashlightGauntletArtifact.cs
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlashlightGauntlet.prefab
  - Assets/Game/Scripts/Characters/Player/Combat/PlayerAimRig.cs
  - Assets/Game/Scripts/Characters/Player/Combat/PlayerArmAim.cs
  - Assets/Game/Scripts/Characters/Player/Combat/ArmAim.cs
  - Assets/Game/Art/Shaders/Effects/Flashlight.hlsl
  - Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader
  - Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab
symptoms:
  - "L does nothing and there is no torch at all"
  - "the beam points somewhere other than the crosshair"
  - "the torch lights past what I am looking at, or lags a frame behind the crosshair"
  - "the arm twists to the crosshair while holding a two-handed item"
  - "the torch arm stays locked forward however far I look up or down"
  - "the beam whips around whenever I fire a gauntlet"
  - "the torch arm hangs at my side and lights my boots"
  - "switching the torch on changes nothing about how the body stands"
  - "the torch comes on and the EMPTY arm goes up instead"
  - "the arm holding the torch drops whenever I pick something up"
  - "the arm stays up after the torch is switched off"
  - "the bulb glows while the torch is switched off"
  - "taking the gauntlet off leaves a light burning on someone's arm"
  - "the torch dies at about 40 m and distant terrain stays black"
  - "a lit shaft hangs in the air with the lamp switched off"
  - "the beam cone collapses to zero length or stops at nothing"
  - "another player disconnecting blacks out my long-throw lighting"
  - "the torch is off after a load, or a remote player's torch state is wrong"
  - "a lit torch and a powered scanner, one per wrist, and only the right arm comes up"
  - "the torch came back on after a reload but the world is still dark for everyone else"
  - "changing beam length or spot angle in the Inspector does nothing at runtime"
reads_with: [PlayerCharacter, BodyEquipment, Artifacts, Multiplayer, Persistence, Environment]
updated: 2026-09-13
---

# Flashlight

A player torch in three layers: a URP spot light, a global-uniform long-throw contribution for shaders that opt in, and a screen-space beam volume. **Worn on a forearm as the Flashlight Gauntlet** and switched with that arm's key (Q or E) — there is no helmet lamp and no L key.
**Scope:** [Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs), [FlashlightGauntletArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/FlashlightGauntletArtifact.cs), [Flashlight.hlsl](Assets/Game/Art/Shaders/Effects/Flashlight.hlsl), [FlashlightBeam.shader](Assets/Game/Art/Shaders/Effects/FlashlightBeam.shader).
**Related:** [BodyEquipment.md](BodyEquipment.md) · [Artifacts.md](Artifacts.md) · [PlayerCharacter.md](PlayerCharacter.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Environment.md](Environment.md)

## Model

- [Flashlight.prefab](Assets/Game/Prefabs/VisualEffects/Lighting/Flashlight.prefab) is nested on the **`Emitter`** of [FlashlightGauntlet.prefab](Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlashlightGauntlet.prefab), at the mouth of the lamp's horn, at an identity local pose. Until 2026-09-03 it hung under Main Camera.prefab instead; that instance is gone.
- **The beam leaves along the ARM, and the arm is AIMED.** The lamp is diegetic — you watch a wrist torch swing — but it points at what the player is looking at, because a torch lighting past the thing you are looking at cannot be told from a torch that is off (GDC-L1-ANIM-0003). `PlayerArmAim` swings the shoulder and elbow in `LateUpdate` so the lamp's own forward converges on the look; the pose layer decides whether the arm is up, this decides where it points. Between 2026-09-03 and 2026-09-13 the beam went wherever the clip left the forearm, on purpose; that is reversed.
- **It converges on a POINT, not a direction.** `convergeRange` (25 m) — the wrist is not the eye, so a beam pointed parallel to the look sits beside the crosshair by the width of that offset, worst on near surfaces. Aiming at a point puts the two together at that range and keeps the error small either side of it.
- **`maxSwing` (75°) is a shoulder limit, and the arm stops following at it** rather than reaching through the chest. Looking further back than that leaves the beam behind the crosshair — the readable failure of the two.
- **No gauntlet worn, no light of any kind.** The torch is an item now; a player who drops it is in the dark. The Flashlight Gauntlet is in `startingBody` slot 2 (the right forearm) on [PlayerCharacterNetworked.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab) so a fresh player still has one.
- **While the torch is LIT the body takes its ordinary held-item pose**, which is the SHAPE of the arm — where it points is `PlayerArmAim`'s, laid over the evaluated clip. The flashlight has no pose of its own. Without it the arm hangs at the player's side and the torch lights their boots. `FlashlightGauntletArtifact` calls `PlayerAimRig.SetWornStyle(WornOn, litPose)` off `Flashlight.Switched`, which writes the same `HoldStyle` parameter a held item does; switching off puts the arm down, so the pose reads the lamp's state across a room. Anything actually in the hands wins, for free — `PlayerAimRig.PoseStyle` prefers `heldStyle`, and there is only one parameter, so the two can never both be on.
- **The arm it is worn on is passed in, because the pose is right-handed.** Measured off the clips: `HumanM@Gun_Aim01` puts the right hand 0.19 up and 0.19 forward of the body centre and leaves the left one at the hip. So a lamp on the LEFT forearm plays the pose mirrored — `PlayerAimRig.PoseMirrored`, written to a `HoldMirror` bool, which selects a `Hold <style> Mirrored` twin state built by the same `state.mirror` trick `Raise Left` already uses. A held item is never mirrored; the off hand grips without the body turning round it. The arm reaches the artifact as `UsableItem.WornOn`, set from the body slot beside `Worn`.
- **A device on each forearm poses BOTH arms, through a second masked layer.** `Upper Body` answers one ask, so a lit torch on one wrist and a powered scanner on the other used to end with the right arm posing and the left hanging. The `Worn Left` layer — masked to the left arm alone, mirrored states, its own `WornLeftStyle` parameter, built by the same `PlayerUpperBodySetup` menu item and ordered directly above `Upper Body` so the Glide layer still outranks it — carries the left arm for exactly that case. `PlayerAimRig.LeftArmStyle` is the left device's style *only while the right arm holds the main layer*; a left device on its own stays on the main layer mirrored, which poses the chest with it, and a held item stops both.
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
| `FlashlightGauntletBuilder` | FlashlightGauntletBuilder.cs | *Tools ▸ SpaceGame ▸ Items ▸ Build Flashlight Gauntlet*. Owns the prefab and the item asset |
| `PlayerArmAim` | [PlayerArmAim.cs](Assets/Game/Scripts/Characters/Player/Combat/PlayerArmAim.cs) | Points a posed forearm at the look. `SetPointer`/`ClearPointer` per arm; runs on every machine off `PlayerViewNetwork.AimPivot`. Added by `PlayerAimRig.Awake`, never authored on a prefab |
| `ArmAim` | [ArmAim.cs](Assets/Game/Scripts/Characters/Player/Combat/ArmAim.cs) | The maths with no frame in it: `Convergence`, the clamped `Swing`, and `Point` — shoulder share then elbow passes. Pinned by `ArmAimTests` |
| `PlayerAimRig.SetWornStyle` | [PlayerAimRig.cs](Assets/Game/Scripts/Characters/Player/Combat/PlayerAimRig.cs) | The pose a working gauntlet on one arm asks for — **shared with the Item Scanner**, which asks for it while powered ([Artifacts.md](Artifacts.md)). `PoseStyle` lets a held item override it, `PoseMirrored` says which arm asked, `Posing` is what the layer weight follows, `LeftArmStyle` is what the second (left-arm) layer plays when both arms ask at once |
| `PlayerUpperBodySetup` | [PlayerUpperBodySetup.cs](Assets/Game/Editor/PlayerUpperBodySetup.cs) | *Tools ▸ SpaceGame ▸ Player ▸ Build Upper Body Layer*. Builds both masked layers, their masks and every hold/raise/mirrored state. Idempotent; re-run it after adding a hold style |

Consumers of the long-throw layer: [StylizedTerrain.shader](Assets/Game/Art/Shaders/Terrain/StylizedTerrain.shader), [CaveTriplanar.shader](Assets/Game/Art/Shaders/caves/CaveTriplanar.shader), [AlgaeRock.shader](Assets/Game/Art/Shaders/caves/AlgaeRock.shader) — each does `lit += SampleFlashlight(IN.positionWS, N, wrap);` after its normal lighting.

## Flows

1. **Wear.** `BodyEquipmentController` instantiates the gauntlet on the forearm bone **on every machine** (derived from replicated body-slot state, never sent). `OnEquipped` hands the lamp to `PlayerViewNetwork.SetTorch`, which on a peer applies `netTorch` immediately. `OnUnequipped` calls `ClearTorch`.
2. **Toggle.** Q or E → `UseChannel` → `FlashlightGauntletArtifact.Use()` on the **owner only** → `lamp.Switch(!lamp.IsOn)`. There is no message: `PlayerViewNetwork.Publish` sees `IsOn` change and writes `netTorch`, and every peer's `ApplyTorch` calls `Switch` on its own copy. A late joiner reads the current value in `OnNetworkSpawn`.
3. **Aim.** Every frame, on every machine, `PlayerArmAim.LateUpdate` asks `PlayerAimRig.WornArmPosed(arm)` — is that arm up because of the device worn on it? If so it eases its own weight in over `aimBlendTime` (0.18 s, the pose's own blend) and calls `ArmAim.Point`: the shoulder takes `shoulderShare` (0.55) of the swing, then the elbow closes the remainder over `elbowPasses` (2). Nothing new goes on the wire — the look comes from `PlayerViewNetwork.AimPivot`, which is this player's live aim on their machine and their replicated one everywhere else.
4. **Beam length.** `UpdateBeamGeometry` casts a centre ray plus `beamProbeRays` (6) around a ring at 0.7 of the outer half-angle, takes the shortest **axial** distance, and lerps `currentBeamLength` by `beamLengthSmoothing`.
5. **Push.** `PushShaderGlobals` every frame, owner only — in `LateUpdate`, after the arm is aimed.
6. **Pose and bulb.** `Flashlight.Switched` fires on whichever machine changed, and the artifact repaints `Mesh_Flashlight_Bulb`'s emission through a `MaterialPropertyBlock` — so the model reads on/off correctly for peers too, and without touching the shared palette material.

## Multiplayer

The lamp no longer needs the input gate it once did — it reads no keyboard, and the gauntlet's own key already arrives through `UseChannel` on the owner alone. What remains owner-gated is the single-slot effects. The **URP spot light** is switched for everyone (it is a real light and lights the world correctly for all viewers); the **long-throw layer and the beam mesh** run only for the lamp that owns the single shader slot. Giving every torch those too means an *array* in `Flashlight.hlsl` — a rendering change, not a netcode one.

## Persistence

One bool, in the **item's own state bag** under key `on` — `FlashlightGauntletArtifact.CaptureItemState`/`RestoreItemState`. `FlashlightSaveable` and its `flashlight` player-record key are **gone**: the lamp is part of an item now, so the bit travels with the gauntlet (into a chest, onto another player) instead of with the body that happened to be wearing it. Off is the default and is not written, so an unlit gauntlet adds no key.

Body-slot bags are saved by `BodyEquipmentSaveable` through `GearSaveCodec.CaptureStates`. Old saves carrying the retired `flashlight` key are ignored.

## Gotchas

- **`RestoreItemState` is owner-only, and the guard is load-bearing.** A peer's copy of a worn slot arrives with an **empty** bag — `BodyEquipmentNetwork` replicates the item id and clears the state on every machine — so an ungated restore runs a frame after `SetTorch` applied `netTorch` and switches a lit torch back off. `LateUpdate` puts it right again, which is exactly the one-frame flicker nobody can reproduce on purpose.
- **`PlayerViewNetwork` is HANDED the lamp; it does not look for one.** A worn gauntlet is instantiated and parented inside one call, and any search of the player for a `Flashlight` run earlier than that — in `Awake`, in `OnNetworkSpawn` — finds nothing and never looks again.
- **`ClearTorch` does not publish false.** `Publish` reads `torch != null && torch.IsOn` every frame and sends it on the next one; doing it twice is how the published value and the variable get to disagree.
- **The layer WEIGHT is a separate question from the `HoldStyle` VALUE, and forgetting that is ANIM-01.** `PlayerAimRig` used to ease `holdT` from `heldStyle` alone, so a lit torch with empty hands wrote its style, the state machine entered the pose, and the layer stayed at weight 0 — a correct pose nobody could see, with a clean console. Both now come off `PoseStyle`, through `Posing`.
- **The pose channel is one per arm and it is not the torch's alone.** `SetWornStyle` is what any worn forearm device asks the body with; the Item Scanner uses it too, to bring its screen up where the wearer can read it. One device per forearm slot, so they cannot fight over an arm — and two devices at once are two asks that are now both answered: the right arm's on `Upper Body`, the left arm's on `Worn Left`.
- **The second arm needs the `Worn Left` LAYER, and a rig without it fails only in that one case.** Until 2026-09-13 there was one pose and it faced one way, so a lit left-arm torch beside a powered right-arm scanner lit the ground while its own bulb said it was on — a device reporting its state wrongly (GDC-L1-ANIM-0003). `PlayerAimRig.Awake` now logs an error naming the menu item when the layer is missing, because everything else keeps working and nothing else says why.
- **The torch pose is the held-item pose, not one of its own.** A bespoke three-clip set (elbow closed at 90°, blended on `AimPitch`) was authored for it first and thrown away: the forearm ends up in the same place either way, and the hold pose already exists, already blends and already has somewhere to lose to. Do not re-add `TorchArm` — the pose belongs in `HoldStyle`, which is what makes "a held item wins" free rather than a rule.
- **The firing raise still outranks both.** `ArmRaise != 0` blocks every hold transition, so firing any gauntlet drops the torch pose for the raise's duration and then returns to it. That is the existing behaviour for held items and it is left alone. The `Worn Left` layer has to yield to it explicitly — it is a layer ABOVE the raise, not a transition beside it, so its weight is held at 0 while that arm's raise latch is up or the left gauntlet would fire with no gesture at all.
- **`Flashlight` runs in `LateUpdate` at execution order 960, and that number is load-bearing.** Everything it does — the shader globals, the beam's probe rays — reads its own transform, and that transform is a forearm bone the Animator writes between `Update` and `LateUpdate` and `PlayerArmAim` (950) aims after that. Running in `Update` pushes LAST frame's direction: a beam a frame behind the crosshair while turning, measured against a cone that was pointing somewhere else.
- **The aim is gated by `PlayerAimRig.WornArmPosed`, not by the lamp.** The gauntlet registers its pointer once, when it is worn, and never touches it again — switching off, picking up a rifle, dying and opening the gear screen all stop the aim because they all stop the POSE. A device that gated the aim itself would have to learn each of those separately, and would miss one.
- **The pointer is the LAMP's transform, not the bone's.** What has to face the crosshair is the thing the beam leaves along; the seat's rotation between the bone and the horn is exactly the offset nobody should be typing twice.
- **A firing raise keeps the aim.** `ArmRaise` drops the hold pose for the gesture's duration, so the raise clip owns the arm's shape — but `WornArmPosed` reads the worn style, not the layer, so the beam stays on the crosshair through it. That is deliberate: the old behaviour was the beam whipping across the cave every time a gauntlet fired.
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
6. Aim tuning is on `PlayerArmAim`, on the player, not on the gauntlet: `convergeRange` (where the beam and the crosshair agree exactly), `maxSwing` (how far the arm will follow), `shoulderShare` (how much of the swing is shoulder rather than elbow), `elbowPasses`, `aimBlendTime`. Any other worn device that should point where the player looks calls `SetPointer(WornOn, <its own emitter>)` in `OnEquipped` and `ClearPointer` in `OnUnequipped` — the Item Scanner deliberately does not, because its screen has to face the wearer.
7. Changing the pose: `litPose` on the prefab's `FlashlightGauntletArtifact` — `OneHanded` is the ordinary item pose, `Relaxed` carries it lower. A new style needs a row in `PlayerUpperBodySetup.HoldStyles` and nothing else: the mirrored twin and the `Worn Left` state are both built from that row. Changing what those poses *are* changes them for every held item too; that is [Artifacts.md](Artifacts.md)'s `HoldStyle`, not this system's.
8. A second torch (a lantern, a vehicle lamp) is a new item carrying its own `Flashlight`; only one may own the long-throw slot, and today that is whichever the owner's `PlayerViewNetwork` was handed last.
