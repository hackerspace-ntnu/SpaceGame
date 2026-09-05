---
system: Oxygen
layer: items
summary: "The ship's oxygen plant: two receptacles, a power cell that wakes it and a bottle it fills in five seconds"
paths:
  - Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGenerator.cs
  - Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGeneratorDock.cs
  - Assets/Game/Scripts/Items/Oxygen
  - Assets/Game/Scripts/Presentation/EmissiveLamp.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/OxygenGeneratorSaveable.cs
  - Assets/Game/Editor/Environment/OxygenGeneratorBuilder.cs
  - Assets/Game/Editor/Items/OxygenGearBuilder.cs
  - Assets/Game/Prefabs/Environment/Structures/Facilities/OxygenGenerator.prefab
  - Assets/Game/Prefabs/Items/Supplies
  - Assets/Game/Resources/Items/Supplies
  - "Assets/Game/Art/Models/_Source~/models/props/oxygen_generator_BUILD.md"
symptoms:
  - "the crosshair lights up on the oxygen plant's body but neither receptacle can be aimed at"
  - "the plant's lamp is dark with a power cell fitted, or lit with none"
  - "a bottle sits in the plant forever and never fills"
  - "a bottle fills on the host and stays empty for everyone else"
  - "the power cell I fitted is gone after loading the world, and the plant is dark again"
  - "a filled bottle is refused by the backpack that took the empty one"
  - "the plant clunks and hisses its way through a reload"
  - "pointing an empty hand at a receptacle does nothing and says nothing"
  - "the plant is missing from the ship after a rebuild, with one warning in the console"
  - "an oxygen tank stands on end on the mat and eats a third of the leaf"
  - "there is an oxygen tank on the expedition rig that nothing can take off"
  - "anything at all can be dropped into the bottle's socket on the back of the pack"
  - "the pack carries a bottle with no visible connection to it"
  - "a huge black shape sticks out of the pack once a bottle is in its socket, far bigger than the pack itself"
  - "nothing on the pack says where the oxygen bottle is supposed to go"
  - "the power cell I fitted never appears in the slot, and the one I took out is still standing there"
  - "a docked item pops in or out much later, at the unrelated moment something else changes"
reads_with: [Inventory, Backpack, PlayerShip, InteractionSystem, Persistence, Multiplayer]
updated: 2026-09-04
---

# Oxygen

A wall-mounted plant on the lander's main deck with two receptacles: a rectangular slot that takes a
**power cell**, and a round collar that takes an **oxygen bottle** and fills it in five seconds.

