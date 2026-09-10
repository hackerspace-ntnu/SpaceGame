---
system: Diagnostics
layer: core
summary: One fault barrier, per-site quarantine, and outcome guards that give a stuck session back
paths:
  - Assets/Game/Scripts/Core/Diagnostics/
  - Assets/Game/Scripts/Core/DiagnosticsBridge/
  - Assets/Game/Scripts/Core/Safety/
symptoms:
  - "a feature stopped working mid-session and the chat says it was switched off"
  - "the game is frozen behind a free cursor with no menu on screen"
  - "I cannot move but the camera still works and nothing is on screen"
  - "the screen is black and the game seems to have crashed but the audio is still running"
  - "I am stuck on a mount that is not there any more"
  - "[InputRestoreGuard] ... no input for 6.1s with no menu, cutscene, death or mount to explain it"
  - "the controls came back in the middle of being frozen, foamed or swallowed"
  - "one broken creature behaviour stopped the creature moving at all"
  - "a coroutine threw once and that feature never worked again for the rest of the session"
  - "[Fault] something threw (x5) — QUARANTINED, this feature is now off"
  - "how do I get a bug report out of a playtest"
reads_with: [Multiplayer, AgentSystem, UI, Testing]
updated: 2026-09-09
---

# Diagnostics

One broken feature stops; the rest of the session keeps playing. Two mechanisms and they are not interchangeable: a **barrier** ([`Fault`](Assets/Game/Scripts/Core/Diagnostics/Fault.cs)) at the seams where one caller invokes N independent plug-ins, and a **guard** ([`ISessionGuard`](Assets/Game/Scripts/Core/Safety/ISessionGuard.cs)) that measures a broken *outcome* and hands the session back.

**Scope:** [Core/Diagnostics/](Assets/Game/Scripts/Core/Diagnostics/) — assembly `SpaceGame.Diagnostics`, `references: []`, `autoReferenced: true`; [Core/DiagnosticsBridge/](Assets/Game/Scripts/Core/DiagnosticsBridge/) and [Core/Safety/](Assets/Game/Scripts/Core/Safety/) — both `Assembly-CSharp`.
**Related:** [Multiplayer.md](Multiplayer.md) · [Testing.md](Testing.md) · [AgentSystem.md](AgentSystem.md) · [UI.md](UI.md) · [WorldStreaming.md](WorldStreaming.md) (`UnderTerrainGuard`, the doctrine every guard here copies)

## Model

Four failure classes. What each costs the player is out of all proportion to what threw, and none of them reaches the player as anything but the game misbehaving:

| Failure | What the engine does | What the player gets |
| --- | --- | --- |
| A throw in a fan-out loop | Skips the rest of *that method* — every plug-in below the broken one starves | A creature that stops moving entirely because one behaviour is broken |
| A throw inside a coroutine | The routine dies where it stands, never resumes, never runs its own tail | Cursor, camera, input or menu scope taken forever; only quitting ends it |
| A leaked lock | Nothing at all — the claim is simply never released | Frozen game behind a free cursor, with no screen left to close |
| Divergence | Nothing at all — one machine applied half a change | Two sessions that disagree permanently, which no guard can repair |

Two answers, and putting either in the other's place is a bug:

