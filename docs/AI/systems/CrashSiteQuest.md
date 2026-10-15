---
system: CrashSiteQuest
layer: presentation
summary: "The story quest after the crash: controls, oxygen, terminal, module, artifact, transmitter, signal, repair"
paths:
  - Assets/Game/Scripts/Gameplay/Objectives/Steps/
  - Assets/Game/Scripts/Gameplay/Objectives/CrewGear.cs
  - Assets/Game/ScriptableObjects/Objectives/CrashSiteChain.asset
  - Assets/Game/ScriptableObjects/Objectives/Steps/
symptoms:
  - "the first objective completes on its own while the ship is still falling"
  - "the first objective completes before I have pressed any keys"
  - "the controls checklist is stuck on waiting for the rest of the crew"
  - "the controls checklist does not tick while I am still strapped into the seat"
  - "a second belly turbine appears by the wreck after loading the world"
reads_with: [Objectives, PlayerShip, Oxygen, ShipSignal, ShipTransmitterFire, SatelliteDish]
updated: 2026-10-14
---

# CrashSiteQuest

The story quest (`CrashSiteChain`, id `crash-site`), begun the moment the hull lands: an ordered chain of steps the whole crew works through after the crash —
try the basic controls, carry the oxygen plant the crash threw out back into the ship and solder its cracks, fill a bottle
and seat it before the suit reserve runs out, read the damage at the terminal, recover and fit a module, try an artifact,
get the long-range transmitter working, answer the signal it hears, repair the ship. The lander's computer briefs each step; the visor names it, lists its status and
points at it.

**Scope:** the story quest's steps and what each asks. The quest machinery — director, quest log, network, save,
briefing, visor — is [Objectives](Objectives.md).

## Model

- **One asset per step under** `Assets/Game/ScriptableObjects/Objectives/Steps/`, ordered by `CrashSiteChain.asset`:
  `learn-controls` → `recover-oxygen-plant` → `refill-air` → `assess-damage` → `first-module` → `first-artifact` →
  `fit-transmitter` → `answer-signal` → `repair-ship`. Each step's class (under `Gameplay/Objectives/Steps/`) is its rule.
- **Taught by doing** (`GDC-L1-UX-0001`, `GDC-L1-UX-0003`): the controls checklist confirms each line on use; the
  lander briefs each step and the visor waypoint and beacon point at it ([Objectives](Objectives.md)).

## Key types

| Type | Role |
| --- | --- |
| `LearnControlsStep` | Checklist of `lessons` (move, sprint, jump, crouch) in `Status`; only counts input while out of the seat and every cutscene; met when every player has finished |
| `RecoverOxygenPlantStep` | Second step (`recover-oxygen-plant`). Met by `OxygenPlantMount.Running` (in its mount, soldered, powered). Status: `Thrown out of the back door. Lift it by the handle` / `Carrying: n m to its mount` / `Damaged: solder the seams k/3` / `Fit a power cell`. Waypoint and beacon: the loose plant, the mount while it is carried; cracked, the gear wall's torch while no crew member carries one (`CrewGear`), then the glowing seam; the mount for the cell; focus: the burst door. **Remark 0** (2026-10-06): the torch tutorial ("on the gear wall… RMB… hold LMB on the glowing seam"), due the first time the plant stands in its mount cracked |
| `RefillAirStep` | Third step (`refill-air`, 2026-10-06, `09_RefillAir.asset`). The crash vented every crew bottle (Oxygen.md): take an empty bottle off the gear wall (RMB), fill it at the plant's collar, press **B** (the `Backpack` action) and seat it. Status walks THIS machine's player through it from their hotbar, pack and socket (take / fill / seat / done); waypoint: the gear wall, then the plant. **Met when ANY crew member has a filled bottle seated** (`OxygenPlantMount.AirRefilled`), so a late joiner, an idle or a dead player never blocks it. Remark 0: "reserve running low", due when any suit on reserve is under 35 % |
| `UseTerminalStep` | The hull's `TerminalConsole.Occupied`; focus = first empty socket |
| `RecoverModuleStep` | Drops `SmallMotor` 90–130 m out; met by any socket fitted beyond `AuthoredMask`; waypoint = loose module, else the empty socket |
| `TryArtifactStep` | Drops `JumpingRod` 22 m off the nose; met on `UseChannel.UsedOnServer` for that asset |
| `FitTransmitterStep` | `fit-transmitter`, after `first-artifact`. Met by a WORKING transmitter in the rack (`ShipSignal.IsTransmitterFitted`). Status from the socket and the fire. **One marker per phase**: the socket while broken; the transmitter itself in its cradle while it is on the dish (the only marker on the tower: nothing for the hook, ladder or console); then the loose transmitter, else the empty socket. Beacon only on a loose transmitter. Remarks: `TowerRemark` (a crew member within 60 m of the dish: "I wonder if we can turn it…") and `HookRemark` (the drives wake: "Press I … then Q or E", the real `BodyInventory`/`GauntletLeft`/`GauntletRight` keys, pinned by a test) |
| `AnswerSignalStep` | `answer-signal`, right after it. Begins once `ShipSignal.Destination.Received`; waypoint + beacon on the destination; met when any crew member is within its radius + `arrivalMargin` (15 m); met at once if the signal leads nowhere. See [ShipSignal.md](ShipSignal.md) |
| `RepairShipStep` | `ShipPartRack.IsComplete`; `Status` = `Modules fitted n/total` |

