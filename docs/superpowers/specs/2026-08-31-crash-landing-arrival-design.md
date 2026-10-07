# Crash Landing Arrival — Design

Status: approved 2026-08-31

## The moment

A world is created. Every player opens their eyes strapped into a seat aboard the
`PlayerShip`, looking out through the canopy. The ship is high in the atmosphere on a
curving descent. It buffets, it heats, the alarms start, the ground rushes up, it hits.
Black. They come to in the wreck, and the wreck is still there — it is the first landmark
of the world and their point of reference from then on.

Players can look around freely the whole way down. They cannot move, act, or skip.

## Decisions taken, and why

| Question | Decision |
| --- | --- |
| When does it fire? | Only on first entry to a **newly created world**. Never on load. |
| What happens to the ship? | The **wreck persists** as a landmark. Players get up out of the seats and walk out. |
| Camera | First person from the seat throughout. No external camera. |
| "Orbit" | The **ship's trajectory** curves — it is not a straight line. Not a camera orbit. |
| Player control | **Free look, locked body.** Mouse turns the head; movement, jump, hotbar and items are dead. |
| Skippable | **No.** Raised as a concern (see UX-0001 below); the call was made to keep it unskippable. |

## Architecture

The load-bearing discovery is that `PlayerShip.prefab` is **already** a networked,
persistent, flyable vehicle. It carries `MountModule`, `MountNetworkSync`, `MountStation`,
`SteerModule`, `HoverRigidbodyMotor`, `NetRelay`, `NetAuthority`, `ClientNetworkTransform`,
`UnderTerrainGuard`, `SandstormShelter`, and a `SaveableEntity` with `TransformSaveable`,
`RigidbodySaveable`, `ArticulatedPartsSaveable` and `MountSaveable`. It seats **four**: the four
`Cockpit_Seat_Command*` objects under `Model/` are the seat meshes, and the sit anchors are four
`SeatPoint` transforms — one under `Cockpit`, three under `PassengerSeat1/2/3` — whose world
positions match those meshes one-to-one. It also has a `Mesh_CanopyDome` to look out of. It is registered in
`Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`.

So the ship is a real `NetworkObject`, server-spawned at altitude, driven down a scripted
arc by the server, replicated by its existing `ClientNetworkTransform`. Players are
parented into its seats. Nothing is faked; everyone rides the same hull and crewmates are
visible in the cabin for free.

An earlier draft used a local, non-networked presentation ship on a closed-form arc
evaluated per machine. That was rejected once the prefab was understood: it would have
rebuilt transport that already exists, and — decisively — players in a shared cabin must
see each other, which a per-machine local ship cannot deliver without also inventing local
stand-in avatars.

### Components

| Component | Responsibility | Depends on |
| --- | --- | --- |
| `ArrivalTrajectory` | Pure function from normalised time to a pose. No Unity state. | nothing |
| `ArrivalDirector` | Server-only sequence: resolve impact site, spawn ship, seat everyone, fly the arc, impact, release. | `IWorldService`, `SpawnManager`, `ShipGrounding`, `SeatOrdering` |
| `SeatOrdering` | Pure: stable seat ordering and wrap-around assignment, over plain ints. | nothing |
| `SeatedRider` | Attaches/detaches player bodies to seat transforms; replicates that. | `NetMsg`, `NetworkVariable` |
| `ArrivalCutscene` | Client presentation: letterbox, input lock, beats, impact flash. | `Cutscene`, `CutsceneDirector`, `ArrivalCameraRig` |
| `ArrivalCameraRig` | Seated free look plus shake, in one `LateUpdate`. | `ShakeMath`, `GameSettings` |
| `ShakeMath` | Pure: capped, decaying shake displacement. | nothing |
| `ArrivalSaveable` | The "arrival already happened" world flag. | `ISaveable` |

Each is separately understandable, and the two with real logic in them —
`ArrivalTrajectory` and `SeatOrdering` — are pure and unit-testable without a scene.

## The descent

`ArrivalTrajectory` is closed-form. Given a normalised time in the zero-to-one range, a
start altitude, an impact pose and a lateral budget, it returns a position and a banked
rotation. Closed-form rather than integrated so it is reproducible and testable, and so the
terminal pose is exactly the requested impact pose rather than wherever integration
happened to land.

The server evaluates it and writes the ship transform each frame; clients receive it over
the existing `ClientNetworkTransform`. There is no cross-machine agreement problem to
engineer, because there is exactly one machine deciding.

### The streaming constraint

Chunks are 500 m across a 4000x3000 m grid and pin under tracked entities. A descent that
traversed the map would drag the streamer across a dozen chunks at speed. So the arc is
**high and curving but laterally modest** — a banked descent covering a few hundred metres.
The lateral budget is a serialized tunable with this reasoning recorded on it, not a magic
number.

