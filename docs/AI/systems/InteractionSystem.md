---
system: Interaction
layer: items
summary: "Look at a collider, press E: one raycast picks the target, one resolver labels it, the target replicates"
paths:
  - Assets/Game/Scripts/Gameplay/Interaction
  - Assets/Game/Scripts/Gameplay/Trading
  - Assets/Game/Scripts/Presentation/UI/HUD
  - Assets/Game/Scripts/Core/Persistence/Adapters
symptoms:
  - "no E prompt appears on the control I am looking at"
  - "the prompt lights up but pressing E refuses to do anything"
  - "a trigger volume in front of a control swallows every interactable behind it"
  - "I can use a control through a window, a windscreen or a canopy from outside"
  - "pressing E at a vehicle from outside seats me in its cockpit"
  - "the door opens for the host and stays shut for clients"
  - "pressing E opens the wrong door on the other machine"
  - "interaction dies silently on the client while the host works fine"
  - "a door, lever or workstation is back in its authored state after loading a save"
reads_with: [Vehicles, Inventory, Persistence]
updated: 2026-09-02
---

# Interaction

Look at a collider, press E: one raycast picks a target, one resolver describes it, and the target
itself owns whatever replication its effect needs.

**Scope:** [`Assets/Game/Scripts/Gameplay/Interaction/`](Assets/Game/Scripts/Gameplay/Interaction/) (Core, Interactions, Triggers), [`Gameplay/Trading/`](Assets/Game/Scripts/Gameplay/Trading/)
**Related:** [Vehicles.md](Vehicles.md) (stations), [MountSystem.md](MountSystem.md), [Inventory.md](Inventory.md), [Persistence.md](Persistence.md), [Interiors.md](Interiors.md), [Cutscenes.md](Cutscenes.md)

## Model