## Flows

N/A — every step runs through the quest flows in [Objectives](Objectives.md): begun once (`TryBegin`), judged every
frame (`IsMet`) on the server, presented everywhere.

## Multiplayer

| What | Carrier | Authority |
| --- | --- | --- |
| Placed module / artifact | Registered item prefabs via `World.Spawn` | Server |
| Artifact use | `UseChannel.UsedOnServer`, raised in `OnUseRequested` after the peer broadcast | Server |

A player who joins during the controls step must do it too; one who leaves stops counting.

## Persistence

Saved as the story quest of `objectives` ([Objectives](Objectives.md)). **Inserting a step is safe** because the
record is the id: a save at `assess-damage` or later never sees the oxygen step; a save still at `learn-controls`
moves on into it, and since that world never had its plant thrown out (no `oxygenMount` record), the step asks only
for a power cell.

## Gotchas

- **Input in the seat proves nothing.** `LearnControlsStep` ignores input while
  `SeatedRider.LocalPlayerMayLeave` or a cutscene plays; the seat's own Esc prompt gets the player up.
- **A per-player step needs every player.** One AFK player holds the controls step for the whole
  crew; that is the price of every player being taught. The checklist tells a finished player they
  are waiting on the others.
- **Ship modules exist in no real world scene** (only `Ferdinand_Test_world`). The first one a player
  ever sees is the one `RecoverModuleStep` drops.
- **A loose item is only found while its chunk is loaded**, so a module dropped 600 m+ from the hull
  drops off the waypoint until someone walks near it.
- **The chain order is the transmitter, then the signal, then the whole repair** (`fit-transmitter`,
  `answer-signal`, `repair-ship`), because the signal must start when the transmitter goes in and that is
  usually long before the hull is complete. A crew that fits the transmitter early meets `fit-transmitter` the
  moment they reach it and walk straight into `answer-signal`; the COMMS page and the map marker do not wait
  for the chain at all. **A save already at `repair-ship` resolves past both new steps** (by id) and keeps its
  place: it never gets the signal objective, though its terminal and map still show the signal.
- **Finishing the ship does not win the game** — `GameManager.WinGame` still has no caller.

## Extending

- **A lead to a settlement** is built: `AnswerSignalStep`. The town is chosen by the SHIP, not the step
  (`ShipSignal`, from the baked `WorldSiteCatalog.towns`), and saved and replicated with the hull, so the
  step stays stateless and `ObjectiveProgress` did not grow. A second lead reuses `SignalDestinationRule`.
- **Another step in the story:** the `spacegame-objectives` skill; add the asset to `CrashSiteChain.asset` where it
  belongs — saves resolve by id, so inserting is safe.