### Altitude, not orbit

This is a high-atmosphere descent starting where the existing `DesertSkybox`, volumetric
clouds and sandstorm still read correctly (roughly 1500-3000 m). Seeing the planet as a
curved sphere from true orbit is a separate art problem — new skybox, planet mesh,
atmosphere shader — and is out of scope.

### The impact site

Resolved from the world's existing spawn anchor via `SpawnManager.TryGetSpawnAnchor`, then
ground-probed with `ShipGrounding.TryResolvePose` (reused from the Versus code). The wreck
therefore lands on ground that is guaranteed loaded and valid, and exactly where players
would otherwise have spawned — so the world's authored content still makes sense around it.

## Seating

`SeatedRider` is modelled directly on `MountNetworkSync`, which documents why one channel
is not enough:

- **Event channel** — a new `NetMsg` pair. Server to everyone: "player X takes seat N on
  this ship." Peers apply it and the body is held at that seat.

**Correction, found in play (2026-08-31):** an earlier version of this spec said "parented into
the seat", and that throws `InvalidParentException` — netcode will not put a spawned
`NetworkObject` under a plain transform. It also turned out to be unnecessary. The player's
`NetworkTransform` is owner-authoritative and world-space, so parenting is not what carries a
rider: the owner's world position is what travels, and the server cannot place a client's body at
all. `SeatedRider` therefore reparents **nothing** — each machine writes its own players to their
seat's world pose every frame. The same fact means the cutscene cannot be started from the
server's descent coroutine; it is raised per machine by `SeatedRider.LocalPlayerSeated`.
- **State channel** — a `NetworkVariable` list of seat occupants, re-asserted, because
  NetworkVariable change events never replay. This is the late-joiner case and it is real:
  a client can connect while the ship is mid-descent. Without state they would spawn
  standing on the ground watching their crew fall out of the sky.

Seat assignment is server-decided. `SeatOrdering` reuses the ordering already proven
in `VersusShipSpawner.Seats.cs`: sort by `ShipSeat.Order`, stable insertion sort so ties
keep hierarchy order, wrap when there are more players than seats.

`SeatedRider` is the **attach/detach half only**. It deliberately does not do steering, the
mount camera, dismount placement, or collision-ignore pairs. `MountModule` is 400+ lines
entangled with all of those, and every vehicle and creature in the game depends on it;
making it N-rider to serve one cutscene is how the ostrich, the crawler and the foil sailer
break at once.

## Client presentation

`ArrivalCutscene` subclasses the existing `Cutscene` and runs under `CutsceneDirector`, so
it inherits letterbox bars, HUD hiding, and the restore-on-exception guarantee in
`RunCutscene`.

**Free look needs no change to `PlayerController` or `PlayerInputManager` at all.** An
earlier draft proposed a `keepLook` overload on `EnterCutsceneMode`. That does not work:
`PlayerLook` reads `LookInput`, which `PlayerInputManager` writes in `Update` and zeroes in
`OnDisable`, so leaving look "enabled" while the input component is disabled yields a
permanently zero look axis — and leaving the input component *enabled* would let jump and
dash through, since those are delivered as events whose handlers fire regardless of
`PlayerMovement.enabled`.

`MountModule.Camera.cs` already solved this exact problem. It reads the raw action —
`InputSystem.actions.FindAction("Look")` — and force-enables it, bypassing
`PlayerInputManager` entirely, precisely because mounting also runs with the player's input
switched off. The arrival camera mirrors that. This is strictly better than the overload:
no existing shared class is touched, and it sidesteps `PlayerLook`'s yaw, which turns the
player's **Rigidbody** in `FixedUpdate` and would fight the seat it is parented into.

So the only new presentation piece is **`ArrivalCameraRig`** — one component added to the
player camera for the duration of the cutscene. In a single `LateUpdate` it reads the look
action, accumulates clamped yaw/pitch relative to the seat, and adds the shake offset.
One component rather than a separate look component and shake component because two
`LateUpdate`s writing the same transform is an ordering bug waiting to happen.

The shake maths itself lives in **`ShakeMath`**, a pure static, so the cap and the
zero-intensity guarantee are testable without a scene.

### Beats

| Beat | What happens |
| --- | --- |
| Wake | Fade from black, already seated. Bars in. |
| Entry buffet | Light shake, rising. |
| Heat | Atmosphere glow builds past the canopy. |
| Alarms | Audio, cabin warning lights. |
| Ground rush | Shake climbs to its cap. |
| Impact | Hard shake burst, white flash, fade to black. |
| Come to | Fade in, seated in the stationary wreck. Release. |

