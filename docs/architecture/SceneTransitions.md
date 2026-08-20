# Scene Transition System

A drop-in component system for sending an initiator (player or AI agent) from one scene to another, with pluggable destinations, effects, and triggers.

## What it replaces

The legacy components `InteriorEntrance`, `InteriorExit`, and `InteriorPortal` each hard-coded a particular trigger style (interactable / interactable / interactable+volume) bound to a particular destination kind (additive interior). This system unbundles the three concerns so any combination is one drag-and-drop away.

## Architecture

Three independent extension axes around a single orchestrator:

```
SceneTransition (orchestrator, MonoBehaviour)
├── SceneDestination     (SO — where to go)
├── SceneTransitionEffect[] (SO — what plays during the load, per channel)
└── public Trigger(GameObject initiator)

Destinations/
└── InteriorSceneDestination   — additive interior load via InteriorManager

Effects/
└── FadeToBlackEffect          — uses LetterboxOverlay

Triggers/
├── InteractableTransitionTrigger  — IInteractable adapter
└── VolumeTransitionTrigger        — OnTriggerEnter adapter

TransitionRunner (DontDestroyOnLoad host for the orchestrator coroutine)
```

Adding a new destination, effect, or trigger means adding **one new file** to the matching folder. The orchestrator never grows.

## Lifecycle of a transition

1. A trigger calls `sceneTransition.Trigger(initiator)`.
2. `SceneTransition` sets a busy flag and starts its coroutine **on `TransitionRunner`** (not on itself — the host GameObject may be inside a scene that the destination is about to unload).
3. All assigned effects' `Begin()` methods run in parallel (fade out, audio muffle, ...).
4. `destination.Apply(initiator)` runs. For `InteriorSceneDestination` this delegates to `InteriorManager.EnterInterior`, which loads additively and moves the initiator. The destination yields until the initiator's scene actually changes (with a configurable timeout to fail loudly rather than deadlock).
5. Once the destination completes, each effect's `End()` is called and the orchestrator awaits each handle's `AwaitCompletion()` (fade in, etc.).
6. `busy` clears.

## Drop-and-go: a door

1. Create a `SceneDestination` asset: `Create → Scene Management → Destinations → Interior Scene`. Assign an `InteriorScene` SO.
2. Create a `SceneTransitionEffect` asset: `Create → Scene Management → Effects → Fade To Black`.
3. On the door GameObject, add `SceneTransition` and `InteractableTransitionTrigger`.
4. Drag the destination + effect SOs into the `SceneTransition`'s slots.

## Drop-and-go: a threshold (walk-through)

Same as the door but use `VolumeTransitionTrigger` instead of `InteractableTransitionTrigger`. Make sure the GameObject has a Collider with `isTrigger = true`.

By default, the volume fires for both players (tag `"Player"`) and AI (`AgentController` in parents). Disable either via the inspector.

## Drop-and-go: clickable AND walk-through

Add both trigger components on the same GameObject. Both call into the same `SceneTransition`. The orchestrator's `busy` flag handles the race.

## Drop-and-go: from script

```csharp
sceneTransition.Trigger(npc.gameObject);
```

Useful for cutscene steps and scripted patrols.

## Effect channels

Each effect declares a `TransitionChannel` (`Screen`, `Audio`, `Camera`, `Time`, `Custom`). Two effects on the same channel will fight each other (e.g., two fades). `SceneTransition.OnValidate` warns at edit time when this happens. Use `Custom` to opt out of the check.

## Adding a new effect

1. Create a new `.cs` file in `Effects/`.
2. Subclass `SceneTransitionEffect`.
3. Pick a `TransitionChannel`.
4. Implement `Begin()` to return an `EffectHandle`. The handle's `End()` is called when the destination load finishes; `AwaitCompletion()` yields until the in-phase is done.
5. The effect should run on a host that survives scene unloads (`LetterboxOverlay`, `TransitionRunner`, or its own DDOL singleton).

## Adding a new destination

1. Create a new `.cs` file in `Destinations/`.
2. Subclass `SceneDestination`.
3. Implement `IsValid()` and `Apply(initiator)`.
4. `Apply` should yield until the initiator is fully placed and the world around it is ready.

Examples of future destination kinds:
- `WorldPositionDestination` — teleport to a `Vector3` in the streamed exterior, wait for chunks to load.
- `ReturnDestination` — return to the previous exterior position recorded by `InteriorManager`.
- `FullSceneSwapDestination` — single (non-additive) scene swap for main menu / cinematics.

## Adding a new trigger

1. Create a new `.cs` file in `Triggers/`.
2. `[RequireComponent(typeof(SceneTransition))]`.
3. On whatever signal you want, call `transition.Trigger(initiator)`.

Examples: timer trigger, signal-bus trigger, network event trigger, cutscene-step trigger.

## What's deliberately out of scope

- **Cancellation** — once started, a transition runs to completion.
- **Group / party transitions** — only the calling initiator transitions. No volume "drag everyone in" semantics.
- **Mounted players / vehicles** — transition fires for the player; the mount is left behind. Revisit if it bites.
- **Same-scene teleports** — not a "transition." Use `InteriorAnchor.TeleportPlayer` or a dedicated teleport component.
- **Full scene swap** — additive only. Add a `FullSceneSwapDestination` when needed.
- **Save/load integration** — the destination doesn't write any "player is at X" state. Save system needs its own hook.

## Files

| File | Role |
|---|---|
| `SceneTransition.cs` | Orchestrator. Holds destination + effect refs. Public `Trigger(initiator)` API. |
| `TransitionRunner.cs` | DontDestroyOnLoad host that runs the orchestrator coroutine so it survives scene unloads. |
| `Destinations/SceneDestination.cs` | SO base for destination kinds. |
| `Destinations/InteriorSceneDestination.cs` | Additive interior load via `InteriorManager`. |
| `Effects/SceneTransitionEffect.cs` | SO base for effects + `EffectHandle` + `TransitionChannel` enum. |
| `Effects/FadeToBlackEffect.cs` | Screen fade via `LetterboxOverlay`, spacebar-skippable in-phase. |
| `Triggers/InteractableTransitionTrigger.cs` | `IInteractable` adapter. |
| `Triggers/VolumeTransitionTrigger.cs` | `OnTriggerEnter` adapter; filters by `"Player"` tag and/or `AgentController`. |

## Dependencies

- `InteriorManager` (`SceneManagement/Interiors/`) — server-authoritative additive scene loader.
- `InteriorScene` / `InteriorAnchor` — designer data + spawn points inside interior scenes.
- `LetterboxOverlay` (`Cutscenes/UI/`) — DontDestroyOnLoad fade overlay used by `FadeToBlackEffect`.
- `IInteractable` / `Interactor` (`InteractionSystem/`) — used by `InteractableTransitionTrigger`.
- `AgentController` (`agents/controller/`) — universal "moving agent" marker used by `VolumeTransitionTrigger`.