- **Detection is a raycast, not a registry.** [`Interactor`](Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs) sits on the `PlayerCharacter` root ([`PlayerCharacter.prefab`](Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab), `_castDistance` overridden to 20), casts from `lookTransform` (camera) every `Update` and again on each press. `RaycastNonAlloc` into a 16-hit buffer, mask `~Player`, sorted by distance.
- **Arbitration** (`Interactor.ResolveAlongRay`, static and testable): skip anything under the interactor's own `transform.root`; a **trigger** collider only answers if the `IInteractable` is on that same GameObject, otherwise the ray passes through it; a solid collider answers with its own or its parents' `IInteractable`, and if it has none it **blocks** the line of sight. The one trigger that is not see-through is an [`InteractionBlocker`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionBlocker.cs) — glass: it offers nothing and stops the ray.
- **One availability gate for hover and press** (`Interactor.IsAvailable`): `IInteractable.CanInteract()` **and** `IContextualInteractable.CanInteract(interactor)`, plus a `Behaviour.isActiveAndEnabled` check. The crosshair can therefore never light up on something that will refuse.
- **Prompts are default-on.** [`InteractionPromptResolver`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionPromptResolver.cs) composes, most specific first: an authored [`InteractionPrompt`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionPrompt.cs) component → [`IInteractionReadout`](Assets/Game/Scripts/Gameplay/Interaction/Core/IInteractionReadout.cs) (live label/prompt/0-1 bar/value text) → the humanised type name with noise suffixes stripped (`DoorInteraction` → "Door"). Default prompt `"E: interact"`, plus `"   LMB: use"` for an `ISecondaryInteractable`.
- **Execution is local to the presser.** `Interact()` only ever runs on the machine of the player who pressed (input is disabled on non-owned bodies). Getting the consequence onto other machines is the interactable's own job — via [`NetLatch`](Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs), an `[Rpc]` + [`InteractorRelay`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractorRelay.cs), or a `NetMsg` of its own.
- **Hold-to-interact: N/A** — `Interact` is bound to `Player.Interact.performed` only ([`PlayerInputManager`](Assets/Game/Scripts/Core/Input/PlayerInputManager.cs)). Continuous controls (winches, helm) are repeated presses plus a station claim; the only held stream is `Use` for held items.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `IInteractable` | [Core/IInteractable.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/IInteractable.cs) | `CanInteract()` / `Interact(Interactor)`. Needs a collider. |
| `IContextualInteractable` | [Core/IContextualInteractable.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/IContextualInteractable.cs) | Per-interactor refusal (already aboard, in a fight with you). Hides prompt too. |
| `ISecondaryInteractable` | [Core/ISecondaryInteractable.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/ISecondaryInteractable.cs) | Opposite action on Use/LMB (ease vs haul). |
| `IInteractionReadout` | [Core/IInteractionReadout.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/IInteractionReadout.cs) | Label, prompt, `float? Value01`, `ValueText` for the HUD panel. |
| `Interactor` | [Core/Interactor.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs) | Raycast, arbitration, `HoveredInteractable`, `ClearHoverState()`. |
| `InteractionBlocker` | [Core/InteractionBlocker.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionBlocker.cs) | Marker on a trigger collider: see through it, do not reach through it. Blocks from **outside** only. |
| `InteractionPromptResolver` / `InteractionDisplay` | [Core/InteractionPromptResolver.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionPromptResolver.cs) | Interactable → drawable text. `SafeCanInteract` swallows author exceptions. |
| `NetLatch` + `ILatchHost` | [Core/NetLatch.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs) | One networked bit: request → server decides → `NetMsg.LatchState` to all; late-joiner ask; `oneWay`; `Restore()`. Plain class, driven from the fixture's `OnEnable`/`OnDisable`. |
| `InteractorRelay` | [Core/InteractorRelay.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractorRelay.cs) | `GetComponentInParent<NetworkObject>()` out, `GetComponentInChildren<Interactor>(true)` back. |
| `ITriggerable` | [Core/ITriggerable.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/ITriggerable.cs) | "Fire this action for an initiator" — scene transitions, cutscenes, portals. |
| `InteractionPromptUI` / `CrosshairUI` | [UI/HUD/](Assets/Game/Scripts/Presentation/UI/HUD/) | Panel reads `HoveredInteractable` each frame (self-finds the Interactor). Crosshair's hover half is unwired on `PlayerHUD.prefab`. |
| `TraderInteraction` / `TraderProfile` / `TradeOffer` | [Gameplay/Trading/](Assets/Game/Scripts/Gameplay/Trading/) | Barter: N slots of X for M of Y. Not an `IInteractable` — see Flows. |
| `TradeUI` | [UI/Pages/TradeUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/TradeUI.cs) | Code-built full-screen panel, `DontDestroyOnLoad` singleton, `GameplayMenuScope`. |

## Interactables

