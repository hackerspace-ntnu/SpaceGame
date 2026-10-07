---
system: Terminal
layer: items
summary: "The lander's CRT console: right-click zooms onto its glass; four pages, deaf until the transmitter works"
paths:
  - Assets/Game/Scripts/Gameplay/Terminal
  - Assets/Game/Scripts/Presentation/UI/World/Terminal
  - Assets/Game/Prefabs/Environment/Structures/Facilities/StandingTerminal.prefab
  - Assets/Game/Art/Models/Props/standing_terminal.fbx
  - "Assets/Game/Art/Models/_Source~/components/props/crt_monitor.blend"
symptoms:
  - "right-clicking the terminal does nothing and the crosshair says Terminal"
  - "the camera flies to the terminal but the screen is blank green"
  - "the terminal's page changes for me but not for the other player"
  - "the terminal says In use after the player using it disconnected"
  - "the zoom camera parks inside the cabinet or looks at the ceiling"
  - "the key strip on the terminal renders pink"
  - "the terminal stands on the deck but pressing a tab does nothing"
  - "the terminal stands in the cockpit instead of beside the gear wall"
  - "every terminal page but the ship drawing is snow saying NO CARRIER"
  - "there is no COMMS tab on the terminal"
reads_with: [ShipSchematic, ShipSignal, InteractionSystem, PlayerShip, Multiplayer, Backpack, Oxygen]
updated: 2026-10-06
---

# Terminal

The standing terminal on the lander's main deck, in line with the gear wall: a leaning
cassette-futurism CRT cabinet the crew walk up to and **right-click**. A camera flies from their eye
to a seat in front of the glass, the cursor comes free, and the glass shows one of four pages — a
live 3D drawing of the hull whose missing modules glow red and can be turned, zoomed and pointed at;
a status readout; a GPS readout with a crew radar; COMMS, the intercepted signal — flipped with the tabs or the keys 1-4.
**Until a working long-range transmitter is fitted only the SHIP page works**: the other three are
animated snow and `NO CARRIER: LONG-RANGE TRANSMITTER OFFLINE`, and the COMMS tab is hidden. Esc,
right mouse again, or reaching for WASD hands everything back.

**Scope:** [`Gameplay/Terminal/`](Assets/Game/Scripts/Gameplay/Terminal) (console, session, camera, telemetry, the pure geometry and text), [`Presentation/UI/World/Terminal/`](Assets/Game/Scripts/Presentation/UI/World/Terminal) (the screen). The SHIP page's 3D hull is its own system: [ShipSchematic.md](ShipSchematic.md).
**Related:** [InteractionSystem.md](InteractionSystem.md) (the press), [PlayerShip.md](PlayerShip.md) (where it stands), [Backpack.md](Backpack.md) (the `FocusCamera` base it shares with the pack and body screens), [ArtPipeline.md](ArtPipeline.md) (the model).

## Model

- **Two halves with a hard line between them.** What is SHARED — which page is up, who is at the
  keyboard — lives in [`TerminalConsole`](Assets/Game/Scripts/Gameplay/Terminal/TerminalConsole.cs),
  server-decided and replicated, because the glass is a thing everybody sees. What is LOCAL — the
  zoomed-in camera, the freed cursor, the raw key reads — lives in [`TerminalFocusSession`](Assets/Game/Scripts/Gameplay/Terminal/TerminalFocusSession.cs)
  on the presser's machine and never touches the wire.
- **What the pages say is derived, never sent.** [`ShipTelemetrySource`](Assets/Game/Scripts/Gameplay/Terminal/ShipTelemetrySource.cs)
  reads the ship five times a second — the oxygen plant, the module rack's mask,
  `PlayerIdentity.All`, the storm field, the day clock — into a plain
  [`TelemetrySnapshot`](Assets/Game/Scripts/Gameplay/Terminal/ShipTelemetry.cs), and
  [`ShipTelemetry`](Assets/Game/Scripts/Gameplay/Terminal/ShipTelemetry.cs) composes the words. Every
  machine composes the same screen from the same replicated numbers.
