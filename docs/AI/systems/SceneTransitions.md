---
system: SceneTransitions
layer: world
summary: Additive interior scenes, the door/threshold transition orchestrator, and the one instant-move teleport
paths:
  - Assets/Game/Scripts/Core/SceneManagement/
  - Assets/Game/Scripts/Core/Teleporting/
  - Assets/Game/Scripts/Core/Persistence/Runtime/SaveTeleport.cs
  - Assets/Game/Scripts/Core/Multiplayer/Authority/NetworkedTeleport.cs
symptoms:
  - "a client walks through a door and nothing happens"
  - "the player teleports and snaps straight back to where they were"
  - "a creature is teleported but its NavMeshAgent stays behind"
  - "walking through a door bounces the player straight back in"
  - "the fade to black hangs and the door stays busy"
  - "a rider is left behind, or arrives twice as far, when its mount teleports"
  - "loading a save does not put the player back inside the cave they were in"
  - "two doors log a duplicate TransitionId and one loses its effects"
reads_with: [Portals, Persistence, Cutscenes, InteractionSystem]
updated: 2026-09-01
---

# Scene Transitions, Interiors & Teleporting

Additive interior scenes, the pluggable door/threshold orchestrator that sends bodies into them, and the single instant-move API every teleport in the game goes through.
**Scope:** [`Assets/Game/Scripts/Core/SceneManagement/`](Assets/Game/Scripts/Core/SceneManagement), [`Assets/Game/Scripts/Core/Teleporting/`](Assets/Game/Scripts/Core/Teleporting), [`SaveTeleport.cs`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveTeleport.cs), [`NetworkedTeleport.cs`](Assets/Game/Scripts/Core/Multiplayer/Authority/NetworkedTeleport.cs).
**Related:** [Portals.md](Portals.md) · [Persistence.md](Persistence.md) · [Cutscenes.md](Cutscenes.md) · [InteractionSystem.md](InteractionSystem.md)

## Model

- Interiors are **additive** scenes loaded beside the streamed exterior. The exterior never unloads, so re-exit is instant and `SceneTracked` entities outside stay alive.
- Every interior transition is split along one line: **session state** (which scenes are loaded, which scene an object lives in, where a body stands) is the server's; **view state** (active scene, exterior lights/volumes off) is one player's machine only.
- A transition is three orthogonal axes around one orchestrator: *trigger* (component) → `SceneTransition` → *destination* (SO) + *effects* (SO[]). Adding a kind is one new file.
- `SaveTeleport.Move` is the **only** instant-move function in the project. It disables the `CharacterController`, `NavMeshAgent.Warp`s (checking the return value), resyncs every `Rigidbody` under the target, then raises `ITeleportAware.OnTeleported` with a `TeleportMove`.
- A `TeleportMove` carries the two poses **and** the rigid `Transfer` matrix, so listeners rebase held world-space state (footholds, path position, leap endpoints, carried riders) with one multiply.
- `NetworkedTeleport.Move` is the authority wrapper: the **owner** performs the move; the server RPCs the owner. Player transforms are owner-authoritative, so a server-side write is overwritten within a tick.

## Key types

