---
system: WorkshopCrane
layer: world
summary: "The workshop hoist driven from a third-floor console: one load, the vault's core, lowered onto the ship"
paths:
  - Assets/Game/Scripts/World/Workshop/WorkshopCrane.cs
  - Assets/Game/Scripts/World/Workshop/WorkshopCraneReach.cs
  - Assets/Game/Scripts/World/Workshop/WorkshopCraneConsole.cs
  - Assets/Game/Scripts/World/Workshop/WorkshopCraneHook.cs
  - Assets/Game/Scripts/World/Workshop/WorkshopCraneMountGhost.cs
  - Assets/Game/Scripts/World/Workshop/WorkshopCraneCamera.cs
  - Assets/Game/Editor/Workshop/WorkshopCraneBuilder.cs
  - Assets/Game/Editor/Tests/WorkshopCraneTests.cs
  - Assets/Game/Prefabs/Items/Workshop/VaultReactorCore.prefab
  - Assets/Game/Resources/Items/Workshop/VaultReactorCore.asset
symptoms:
  - "the crane console says: Step up to the crane console"
  - "the hoist will not come any closer to the balcony"
  - "the hook stops in mid-air and will not go down any further"
  - "I cannot hang the core on the hook / the hook says nothing when I look at it"
  - "the grab lever says: Nothing under the hook to take hold of"
  - "I let go of the core over the ship and it fell on the floor instead"
  - "my gun fires when I pull the crane's grab lever"
  - "the hoist runs its round again after loading the world"
  - "the core hanging on the hook is gone after loading the world"
  - "it is hard to see where the hook is from the console"
  - "the core lands next to the ship's mount instead of on it"
reads_with: [Workshop, WorkshopMission, Vehicles, PlayerShip, Multiplayer, Persistence]
updated: 2026-10-12
---

# WorkshopCrane

The main room's hoist ([Workshop.md](Workshop.md)) runs its round by itself until somebody takes its **control
console** on a balcony of the third floor (the Ring1 gallery, z 0.63 in the model, east side). From then on it is
driven, for good, and stands wherever it was left. It carries one load: the sealed reactor core from the Shellbacks'
vault ([WorkshopMission.md](WorkshopMission.md)). The player brings the hook alongside the balcony's loading gap, hangs
the core on it, steers it over their ship parked in the pit and lets go over the empty reactor mount, which fits it.

## Model