**Scope:** [OxygenGenerator.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGenerator.cs), [OxygenGeneratorDock.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGeneratorDock.cs), [Items/Oxygen/](Assets/Game/Scripts/Items/Oxygen), [OxygenGeneratorBuilder.cs](Assets/Game/Editor/Environment/OxygenGeneratorBuilder.cs), [OxygenGearBuilder.cs](Assets/Game/Editor/Items/OxygenGearBuilder.cs).
**Related:** [Inventory.md](Inventory.md) (item defs, the hotbar) · [Backpack.md](Backpack.md) (what a supply costs on the mat) · [PlayerShip.md](PlayerShip.md) (it is the hull's fourth fixture) · [InteractionSystem.md](InteractionSystem.md) · [Persistence.md](Persistence.md).

## Model

- **Three items, one machine.** [`OxygenTank`](Assets/Game/Resources/Items/Supplies/OxygenTank.asset), `OxygenTankEmpty` and `PowerCell` are ordinary `InventoryItem`s carrying [`DockableSupply`](Assets/Game/Scripts/Items/Oxygen/DockableSupply.cs); the plant is a ship fixture carrying [`OxygenGenerator`](Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGenerator.cs) plus two `OxygenGeneratorDock`s.
- **A bottle's charge is its IDENTITY, not a number.** Filling swaps `OxygenTankEmpty` for `OxygenTank`. The hotbar replicates item **IDs** and `ItemState` does not replicate at all ([Inventory.md](Inventory.md)), so a charge kept in a bag would be a value only the server could ever see — as two assets it reaches the wire, the save file, the hotbar, the mat and the icon for free.
- **Oxygen has a consumer; the cell still does not.** [`SuitOxygen`](Assets/Game/Scripts/Gameplay/Oxygen/SuitOxygen.cs) on the player drains outside a [`BreathableVolume`](Assets/Game/Scripts/Gameplay/Oxygen/BreathableVolume.cs), and a charged bottle is spent by **using** it — `DockableSupply.Use` refills the suit and swaps the item for its drained twin in the same hotbar slot. The loop is now *find a bottle → power the plant → fill the bottle → breathe it*. There is still no battery meter: the plant's supply is unlimited by construction and the cell never drains. See [Visor.md](Visor.md) for the gauge and the warnings.
- **Two docks because a receptacle is the signifier for its verb.** A round collar physically cannot take a slab cell and the slot cannot take a bottle, which the player reads before any text appears (`GDC-L1-UX-0004`); colour is only confirmation (`GDC-L1-UX-0003`). The model was built to that rule — see [oxygen_generator_BUILD.md](Assets/Game/Art/Models/_Source~/models/props/oxygen_generator_BUILD.md).
- **The docked poses are the model's, not the builder's.** `Marker_OxyGen_TankDock` and `Marker_OxyGen_CellDock` are 6 mm cubes in the FBX whose **origins are the docked poses**; the builder reads their positions and never re-derives the arithmetic.
- **A bottle costs 3 x 6 = 18 cells on the pack and fits six of the rig's seven faces** — exactly a back panel, and inside the leaf, the rack and both wings with room; only `LongGoods` (one cell deep) refuses it. `packSize` is **0.50**, not the roster's usual "true size rounded up plus a cell": that rule assumes an item stands on the face, and this one lies down, so its length is in the footprint. See `PackSizeTests`.
- **The bottle has a SOCKET on the rig, and only a bottle fits it.** [`PackSurfaceId.BackPanelCentre`](Assets/Game/Scripts/Items/Backpack/Placement/PackSurfaceId.cs) is a 3 x 6 cell face between the two back panels — where the rig's own modelled bottle used to be bolted — and it is the only RESERVED face in the game: `PackSurface.AcceptsOnly` names the two bottles and it refuses everything else, on every path (place, move, first-fit, and both halves of a hotbar swap). First-fit PREFERS it, so a bottle picked up or stowed goes to its socket rather than onto the mat.
- **A hose is drawn from the pack's valve block to whatever is in that socket** ([`PackHose`](Assets/Game/Scripts/Items/Backpack/PackHose.cs)), so it is visible that the rig is plumbed into the bottle rather than merely carrying it. Drawn rather than modelled: a hose in the model runs to thin air whenever the bottle is out, which is exactly what the socket is for. Its tube hangs off a MARKER on the rig, so its thickness is written in that marker's frame and not in metres — see [Backpack.md](Backpack.md)'s gotcha on the centimetre convention.
- **Carrying a bottle in focus mode lights the socket up.** It is the only face on the rig reserved for anything, and it looks exactly like the two ordinary back panels beside it, so nothing else on the pack could tell a player where a bottle goes. `PackContainer.SocketFor` names it and `PackGridVisual.ShowSocket` draws its free cells in the placement green — see [Backpack.md](Backpack.md)'s `Find the socket` flow.
- **The bottle on your back is the real item.** Until 2026-09-03 the expedition rig had one MODELLED INTO it — `Mesh_Rig_OxygenTank` plus its bands, authored as "a fixed fitting, not an item" — so the pack showed a bottle nothing could take off. That geometry is deleted and the rig carries a real `OxygenTank` in its authored starting items instead ([Backpack.md](Backpack.md)).
- **Everything above the meshes is generated.** Authored = the `.blend` and the two constants in each builder. A hand edit to either prefab is destroyed by the next run, silently.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `OxygenGenerator` | [OxygenGenerator.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGenerator.cs) | The machine. One `NetworkVariable<Plant>` (cell / tank / fill deadline), both presses, the lamps, the docked copies, the fill clock. `IPersistentEntity`. |
| `OxygenGenerator.Plant` | same | The replicated struct: `Cell`, `Tank` (a `DockedTank`), `FillEndsAt` on the **server's** clock. |
| `OxygenGeneratorDock` | [OxygenGeneratorDock.cs](Assets/Game/Scripts/Gameplay/Interaction/Interactions/OxygenGeneratorDock.cs) | One receptacle: `IInteractable` + `IInteractionReadout` on a **trigger** volume. Owns no state. |
| `DockableSupply` | [DockableSupply.cs](Assets/Game/Scripts/Items/Oxygen/DockableSupply.cs) | The carried item. No use verb; exists for the hold pose and to paint its own gauge. |
| `EmissiveLamp` | [EmissiveLamp.cs](Assets/Game/Scripts/Presentation/EmissiveLamp.cs) | Paints one lamp or one **submesh** of one through a shared `MaterialPropertyBlock`. Also used by `RepairWorkstation`. |
| `OxygenGeneratorSaveable` | [Adapters/OxygenGeneratorSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/OxygenGeneratorSaveable.cs) | Save key `oxygen`. Both docks; never the fill deadline. |
| `OxygenGearBuilder` | [OxygenGearBuilder.cs](Assets/Game/Editor/Items/OxygenGearBuilder.cs) | Builds the three item prefabs + assets, registers them for clients, stocks two of them on the ship's gear wall. |
| `OxygenGeneratorBuilder` | [OxygenGeneratorBuilder.cs](Assets/Game/Editor/Environment/OxygenGeneratorBuilder.cs) | Builds the fixture: body collider, the two aim volumes, the lamps, the light, the saver. |
| `PlayerShipBuilder.BuildOxygenGenerator` | [PlayerShipBuilder.cs](Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs) | Nests it on the hull at `CrewFixtureScale`, `OxygenGeneratorFore` forward of the deck centre. |

## Flows

**Fit a cell.** Select the cell → right-click the slot → server takes it out of the hotbar, `Cell = true`. The lamps light, the point light switches on, an inert copy of the cell appears in the slot, and `RefreshFill` starts a fill if a drained bottle is already standing in the collar.

**Fill a bottle.** Select a bottle → right-click the collar → server takes it, `Tank = Drained`, `RefreshFill` sets `FillEndsAt = now + 5 s`. Every machine reads its own clock against that deadline: the loop plays, the HUD bar climbs, and the docked bottle's own gauge lerps from dark to green. On the deadline the server sets `Tank = Charged` and the completion clunk plays everywhere.

**Take it back.** Right-click either receptacle with something in it. The item goes to the hotbar, or overflows to the backpack — the same fallback a world pickup takes. Doing this mid-fill returns the **drained** bottle: there is no partial charge and the next fill starts from the beginning.

**Refuse.** Pointing the wrong thing (or nothing) at a receptacle plays `InteractDenied` on the presser's machine only, and sends nothing. The prompt is still shown, and says what the receptacle wants — a machine that only lights up once you already hold the answer cannot teach anyone what it is for.

## Multiplayer

| Concern | Authority |
| --- | --- |
| Cell in / bottle in / bottle filled | **Server**, through one `NetworkVariable<Plant>`. Clients ask with `[Rpc(SendTo.Server, InvokePermission = Everyone)]` + `InteractorRelay`. |
| Fill progress, lamps, docked copies, every sound | **Derived**, on every machine, from a state *transition* — no feedback message exists. |
| The refusal beep | **Local to the presser.** Their own machine can read their own hotbar (item IDs replicate), so it needs no round trip and the whole crew does not hear it. |

The plant has **no `NetworkObject` of its own** — nested on `PlayerShip.prefab` it inherits the hull's, which is what makes the variable and the RPC work at all. Dropped into a chunk on its own it is inert netcode-wise (the repair station has the same arrangement).

`FillEndsAt` is an instant on `NetworkManager.ServerTime.Time`, not a progress float. That is what lets a player who joins mid-fill see the rest of it, and it is why the fill costs exactly two network writes rather than one a frame.

## Persistence

| What | Where |
| --- | --- |
| Cell fitted, bottle standing in the collar (and whether it is full) | `OxygenGeneratorSaveable`, key `oxygen`. Baked into the fixture prefab by its own builder, because `SaveablePolicy.Ensure` only runs on an entity's ROOT and this one is nested. Collected by the **hull's** entity. |
| The fill deadline | **Nothing.** It is an instant on a clock the loaded session does not share. `RestoreDock` calls `RefreshFill`, so a world saved with a bottle half-filled in a powered plant reloads and fills it again from the start. |
| An untouched plant | **Nothing.** `CaptureState` returns null with both docks empty, so a ship nobody has used carries no record. |

## Gotchas

- **A dock's aim volume must be a TRIGGER *and* stand proud of the machine's own body box.** The interaction ray takes the nearest hit; a solid collider answers with the first `IInteractable` on itself or above it, so the plant's body box — which encloses both receptacles — would answer for the whole machine and neither dock could ever be aimed at. Two things fix it together: the volumes are triggers (see-through unless the interactable is on the trigger's own GameObject) and `OxygenGeneratorBuilder` pushes each one `DockFrontClearance` **in front of the measured body box**. The cell slot needs the push — a docked cell clears the machine's front face by 32 mm and nothing else. Both halves are guarded by `PlayerShip_BothOxygenDocksAreAimedAtFromTheAisle`, whose probe deliberately *includes* triggers, unlike every walkability probe beside it.
- **Re-orienting the ITEM silently breaks the MACHINE's dock.** The plant seats a bottle by turning it onto the machine's +Z; that used to be `FromToRotation(up, forward)`, which was right only while the bottle's length was its own up. Laying the bottle down for the pack turned a bottle plugged into the hatch into one lying flat against the machine — nothing failed, the dock just stopped meaning anything, and the only tell was the aim volume reaching 0.548 instead of 0.910. `OxygenGeneratorBuilder.PlugPose` now derives it: the direction the item extends from its own ORIGIN (signed, because the long AXIS alone plugged it into the wall), and the roll from where its gauge sits, built with `LookRotation` because `FromToRotation` between opposite vectors is free to pick any perpendicular axis. Anything that re-orients an item has to re-check every dock that seats it.
- **Never parent anything to a marker.** `Marker_OxyGen_*` arrive with `localScale` **100** (the FBX's centimetre convention) and whatever rotation the Blender empty had. Only their POSITION is trusted; the builder copies it onto plain children of its own root. A display copy parented straight to a marker is drawn a hundred times life size.
- **Each dock redraws on its OWN change — one refresh for both is a latency bug.** `Adopt` reached a single `RefreshDockedVisuals` from the TANK's line only, so fitting a cell lit the lamps, played the clunk and drew no cell; it then appeared in the slot at the unrelated moment a bottle was docked or a fill landed five seconds later. The machine was never slow — it heard the press and answered in every channel except the one that says *what* happened, which is the defect `GDC-L1-FEEL-0002` names (acknowledge input on the frame it arrives) and the missing layer `GDC-L1-FEEL-0004` asks for. `RefreshCellVisual` and `RefreshTankVisual` are also separate because fitting the cell is the press that STARTS a fill: rebuilding the bottle there would throw away the gauge renderer `DrawFill` is about to paint. `OxygenSystemTests.EachDockDrawsItsOwnCopyWhenItsOwnStateChanges` pins both halves. Nothing about the write path was ever delayed — `NetworkVariable`'s setter invokes `OnValueChanged` synchronously, so the host redraws inside the press frame and a client redraws on the delta.
- **A display copy has no scripts, so it cannot paint its own gauge.** `DisplayCopy.Strip` takes every MonoBehaviour off, including `DockableSupply`. The generator therefore asks the item's **prefab** which part its gauge is and finds the same-named renderer on the copy — never the copy's own component, which is gone.
- **`packSize` 0.72 for the bottle and 0.63 for the cell, and the difference is not an oversight.** Both follow "true size rounded up to the next 0.09 m webbing pitch, plus a cell" — except the cell, where that lands on **exactly** the leaf's eight cells across. Eight cells is a float division landing precisely on an integer: it rounds either way and so decides at random whether the item fits the leaf at all. The margin is dropped instead. `PackSizeTests` carries both rows with their reasons; an unlisted divergence fails that sweep.
- **The two bottles must stay the same size.** They are one model and one `packSize`; if they ever diverge, filling a bottle can make it unstowable in the pack that took the empty one. `OxygenSystemTests.AFilledBottleIsADifferentItemFromADrainedOne` pins it.
- **A restore must not sound like a press.** `RestoreDock` writes the mirror FIRST and `Adopt` early-outs on an unchanged value, so the `NetworkVariable` write behind it re-enters and does nothing — otherwise a reloaded world clunks and hisses its way through work that was already done. (A client already connected when the host loads a save still hears one clunk; clients do not load worlds, so this is the only window it can happen in.)
- **The lamps are found by MATERIAL NAME, not by a typed submesh index.** These models are one mesh per PART and up to nine materials deep, so the emissive submesh's index is an accident of the export order. A stale index paints the enamel instead of the lamp, which looks like a broken shader rather than like a wrong number. Same rule in `OxygenGearBuilder`, which additionally *asserts* that the index it was given still names an emissive material.
- **The light is switched, never dimmed.** A URP light at zero intensity is still a light the renderer sorts — the lesson `CabinAlert` records. `powerLight.enabled` is the only thing that moves.
- **The bottle LIES DOWN in its own prefab, and that is what makes it usable on the pack.** `BackpackItemVisual` seats a copy with the ITEM's own up along the SURFACE NORMAL and never turns it over, so a bottle modelled standing on its skirt stands straight out of a vertical back panel by its whole length. `OxygenGearBuilder.BottleLiesDown` turns the MODEL a quarter back about X — laid on a horizontal face, flat against a vertical one — and gives `ItemGrip.rotationOffset` the exact inverse, so the pose in the HAND is untouched (the rule `ItemPackOrientation` applies, put in the builder because a builder-owned prefab is replaced wholesale on the next run). The SIGN is the half that fails silently: +90 and -90 both lay it down and one buries the gauge in the surface. -90 puts the gauge on the item's +Y, which is the axis the seat aligns with the normal, so it faces out of whatever it is lying on. `OxygenSystemTests.TheBottleLiesDownWithItsGaugeOutward` measures both halves off the built prefab.
- **A docked bottle has no collider and reaches 1.46 m into the room.** `DisplayCopy` strips colliders, deliberately: a collider on the copy would join the *ship's* compound Rigidbody, which is the fault that wedges players in doorways ([Backpack.md](Backpack.md)). The cost is that a crewmate can walk their head through a docked bottle 2.72 m up. Accepted; do not "fix" it by adding a collider.
- **The plant's spot on the deck is a 0.6 m window, and it was swept rather than reasoned about.** `PlayerShipBuilder.OxygenGeneratorFore` is 1.35 m forward of the deck centre because the fixture is only clear between **1.05 and 1.65 m**: aft of that it is inside the map projector's pedestal, forward of it inside `Cockpit_Seat_Command.003` — whose FITTING COLLIDER bites about 0.1 m before its renderer bounds do, so a placement derived from the meshes you can see lands the plant inside a chair. It shipped at 2.0 m for exactly that reason for one build. `PlayerShip_OxygenPlantStandsOnTheDeckClearOfEverything` is the guard; re-sweep with an `OverlapBox` over the fixture's own body box if the blockout ever moves.
- **A new trigger-with-an-interactable on the hull is not automatically a SEAT.** `PlayerShipTests.BoardingVolumes()` used to mean "every trigger carrying an `IInteractable`", which was the same set only while chairs were the only such controls; the docks joined it and `DismountPointOf` threw on a mount that was not there. It asks for a `MountStation`/`MountModule` now. Anything else mounted behind a trigger on this hull will meet the same assumption somewhere.
- **Run the builders in order.** `OxygenGearBuilder` first (the fixture's aim volumes are measured off the items that go in them), then `OxygenGeneratorBuilder`, then **Tools ▸ Vehicles ▸ Build PlayerShip Prefab**. `Tools ▸ SpaceGame ▸ Build Oxygen System (items + generator)` does the first two in one press. Rebuilding the fixture alone orphans the hull's strip of its savers — see [PlayerShip.md](PlayerShip.md)'s gotcha and re-run **Tools ▸ Save System ▸ Wire Saveable Prefabs** after it.
- **The items enter the game on the ship's gear wall.** `OxygenGearBuilder.RouteIntoTheShip` stocks one drained bottle and one cell in `WallInventory.startingMainItems`; `InventoryWallBuilder` reads that list off the existing prefab and writes it back, so it survives a rebuild of the wall. Without it the plant is unreachable — a machine that needs a cell nobody can obtain. A **charged** bottle is deliberately not stocked: it is the thing the machine makes.

## Extending

**Tune what a tank is worth** — the consumer exists now, built the way this section prescribed: a float on the player's own record, with the bottle left as the *unit* it is spent in. `SuitOxygen.drainPerSecond` is the one number that decides whether the open world reads as a journey or as a stopwatch (the default empties a full suit in about ten minutes), and `bottleRestores` decides whether a bottle is a tank or a sip. Both are serialized on the player. **Neither has been playtested.**

**Add a third receptacle** — 1) Add a `DockKind` value (they are not persisted, so appending is free). 2) Give it a marker in `oxygen_generator.blend` named `Marker_OxyGen_*` and re-export. 3) Add a `BuildDock` call. 4) Extend `Plant` — and note the struct is a `NetworkVariable` payload, so a new field is a wire change every machine in a session must share. 5) Extend `OxygenGeneratorSaveable.State` **by appending**; the enum numbers are frozen. 6) Add its row to `PlayerShipBuilder.Verify` and to `PlayerShip_BothOxygenDocksAreAimedAtFromTheAisle`.

**Change where the supplies start** — `OxygenGearBuilder.RouteIntoTheGame` stocks both containers: the ship's gear wall (drained bottle + cell) and the expedition rig (charged bottle). Both are authored `PackContainer` starting lists that each container's own builder reads forward, so a rebuild of either keeps them.

**Put a plant somewhere other than the ship** — the prefab has no `NetworkObject`, so whatever places it must add one (as `ShipRV` does for the older workstation), and `SaveablePolicy` will then wire its saver itself.