| Interactable | File | Pressing E |
| --- | --- | --- |
| `DoorInteraction` | [Interactions/DoorInteraction.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/DoorInteraction.cs) | Toggles a `NetLatch`; two leaves swing ±90° from their authored **local** pose. `IsOpen` is read by `SandstormShelter`. |
| `LeverInteraction` | [Interactions/LeverInteraction.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/LeverInteraction.cs) | Swings the handle, fires `onPulled` on **every** machine once per pull. `oneShot` ⇒ one-way latch; `replayOnJoin` decides whether joiners/loads re-run the event. |
| `RepairWorkstation` | [Interactions/RepairWorkstation.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/RepairWorkstation.cs) | Consumes the selected hotbar slot if it holds `requiredItem`; server `NetworkVariable<int>` progress; accept/reject feedback via `[Rpc(SendTo.Everyone)]`. Refuses when repaired. |
| `DialogInteraction` | [Interactions/DialogInteraction.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/DialogInteraction.cs) | Speaks the next line (sequence / random pool / [`DialogPool`](Assets/Game/Scripts/Gameplay/Interaction/Interactions/DialogPool.cs) / branching Y-N). Contextual: silent while `AgentTargeting`/`ProvocationModule` says it is fighting **you**. Purely local, no netcode. |
| `ShipInteraction` | [Interactions/ShipInteraction.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/ShipInteraction.cs) | Hands the selected scrap to `Ship.AddScrap()`. `Network.Execute` + `InteractServerRpc`. Sound plays on attempt, not outcome. |
| `InteractableTrigger` | [Triggers/InteractableTrigger.cs](Assets/Game/Scripts/Gameplay/Interaction/Triggers/InteractableTrigger.cs) | Forwards to the `ITriggerable` on the same GameObject. Ungated on purpose (unlike [`VolumeTrigger`](Assets/Game/Scripts/Gameplay/Interaction/Triggers/VolumeTrigger.cs), which is server-only). |
| `InteractableProxy` | [Triggers/InteractableProxy.cs](Assets/Game/Scripts/Gameplay/Interaction/Triggers/InteractableProxy.cs) | Redirects the same press to an `IInteractable` on another GameObject. Must stay netcode-free. |
| `PickupableItem` | [Items/Core/PickupableItem.cs](Assets/Game/Scripts/Items/Core/PickupableItem.cs) | Picks the item up (`Network.Execute` → server). |
| `BackpackObject` | [Items/Backpack/BackpackObject.cs](Assets/Game/Scripts/Items/Backpack/BackpackObject.cs) | Grounded pack: open lid, or reshoulder it (server-decided). |
| `MountModule` | [agents/Modules/Riding/MountModule.cs](Assets/Game/Scripts/agents/Modules/Riding/MountModule.cs) | Mounts, via `MountNetworkSync.RequestMount`. Gated by `mountableByDirectInteraction`. |
| `MountStation` | [Vehicles/Stations/MountStation.cs](Assets/Game/Scripts/Vehicles/Stations/MountStation.cs) | Same seat, but only from an authored control (cockpit, wheel). |
| `VehicleStation` (abstract) | [Vehicles/Stations/VehicleStation.cs](Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs) | Claim/work/stand-down over `NetMsg.StationClaim`/`StationState`, addressed to the **vehicle's** channel. |
| `DuneFoilHelm` | [Vehicles/Stations/DuneFoilHelm.cs](Assets/Game/Scripts/Vehicles/Stations/DuneFoilHelm.cs) | Take the wheel / stand down; steers the foil. `IInteractionReadout`. |
| `DuneFoilRiggingStation` | [Vehicles/Stations/DuneFoilRiggingStation.cs](Assets/Game/Scripts/Vehicles/Stations/DuneFoilRiggingStation.cs) | E = more, LMB = less (`ISecondaryInteractable`), predicted locally then confirmed. |
| `DeckBoarding` | [Vehicles/Stations/DeckBoarding.cs](Assets/Game/Scripts/Vehicles/Stations/DeckBoarding.cs) | Teleports you onto the deck. Contextual: refused while you stand in the carry volume; `Network.Owns(player)` guard. |
| `ArticulatedPartInteraction` | [Vehicles/Parts/ArticulatedPartInteraction.cs](Assets/Game/Scripts/Vehicles/Parts/ArticulatedPartInteraction.cs) | Toggles a group of hinged parts via `NetMsg.PartToggle`. |
| `SpaceshipLaunchInteract` | [Spaceship/SpaceshipLaunchInteract.cs](Assets/Game/Scripts/Spaceship/SpaceshipLaunchInteract.cs) | Launches the ship; `NetLatch` (`ILatchHost`). |
| `InteriorEntrance` | [Core/SceneManagement/Interiors/InteriorEntrance.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorEntrance.cs) | `InteriorManager.EnterInterior`, unless lock-out is active. |
| `CaveExitCover` | [World/…/CaveExitCover.cs](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Generation/CaveExitCover.cs) | Leaves the cave (also a walk-in volume). |

## Flows

**Press E**
1. `Update` casts, resolves along the ray, stores `HoveredInteractable` when `IsAvailable`.
2. `InteractionPromptUI` resolves that into label/prompt/bar and fades the panel in.
3. `OnInteractPressed` → `Interactor.Interact()` re-casts and re-checks (never trusts the cached hover).
4. The interactable asks its authority: `latch.Toggle()`, `NetToServer(...)`, or `Network.Execute(local, client: InteractorRelay.RequestFrom(…, rpc))`.
5. Server validates again, mutates, broadcasts (`LatchState` / `NetworkVariable` / `SendTo.Everyone` feedback RPC); every machine applies in the same handler. Offline/host collapses to the same frame.