- **Barriers at fan-out seams.** `Fault.Run(owner, site, body)` runs one plug-in and returns false if the owner is gone, the site is quarantined, or the body threw — the caller treats all three the same and carries on with the next plug-in. `Fault.Coroutine(owner, site, body, onFail)` wraps a routine so a throw *ends* it and runs the teardown it would have run.
- **Guards on outcomes.** A guard never enumerates causes; it measures a state that cannot be legitimate ("nobody holds input and it is off", "zero enabled cameras") and repairs it after a bounded wait. Same bargain as `UnderTerrainGuard`: the value is catching the cause nobody has hit yet.
- **Loud, bounded, and ignorant of the game.** Every fault logs an error and lands in the ledger; a site quarantines after `MaxFaultsPerWindow` (5) faults inside `WindowSeconds` (10), once, so a per-frame throw costs one line and not sixty a second. `SpaceGame.Diagnostics` knows nothing about chat, the HUD or the player — it raises events, and `Assembly-CSharp` decides what a person is told.
- **Never around a single decision that must not half-happen.** Damage, ownership transfer and spawning abort instead. See `Fault.cs`'s header and [INVARIANTS.md](docs/AI/INVARIANTS.md) → *Degrade at the seams, abort at the decisions*.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `Fault` | [Diagnostics/Fault.cs](Assets/Game/Scripts/Core/Diagnostics/Fault.cs) | `Run`, `Coroutine`, `IsQuarantined`, `Raised`/`Quarantined` events, `ResetForPlaySession`. Key is `instanceID:site` |
| `FaultBudget` | [Diagnostics/FaultBudget.cs](Assets/Game/Scripts/Core/Diagnostics/FaultBudget.cs) | Pure arithmetic, caller passes the clock. `Record` returns true on the one call that trips |
| `FaultRecord` | [Diagnostics/FaultRecord.cs](Assets/Game/Scripts/Core/Diagnostics/FaultRecord.cs) | Immutable struct of strings. Holds **no** Unity references — it outlives what it describes |
| `FaultLedger` | [Diagnostics/FaultLedger.cs](Assets/Game/Scripts/Core/Diagnostics/FaultLedger.cs) | Static, `Capacity` 64 oldest-dropped, plus `TotalFaults` which counts the dropped ones |
| `IQuarantinable` | [Diagnostics/IQuarantinable.cs](Assets/Game/Scripts/Core/Diagnostics/IQuarantinable.cs) | Opt out of `enabled = false`: shed one job, keep the component running |
| `FaultChatBridge` | [DiagnosticsBridge/FaultChatBridge.cs](Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs) | Announces **quarantines only** in chat; registers `/faults` (alias `/errors`) and `/faults dump` |
| `FaultLogSink` | [DiagnosticsBridge/FaultLogSink.cs](Assets/Game/Scripts/Core/DiagnosticsBridge/FaultLogSink.cs) | `Application.logMessageReceived` → ledger for Error/Exception/Assert raised *outside* any barrier |
| `FaultReport` | [DiagnosticsBridge/FaultReport.cs](Assets/Game/Scripts/Core/DiagnosticsBridge/FaultReport.cs) | Writes `faults-<stamp>.txt` to `persistentDataPath`. Never leaves the machine |
| `ISessionGuard` | [Safety/ISessionGuard.cs](Assets/Game/Scripts/Core/Safety/ISessionGuard.cs) | `Name` + `Check(interval)`. The interval is passed in, so a test marches a guard with no frames |
| `SessionGuardRunner` | [Safety/SessionGuardRunner.cs](Assets/Game/Scripts/Core/Safety/SessionGuardRunner.cs) | Self-bootstrapping `DontDestroyOnLoad` singleton; sweeps every `CheckIntervalSeconds` (0.5) of **unscaled** time, each guard inside `Fault.Run` |

Four rule/guard pairs — the pure decision is separate from the component that reads the world, exactly as `UnderTerrainRule` is from `UnderTerrainGuard`:

| Rule (pure) | Guard (reads + repairs) | Fires when | Repair |
| --- | --- | --- | --- |
| [StuckScopeRule](Assets/Game/Scripts/Core/Safety/Rules/StuckScopeRule.cs) | [StuckScopeGuard](Assets/Game/Scripts/Core/Safety/Guards/StuckScopeGuard.cs), `GraceSeconds` 2 | A `GameplayMenuScope` owner is destroyed or disabled and still holding | `GameplayMenuScope.Exit(owner)` |
| [InputRestoreRule](Assets/Game/Scripts/Core/Safety/Rules/InputRestoreRule.cs) | [InputRestoreGuard](Assets/Game/Scripts/Core/Safety/Guards/InputRestoreGuard.cs), `TimeoutSeconds` 5 | `player.Input` off with no menu, cutscene, death, mount or **hold on the body** (`PlayerRagdoll.IsHeldOrDown`) to explain it | `ExitCutsceneMode()`, else `Input.enabled = true` |
| [MountRecoveryRule](Assets/Game/Scripts/Core/Safety/Rules/MountRecoveryRule.cs) | [MountGuard](Assets/Game/Scripts/Core/Safety/Guards/MountGuard.cs), `TimeoutSeconds` 3 | `MountModule.LocalRiderMount` is destroyed, or no longer claims the rider | `mount.Dismount()` if alive, then `ClearLocalRiderMount()` |
| [ViewRecoveryRule](Assets/Game/Scripts/Core/Safety/Rules/ViewRecoveryRule.cs) | [ViewGuard](Assets/Game/Scripts/Core/Safety/Guards/ViewGuard.cs), `TimeoutSeconds` 3 | `Camera.allCamerasCount == 0` | Re-enable `player.PlayerCamera` |

