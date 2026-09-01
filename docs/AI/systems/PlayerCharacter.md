---
system: PlayerCharacter
layer: characters
summary: The astronaut the player drives: rigidbody movement, first-person look, stances, aim rig, suit, death
paths:
  - Assets/Game/Scripts/Characters/Player/
  - Assets/Game/Prefabs/Characters/Player/
  - Assets/Game/Scripts/Core/Input/
  - Assets/Game/Scripts/Presentation/Appearance/
  - Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs
symptoms:
  - "my player cannot move at all after loading a saved world"
  - "setting transform.position on the player does nothing, it snaps back the same frame"
  - "the flashlight or thing I attached to the player is invisible to other players"
  - "the cursor keeps re-locking itself while my panel is open"
  - "a dead player gets his controls back after dismounting or leaving a cutscene"
  - "I added a binding to the .inputactions asset and nothing happens in game"
  - "the player's arms do not move when holding an item, or the rig looks armless"
  - "another player's head never moves — they stare straight ahead while their view is clearly turning"
  - "a seated player's head stays turned after they stand up"
  - "my own backpack bounces into view in front of the first-person camera"
reads_with: [Persistence, Inventory, Artifacts, Vehicles]
updated: 2026-09-01
---

# Player Character

The astronaut the human drives: rigidbody movement, first-person look, stances, aim rig, suit colour, death, and what of all that other machines and save files see.

**Scope:** [Assets/Game/Scripts/Characters/Player/](Assets/Game/Scripts/Characters/Player/), [Presentation/Appearance/](Assets/Game/Scripts/Presentation/Appearance/), [Core/Input/](Assets/Game/Scripts/Core/Input/), [PlayerCharacter.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab) + [PlayerCharacterNetworked.prefab](Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab).
**Related:** [Persistence.md](Persistence.md) · [Inventory.md](Inventory.md) · [Artifacts.md](Artifacts.md) · [MountSystem.md](MountSystem.md) · [Flashlight.md](Flashlight.md) · [Cutscenes.md](Cutscenes.md) · health lives in [Gameplay/Health/](Assets/Game/Scripts/Gameplay/Health/) (own doc).

## Model