| Idea | Mechanism |
|---|---|
| Three numbers place the hoist | `WorkshopCranePose`: bridge and trolley in metres along their runs from the model, and `height`: below 0 the hook is let down with the gantry at the bottom of its towers, above 0 the hook is right up and the gantry has climbed. One lever works both, the way a driver would. `WorkshopMachinery` turns it into the four hoist channels (`HookDropOf`, `LiftOf`) |
| How far it runs | `WorkshopCraneReach`, from the export's measurement (`layout.hoist.bridgeTravel` / `trolleyTravel`: where the bridge's trucks meet the tower carriages, and the trolley meets the bridge's trucks, less 0.15 m): bridge -5.0..19.0 m, trolley -0.5..18.5 m, hook 12.1 m down, lift 4.5 m up |
| Driving | `WorkshopCrane.Drive(command, dt)`: each axis ramps to `travelSpeed` 2.4 / `liftSpeed` 1.6 m/s over `rampSeconds`, one axis at a time, and a step is refused when the hook block's box (and the load's) would overlap anything but the hoist's own parts and agents (`blockers`: all but Player, Ignore Raycast, UI, Hologram, PackItem, Schematic). So the hook stops on what is under it and a load slides along a wall; a box already stuck may still go up |
| The round stops for good | The first claim on the console calls `TakeOver` on every machine: it starts from where the round has the hoist on the shared clock, so nothing jumps |
| The console | `WorkshopCraneConsole`, a `VehicleStation` (one driver). RMB takes it; RMB again or Escape lets go (Escape gated on `GameplayMenuScope.AcceptsGameplayInput`, as on mounts; pause is M, so nothing else wants it); `StationFooting` plants the operator at `CraneConsole_Stance`. WASD runs the hook the way the crane camera faces (projected onto the runs, `leverDeadzone`), Space (Jump) up, Shift (Sprint) down, LMB the grab lever. The levers ride the claim's request packed base 3 (`WorkshopCraneCommand`) |
| One load | `WorkshopCraneHook`, a trigger round the hook block: RMB with the core in the hands within `hangReach` hangs it. Or the grab lever takes a loose core within `grabRadius` of the hook. Let go: over an empty socket of `installs` (`ReactorCore`) on any `ShipPartRack` within `snapRadius` of the load's middle it is fitted (`TryInstall`, event `Installed`); anywhere else it drops as a saved pickup |
| Where it goes, before the press (GDC-L1-UX-0003) | While the core hangs, the operator's machine paints every empty reactor mount red and the one a let-go would fit green (`WorkshopCraneMountGhost`, the sockets' own ghosts) |
| It snaps onto the ship | Within `snapRadius` (3 m) of an empty mount the drawn core swings onto it in `snapSeconds`, its biggest mesh laid over the mount's own (`ShipPartSocket.Part`: the module's mesh is the hull's, exported again), and back under the hook out of reach. Drawing only, on every machine: what it bumps into and what a let-go fits stay the hook's. Let go there and it is fitted, so the picture does not move |
| The crane's own camera | `WorkshopCraneCamera` (on the console, local only): taking the console turns the operator's head camera and listener off and spawns a camera from the project's third-person prefab (`ThirdPersonCameras.Spawn`, shared with `MountModule`) that **does not move**: `back` 2 m behind and `rise` 10 m over the stance, looking at the middle of the main room's floor (the `Workshop` root's origin), `fieldOfView` 60, visor glass off. From there the whole pit floor, the balcony and the operator are in frame (checked by a preview-scene render of `Workshop.prefab`, 2026-10-12). The mouse does nothing. WASD runs the hook the way this view faces. Letting go of the console gives the head back |
| The balcony | `Cave_CraneStation` in the model: deck flush with the gallery from y 43.45 to 48.65, tip at x 13.4, railings with a 2 m loading gap (`Crane_LoadingSpot`), the console (`CraneConsole_Panel`). The gallery rail is cut there by a Boolean modifier on `Cube.055` (`Cutter_CraneBalcony_RailGap`) |

## Key types

| Type | Role |
|---|---|
| `WorkshopCrane` | `TakeOver`, `Drive`, `HookAt`, `RequestLatch`, `RequestHang`, `TryFindMount`, `TryFindLooseLoad`, `Restore`, `IsDriven`, `IsLoaded`, `DrivenHere`, `Installed`, `Changed` |
| `WorkshopCraneReach` / `WorkshopCranePose` / `WorkshopCraneCommand` | pure: reach and clamping, the pose and its wire form, the levers and their packing |
| `WorkshopCraneConsole` / `WorkshopCraneHook` / `WorkshopCraneMountGhost` / `WorkshopCraneCamera` | the console station, the hang-it-here trigger, the operator's ghosts, the operator's still camera |
| `WorkshopCraneBuilder` (editor) | in Rebuild: the crane (hook point 0.35 m above the hook block's tip, hook box from its meshes), `LoadPoint` and `HangHere` on the hook, the console and stance from the marks, and the `VaultReactorCore` item (the ship's `reactorcore.fbx` at true size, made once and kept) |

## Flows

- **Driving.** RMB on the console → `RequestClaim` → `OnManned` everywhere (`TakeOver`; the operator's machine plants
  them, `DrivenHere`) → the levers in the claim's request → server `AdvanceOnServer` → `Drive` → `WorkshopCraneState`
  at `publishSeconds` while it moves. The operator's machine drives its own copy at once and ignores the echoes;
  everyone else eases to them at `followSpeed`.
- **The core onto the ship.** Hook beside the loading gap at its highest (the gantry up its towers) → RMB on the hook
  with the core in the hands → server: in reach, out of the hotbar, loaded → drive it over the ship → LMB → fitted, or
  dropped.

## Multiplayer

On the building's channel. The console's claim is `StationClaim` 65 / `StationState` 66. `WorkshopCraneOperate` 133
(player → server): A = -1 ask, 1 grab lever, 2 hang; Target = the player, checked with `MayActFor`.
`WorkshopCraneState` 134 (server → all): P = the pose, B = driven, loaded, land in it, just fitted. A late joiner asks once
spawned. **Not run on a client.** A workshop with no networked wrapper runs its crane alone on each machine.

## Persistence

In `WorkshopSaveable` (key `"workshop"`), field `crane` once it has been driven: `bridge`, `trolley`, `height`, `loaded`.
Absent, it runs its round. The core on the hook is in no hotbar and on no ground, so `loaded` is its only record.
Round trip in `WorkshopCraneTests`; **not run through a save file and a reload.**

## Gotchas

- **The hook block is wider than it is deep** (its crosshead is ±1.25 m across the bridge): at the bridge's east end it
  reaches over the balcony's tip rail, and only the overlap test stops it. Line the trolley up with the loading gap.
- **The reach is the runs', not the room's.** The bridge stops at x ±12 (Blender) and the trolley's south end is short.
  The balcony's tip is placed where the hook can come alongside: moving the balcony means checking the gap is still in reach.
- **Use is the grab lever**, so `StationFooting.LocalPlanted` keeps a held item from firing while anyone is planted at a
  station ([Vehicles.md](Vehicles.md)). Before it, LMB on a hand cog also fired the weapon in the hands.
- **The core is not a `ShipPartItem`**: only the hoist fits it. It fills a `ReactorCore` socket like the module does.
- **A new .cs can be left out of Unity's compile list** (`Library/Bee/.../Assembly-CSharp.rsp`) while every other file
  compiles: `WorkshopCraneView.cs` was, and the console failed with CS0246 in Unity while the terminal type-check, which
  lists files from disk, passed. Renaming the file (now `WorkshopCraneCamera.cs`) got it picked up.
- **The snap lines meshes up, not bounds**: it assumes the item's model and the hull's part share a local frame and
  scale. If a re-export of either breaks that, the snapped core sits rotated or off the mount; the fit itself is unaffected.
- **The still camera stands where it was framed.** Its pose is worked out from the stance, so moving the balcony or
  its stance mark moves the view with it; a new wall, gallery or tier above the balcony may stand in front of it. Render
  the prefab from the pose (preview scene) after such a change.
- **Verified 2026-10-11:** compiles; Rebuild logs `crane: bridge -5..19 m, trolley -0.5..18.5 m, hook 12.1 m down, lift
  4.5 m up`; `WorkshopCraneTests` green (the lever shared by hook and lift, every lever position over the wire, driving
  stops at the run's ends, an undriven hoist is not written, a driven one comes back). **Not run:** Play, a person at the
  console, the hook at the balcony, a let-go over a real ship, a client, a save file.

## Extending

- **Another load:** today one item (`load`) fitting one kind (`installs`). A second needs a list of loads with the kind
  each fits, and the wire's loaded bit becomes an index.
- **Faster, slower, softer:** `travelSpeed`, `liftSpeed`, `rampSeconds`, `snapRadius`, `snapSeconds`, `grabRadius`, `hangReach`, the camera's `view.back`/`rise`/`fieldOfView` on
  `Workshop/Crane`; change their defaults in code, since Rebuild makes a fresh component.