| Type | File | Role |
|---|---|---|
| `InteriorManager` | [InteriorManager.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorManager.cs) | Server-side loader. Refcounts scenes, holds `ReturnInfo` + streamer pin per player, raises `OnInteriorLoaded` / `OnInteriorWillUnload` / `OnInteriorUnloaded`. Plain MonoBehaviour — **no RPCs of its own**. |
| `PlayerInteriorTransit` | [PlayerInteriorTransit.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/PlayerInteriorTransit.cs) | `NetworkBehaviour` on the player. Owner→server RPCs for enter/exit; server→owner `ViewChangedRpc`. |
| `PersistentSceneVisibility` | [PersistentSceneVisibility.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/PersistentSceneVisibility.cs) | Suspends/restores persistent-scene directional lights, `Volume`s and objects named `*visor*`. Per-machine. |
| `InteriorScene` | [InteriorScene.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorScene.cs) | SO: scene name + `spawnAnchorId`. `OnValidate` checks Build Settings and (if loaded) the anchor. |
| `InteriorAnchor` | [InteriorAnchor.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorAnchor.cs) | Spawn/exit marker; static `(scene.name, id)` registry, `Find` / `FindAnywhere` / `SetAnchorId`. |
| `InteriorEntrance` | [InteriorEntrance.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorEntrance.cs) | Minimal `IInteractable` door — no fade, no cutscene. Not deprecated, just simpler than the transition stack. |
| `SceneTransition` | [SceneTransition.cs](Assets/Game/Scripts/Core/SceneManagement/Transitions/Core/SceneTransition.cs) | `ITriggerable` orchestrator. `Trigger(initiator)`, busy flag, static cross-transition lockout, stable `TransitionId`. |
| `SceneTransitionViewer` | [SceneTransitionViewer.cs](Assets/Game/Scripts/Core/SceneManagement/Transitions/Core/SceneTransitionViewer.cs) | Auto-installed on every player via `PlayerIdentity.RosterChanged`. Plays effects for a remote owner; carries the out-phase ack. |
| `TransitionRunner` | [TransitionRunner.cs](Assets/Game/Scripts/Core/SceneManagement/Transitions/Core/TransitionRunner.cs) | DDOL coroutine host — the door's own GameObject is routinely unloaded mid-transition. |
| `SceneDestination` | [Destinations/](Assets/Game/Scripts/Core/SceneManagement/Transitions/Destinations) | SO base. `InteriorSceneDestination`, `ExitInteriorDestination`, `SameSceneAnchorDestination`. |
| `SceneTransitionEffect` / `EffectHandle` | [Effects/](Assets/Game/Scripts/Core/SceneManagement/Transitions/Effects) | SO base + per-run handle. `FadeToBlackEffect` (Screen), `WalkThroughCutsceneEffect` (Camera, blocks the load). |
| `SaveTeleport` | [SaveTeleport.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveTeleport.cs) | The one instant move. `Move(go, pos, rot, zeroVelocity = true)`. |
| `TeleportMove` / `ITeleportAware` | [Teleporting/](Assets/Game/Scripts/Core/Teleporting) | Own tiny asmdef (`SpaceGame.Teleporting`, zero references) so any assembly can implement it. |
| `Bootstrapper` / `SceneReference` | [Core/](Assets/Game/Scripts/Core/SceneManagement/Core) | Forces build-index 0 to load first; SO wrapper for a scene name. |

## Flows

1. **Enter.** Trigger → `SceneTransition.Trigger` → busy + coroutine on `TransitionRunner` → effects out-phase → `InteriorSceneDestination.Apply` → `InteriorManager.EnterInterior` → `PlayerInteriorTransit.RequestEnter` → server.
2. **Server enter.** Record `ReturnInfo` (position, rotation, exterior scene) + spawn an `InteriorReturnPin` registered with `WorldStreamer` → refcount++ → `LoadInteriorAdditive` → `AnnounceLoaded` (raises `OnInteriorLoaded` **before** placing the player) → `MoveGameObjectToScene` + `NetworkedTeleport.Move` to the anchor → `NotifyEntered` to the owner only.
3. **Effects.** `AudienceFor(initiator)`: offline/owner ⇒ this machine; server + remote owner ⇒ `NetMsg.SceneEffects` broadcast on the initiator's channel, filtered by ownership, acked with `NetMsg.SceneEffectsDone` (8 s cap); AI or unowned ⇒ nobody.
4. **Exit.** `ExitInteriorDestination` → `ServerExitInterior` → coroutine waits for `WorldStreamer.IsChunkLoadedAt(returnPos)` (8 s cap) → unparent + `MoveGameObjectToScene` back → teleport → `NotifyExited` → one frame → `GroundClampPlayer` (lift only, capped) → arm `postExitEntranceLockout` → refcount-- → `OnInteriorWillUnload` then `UnloadScene`.
5. **Same-scene teleport.** `SameSceneAnchorDestination` → `InteriorAnchor.FindAnywhere` → `anchor.TeleportPlayer`. No scene load.
6. **Any teleport.** `SaveTeleport.Move` reads the pre-move pose, moves, resyncs bodies, then announces a `TeleportMove` to every `ITeleportAware` under the object.