- **Two prefabs, one body.** [`PlayerCharacterNetworked`](Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab) is a *variant* of [`PlayerCharacter`](Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab); the variant only adds `NetworkObject`/`NetworkTransform`/`NetworkRigidbody` + the `Network*` behaviours. Everything gameplay is on the base. The variant is what [`SpawnManager.SpawnPlayerForClient`](Assets/Game/Scripts/Gameplay/Game/Spawning/SpawnManager.cs) and [`SingleplayerInitializer`](Assets/Game/Scripts/Gameplay/Game/State/SingleplayerInitializer.cs) instantiate; the base prefab appears in scenes as an editor placeholder.
- **Movement owns the body.** [`PlayerMovement`](Assets/Game/Scripts/Characters/Player/Movement/Movement.cs) *assigns* `rb.linearVelocity.x/z` in `FixedUpdate`; the y axis is left free for jump, pogo and flings.
- **Look owns yaw+pitch, split by clock.** [`PlayerLook`](Assets/Game/Scripts/Characters/Player/Movement/PlayerLook.cs) banks yaw per frame and spends it as one `MoveRotation` in `FixedUpdate`; pitch is a private float written to the camera transform each `Update`.
- **First person only.** Camera is the nested [Main Camera.prefab](Assets/Game/Prefabs/Camera/Main%20Camera.prefab) at local `(0, 1.45, 0.16)`, inside the helmet. Third-person views are other systems' (mount orbit cam, ragdoll cam, [`SpectatorCamera`](Assets/Game/Scripts/Characters/Player/Movement/SpectatorCamera.cs)).
- **Remote copies are switched off, not deleted.** [`PlayerController.DisablePlayer`](Assets/Game/Scripts/Characters/Player/Core/PlayerController.cs) kills input/movement/look and `SetActive(false)`s the camera + HUD GameObjects. Anything that must be visible on *other* machines therefore cannot live under the camera or in those components — hence `PlayerStance`, `PlayerAimRig` and [`PlayerViewNetwork`](Assets/Game/Scripts/Characters/Player/Core/PlayerViewNetwork.cs), which run everywhere.
- **`PlayerController.isDead` is the authority on control**, not the enabled flags — mounts/cutscenes/spectator all restore captured flags and would otherwise hand a corpse its controls back.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `PlayerController` | [Core/PlayerController.cs](Assets/Game/Scripts/Characters/Player/Core/PlayerController.cs) | Enable/disable the local player, cutscene handover, death freeze, spectator swap, `OnPlayerDeath`/`OnPlayerRevive` |
| `PlayerMovement` | [Movement/Movement.cs](Assets/Game/Scripts/Characters/Player/Movement/Movement.cs) | Walk/sprint/crouch/aim speeds, jump, dash, ground probe, fall damage, animator blend + stride rate, `CarryMomentum`/`SetTethered`/`SetBouncing`/`EnsureMovableBody` |
| `PlayerLook` | [Movement/PlayerLook.cs](Assets/Game/Scripts/Characters/Player/Movement/PlayerLook.cs) | Mouse look, cursor lock, FOV base+offset, `LookAlong`/`RestorePitch`/`Pitch`, per-camera hiding of own helmet/scarf (serialized) and worn gear (`SetWornHidden`, runtime) |
| `PlayerStance` | [Movement/PlayerStance.cs](Assets/Game/Scripts/Characters/Player/Movement/PlayerStance.cs) | Crouch (capsule + eye) and double-tap sprint with a charge tank; runs on every machine |
| `PlayerAimRig` | [Combat/PlayerAimRig.cs](Assets/Game/Scripts/Characters/Player/Combat/PlayerAimRig.cs) | Owns the masked `Upper Body` layer: hold pose weight, aim blend, right-hand IK; runs on every machine |
| `AimPose` | [Combat/AimPose.cs](Assets/Game/Scripts/Characters/Player/Combat/AimPose.cs) | Pure maths for hand goal, elbow hint, ease, grip-frame undo |
| `PlayerHeadLook` | [Combat/PlayerHeadLook.cs](Assets/Game/Scripts/Characters/Player/Combat/PlayerHeadLook.cs) | The one owner of head/neck aim: `Mode` (Free/Seated), `AddLook`, `Yaw`/`Pitch`/`LookRotation`; writes both bones in `LateUpdate` at order **950**. Runs on every machine; added at runtime by `PlayerViewNetwork` |
| `HeadAim` | [Combat/HeadAim.cs](Assets/Game/Scripts/Characters/Player/Combat/HeadAim.cs) | Pure maths + `HeadAimMode`: neck clamps, the seated/on-foot yaw rule, body-frame delta, bone share |
| `AimIkRelay` | [Combat/AimIkRelay.cs](Assets/Game/Scripts/Characters/Player/Combat/AimIkRelay.cs) | Runtime-added on the Animator GO; forwards `OnAnimatorIK` to the rig on the root |
| `AimProvider` | [Combat/AimProvider.cs](Assets/Game/Scripts/Characters/Player/Combat/AimProvider.cs) | The one camera-derived aim ray every other system should ask for |
| `PlayerViewNetwork` | [Core/PlayerViewNetwork.cs](Assets/Game/Scripts/Characters/Player/Core/PlayerViewNetwork.cs) | Replicates pitch / **head yaw** / torch / aiming; builds the runtime `AimPivot` child every machine can hang things on |
| `NetworkPlayerController` | [Core/NetworkPlayerController.cs](Assets/Game/Scripts/Characters/Player/Core/NetworkPlayerController.cs) | On spawn: adopt the spawn pose via `SaveTeleport`, then enable (owner) or disable (remote) |
| `PlayerRespawn` | [Core/PlayerRespawn.cs](Assets/Game/Scripts/Characters/Player/Core/PlayerRespawn.cs) | `NetMsg.Respawn` on this player's channel; server places then heals — never despawns the body |
| `FlungBody` | [Movement/FlungBody.cs](Assets/Game/Scripts/Characters/Player/Movement/FlungBody.cs) | `NetMsg.Flung` → owner-side impulse, `[DefaultExecutionOrder(200)]` so it drains *after* movement |
| `SuitPalette` / `SuitRecolor` | [Appearance/](Assets/Game/Scripts/Characters/Player/Appearance/) | Static swatch table + material relationships; `SuitRecolor` is a [`PaletteRecolor`](Assets/Game/Scripts/Presentation/Appearance/PaletteRecolor.cs) subclass shared with ship livery |
| `PlayerInputManager` | [Core/Input/PlayerInputManager.cs](Assets/Game/Scripts/Core/Input/PlayerInputManager.cs) | The only input source: wraps generated `InputControls`, exposes `MoveInput`/`LookInput`/`CrouchHeld`/`AimHeld` + press events |
| `PlayerIdentity` | [Multiplayer/Players/PlayerIdentity.cs](Assets/Game/Scripts/Core/Multiplayer/Players/PlayerIdentity.cs) | Owner-write name + suit index, server-write team; drives `SuitRecolor` on every peer |
| `PlayerRagdoll` | [Gameplay/Ragdoll/PlayerRagdoll.cs](Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs) | Death/knockdown limpness, moves the camera out of the head; subscribes to health directly so remotes go down too |