- **The glass is measured, not authored.** [`ScreenPlane.Measure`](Assets/Game/Scripts/Gameplay/Terminal/ScreenPlane.cs)
  takes the plate's own triangles — largest face, flipped away from the housing — for centre, normal,
  up and size, because the plate's transform carries the FBX bake and the cabinet's lean.
  [`TerminalShot`](Assets/Game/Scripts/Gameplay/Terminal/TerminalShot.cs) then parks the lens along
  that normal where the glass fills 80 % of a 40° frame. No per-terminal numbers.
- **The screen is a world-space canvas 2 mm over the plate** ([`TerminalScreen`](Assets/Game/Scripts/Presentation/UI/World/Terminal/TerminalScreen.cs)),
  a millimetre-unit canvas at page size: every word stays crisp at any zoom and costs no render
  texture, and the plate's emissive green shows through the 94 % ink as a phosphor tint. Its
  `GraphicRaycaster` is on only while a session is open and its event camera is the focus camera
  for exactly that long, so the tabs are clickable by the operator and nobody else. The one thing
  on it that is not text or a flat panel is the SHIP page's viewport.
- **Deaf until the transmitter works.** `ShipTelemetry.TransmitterOnline` = a `Transmitter` socket in the rack's
  FITTED mask (never the burnt-out unit). Offline, `ShowsStatic` puts `NoCarrier` (glass, `TerminalStatic` snow,
  the line) over every page but SHIP and `TabCOMMS` is hidden; SHIP and its strip are the internal computer. COMMS
  ([ShipSignal.md](ShipSignal.md)) is a NEW page, not a replaced one: STATUS and GPS keep their jobs (the crew
  radar has no other home), and a tab arriving is itself the news.
- **The SHIP page is a live 3D drawing of the hull** ([ShipSchematic.md](ShipSchematic.md)); all this
  system owns of it is the viewport on the canvas and the Esc that backs it out.
- **One operator at a time.** `IContextualInteractable.CanInteract(interactor)` refuses while
  somebody else holds the claim ("In use"); it is released on every exit path, and by the server
  when the operator's client disconnects.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `TerminalConsole` | [Gameplay/Terminal/TerminalConsole.cs](Assets/Game/Scripts/Gameplay/Terminal/TerminalConsole.cs) | [`ClaimableConsole`](Assets/Game/Scripts/Gameplay/Interaction/Core/ClaimableConsole.cs) (the operator claim, shared with the satellite dish's lectern — [SatelliteDish.md](SatelliteDish.md)), `IInteractable`, `IInteractionReadout`. `NetworkVariable<int>` page; `RequestPage`, `Release`; server RPCs via `InteractorRelay`. `PageNames` is the page list. |