**Trade**
1. `TraderInteraction` is **not** an `IInteractable` (one `IInteractable` per collider). `DialogInteraction.Interact` calls `trader.TryOfferTrade(this, interactor)` first.
2. It gates on `offerBeforeDialog`, `TradeUI.IsOpen`, `declineCooldown`, stock; then asks via `DialogInteraction.AskQuestion` (Y/N in `NpcDialogPopupUI`). No stock ⇒ `soldOutLine` once, then silence for the cooldown. Decline ⇒ `declineLine` + cooldown.
3. Yes ⇒ `TradeUI.Open(trader, interactor, onClosed)` — `GameplayMenuScope.Enter` frees the cursor, freezes time solo, puts the player in cutscene mode; panel is built with `UIBuilder`, redraws on `inventory.OnSlotChanged`.
4. Click a row ⇒ `CanAfford` (slots held ≥ `wantsCount`, free slots + payment ≥ `givesCount`, `InStock`) ⇒ `TryExecute`: remove payment slots, add goods (player half is server-authoritative inventory), then the trader half — direct `SettleTraderSide` when `Network.Simulates(this)`, else `NetToServer(NetMsg.Trade)`.
5. `SettleTraderSide` re-checks stock, decrements, and drops the payment into the trader's `EntityInventoryComponent`. Escape/Tab/close/trader destroyed ⇒ `CloseInternal` → `GameplayMenuScope.Exit`.

## Multiplayer

| Path | Carrier | Authority |
| --- | --- | --- |
| Doors, levers, spaceship launch | `NetMsg.LatchSet` (63) / `LatchState` (64), `A` = latch index, `B` = verb (−1 ask, 0/1 set, 2/3 instant) | `Network.Simulates(owner)`, re-runs `Accepts` on arrival, idempotent on state |
| Vehicle stations | `NetMsg.StationClaim` (65) / `StationState` (66) on the vehicle's channel | Server owns the claim table; occupant predicts locally |
| Articulated parts | `NetMsg.PartToggle` (60) / `PartState` | Server |
| Workstation, ship scrap, pickups | `[Rpc(SendTo.Server, InvokePermission = Everyone)]` + `InteractorRelay` | Server; feedback re-broadcast `SendTo.Everyone` |
| Trade | `NetMsg.Trade` (50) for the trader's books only | Server re-checks stock; player's inventory replicates on its own |
| Dialog, `InteractableProxy`, `DeckBoarding` | none | Local to the presser by design |

`NetLatch.Index` is **positional** over every `ILatchHost` under the entity — same build, same order. It does not survive reordering a prefab's children between builds.

## Persistence

| State | Saver | Key |
| --- | --- | --- |
| Door open/shut | [`DoorSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/DoorSaveable.cs) → `RestoreOpen` | `door` |
| Lever pulled (incl. one-shot spent) | [`LeverSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/LeverSaveable.cs) → `RestorePulled` | `lever` |
| Scrap progress | [`RepairWorkstationSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/RepairWorkstationSaveable.cs) → `RestoreProgress` | `repair` |
| Trader stock + decline cooldown (remaining seconds, not a deadline) | [`TraderSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/TraderSaveable.cs) → `RestoreOffers` | `trader` |

All four are auto-attached by [`SaveablePolicy`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs); doors, levers and workstations are `IPersistentEntity` because nothing else about them qualifies. Restores go through `NetLatch.Restore` / the `NetworkVariable` (instant + silent, then announced) — never by posing transforms. Stations, mounts and dialog progress are **not** saved.

## Gotchas