Also on the prefab root: [`Interactor`](Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs), [`EquipmentController`](Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs), [`PlayerInventoryComponent`](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryComponent.cs)/[`Network`](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs), [`BackpackController`](Assets/Game/Scripts/Items/Backpack/BackpackController.cs)/[`Network`](Assets/Game/Scripts/Items/Backpack/BackpackNetwork.cs), [`EffectManager`](Assets/Game/Scripts/Items/Artifacts/Effects/EffectManager.cs), [`DamageFeedback`](Assets/Game/Scripts/Gameplay/Health/DamageFeedback.cs), [`PlayerAudioModule`](Assets/Game/Scripts/Presentation/Audio/PlayerAudioModule.cs), [`EntityFaction`](Assets/Game/Scripts/agents/Faction/EntityFaction.cs), [`UnderTerrainGuard`](Assets/Game/Scripts/World/Safety/Core/UnderTerrainGuard.cs), [`PlayerPortalTraveller`](Assets/Game/Scripts/Portals/PlayerPortalTraveller.cs), [`PlayerInteriorTransit`](Assets/Game/Scripts/Core/SceneManagement/Interiors/PlayerInteriorTransit.cs), [`SandstormVictim`](Assets/Game/Scripts/World/Environment/Sandstorm/Effects/SandstormVictim.cs), [`NetRelay`](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetRelay.cs), [`ClientNetworkAnimator`](Assets/Game/Scripts/Core/Multiplayer/Authority/ClientNetworkAnimator.cs), [`NetworkedTeleport`](Assets/Game/Scripts/Core/Multiplayer/Authority/NetworkedTeleport.cs), [`SaveableEntity`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveableEntity.cs) + savers. [`LightningSpawner`](Assets/Game/Scripts/Characters/Player/Combat/LightningSpawner.cs) and [`InputManager`](Assets/Game/Scripts/Core/Input/InputManager.cs) are **not** on the prefab (legacy/unused).

## Geometry

Root pivot is **1 m above the soles** — measure with [`BodyFeet`](Assets/Game/Scripts/Locomotion/Ground/BodyFeet.cs), never a literal.