| `TerminalFocusSession` | [Gameplay/Terminal/TerminalFocusSession.cs](Assets/Game/Scripts/Gameplay/Terminal/TerminalFocusSession.cs) | Per-machine zoom-in: `GameplayMenuScope.Enter(freezeTime: false, hideHud: true)`, spawns the camera, wires the canvas's event camera, reads exits and 1-4 raw. Static `Active`, at most one. |
| `TerminalFocusCamera` | [Gameplay/Terminal/TerminalFocusCamera.cs](Assets/Game/Scripts/Gameplay/Terminal/TerminalFocusCamera.cs) | `FocusCamera` subclass; the shot from `TerminalShot` off a `ScreenAnchor` transform read live. `Shot` (FOV 40, fill 0.8, fly-in 0.35 s) serialized on the session. |
| `TerminalShot` / `ScreenPlane` | [TerminalShot.cs](Assets/Game/Scripts/Gameplay/Terminal/TerminalShot.cs), [ScreenPlane.cs](Assets/Game/Scripts/Gameplay/Terminal/ScreenPlane.cs) | Pure: lens distance/yaw/pitch for a plane; a plane from vertices and triangles. Tested. |
| `ShipTelemetry` / `TelemetrySnapshot` / `ShipTelemetrySource` | [ShipTelemetry.cs](Assets/Game/Scripts/Gameplay/Terminal/ShipTelemetry.cs), [ShipTelemetrySource.cs](Assets/Game/Scripts/Gameplay/Terminal/ShipTelemetrySource.cs) | Snapshot struct, page text and pip states (pure, tested); the reader on the fixture. |
| `TerminalScreen` | [Presentation/UI/World/Terminal/TerminalScreen.cs](Assets/Game/Scripts/Presentation/UI/World/Terminal/TerminalScreen.cs) | Tabs, pages, clock, cursor blink, the coloured subsystem strip, crew radar dots, COMMS text, the `NoCarrier` overlay and the hidden COMMS tab. `ShowPage` off the console's `PageChanged`; `Present(snapshot)`; `TryStepBack()` spends an Esc on the schematic. |
| `TerminalStatic` | [Presentation/UI/World/Terminal/TerminalStatic.cs](Assets/Game/Scripts/Presentation/UI/World/Terminal/TerminalStatic.cs) | The snow: one runtime noise texture (point, repeat), a new random `uvRect` window 24 times a second, a `rollBand` sliding down. No per-frame upload. |
| `ShipSchematicStage` / `ShipSchematicView` | [Presentation/UI/World/Terminal/](Assets/Game/Scripts/Presentation/UI/World/Terminal) | The SHIP page's 3D hull and its cursor — [ShipSchematic.md](ShipSchematic.md). |

## Flows

**Press.** `Interactor` hovers the box ("Terminal · RMB: use terminal") → `TerminalConsole.Interact`
on the presser's machine → `TerminalFocusSession.Enter`: refuses if any `GameplayMenuScope` owner is
up, takes the scope without freezing time, spawns `TerminalFocusCamera` from the player's eye,
enables the raycaster, points the canvas at the camera. The claim then goes to the server
(`Network.Execute` → `ClaimServerRpc` via `InteractorRelay`); the operator `NetworkVariable` lands
on every machine.

**Page.** A tab click or 1-4 → `TerminalConsole.RequestPage` → server clamps and writes the
`NetworkVariable` → every machine's `PageChanged` → `TerminalScreen.ShowPage` (the host takes the
same path). Showing the SHIP page starts its lens — [ShipSchematic.md](ShipSchematic.md).

**Leave.** Esc asks `TerminalScreen.TryStepBack()` first (it clears a selected schematic module);
only an Esc nothing spent closes the session. RMB / WASD / Space / gamepad B, death, or the
component disabling go straight to `TerminalFocusSession.Exit`: raycaster off, event camera
cleared, `Release` to the server, camera home (`FlyOut` 0.25 s), scope released.

## Multiplayer

| Path | Carrier | Authority |
| --- | --- | --- |
| Page, operator | `NetworkVariable<int>`, `NetworkVariable<ulong>` on the console, under the **ship's** NetworkObject | Server; requests via `[Rpc(SendTo.Server, InvokePermission = Everyone)]` + `InteractorRelay`. Offline/host collapses to the local mirrors |
| Zoom-in, cursor, key reads | none | Local to the presser by design |
| The SHIP page's modules and framing | `ShipPartRack`'s replicated mask; framing is local | See [ShipSchematic.md](ShipSchematic.md) |
| Page content | none — derived from state that is already replicated | — |

A late joiner receives both variables with the ship. The console has **no NetworkObject of its own**;
standing alone in a chunk it is unnetworked and every machine keeps its own page.

