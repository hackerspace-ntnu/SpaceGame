---
system: Cutscenes
layer: presentation
summary: Coroutine cutscene components plus shared presentation helpers: letterbox, shake, cloth wind, tint.
paths:
  - Assets/Game/Scripts/Presentation/Cutscenes/
  - Assets/Game/Scripts/Presentation/Cloth/
  - Assets/Game/Scripts/Presentation/Placement/
symptoms:
  - "the camera freezes during a cutscene and look input does nothing"
  - "after a cutscene the camera stays at chest height instead of the head"
  - "camera shake does nothing anywhere in the game"
  - "the camera shake accessibility slider has no effect"
  - "a cutscene played for one player only, or moved a body the server overwrote"
  - "the playOnce cutscene replays after loading a save"
  - "the letterbox bars are stuck on screen after respawning"
  - "an impact effect spawns dozens of GameObjects and spikes the frame"
  - "the screen only goes black after the ship has finished crashing, so I watch the impact"
  - "the crash cutscene starts seconds earlier on the host than on the client"
  - "a seated crewmate sits rigidly staring ahead while their view is clearly sweeping the cabin"
  - "the arrival sits on a black screen and never plays"
  - "a rival team's ship falls past me stone cold while mine is on fire"
  - "the cutscene played but the screen was not black when the ship hit the ground"
  - "the arrival intermittently loses its blackout, look and exit hint all at once, with a clean console"
  - "after landing I could not look around from the seat until I stood up"
  - "everything is blurry for a few seconds after the crash landing"
  - "no hint ever appears telling me how to get out of the crashed ship"
  - "some players could look around during the intro descent and others could not, or kept their HUD through it"
  - "a cutscene locked or blacked out the wrong player in a multiplayer session"
reads_with: [SceneTransitions, PlayerShip, CutsceneExamples, audio]
updated: 2026-09-02
---

# Cutscenes & Presentation

Coroutine-driven scripted camera moments (no Timeline, no Cinemachine) plus the shared presentation helpers around them: letterbox/fade, shake maths, cloth wind, placement tint, manual-emit particles.

**Scope:** `Assets/Game/Scripts/Presentation/Cutscenes/{Core,Actions,UI}`, `Presentation/Cloth`, `Presentation/Placement`, `Assets/ThirdParty/FirstGearGames/SmoothCameraShaker` (integration only).
**Related:** [SceneTransitions.md](SceneTransitions.md) · [Interiors.md](Interiors.md) · [CutsceneExamples.md](CutsceneExamples.md) · [SceneTransitionAndCutscene.puml](SceneTransitionAndCutscene.puml)

## Model