| Thing | Value |
| --- | --- |
| Capsule (child `Collider`) | local pos `y 0.5`, height 2, radius 0.5, **localScale y 1.5** → 3 m tall world, feet at root `y −1.0`, head `+2.0` |
| Eye / Main Camera | local `(0, 1.45, 0.16)` → 2.45 m above soles; drops `crouchEyeDrop` (0.6 m) when crouched |
| Model | [astronaut.fbx](Assets/Game/Art/Models/Characters/Astronaut/astronaut.fbx) nested instance; `Animator` + `Rigidbody` are on the **root**, not the model |
| Other children | `hand`, `PortalTrackPoint`, `Muzzle` (variant only), [PlayerHUD](Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab) |

## Flows

1. **Spawn.** `SpawnManager` instantiates the networked variant at a clamped spawn point → `SpawnAsPlayerObject` → `NetworkPlayerController.OnNetworkSpawn` calls `SaveTeleport.Move` (a raw transform write is undone by the rigidbody within the frame) → `EnablePlayer` on the owner, `DisablePlayer` elsewhere → `PlayerSaveSync` claims the profile and the server restores it.
2. **Move.** `PlayerInputManager.Update` reads `Move` → `PlayerMovement.FixedUpdate`: `EnsureMovableBody` → ground `SphereCast` → fall damage → `CurrentMoveSpeed` (crouch > aim > sprint > walk) → lerp toward target (`airControl` 0.3 in air) or `SteerTether` → `SteerWithoutBraking` → write x/z → animator `SpeedX/SpeedY/FallSpeed/MoveAnimSpeed/IsGrounded`.
3. **Look.** `Update` accumulates `pendingYaw` (× `sensitivity` × `GameSettings.MouseSensitivity` × aim scale) and writes pitch to the camera; `FixedUpdate` spends the banked yaw as one `MoveRotation`; `LateUpdate` re-asserts the cursor lock.
4. **Stance.** `PlayerStance.Update`: owner reads `CrouchHeld` + `IsOnGround`, refuses to stand under a ceiling (`HasHeadroom` sphere cast), sets the animator bool; remotes read the same bool back out of the replicated Animator. `ApplyStance` eases capsule height/centre and eye height.
5. **Aim.** `PlayerAimRig.Update`: owner ANDs `AimHeld` with "is driving" and a non-`None` `HoldStyle`, publishes via `PlayerViewNetwork.PublishAiming`; blends `holdT`/`aimT` (`aimT ≤ holdT`), writes layer weight + `HoldStyle`/`IsAiming`; `AimIkRelay` → `ApplyIk` pulls the right hand to `AimPivot` and undoes the grip frame.
6. **Head.** `PlayerHeadLook.Update` (owner, on foot) takes pitch from `PlayerLook` and zero yaw; seated, the angles arrive via `AddLook` from the seat's camera rig. `PlayerViewNetwork.LateUpdate` publishes both past `publishThreshold` and poses `AimPivot` as `Euler(pitch, headYaw, 0)`; `PlayerHeadLook.LateUpdate` (order 950) splits the body-frame delta `neckShare`/rest across Neck and Head, reading the replicated pair on remotes.
7. **Death.** `HealthComponent.OnDeath` → `PlayerController.OnDeath` sets `isDead`, `ApplyDeathFreeze` (input off, movement off, look off, cursor released) and raises `OnPlayerDeath`; `PlayerRagdoll` goes limp off the *health* event on every machine. Respawn: click → `PlayerRespawn.Request` → server `TryGetRespawnPosition` → `NetworkedTeleport.Move` **then** `health.ResetToFull()` → `OnRevive` → controls back.

## Multiplayer