## Multiplayer

- **Server decides, owner moves.** `VolumeTrigger` fires only on `!Network.IsNetworked || Network.Server`; interactables fire for the body their machine owns. `InteriorManager.Server*` methods are reachable only via `PlayerInteriorTransit`, which guarantees the server.
- Interior loads use `NetworkManager.SceneManager.LoadScene(..., Additive)` when networked and `SceneManager.LoadSceneAsync` offline; unload mirrors that. Clients receive the scene through NGO's own scene event, so client-side scene arrival lags the server by a synchronisation round trip — every destination waits on `initiator.scene.name` with a timeout rather than assuming.
- Enter/exit RPCs are `SendTo.Server` with `InvokePermission = RpcInvokePermission.Owner` — one client cannot shove another through a door.
- `FixedString64Bytes` throws rather than truncating: scene and anchor names are pre-checked against 61 UTF-8 bytes and refused with a named error.
- `ITeleportAware` implementors: [`LeggedLocomotion.Teleport.cs`](Assets/Game/Scripts/Locomotion/Core/LeggedLocomotion.Teleport.cs), [`NavMeshAgentMotor`](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs), [`RigidbodyMotor`](Assets/Game/Scripts/agents/AI/Motors/RigidbodyMotor.cs), [`OrnithopterFlightMotor`](Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs), [`AgentRagdoll`](Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs) / [`RagdollRig`](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs), [`DuneFoilLocomotion`](Assets/Game/Scripts/Vehicles/DuneFoil/Core/DuneFoilLocomotion.cs), [`WalkerPlatformCarrier`](Assets/Game/Scripts/Vehicles/Systems/WalkerPlatformCarrier.cs), [`PortalTraveller`](Assets/Game/Scripts/Portals/PortalTraveller.cs).
- **Rider + mount as one composite.** `WalkerPlatformCarrier.OnTeleported` re-teleports last-step riders it owns by `move.Point/Rotation` with `zeroVelocity: false`, skipping any rider whose `PortalTraveller.InPortal` is true (it is traversing under its own name; carrying it too applies the transfer twice). Riders parented to a mount move with it for free — see `PortalTraveller.Carrier`.

## Persistence

