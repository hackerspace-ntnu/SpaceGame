# Interaction System

Two layers:

1. **`IInteractable` / `Interactor`** — raycast-based "look at thing, press E" interaction. The original surface; most one-off interactions implement this directly (`DoorInteraction`, `DialogInteraction`, `MountModule`, …).
2. **`ITriggerable` + generic triggers** — a seam for any action that can be "fired with an initiator" (scene transitions, cutscenes, future actions). Lets one set of trigger components work with every action type.

## Layer 1 — `IInteractable` / `Interactor`

### `IInteractable.cs`

Interface for all interactable targets.

- `CanInteract()`: returns whether interaction is currently allowed.
- `Interact(Interactor interactor)`: executes interaction logic.

### `Interactor.cs`

Should be placed on the player (or any actor that can interact).

- Performs a raycast to find objects with components implementing `IInteractable`.
- Calls `CanInteract()`, then `Interact(...)` when the interact input is pressed.

### Basic setup

1. Add `Interactor` to your player object.
2. Add a collider to any object you want to interact with.
3. Add an interaction component (`DoorInteraction`, `DialogInteraction`, etc.) to that object.

### `RepairWorkstation` (item-gated interaction)

`Assets/Prefabs/Environment/RepairWorkstation.prefab` — a motor that is repaired by feeding it ship scrap.

Interacting checks the player's **selected hotbar slot**: if it holds `requiredItem` (`Assets/Resources/Items/Scraps.asset`) the item is consumed and progress advances by one; anything else fires `onScrapRejected`. Progress lives in a server-owned `NetworkVariable`, so the gauge matches on every client, and the whole thing still works with no session running.

Feedback, all driven off the same progress value:

- The world-space `Gauge` canvas (`RepairProgressUI`) — fill bar plus an `x / y` readout, red → amber → green, `NEEDS SCRAP` → `ONLINE`.
- The `StatusLight` sphere, tinted through a `MaterialPropertyBlock`.
- A piston "clunk" on each accepted scrap, and the flywheels spin once repaired.
- `onScrapAccepted(float progress01)` / `onScrapRejected` / `onRepaired` UnityEvents for sound and gameplay hooks.

`CanInteract()` returns false once repaired, so the crosshair stops lighting up on a finished machine.

## Layer 2 — `ITriggerable` + triggers

For "do a thing to whichever entity caused this" actions (open a door + load a scene, play a cutscene + fire a UnityEvent, teleport somewhere), use the trigger seam.

### `ITriggerable.cs`

```csharp
public interface ITriggerable
{
    bool      CanTrigger(GameObject initiator);
    Coroutine Trigger   (GameObject initiator);
}
```

Anything fireable. The initiator is the player or AI agent the action runs *on*. Current implementers:

- [`SceneTransition`](../SceneManagement/Transitions/SceneTransition.cs) — orchestrates effects + a scene destination. See [INTERIORS.md](../../../INTERIORS.md).
- [`CutsceneAction`](../Cutscenes/CutsceneAction.cs) — plays a `Cutscene` then fires a `UnityEvent<GameObject>`. See [CUTSCENES.md](../../../CUTSCENES.md).

### Trigger components

Drop one of these on the same GameObject as your `ITriggerable`. They auto-discover it via `GetComponent<ITriggerable>()`, so the trigger never has to know which action is wired up.

| Component | Fires when |
|---|---|
| `InteractableTrigger` | Player raycast → E (implements `IInteractable` and forwards). |
| `VolumeTrigger` | A player or AI agent enters a trigger collider. Eligibility flags + rearm cooldown. |

Either component has an optional `triggerableOverride` field if you want to point at an `ITriggerable` on a different component on the same GameObject (rare; auto-discovery covers nearly all cases).

### Adding a new triggerable action

```csharp
public class MyAction : MonoBehaviour, ITriggerable
{
    public bool CanTrigger(GameObject initiator) => /* gate */;
    public Coroutine Trigger(GameObject initiator) => StartCoroutine(Run(initiator));

    IEnumerator Run(GameObject initiator) { /* … */ yield break; }
}
```

Drop `InteractableTrigger` or `VolumeTrigger` on the same GameObject. Done — no new trigger class.


## Dialogue Setup Guide (Developer)

Use this when implementing NPC dialogue quickly.

### 1. Add Dialogue Interactor Component

On your object:

1. Add `DialogInteraction` (`Assets/Scripts/InteractionSystem/Interactions/DialogInteraction.cs`).
2. Make sure the NPC has a collider so the player raycast can hit it.
3. Optional: add `NpcBrain` if you want the NPC to stop and face the player during dialogue.

### 2. Add Dialogue Panel Prefab

In your UI canvas:

1. Drag in `Assets/Prefabs/UI/dialoge/DialogePanel.prefab`.
2. Ensure there is exactly one active `NpcDialogPopupUI` in the scene.
3. In `NpcDialogPopupUI`, verify references:
- `popupRoot`
- `dialogText`
- `choiceRoot` (for branching questions)
- for branching dialoge: `optionAText`, `optionBText`, `yesButton`, `noButton`

### 3. Configure `DialogInteraction` in Inspector

Set `Dialog Mode` depending on behavior:

- `PredefinedSequence`: walks through `Dialog Lines` in order.
- `RandomFromGlobalPool`: random line from `Global Dialog Pool` (`DialogPool` asset or built-in defaults).
- `RandomFromPredefinedPool`: random line from this NPC's local `Predefined Random Pool` array.
- `BranchingSequence`: uses `Branching Steps` with line/question nodes and Y/N branches.

Common settings:

- `Loop Dialog Lines`: repeat when reaching end.
- `Allow Restart After End`: allow starting dialogue again after completion.
- `Finish Current Line On Interact While Typing`: second interact key finishes typewriter line first.
- `Popup Duration`: auto-hide timing for non-question lines.
- `Restart From Beginning After Seconds`: inactivity timeout before sequence resets.
- `Use Delay Between Dialogues` + `Dialogue Delay Seconds`: cooldown between full dialogue sessions.