- `NetworkTransform` is **owner-authoritative** (`AuthorityMode: 1`); the server cannot move a remote player's body — use [`NetworkedTeleport`](Assets/Game/Scripts/Core/Multiplayer/Authority/NetworkedTeleport.cs)/`SaveTeleport`, or the owner's next state update overwrites it.
- Replicated: transform (owner), animator via `ClientNetworkAnimator` (which carries `IsCrouching`, `HoldStyle`, `IsAiming`), `PlayerViewNetwork` pitch/torch/aiming (owner-write `NetworkVariable`), `PlayerIdentity` name/suit (owner-write) and team (server-write), health via `NetworkedHealthComponent` (server), inventory/backpack via their `*Network` components.
- Remote instances: camera GameObject and HUD inactive, `PlayerInputManager`/`PlayerMovement`/`PlayerLook`/`DamageFeedback` disabled, rigidbody kinematic. `PlayerStance` and `PlayerAimRig` stay on.
- The flashlight is authored under the camera, so on remotes `PlayerViewNetwork` **reparents it onto `AimPivot`** before applying its lit state.
- Damage from the owner's own fall goes through [`NetDamage.Apply`](Assets/Game/Scripts/Gameplay/Health/NetDamage.cs) so the server owns the result.

## Persistence

Player state is keyed by profile, not by scene: [`PlayerSaveSync`](Assets/Game/Scripts/Core/Persistence/Runtime/PlayerSaveSync.cs) (networked) or [`PlayerSaveBinder`](Assets/Game/Scripts/Core/Persistence/Runtime/PlayerSaveBinder.cs) (editor/offline) binds the body to a `PlayerRecord` (position, rotation, display name, state bag). Savers on the root: `HealthSaveable`, `PlayerInventorySaveable`, `BackpackSaveable`, [`PlayerLookSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/PlayerLookSaveable.cs) (`"look"` — pitch only; yaw rides the rotation), [`SuitColorSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/SuitColorSaveable.cs) (`"suit"`), `FlashlightSaveable`, `EffectsSaveable`, `InteriorVisitSaveable`, `PortalPairSaveable`. See [Persistence.md](Persistence.md).

## Gotchas