The two-process autotest ([Testing.md](Testing.md)) has a terminal step
([`AutotestRunner.Terminal.cs`](Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Terminal.cs)):
the client asks the replicated terminal for page 2, presses it and lets go; the host watches each land
(`CLIENT_TERMINAL_PAGE_SEEN == HOST_TERMINAL_PAGE == 2`, `HOST_TERMINAL_OCCUPIED` then `…RELEASED`).
`CLIENT_TERMINAL_SESSION_OPEN` may be false in a headless player — the zoom-in needs an eye to fly
from, and the claim is only sent once the session opened, so a false there explains a false
occupancy without indicting the wire.

## Persistence

Nothing. The page, the operator and the schematic's framing are session state, and every page is
derived — what the SHIP page shows is saved by the ship's rack. The prefab carries no saver;
`PlayerShipBuilder.StripNestedSavers` still runs on the nested instance in case that changes.

## Gotchas

- **The builders are gone: edit `StandingTerminal.prefab` itself** (COMMS was added in place, 2026-10-06).
  `PlayerShip.prefab` nests it overriding only the root pose, so edits propagate. Key 4 offline shows static.
- **The schematic has gotchas of its own** (a layer, a per-camera hide, model-space measurement,
  name-not-index sockets, and Esc's layering) — read [ShipSchematic.md](ShipSchematic.md) before
  touching the SHIP page.
- **Scale 1.0 in the ship, on purpose**, where every other fixture nests at `CrewFixtureScale` 1.7x:
  its glass leans back 24° to face an eye ABOVE it, and at 1.7x it would stand above the crew's
  2.45 m eye, lean facing away. Do not "fix" it.
- **Its placement is measured off the GEAR WALL, not off a deck**: `BuildStandingTerminal` takes the
  wall `BuildInventoryWall` returns, face on the wall's grid plane, aft edge `StandingTerminalWallGap`
  (0.60 m) forward of the wall's forward edge — forward, because aft of the wall is the bay doorway.
- **Its back is inside the hull skin on purpose**, in the fill the wall's back is tucked into: flush
  with that face, it is the deeper of the two. So `…StandsClearOfTheShipsFittings` excludes the
  ship's `Collision` child and guards only what a crew walks into.
- **A world-space canvas's tabs are only clickable through its event camera.** With
  `canvas.worldCamera` null `GraphicRaycaster` finds nothing; the session sets it to the focus camera and
  clears it on exit — leaving it on would let a free cursor click a terminal nobody is at.
- **The key strip left Blender with no material** and draws pink without the `Mat_Metal_Steel_Dark (DoubleSided)`
  the prefab gives it; a model re-import must keep that.
- **The exits are read raw** (`Keyboard.current`, `Mouse.current`), like the pack's: entering the
  scope disables `PlayerInputManager`, so no action fires. The entry frame is skipped, or the
  right-click that opened it would close it in the same gesture.
- **Only one `FocusCamera` may hold the eye** — the base class dismisses the incumbent. The session
  still refuses to open over any `GameplayMenuScope` owner (pack, body screen, pause, chat).

## Extending

1. **A new hull module** on the SHIP page: [ShipSchematic.md](ShipSchematic.md).
2. **A new page**: add its name to `TerminalConsole.PageNames` and bump `PageCount`; add its root
   and widgets to `StandingTerminal.prefab`; add the fields to `TerminalScreen` and fill
   them in `Present`; add a key in `TerminalFocusSession.PageKey`. Compose its text in `ShipTelemetry`.
3. **A new readout**: add the field to `TelemetrySnapshot`, read it in `ShipTelemetrySource.Read` from something already replicated, compose it in `ShipTelemetry`.
4. **Another screen prop** (a desk monitor, the scanner's wrist display — `ItemScannerScreenBuilder`,
   [Artifacts.md](Artifacts.md)): reuse `ScreenPlane`, the phosphor palette every screen shares and `TerminalScreen`; `TerminalConsole` only if shared.
5. **Verify on a client and after a reload**: the page must follow the operator on the other machine,
   "In use" must clear when they leave, and the fixture must stand where it was built after a load
   (the hull's record places it; nothing of its own is saved).