- **Interior contents** hydrate/dehydrate like chunks: `SaveManager` subscribes to the three `InteriorManager` events and drives `WorldSaveStore`. `OnInteriorWillUnload` fires *before* the unload — the last moment anything in the cave is readable.
- **Which interior a player is in** is player-scoped, not world-scoped: [`InteriorVisitSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/InteriorVisitSaveable.cs), save key `"interior"` (never rename). Absent key = not in an interior.
- Restore is deferred (`IDeferredSaveable.OnLoadComplete`) and idempotent — it runs world-wide, per player binding and per late chunk hydrate. `InteriorManager.RestoreVisit` bails on clients, bails if a `ReturnInfo` already exists, and places the player at the **saved position**, not at the anchor.
- A saved entity belongs to the scene it was in: exterior chunk scenes for the world, the interior's own scene for anything inside one. Return position and return rotation are saved alongside so the exit still works after a reload.

## Gotchas

- **`InteriorManager` has no `NetworkObject`.** Declaring `[Rpc]` on it is inert — Netcode only rewrites `NetworkBehaviour`s. That bug made clients run the *server* half on themselves. Route through `PlayerInteriorTransit`.
- **Never write a remote player's transform on the server.** Player `NetworkTransform` is owner-authoritative. Always `NetworkedTeleport.Move` (which delegates to `SaveTeleport.Move`).
- **Anchors key on `scene.name`.** Two loaded scenes with the same name collide. Instanced interiors need a `Scene`-handle key first.
- **Interiors load at world origin** and overlap whatever exterior chunk sits at (0,0,0). Harmless today, not by design.
- **`player.scene != trigger.scene` is normal** — world streaming migrates players between chunk sub-scenes. Use `InteriorManager.IsInsideInterior`, never scene equality.
- **Yo-yo guards are three separate mechanisms**: `InteriorManager.postExitEntranceLockout` (per player), `SceneTransition.postTransitionLockoutSeconds` (static, cross-transition, armed *before* `Apply` and again after), and `VolumeTrigger.reentryCooldown` (per volume+player, keyed by stable identity so it survives streaming).
- **`TransitionId` is a FNV-1a hash of scene name + hierarchy path + sibling indices.** Two doors that hash equal log an error and one loses its remote effects — rename the GameObject. `string.GetHashCode` cannot be used (per-process seed).
- **Statics survive play-mode exit** when domain reload is off. `SceneTransition`, `VolumeTrigger` and friends clear theirs from `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`; new statics here must do the same.
- `busy` clears as soon as the destination lands, not after the in-phase — a hanging fade must never dead-lock a door. A 20 s self-heal is the last line of defence, not the mechanism.
- **`NavMeshAgent.Warp` fails silently** and `agent.isOnNavMesh` answers `true` exactly when it failed. Check the return value; `SaveTeleport` schedules a `DeferredNavMeshWarp` retry.
- `SaveTeleport` treats a move under 0.1 mm / 0.01° as a **resync** and raises no `ITeleportAware` — netcode does several of those a second.
- `InteriorAnchor.TeleportPlayer` predates `SaveTeleport` and does its own CC/Rigidbody dance; it does **not** raise `ITeleportAware`. Prefer `SaveTeleport.Move` for anything with world-space state.
- `Bootstrapper.AfterSceneLoad` is `async void` — it swallows exceptions.
- Live interior assets: `Interior_AlgeaCave` → `AlgeaCave.unity`, `Interior_SandstoneCave` → `SandstoneCaveInterior.unity` (in [`Assets/Game/Resources/Interiors/`](Assets/Game/Resources/Interiors)). `InteriorTestBootstrap` is off (`autoInstallEnabled = false`).
- Open: a **late joiner is not placed into an interior others are inside**; items dropped in an interior are lost when the last occupant leaves unless the object is save-wired.

## Extending

1. **New interior:** build the scene under `Assets/Game/Scenes/Interiors/`, add an `InteriorAnchor` (`anchorId = "entrance"`), bake NavMesh, **add the scene to Build Settings and enable it**, then `Create → Scene Management → Interior Scene` in `Assets/Game/Resources/Interiors/`.
2. **New door:** GameObject + `SceneTransition` + `InteractableTrigger` (E-press) and/or `VolumeTrigger` (walk-in, needs `isTrigger`); assign an `InteriorSceneDestination` and effects on distinct `TransitionChannel`s.
3. **New exit:** same, inside the interior scene, with an `ExitInteriorDestination`.
4. **New destination:** subclass `SceneDestination` in `Destinations/`; implement `IsValid()` and `Apply()`; `Apply` must yield until the initiator is actually placed **and** time out loudly rather than deadlock.
5. **New effect:** subclass `SceneTransitionEffect` in `Effects/`; pick a channel; return an `EffectHandle`; run it on a DDOL host (`LetterboxOverlay`, `TransitionRunner`) because the door's scene may unload. Override `AwaitOutPhase` only if the effect must block the load.
6. **New teleport-aware system:** implement `SpaceGame.Teleporting.ITeleportAware` and rebase every world-space field with `move.Point` / `move.Direction` / `move.Rotation`. Do not move the object again inside the handler.
7. **New way to teleport:** call `NetworkedTeleport.Move` (networked bodies) or `SaveTeleport.Move`. Never assign `transform.position` — `Physics.autoSyncTransforms` is off and the body snaps home.