- **A kinematic rigidbody swallows every velocity write silently.** `EnsureMovableBody` runs first in `FixedUpdate` and warns once; it deliberately leaves parented (mounted) and non-owned bodies alone. A quit-time autosave that captured `isKinematic: true` is the classic "cannot move after loading".
- **`transform.position` does not move this body.** It is restored by the interpolating rigidbody within the frame. Always `SaveTeleport.Move` / `NetworkedTeleport.Move`.
- **Anything that pre-adds horizontal velocity is deleted.** `FixedUpdate` *assigns* x/z. Latch and drain at execution order ≥ 200 (`FlungBody`), and call `CarryMomentum()` or air control confiscates the fling in ~0.2 s.
- **`IsGrounded` stays true for ~0.6 m of clearance** (sphere cast over half-height + `groundCheckDistance`), which is why `ShouldEndCarry` also requires "not rising".
- **Jump/dash arrive as input *events*.** Disabling `PlayerMovement` does not stop them; the freeze must also disable `PlayerInputManager`.
- **Worn gear is hidden from its wearer's own camera through `PlayerLook.SetWornHidden`, not by posing it clear of the eye.** The serialized `firstPersonHidden` array is `Renderer[]` and covers the helmet and scarf, which a prefab can name; anything instantiated at runtime registers through `SetWornHidden` instead, which *replaces* the register and hands the outgoing renderers back `ShadowCastingMode.On`. Forget that hand-back and a pack set down on the sand stays `ShadowsOnly` for the rest of the session — a shadow with nothing casting it, and a clean console. Both sets are gated by the same `SetFirstPersonHidden` flag, so a ragdolled player sees their own body *and* their pack. See [Backpack](Backpack.md).
- **`PlayerLook` re-locks the cursor every `LateUpdate`.** UI that needs a cursor must go through `GameplayMenuScope`/`EnterCutsceneMode`, or disable the component.
- **Anything parented under the camera is invisible to other players** — the whole camera GameObject is inactive on remotes. Hang it on `PlayerViewNetwork.AimPivot` instead.
- **Never use `AimPivot` as the aim of a shot.** It is smoothed/replicated; a shot's direction must travel in its own use message.
- **Editing `.inputactions` does nothing.** `InputControls.cs` embeds its own copy of the JSON; new bindings are either code-built in `PlayerInputManager.EnsureInputs` or need the generated file regenerating.
- **Input callbacks are bound once in `BindActions`, never in `OnEnable`** — they are lambdas nothing can unsubscribe, and death/respawn toggles this component.
- **`PlayerStance`/`PlayerAimRig` reset themselves in `OnDisable`** — a component switched off mid-crouch/mid-aim would otherwise leave a short capsule or a weighted IK layer with nothing running to clear it.
- **Missing `Upper Body` animator layer = a silently armless rig.** `PlayerAimRig.Awake` logs an error pointing at `Tools/SpaceGame/Player/Build Upper Body Layer`.
- **The head is deliberately OUT of the `Upper Body` mask**, so there is no masked layer to hang a head goal on and `OnAnimatorIK` only arrives for layers whose *IK Pass* tick is on — a flag invisible from code. `PlayerHeadLook` therefore lays a world-space rotation on the bones in `LateUpdate` instead, at order **950**: after `MountedRiderPose` (900), which writes the rider's spine and chest, because the neck and head hang off those and a head posed first is dragged off its aim by its own parent. Nothing restores the bones — the Animator rewrites them from the clip every frame, which is also why a **disabled** Animator disables the head look: `RagdollRig` switches it off and hands every bone to physics, and a rotation written on top of that is a second driver fighting a joint, not a pose.
- **Head yaw is the mode's business, pitch is not.** On foot `PlayerLook` spends yaw turning the *Rigidbody*, so `HeadAimMode.Free` answers zero head yaw; asking the neck for it as well applies the same look twice and the character reads as permanently glancing over their own shoulder. Only a body that cannot turn (`Seated`, set by `ArrivalCameraRig`) gets head yaw — and the mode must be put **back**, or the neck keeps the angle the seat ended on.
- **Neck clamps are not camera clamps.** `ArrivalCameraRig` used to allow ±110° of yaw because nothing followed the camera; the head now does, and the limits (`yawClamp` 80, `lookDownClamp` 60, `lookUpClamp` 70) live on `PlayerHeadLook` as the single source for both.
- **The seated camera is posed *from* the head's rotation, not parented to the head bone** — parenting would feed every wobble the seated clip puts through the neck straight into a first-person camera. One angle pair, used twice: `AddLook` then `LookRotation`.
- **Suit materials are matched by NAME** (`Material.043`, `.049`…). A Blender re-export that renames them stops recolouring; `PaletteRecolor.Scan` logs an error and EditMode tests pin the names. `MaterialPropertyBlock` does *not* gamma-convert — `PaletteRecolor` does it on upload — and constructing one in a static field initializer throws at import time.
- **`EnablePlayer` re-reads `playerHealth.Alive`** because a save-restored death is announced before anything subscribed.

## Extending

1. **Decide who owns it.** If other players must see it, it cannot live on the camera or in a component `DisablePlayer` switches off — put it in a component that runs everywhere (`PlayerStance`/`PlayerAimRig` pattern) and gate the *decision* with `Network.Owns(this)`.
2. **Read input through `PlayerInputManager`** — add the event/latch there (code-built `InputAction` if the generated asset lacks it), never bind devices in the feature component.
3. **State vs. event.** A late joiner must see current state → owner-write `NetworkVariable` on `PlayerViewNetwork`/`PlayerIdentity`. A one-off → `NetMsg` through the player's `NetRelay`, and act only where `Network.Owns`/`Network.Simulates` says so.
4. **Animation goes through the Animator**, so `ClientNetworkAnimator` replicates it for free; read the parameter back on remotes rather than adding a second synced value.
5. **Touching movement?** Write only x/z from a fixed step, respect `tethered`/`carryingMomentum`/`bouncing`, and never brake a fling.
6. **Freeze correctly**: a new control owner must consult `PlayerController.IsDead` before restoring captured enabled-flags, or it revives a corpse.
7. **Persist it**: add an `ISaveable` next to the others on the prefab root, keep the key short, return `null` for defaults, and verify the key really appears in the save JSON after a reload.