Barrier sites, and the teardown each coroutine gives back:

| Site | Where | Teardown |
| --- | --- | --- |
| `AgentModule.Tick` · `AgentModule.Facing` | [AgentController.RunModule](Assets/Game/Scripts/agents/controller/AgentController.cs) — all four module loops | — |
| `UseChannel.Present` · `UseChannel.Effect` | [UseChannel.cs](Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs) — two sites so presentation and effect quarantine apart | — |
| `Interactable.Interact` · `Interactable.SecondaryInteract` | [Interactor.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs) — owner is the **target**, not the Interactor | — |
| `CutsceneDirector.RunCutscene` | [CutsceneDirector.cs](Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs) | `EndCutscene()` |
| `Letterbox.Bars` · `.Fade` · `.FadeOutIn` | [LetterboxOverlay.cs](Assets/Game/Scripts/Presentation/Cutscenes/UI/LetterboxOverlay.cs) | `SnapClear()` |
| `FocusCamera.FlyIn` · `.FlyOut` | [FocusCamera.cs](Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs) | `Dismiss` (not `FlyOut`, which takes a duration) |
| `FadeToBlack.Out` · `.In` | [FadeToBlackEffect.cs](Assets/Game/Scripts/Core/SceneManagement/Transitions/Effects/FadeToBlackEffect.cs) | `FinishOut()` · `AbortIn()` |
| `PackFocus.Reshoulder` | [PackFocusSession.cs](Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs) | `Exit` |
| `Arrival.FlyFormation` | [ArrivalDirector.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs) | `CompleteArrival()` |
| `Arrival.FlyDescent` · `Arrival.EmptySeats` · `VehicleStation.AskForState` · `NetLatch.Ask` | [ArrivalDirector.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs), [VehicleStation.cs](Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs), [NetLatch.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs) | **none, deliberately** — see Gotchas |
| `Guard.<Name>` | [SessionGuardRunner.cs](Assets/Game/Scripts/Core/Safety/SessionGuardRunner.cs) | — |
| `Autotest.Inject` | [AutotestRunner.Faults.cs](Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Faults.cs) — three injected throws, checked from both logs | — |

## Flows

**A throw becomes a chat line.** `Fault.Run` catches → `FaultBudget.Record(key, realtimeSinceStartup)` → a `FaultRecord` into `FaultLedger` → `Debug.LogError("[Fault] <owner> · <site> threw (xN)…")` → `Raised` for every fault. On the call that trips: `IQuarantinable.OnQuarantined()` if implemented, otherwise `behaviour.enabled = false`, then `Quarantined` → `FaultChatBridge.Announce` writes one `ChatLog` system line. Every later call at that site returns false without entering the body. Errors thrown *outside* a barrier reach the same ledger through `FaultLogSink`, tagged `host:<id>` / `client:<id>` / `offline`. `/faults` prints the last 10; `/faults dump` writes the file.