### Screen shake, per GDC-L1-FEEL-0006

The principle is `contextual`, confidence 4, and its implementation clause is explicit:
scale amplitude and duration to the event, decay quickly, **cap the maximum so no
combination stacks into an unreadable frame**, and provide an intensity setting including
off. The corpus notes the player-control-and-cap clause is "the part practitioners most
often skip and later regret".

That applies with force here because the shake runs for multiple seconds and the player
cannot skip it, which is a genuine motion-sickness and vestibular-accessibility exposure
rather than a polish detail. So `ShakeMath` takes an intensity in the zero-to-one range,
caps total amplitude, and multiplies by a new `GameSettings.CameraShakeIntensity` that
reaches zero. `GameSettings` is the right home — static, PlayerPrefs-backed, with a
`Changed` event and a `SchemaVersion` for re-seeding defaults.

### Skippability, per GDC-L1-UX-0001

The principle warns against the front-loaded unskippable wall. It is `contextual` and the
corpus records genuine disagreement (guided just-in-time vs. upfront vs. discovery-first),
so it is not a veto — and its core target is *teaching*, which this cutscene does not do.
A skip was recommended anyway, primarily for iteration cost during development. **The
decision was unskippable**, and that is recorded here as a deliberate choice rather than an
oversight. The shake slider above is the remaining accessibility mitigation and is
therefore not optional.

## Persistence

- **Arrival flag** — `ArrivalSaveable` implementing `ISaveable`, on the `ArrivalDirector` in
  `persistentScene`, following `NpcWorldSaveable`'s "one saver for a whole subsystem" shape.
  Its `SaveKey` is `arrival`. A save taken mid-descent restores as **done**: replaying a
  crash on somebody who already landed is worse than skipping it for somebody who did not
  finish. A null state means defaults — i.e. not yet arrived — as `ISaveable` requires.
- **The wreck** — free. `PlayerShip` already carries `SaveableEntity` with
  `TransformSaveable` and `RigidbodySaveable`, so the hull's final pose persists with no new
  code, and it is already in the network prefab list so it spawns for clients too.
- **Seated players** — not persisted. Seating is released before the arrival is marked done,
  so there is no state to restore; a save can never contain a player parented to the ship by
  this system.

## Failure handling

Every failure is loud, and the sequence either completes or says why not. No silent
fallback, per the project's "no empty or silent catch" rule.

| Failure | Response |
| --- | --- |
| Impact site will not resolve (terrain not streamed) | Retry each frame up to a serialized timeout, then log an error, place players at the ordinary spawn point, mark arrival done. |
| Ship prefab missing or unregistered | Log an error, skip arrival entirely, ordinary spawn. |
| Ship has no `ShipSeat` markers | Log a warning and fall back to the seat anchors already on the prefab; if there are none, log an error and skip arrival. |
| A player disconnects mid-descent | Their seat is released; the descent continues for everybody else. |
| A player joins mid-descent | Seated from the state channel and joins the cutscene in progress. |
| Cutscene throws | `CutsceneDirector.RunCutscene` already catches, logs and restores input. Arrival is still marked done by the director, not the cutscene. |

## Testing

EditMode, no scene required, because the two components with real logic are pure:

**`ArrivalTrajectory`**
- Time zero is at the configured start altitude.
- Time one is exactly the requested impact pose, position and rotation.
- Altitude decreases monotonically across the whole arc.
- Lateral displacement never exceeds the lateral budget.
- The path is not a straight line — mid-arc position is off the start-to-impact segment.
- Banking is zero at the terminal pose, so the wreck does not land on its side.

**`SeatOrdering`**
- Seats come back ordered by `Order`.
- Ties keep hierarchy order (the stable-sort guarantee).
- More players than seats wraps rather than dropping anyone.
- Zero seats is refused, not silently mishandled.

**`ShakeMath`**
- Amplitude never exceeds the cap at any input intensity, including out-of-range input.
- A zero settings scale produces exactly zero displacement.
- Displacement is continuous in time (no jump between adjacent samples).

This ordering is deliberate: per the net gun post-mortem, five silent defects shipped and
three of them were in the one class that had no tests. The pure logic gets tests before the
Unity wiring.

## Out of scope

- True orbital view (planet as a sphere, space skybox).
- Making `MountModule` multi-rider.
- Replaying the arrival on demand, or a "watch it again" option.
- Interior ship damage states, fire, or a repairable hull.
- Any cutscene other than this one becoming replicated. `SeatedRider` is reusable, but this
  work does not generalise `CutsceneDirector` to the network.