- **A trigger collider is see-through** unless the `IInteractable` is on that exact GameObject — carry volumes used to swallow every control on a deck. Put the component on the trigger, or use `InteractableProxy`.
- **See-through means reach-through, and a hull is only as opaque as its collision.** The ray stops at *solid* colliders, so anything drawn without one — glass, a hologram, a grille — is not in the way at all, and whatever is behind it is usable from outside. This is how the PlayerShip became boardable from the air above its canopy: the dome carries no collider on purpose and the cockpit chairs' own trigger volumes were the first thing an outside ray met. Glass gets an [`InteractionBlocker`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionBlocker.cs): a **trigger**, so no body collides with it and every movement, ground and clearance query (all of which ask with `QueryTriggerInteraction.Ignore`) still ignores it. It must **enclose what it protects**, not merely stand in front of it — a ray that *starts* inside a collider is not reported as hitting it, which is exactly what keeps the pilot standing under the canopy able to reach their own chair, and is why the blocker is a box over the cockpit rather than a skin on the glass.
- **A solid collider with no interactable blocks the ray.** A stray hull box in front of a control silently kills its prompt.
- **`CanInteract()` must equal what the press will do**, or the prompt lights up and refuses. Per-player refusals belong in `IContextualInteractable`, not in `CanInteract()`.
- **Disabling the Interactor freezes nothing** — `OnDisable` calls `ClearHoverState()`; mounting takes that route. A future path that stops `Update` without disabling would strand the panel.
- **`InteractorRelay` exists because the lookups are one level off:** the `Interactor` is not on the `NetworkObject` root, so `interactor.GetComponent<NetworkObject>()` and `netObj.GetComponent<Interactor>()` both return null and client interaction dies silently. Always use the relay.
- **A latch owner that is not an `ILatchHost`** warns once and falls back to its slot number — which collides with other fixtures' numbering, so the wrong door opens.
- **Every `latch.Enable()` needs a `Disable()`**, or a disabled fixture still swings on a broadcast.
- **`NetLatch` state starts `false`**: fixtures must be authored in their off pose (door shut, lever at rest).
- **`TraderProfile.CloneOffers()` rebuilds the live list in `Awake` every session**, so a restore has to *replace* the list (`RestoreOffers`), not nudge it. Mutating the asset directly empties every trader sharing it — and persists to disk in the editor.
- **Trading is code-complete but unauthored**: no `TraderProfile` asset exists and no prefab or scene references `TraderInteraction`. It also needs a `DialogInteraction` on the same GameObject (that is the only entry point) and an `EntityInventoryComponent` if payment should land anywhere.
- **A second `IInteractable` on the same collider is ambiguous** — `GetComponent<IInteractable>()` picks by component order. That is why `TraderInteraction` routes through `DialogInteraction`.
- **Menus gate input via `GameplayMenuScope`**, which puts the player in cutscene mode; the E key stops reaching `Interactor` while `TradeUI` (or any scoped panel) is open. `TradeUI` fades on `unscaledDeltaTime` because the scope freezes the clock solo.
- **`CrosshairUI.playerInteractor` is unassigned** on `PlayerHUD.prefab`, so hover-brightening has never run. `InteractionPromptUI` self-finds instead.

## Extending

1. Add a collider (or reuse one) and a component implementing `IInteractable`; return an honest `CanInteract()`.
2. Per-player availability ⇒ also implement `IContextualInteractable`. Reverse action on LMB ⇒ `ISecondaryInteractable`. Live value/bar ⇒ `IInteractionReadout`.
3. Pick a replication route: binary state ⇒ `NetLatch` + `ILatchHost` (`LatchCount` answerable before `Awake`; `Enable`/`Disable` from `OnEnable`/`OnDisable`); acts on the presser's inventory ⇒ `Network.Execute` + `InteractorRelay`; crew control ⇒ subclass `VehicleStation`; a whole action ⇒ implement `ITriggerable` and drop an `InteractableTrigger` or `VolumeTrigger` on it.
4. Name it: rely on the derived type name, or add an `InteractionPrompt` to relabel or silence it.
5. Persist it: expose a `Restore…` method that goes through the latch/NetworkVariable, add an `ISaveable` adapter under `Core/Persistence/Adapters/` with a frozen key, mark the component `IPersistentEntity`, and register it in `SaveablePolicy`.
6. Verify on a real client and after a reload — both failure modes here are silent.