**A coroutine dies and gives back what it took.** `Fault.Coroutine` drives the inner routine with `MoveNext` inside the `try` and `yield return current` outside it (C# forbids yielding from a `try` with a `catch`), so yielded values reach Unity unchanged. On a throw it reports, runs `onFail` behind its own barrier at site `<site>.teardown`, and ends. That teardown is the routine's *own* ending, extracted so the two cannot disagree — `EndCutscene`, `CompleteArrival`, `FinishOut` were pulled out of routine tails for exactly this.

**A leaked scope is released.** `SessionGuardRunner` sweeps at 0.5 s of unscaled time (the scope stops the clock solo, so scaled time would wait forever for the failure it exists to end) → `StuckScopeGuard` walks `GameplayMenuScope.Owners`, ages each abandoned owner by the interval, collects the ones past 2 s into a second list — `Exit` mutates the set being walked — logs an **error** naming the type, and releases. Input comes back the same tick the scope empties; if it does not, `InputRestoreGuard` takes it 5 s later.

## Multiplayer

- **Barriers run on every machine**, because the code they wrap does. Quarantine is per `(instance id, site)` on *this* machine only: a client whose copy of a module is broken sheds it while the host keeps ticking its own. Nothing about faults is replicated, and nothing should be — the ledger is local evidence, not shared state.
- **Guards are local-machine repairs.** All four read this machine's own player, cameras, scope and rider, and `MountGuard`'s recovery calls the ordinary `Dismount()`, which takes the usual authority path. Same reasoning as `UnderTerrainGuard` running on the owner rather than the server: the broken outcome is only visible where it happened.
- **Presentation degrades, decisions abort.** In [UseChannel.cs](Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs) `OnRequestUse` (which writes the aim into the `NetArg` every machine will act on) is deliberately *not* barriered, and `NetToServer` / `NetToOthers` are sent regardless of whether the local effect threw — a peer that never hears the message diverges permanently, which is worse than a missing muzzle flash.
- The two-process autotest proves the contract across machines: `HOST_FAULTS_CONTAINED=3`, `HOST_FAULTS_LEDGERED`, `HOST_ALIVE_AFTER_FAULTS`, with the pre-existing client assertions still holding. See [Testing.md](Testing.md).

## Persistence

N/A — the ledger is deliberately per-session and is never saved. A fault from three worlds ago is noise in a bug report, and a save file is not a log. `FaultLedger.Clear` and `Fault.ResetForPlaySession` run from `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` because statics survive play-mode exit here; `/faults dump` is the only thing that reaches the disk, and only because a player asked.

## Gotchas

- **A guard's list of legitimate holders is a liability, not a decoration.** `InputRestoreRule`
  knew about menus, cutscenes, death and mounts, and not about a body being *held down* — so
  `Frozen` (10 s), `Foamed` (10 s) and `Swallowed` (~6 s) each had the controls handed back
  mid-effect, and the player then walked around frozen solid or invisible inside a singularity. The
  guard logged its error correctly every time; nobody read it until an artifact made it obvious.
  **Adding a condition that takes input for longer than `TimeoutSeconds` means adding it here.**
- **`SpaceGame.Diagnostics` has an empty `references` list on purpose and must never gain one.** An asmdef cannot reference `Assembly-CSharp`, and 19 module assemblies (Jetpack, Locomotion, Persistence, …) need the barrier — the empty list plus `autoReferenced: true` is the only arrangement that reaches all of them. Adding a reference to reach `ChatLog` or `PlayerController` from here would compile and then quietly make the barrier unusable from every module that is not `Assembly-CSharp`. Raise an event and subscribe from the bridge instead.
- **A barrier around a state change that must not half-happen is a bug, not a safety net.** Damage, ownership transfer, spawning, save writes: these abort. A caught half-application diverges the session permanently and no guard can measure its way out of that. Barriers belong only where one caller invokes N independent plug-ins and the others are entitled to run.
- **`InputRestoreRule` must not read `PlayerController.InCutsceneMode`.** That flag is the *symptom* — a menu, a cutscene and a leaked scope all express themselves through it — so a guard that consulted it would refuse to fire in exactly the case it exists for. Ask the five legitimate holders (menu scope, `CutsceneDirector.IsPlaying`, `IsDead`, `LocalRiderMount`, `PlayerRagdoll.IsHeldOrDown`) instead — each is a claim held by a named owner rather than a symptom, which is what makes it safe to ask.
- **A guard that recovers without logging an error hides the real bug.** Every repair here is `Debug.LogError` and says *"…and that is the real bug"*, because the guard firing means something else failed. Downgrade one to a warning and the underlying fault ships, silently repaired forever.
- **Every guard timeout must outlast one `SessionGuardRunner` sweep.** Guards are checked every 0.5 s of unscaled time and age themselves by the interval they are handed, so a timeout at or under the sweep fires on its first sighting — closing a menu mid-animation, or dismounting a rider during a legitimate frame of handover. The shipped floor is `StuckScopeGuard`'s 2 s.
- **A cutscene that dies leaving `IsPlaying` true is stuck forever, and it also disables `InputRestoreGuard`.** `RunCutscene` is guarded with `EndCutscene()`, extracted from the routine's tail with its two locals (`playing`, `lockedPlayer`) hoisted to fields so it is callable from outside the routine. Without it: every later `Play` is refused, the bars stay across the screen, the player stays in cutscene mode — and because the guard asks `CutsceneDirector.IsPlaying` before handing input back, the one thing that could rescue the session politely declines to. `EndCutscene` is idempotent and safe on the abort path, where there is nothing to hand back but the flag.
- **`FadeToBlackEffect` hangs the whole scene transition on a black screen with a clean console.** `AwaitOutPhase` and `AwaitCompletion` spin on `outDone` / `inDone`; a routine that dies before setting them leaves the transition waiting on a coroutine that is never coming back and the destination never loads. Hence `FinishOut()` (new, extracted) and `AbortIn()` (pre-existing, the spacebar-skip ending) as teardowns. Any new phase flag in this file needs the same treatment.
- **Some routines get no teardown on purpose, and adding one would break them.** `Arrival.FlyDescent` must not run `AbandonDescent`: that decrements the counter the landing watchdog waits on, so the watchdog would be satisfied, `RecoverStalledDescents()` would never run, and the hull would stand on its nose forever. `Arrival.EmptySeats` is itself the backstop for a crew that never stood up. `VehicleStation.AskForState` and `NetLatch.Ask` are late joiners' *questions* — they claim nothing, and a dead ask reads as the state the object already had. **Known gap:** if `Arrival.FlyFormation` dies before its own landing watchdog, `CompleteArrival` frees the crew but nothing levels the hull.
- **Quarantine's default is blunt: the whole `Behaviour` is switched off.** For `UseChannel.Effect` that is the `UsableItem` component, for `Interactable.Interact` the door — which is the point, the broken thing stops rather than the player's ability to use items or interact at all. A component that owns several jobs should implement `IQuarantinable` and shed one; an empty `OnQuarantined()` is worse than not implementing it, because the component is then *not* disabled and keeps faulting.
- **`AgentController.RunModule` returns null for a module that is not a `Component`**, which the arbitration reads as "I pass". Every module today derives from `BehaviourModuleBase : MonoBehaviour`, but a plain-object module would silently never tick and never log.
- **`Fault.Run` returning false is not always a fault.** It also means the owner is destroyed (a teardown, deliberately not ledgered) or the site is already quarantined. Never retry on false; carry on with the next thing.
- **The barrier's log line is `[Fault] …`, and `FaultLogSink` skips exactly that prefix** so guarded faults are not counted twice. A new log path that reformats those lines would double every entry in the ledger.

## Extending

**Add a barrier.** Find the *fan-out* point — one caller, N independent plug-ins — and wrap the call, not the loop. One site name per independently-failing job (`UseChannel.Present` and `.Effect` are two for a reason: one shared site quarantines both together). Site strings are stable literals, never interpolated with an id; the id is already in the key. If the call is a single authoritative decision, do not barrier it at all.

**Guard a coroutine.** `owner.StartCoroutine(Fault.Coroutine(owner, "Site.Name", Routine(), Teardown))`. The teardown is the routine's own ending, extracted so the two cannot drift — hoist the locals it needs onto the component. It must be idempotent (the normal path has usually run it already) and must release anything a caller may be spinning on. Pass no teardown only when the routine claims nothing, and say why in a comment. For a plain object that borrows a component's coroutines (`NetLatch`, `FadeHandle`), the owner is the component.

**Add a guard.**
1. Write the pure rule in [Safety/Rules/](Assets/Game/Scripts/Core/Safety/Rules/): parameters in, `bool` out, no Unity lookups, and a timeout parameter rather than a constant. Test it directly.
2. Write the reader in [Safety/Guards/](Assets/Game/Scripts/Core/Safety/Guards/): it reads the world, ages its own counter by the `interval` it is handed (never `Time.time`), applies the rule, logs an **error**, and repairs through the game's existing primitive rather than by writing the flag.
3. Register it in `SessionGuardRunner.Awake`, and keep the timeout comfortably above 0.5 s.
4. Ask what the guard needs to *see*. Exposing it is normal — `GameplayMenuScope.Owners` and `MountModule.LocalRiderMount` exist for this — but keep the accessor read-only and document the reader on it.