- A cutscene is a **`MonoBehaviour` component**, not an asset or a Timeline. Subclass [`Cutscene`](Assets/Game/Scripts/Presentation/Cutscenes/Core/Cutscene.cs), implement `IEnumerator Play(CutsceneContext)`. Authoring = drop the component on a GameObject and serialize its targets/durations.
- `CutsceneContext` carries `Player` (`PlayerController`), `PlayerCamera`, and `Subject` — the GameObject the cutscene is *about* (defaults to the player's GameObject; can be an AI agent).
- [`CutsceneDirector`](Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs) is a scene singleton owning the lock/restore envelope. **One at a time**; a concurrent `Play` warns and returns `false`.
- Four separated concerns: **trigger** (how it fires) → [`CutsceneAction`](Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneAction.cs) (`ITriggerable`) → the **`Cutscene`** (what plays) → `onCutsceneEnded` UnityEvent (what happens after). None knows about the others.
- Everything is **local, per machine**. Nothing on the wire.
- Actions advance on `Time.unscaledDeltaTime` so a zero-timescale frame still animates. `ArrivalCutscene` is the exception (`Time.time`, `WaitForSeconds`).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `Cutscene` / `CutsceneContext` | [Core/Cutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/Cutscene.cs) | Abstract base + the (Player, Camera, Subject) triple |
| `CutsceneDirector` | [Core/CutsceneDirector.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs) | Singleton; `Play(cutscene, subject)`, `IsPlaying`, `OnCutsceneStarted/Ended` |
| `CutsceneRunner` | [Core/CutsceneRunner.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneRunner.cs) | Static `PlayAndAwait(cutscene, initiator, started)` — yields until the Director's end event |
| `CutsceneAction` | [Core/CutsceneAction.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneAction.cs) | `ITriggerable` + `IPersistentEntity`; `playOnce`, `onCutsceneEnded(GameObject)` |
| `ShakeMath` | [Core/ShakeMath.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/ShakeMath.cs) | Pure Perlin displacement, capped, scaled by `GameSettings.CameraShakeIntensity` |
| `LetterboxOverlay` | [UI/LetterboxOverlay.cs](Assets/Game/Scripts/Presentation/Cutscenes/UI/LetterboxOverlay.cs) | Self-building `DontDestroyOnLoad` canvas: `ShowBars/HideBarsAsync`, `FadeTo/FromBlackAsync`, `FadeOutInAround`, `SnapClear`, `FadeAlpha`. Unscaled time. Supersession is by GENERATION, never `StopCoroutine` — see Gotchas |
| `PlayerController.EnterCutsceneMode(bool hideHud)` | [Player/Core/PlayerController.cs](Assets/Game/Scripts/Characters/Player/Core/PlayerController.cs) | The lock primitive: saves+disables `Input`, `PlayerMovement`, `PlayerLook`, `DamageFeedback`, optionally the HUD |
| `WalkThroughCutsceneEffect` | [Transitions/Effects/…](Assets/Game/Scripts/Core/SceneManagement/Transitions/Effects/WalkThroughCutsceneEffect.cs) | `SceneTransitionEffect` (channel `Camera`) holding the teleport until the cutscene ends |
| `ClothWindDriver` | [Cloth/ClothWindDriver.cs](Assets/Game/Scripts/Presentation/Cloth/ClothWindDriver.cs) | Pushes `_WindDirection`/`_WindStrength` into every `SpaceGame/ClothWind` renderer via one MaterialPropertyBlock |
| `PlacementTint` | [Placement/PlacementTint.cs](Assets/Game/Scripts/Presentation/Placement/PlacementTint.cs) | Shared `Legal`/`Refused` colours + `BuildMaterial()` (`SpaceGame/PackDragTint`; caller destroys) |

## Actions

| Action | File | What it does |
| --- | --- | --- |
| `LookAtCutscene` | [Actions/LookAtCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/LookAtCutscene.cs) | Slerp camera rotation to `target`, hold, slerp back. Rotation only |
| `CameraShakeCutscene` | [Actions/CameraShakeCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/CameraShakeCutscene.cs) | Perlin pos+rot jitter, quadratic decay, exact local-pose restore. Own noise, **not** `ShakeMath` |
| `WalkThroughDoorCutscene` | [Actions/WalkThroughDoorCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/WalkThroughDoorCutscene.cs) | Eased FP camera glide to `throughPoint`'s pose. Moves the camera, not the body |
| `ThirdPersonWalkThroughCutscene` | [Actions/ThirdPersonWalkThroughCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/ThirdPersonWalkThroughCutscene.cs) | Disables player cam + `AudioListener`, spawns `CutsceneTempCamera`, dollies `startOffset`→`endOffset` while lerping the player Rigidbody to `throughPoint`; `finally` restores |
| `ArrivalCutscene` | [Actions/ArrivalCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs) | Crash landing: black on seating → **hold until `Launch()`** → wake fade → `shakeOverDescent` curve → fade to black **finishing at first contact** (and snapped there regardless — see Gotchas) → black through `settleWindow + blackout` → starts `ArrivalConcussion` → fade in → raises static `LocalPlayerRecovered` (what the seat-exit hint times itself from). Adds/destroys `ArrivalCameraRig`. Mutates nothing |
| `ArrivalConcussion` | [Actions/ArrivalConcussion.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalConcussion.cs) | The coming-to blur: a runtime global `Volume` with one wide-open Bokeh `DepthOfField` (focus 0.1 m, focal 300, aperture 1 — the `PackFocusCamera` pattern, maxed), weight 1→0 over ~9 s with a dazed hold at full. Started under the final blackout so the first visible frame is already soft; lives OUTSIDE the cutscene because the player must have their look back while it clears. Destroys its volume and assetless profile with itself |
| `ArrivalBeats` | [Core/ArrivalBeats.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/ArrivalBeats.cs) | Pure timing: `Contact`, `FadeStart`, `FadeDuration`, `BlackHold`, `DescentProgress(elapsed)`. `FadeStart + FadeDuration == Contact` is the whole contract |
| `ArrivalCameraRig` | [Actions/ArrivalCameraRig.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCameraRig.cs) | Not a `Cutscene` — a `[DefaultExecutionOrder(200)]` component reading the raw Look action, handing it to [`PlayerHeadLook`](Assets/Game/Scripts/Characters/Player/Combat/PlayerHeadLook.cs), and posing the camera from that same rotation plus `ShakeMath` shake, in one `LateUpdate` |

## Flows

**Start** — 1. A trigger (`InteractableTrigger`, `VolumeTrigger`, or code) calls `CutsceneAction.Trigger(initiator)`. 2. `CanTrigger` rejects on null cutscene / busy / `playOnce` already fired / no Director / Director busy. 3. `CutsceneRunner.PlayAndAwait` subscribes to `OnCutsceneEnded`, calls `Director.Play(cutscene, initiator)`. 4. The Director resolves a `PlayerController` from the subject (`CutsceneDirector.ResolvePlayer`: `GetComponentInParent`, else `GameplayMenuScope.FindLocalPlayer()` — never a scene search), builds the context, calls `EnterCutsceneMode()`, shows bars (0.4 s), raises `OnCutsceneStarted`.

**Step** — 5. The Director hand-iterates `inner.MoveNext()` inside `try/catch` (you cannot `yield` inside a `try`-with-`catch`), logs and breaks on exception, and yields whatever the action yielded.

**End** — 6. `ExitCutsceneMode()` restores the saved enable flags — or re-asserts the death freeze if the player died mid-cutscene. 7. Bars hide, `IsPlaying = false`, `OnCutsceneEnded(cutscene)` fires. 8. `CutsceneAction` sets `fired` only if the Director *accepted*; the UnityEvent fires either way (try/catch wrapped) so a rejected play cannot strand the player.

## Multiplayer

- **Not replicated.** A cutscene plays only on the machine that triggered it, for that machine's own player. Do not mutate networked state inside `Play()` — the mutation lands on one machine.
- The arrival is the worked pattern for a cutscene that must be right everywhere: split it. [`ArrivalDirector`](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs) flies the hull **server-side**; [`SeatedRider`](Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs) holds bodies at seats on every machine — while the flight is live it stamps **every** occupant, remote crewmates included, onto its local copy of the seat, because the wire's answer (owner-authoritative interpolated `NetworkTransform`) trails a diving hull by metres; what stays on the wire for a seated body is the seat index and the head look (see [PlayerShip](PlayerShip.md) § Multiplayer); [`CabinAlert`](Assets/Game/Scripts/Vehicles/Systems/CabinAlert.cs) runs off replicated occupancy, not the cutscene, so it lights on machines merely watching a crewmate.
- **Two per-machine hooks, and they answer different questions.** `SeatedRider.LocalPlayerSeated(GameObject body)` fires where *this* machine's player sat down, carrying that body; `ArrivalDirector.PlayLocalCutscene` subscribes, calls `cutscene.Configure(descentDuration, settleHold + settleDuration)` (both halves — the hull keeps moving after first contact) and starts the cutscene **with the body as the subject**, which goes straight to black and **waits**. `SeatedRider.LocalCrewLaunched(secondsAgo)` fires from the server's `NetMsg.ArrivalLaunched` and releases it. Seating is per-machine and up to `crewGatherTimeout` apart; the launch is one server frame, so the timed beats hang off the launch and only the black hangs off seating.
- A late joiner never receives that message, so `SeatedRider` also replicates the instant (`launchedAt`, a `NetworkVariable<double>` on the server clock, `-1` = not launched) and `Attach` raises `LocalCrewLaunched` with its **age** when it seats a local player into a descent already under way. Nothing subscribes to that variable changing — event for the present, state for the late — which is what stops a machine starting the presentation twice.

## Persistence

- `CutsceneAction.playOnce` persists via [`CutsceneActionSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/CutsceneActionSaveable.cs), key `cutscene`. `CaptureState()` returns **null until it has played** (no key per unplayed cutscene); a null on restore explicitly means "not played" and clears the live flag.
- The arrival is one flag per world: [`ArrivalSaveable`](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs), key `arrival`, `arrived = HasArrived || IsRunning` (a save taken mid-descent counts as arrived). The wreck persists separately via `PlayerShip`'s own `SaveableEntity` + `TransformSaveable`.
- Both savers need a `SaveableEntity` on the same GameObject; neither is deferred.

## Gotchas

- **The Director locks the player it is TOLD about, and with no subject it asks the session — never the scene.** Every peer holds one `PlayerController` per player in the session, so `FindFirstObjectByType<PlayerController>` (what `ResolvePlayer` used to fall back to) answered with whichever body spawned first. In a six-player arrival that was a crewmate on most machines: `EnterCutsceneMode` hid *their* HUD, `ArrivalCameraRig` and `ArrivalConcussion` went onto *their* camera — which `DisablePlayer` had switched off, so the rig's `OnEnable` never ran — and the local player kept their HUD and got no shake, no blur and no seated look, while the one machine whose own body came first got all of it. Symptom as reported: "some players could look around during the intro and others could not". `SeatedRider.LocalPlayerSeated` now carries the seated body and `PlayLocalCutscene` passes it as the subject; the fallback is `GameplayMenuScope.FindLocalPlayer()`. Guarded by [CutsceneSubjectResolutionTests](Assets/Game/Editor/Tests/CutsceneSubjectResolutionTests.cs), which always builds more than one player — a one-player fixture passes against the bug. Any new subject-less `Play` call is suspect for the same reason.
- **Vendor shake is currently dead.** `CameraShakerHandler.Shake(data)` returns `null` when no `CameraShaker` is alive, and the project's only one sits on `Assets/Game/Prefabs/Camera/3rd person.prefab`, which **nothing references** (its GUID `57b7226b…` has zero hits across scenes/prefabs). So `DamageFeedback`, `DragonBazookaArtifact`, `GravelBlastFx`, `RepulsorGauntletArtifact`, `SuckerPuncherArtifact` and `FlungBody` shake nothing today.
- **Vendor shake ignores accessibility.** `GameSettings.CameraShakeIntensity` is honoured only by `ShakeMath`; nothing calls `CameraShakerHandler.SetScale`. `ShakeMath` early-**returns** `Vector3.zero` at zero scale rather than scaling, because Perlin is 0.5 at its origin and would otherwise leave a constant off-centre offset.
- **Camera writes are offsets, never assignments.** The player camera's authored local pose is the head (~`(0, 1.45, 0.16)`), not identity. `localPosition = shake` drops the view to chest height, and "restoring" by zeroing leaves it there for the session. Capture in `OnEnable`, restore in `OnDisable`.
- **Input bypass.** A cutscene runs with `PlayerInputManager` disabled, and that component zeroes its look axis in `OnDisable` — anything reading `LookInput` gets a frozen camera. `ArrivalCameraRig` reads `InputSystem.actions.FindAction("Look")` directly, and re-disables it only if it enabled it. Do **not** leave `PlayerInputManager` on instead: jump/dash arrive as *events* whose handlers fire regardless of `PlayerMovement.enabled`. `MountModule.Camera.cs` does the same for the same reason. Also bypass `PlayerLook` — it spends yaw rotating the player Rigidbody.
- **One rig, one `LateUpdate`.** Look and shake must write the transform from a single component; two components racing on it lose one contribution on Unity-undefined frames. The head is a *different* transform with its own single writer (`PlayerHeadLook`, order 950) — what the two share is the **angle pair**, not the write. `ArrivalCameraRig` feeds `AddLook` and then poses the camera from `headLook.LookRotation`; a rig that integrated its own copy would need a second clamp, and the view would leave the head at exactly the extremes a player notices.
- **The fade to black is started early, not at the impact.** `LetterboxOverlay.FadeToBlackAsync` takes a *duration*, so a fade begun at first contact finishes a second into the topple — the beat the black exists to hide. `ArrivalBeats.FadeStart` is `Descent − impactFade`, and the fade is opened with the time actually **remaining** (`Contact − elapsed`) rather than its authored length, so a frame spike in the last second cannot leave the player watching the impact through a half-faded screen. `impactFade` is an **absolute** 0.6 s, not a fraction, so it does not scale when the descent is retimed — check it by hand against a new `descentDuration` (18.2 s since 2026-09-02, so 3.3 %; much past ~10 % and the fade reads as blacking out rather than as an impact). `shakeOverDescent` is the opposite: sampled by normalised time, it stretches on its own. **Black at contact is ENFORCED, not assumed**: `LetterboxOverlay` runs one fade at a time and any other system fading during the descent silently cancels the impact fade, so `Descend` snaps to black (`FadeToBlackAsync(0)`) on the contact frame whatever happened to the fade it opened — invisible when the fade ran, the whole point when it did not.
- **The camera rig can die under the cutscene, and losing it must cost only the shake.** Anything that rebuilds the player camera mid-descent destroys `ArrivalCameraRig`; dereferencing it then aborted the whole cutscene through `CutsceneDirector`'s catch — controls handed back mid-dive, no fade, no black at the impact, one exception in the console. Every touch of the rig is null-guarded and the beats play on without it.
- **Never `StopCoroutine` a routine somebody may be `yield return`ing on — the waiter freezes FOREVER, silently.** Unity neither resumes nor errors a coroutine waiting on a stopped one. `LetterboxOverlay` used to stop its running fade whenever a new fade was requested, so any two systems touching the overlay in the same window froze one of them mid-sequence with a clean console — the arrival cutscene froze exactly this way: no black at the impact, `ExitCutsceneMode` never ran (look and movement stayed locked in the chair), `LocalPlayerRecovered` never fired (no exit hint), intermittently, in both modes. The overlay now retires superseded animations by a **generation counter** (the stale routine notices and completes, resuming its waiter), and `ArrivalCutscene` additionally waits on TIME (`WaitForSeconds`), never on the overlay's coroutines — beats are timings and time is the only thing they may depend on.
- **The rig outlives its cutscene: it IS the seated look.** `PlayerLook` spends yaw rotating the player's BODY — wrong for someone strapped into a chair — so `SeatedRider` suspends it with the movement, and `ArrivalCameraRig.ReleaseWithSeat()` keeps the rig feeding `PlayerHeadLook`'s clamped neck until `LocalPlayerReleased` destroys it. A landed rider looks around the blurred cabin through the neck, exactly as they did during the descent; standing up is what hands the body (and `PlayerLook`) back.
- **The black has to outlast the settle.** The wreck keeps moving for `settleHold + settleDuration` after contact and the settle is the only thing that levels it (see [PlayerShip](PlayerShip.md)) — so `BlackHold` is `settle + blackout`. Shortening it shows the last of the topple; shortening the *settle* to compensate leaves the wreck on its nose forever.
- **The arrival's fire is not a cutscene, and must not become one.** The atmospheric entry burn
  lives on the SHIP (`EntryBurn`, see [PlayerShip](PlayerShip.md)) rather than in
  `ArrivalCutscene`, because this cutscene runs per machine for the LOCAL player and a versus
  match launches one hull per team — driven from here, a rival team's ship would fall past you
  stone cold. Both are timed off the same replicated launch instant, so they agree without
  either knowing about the other, and `EntryBurnCurve` is authored to have the burn OUT before
  `impactFade` starts.
- **A cutscene that waits on the wire needs a bounded wait.** `ArrivalCutscene.launchWait` (30 s) exists because the failure mode of the launch gate is a player staring at black for the rest of the session. It warns and plays anyway.
- **`ThirdPersonWalkThroughCutscene` writes the player Rigidbody directly** — correct offline, fights `NetworkTransform` in a session. It needs to move to the teleport seam.
- **Manual-emit particles: one system for N impacts.** See [`GravelBlastFx`](Assets/Game/Scripts/Items/Artifacts/Gadgets/GravelBlastFx.cs) and `Manual()` in [`GravelBlasterBuilder`](Assets/Game/Editor/AssetPipeline/GravelBlasterBuilder.cs). Four things must hold at once: `main.loop = true` + `playOnAwake = true` (a stopped system never simulates handed-in particles); `emission.enabled = false` with bursts cleared (an authored burst goes off at the gun on equip); `cullingMode = AlwaysSimulate` (the emitter is in your hands, the impacts are 70 m away); `scalingMode = Local` (`ItemGrip` rescales the prefab to fit the hand). Emit by **moving the system to the hit point** then `system.Emit(emitParams, count)` — world simulation space means particles already in the air do not follow it. The alternative, thirty GameObjects per shot, spikes frames.
- **`ClothWindDriver` resolves `WindField` reflectively** (`SpaceGame.Vehicles.DuneFoil` cannot be referenced from here) and collects renderers by **shader name** using `sharedMaterials` — touching `.materials` in edit mode leaks a cloned material into the scene every run.
- **Stale claims now removed:** `persistentScene` no longer has the four showcase stations (only `CutsceneDirector`, `ArrivalDirector`, `ArrivalCutscene`). Example prefabs live in `Assets/Game/Prefabs/VisualEffects/cutsceneExamples/` as `CutsceneDoor*.prefab` / `CutsceneTriggerPad.prefab`, not the `Example_*` names in [CutsceneExamples.md](CutsceneExamples.md).
- Still true: no audio ducking during cutscenes; `LetterboxOverlay.SnapClear()` is the hard reset (use on respawn).

## Extending

**A new cutscene**
1. Add a `Cutscene` subclass; serialize every tunable (durations, targets, offsets) — no magic numbers.
2. Advance on `Time.unscaledDeltaTime`; capture any transform you write and restore it, including in a `finally` if you spawn or disable anything.
3. Put it on a GameObject with a trigger — [`InteractableTrigger`](Assets/Game/Scripts/Gameplay/Interaction/Triggers/InteractableTrigger.cs) (click) or [`VolumeTrigger`](Assets/Game/Scripts/Gameplay/Interaction/Triggers/VolumeTrigger.cs) (walk-in) — plus `CutsceneAction`, and point `CutsceneAction.cutscene` at it.
4. For a door, prefer `SceneTransition` + `WalkThroughCutsceneEffect`: it binds the `Cutscene` component found on the transition's GameObject and delays the teleport until the coroutine finishes.
5. If it uses `playOnce`, add `CutsceneActionSaveable` **and** a `SaveableEntity`, then confirm the `cutscene` key appears in the save JSON after it fires.
6. If it must agree across machines, split it as the arrival does: authority in a server component, the `Cutscene` reduced to pure presentation started off a local-only event.

**A new action type** — same as steps 1–2; there is no registry, enum, or asset to update. The only contract is `Play(CutsceneContext)` returning an `IEnumerator`. Prefer `ShakeMath.Displacement` over rolling new noise so `GameSettings.CameraShakeIntensity` keeps working.
