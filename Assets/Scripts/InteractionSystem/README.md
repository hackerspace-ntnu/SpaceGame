# Interaction System

This folder contains a generic interaction framework (`IInteractable`, `Interactor`) and interaction implementations such as `DoorInteraction` and `DialogInteraction`.

## Core Concepts

### `IInteractable.cs`

Interface for all interactable targets.

- `CanInteract()`: returns whether interaction is currently allowed.
- `Interact(Interactor interactor)`: executes interaction logic.

### `Interactor.cs`

Should be placed on the player (or any actor that can interact).

- Performs a raycast to find objects with components implementing `IInteractable`.
- Calls `CanInteract()`, then `Interact(...)` when the interact input is pressed.

## Basic Setup

1. Add `Interactor` to your player object.
2. Add a collider to any object you want to interact with.
3. Add an interaction component (`DoorInteraction`, `DialogInteraction`, etc.) to that object.


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
