# Versus ship spawn points

**Date:** 2026-08-31
**Branch:** movement-and-perspective
**Status:** implemented

## What changed during implementation

Three things came out differently from the design above; the design text is left as written and
these are the corrections.

1. **`VersusShipSpawner` is a plain `MonoBehaviour`, not a `NetworkBehaviour`.** Reviewing it once
   written, it used no netcode feature at all — no RPCs, no NetworkVariables. Its ships are
   networked by `IWorldService`, and its only caller already runs server-side. Being a
   `NetworkBehaviour` would have cost a scene-placed `NetworkObject`, whose id has to survive
   authoring into a scene to work.
2. **Shipped as a prefab** at `Assets/Game/Prefabs/Systems/VersusShipSpawner.prefab`, pre-wired to
   `PlayerShip` and the config asset, and placed in `persistentScene` under `Managers` beside
   `SpawnManager` and `NetworkGameManager`.
3. **`PlayerShip` was already registered** in the network prefab list. The design flagged this as
   work; it was not. A name-grep of the list returns nothing because the list stores GUIDs, which
   is worth knowing before concluding a prefab is unregistered.

## Scene wiring

`persistentScene` gains one object: `Managers/VersusShipSpawner`, a linked prefab instance. The
change is purely additive — 63 inserted lines, nothing deleted, still 15 root objects — and the
scene was clean at HEAD beforehand, so `git checkout` on that one file reverses it exactly.

It is inert outside a versus match: `NetworkGameManager` takes the versus branch only when
`VersusSession.IsActive` *and* a spawner instance exists, so story worlds behave exactly as before.

Verified by reopening the scene from disk rather than trusting `SaveScene`'s return value: the
component survives the round trip, both serialized references resolve, and the prefab link is
intact.

## Launch wiring (added after the first pass)

The first pass built the spawner on a switch nothing flipped: **nothing in the project ever called
`VersusSession.Begin`**, so `IsActive` was permanently false, the versus branch never ran, and every
player — both teams — spawned together on the world's only `SpawnPoint`, inside ShipRV's cargo bay.

Four pieces close it:

| Piece | Where |
| --- | --- |
| `LobbySession.Existing` — the session if there is one, without conjuring one | `Lobby/LobbySession.cs` |
| `LobbySession.LocalTeam` — this peer's chosen side, read by lobby slot | same |
| `AdoptVersusSessionFromLobby()` — every peer derives the match from the lobby in `OnNetworkSpawn` | `NetworkGameManager.Versus.cs` |
| `ReportVersusTeamServerRpc` + `WaitForVersusTeam` — the client tells the server its side before it has a body | same |

The versus spawn flow moved into `NetworkGameManager.Versus.cs`, matching the existing
`NetworkGameManager.Profiles.cs` split.

**The team is derived from the lobby, not staged by the screen that starts the match**, so host and
clients compute it from one source and cannot disagree. It clears as readily as it begins — a peer
who played a match and then loaded a story world would otherwise still be carrying an active
session and be sent looking for a team ship in a world that has none.

**A chosen team outranks the balancer.** `VersusTeamRoster.Claim` records what a player picked;
round-robin is only the fallback for someone who never reported. Without that, a party that queued
together gets split across opposing ships — silently.

The report copies the profile handshake beside it: connection approval is off in this project, so
the answer arrives on the scene object's own channel, is bounds-checked rather than believed, and
is waited for with a timeout so a client that never reports still spawns.

## Remaining work

Real seats. `PlayerShipBuilder` does not add `ShipSeat` markers yet, so every ship falls back to
the stand-in ring and logs a warning saying so. Adding the markers is the whole job — the spawner
picks them up with no code change.

## The ask

Define where each team's ship starts in VS mode. Spawn points must be simple to author,
definable at runtime, and raycast down so the ship lands on the ground. Every player spawns
inside their own team's ship — eventually in a seat, though seats are not defined yet.

## What already exists

Two systems carry team semantics and neither one spawns anything.

- **`MatchManager`** (`Gameplay/Minigame/Runtime/`) is the arena deathmatch orchestrator. It
  collects `SpawnPoint` components, splits them between two teams with
  `TeamAssignment.SplitEvenly`, and moves players via a `TeleportRpc` of its own. It is
  code-only: its GUID appears in no scene or prefab, and `MinigameArena.unity` is empty.
- **`VersusSession`** (`Gameplay/Versus/Core/`) is the newer VS lobby handoff — a static
  carrying `TeamCount`, `TeamSize`, `LocalTeam` and team colours across the scene load.
  Nothing in gameplay reads it to place anyone.

Supporting pieces this design builds on rather than reinventing:

| Piece | Path | Why it matters |
| --- | --- | --- |
| `TerrainProbe.TryGetTerrainHeight` | `World/Safety/Core/` | Heightmap query that cannot be shadowed by a hull or roof, unlike a raycast |
| `NetworkedTeleport.Move` | `Core/Multiplayer/Authority/` | The one correct way to place a player, whose `NetworkTransform` is owner-authoritative |
| `IWorldService.Spawn` | `Core/GameServices/` | Server-only networked instantiate |
| `NetworkGameManager.SpawnWhenReady` | `Core/Multiplayer/Joining/` | The per-client spawn coroutine, already branching on saved-position restore |
| `SpawnPoint` | `Gameplay/Game/Spawning/` | The "answering *not yet* is legitimate" contract this design copies |

### Gap found during exploration

`PlayerIdentity` replicates a display name and a suit colour. It has **no team field**. Its
own class comment says other players' teams "arrive over the wire on `PlayerIdentity`", but
that is aspirational — it is not implemented. So there is currently no way for the server to
know which team a remote player is on, and "spawn in your team's ship" cannot work without
adding it. See *Scope note* below.

## Design

### Authoring: one ScriptableObject, two layouts

`VersusShipSpawnConfig`, a `[CreateAssetMenu]` asset at
`Assets/Game/ScriptableObjects/Versus/VersusShipSpawnConfig.asset`.

```
layout:  Ring | Explicit

// Ring — the simple case. Two numbers describe any team count.
ringCenterXZ:  (0, 0)
ringRadius:    120

// Explicit — the escape hatch, one row per team.
explicitPoints: [ { team: 0, groundXZ: (100, -20), yaw: 0   },
                  { team: 1, groundXZ: (-100, 20), yaw: 180 } ]
```

**Ring is the default**, and it places teams evenly around a circle, each ship facing inward.
That makes a whole balanced VS map out of a centre and a radius, which is the "simple way of
defining this" the ask calls for. It is also the safe balance choice: symmetric starts are
fair by construction, where hand-placed asymmetric starts are a balance liability that has to
be earned through playtesting the team cannot currently afford
(`GDC-L1-BAL-0003`, contextual, confidence 4). Explicit mode exists for when a specific arena
wants specific spots, and accepts that cost knowingly.

Positions are authored as **`Vector2` XZ, not `Vector3`**. Height is never authored, because
height is derived from the ground — encoding a Y that the raycast then discards is an
invitation to author a number that silently means nothing.

The asset also carries the tunables, so nothing below is a magic number:
`probeHeight`, `shipGroundClearance`, `seatRingRadius`, `seatInteriorOffset`.

Putting placement in an asset rather than in code is `GDC-L1-ARCH-0001` (data-driven,
contextual, confidence 4). That principle's stated exception is real — "data-drive what will
actually be iterated; hardcode what won't" — and it is satisfied here rather than assumed:
arena spawn placement is precisely the value a VS mode gets tuned on repeatedly.

### Runtime definition: an override static, not a mutated asset

Editing the asset at runtime is the wrong mechanism — in the editor those edits persist into
the project, and in a build they do not persist at all.

Instead `VersusShipSpawns` is a small static override layer, the same idiom `VersusSession`,
`MatchSettings` and `WorldSession` already use for config that must outlive the scene load
that destroys whoever chose it:

```csharp
VersusShipSpawns.UseRing(centerXZ, radius);        // define at runtime
VersusShipSpawns.UseExplicit(points);
VersusShipSpawns.Clear();                          // fall back to the authored asset
```

The spawner consults the override first and the asset second. This is `GDC-L1-ARCH-0005`
(iteration speed, objective, confidence 4) — though that principle is confirming a
requirement the ask already stated, not supplying it.

### Grounding: heightmap first, raycast second

For each team point, in this order:

1. `TerrainProbe.TryGetTerrainHeight(xz)`. Asked **first** because the heightmap cannot be
   shadowed. This is the same ordering `SpawnManager.TryFindOpenGround` documents: a downward
   ray fired near a ship takes the hull, a roof or a crate as "ground".
2. Failing that, `Physics.Raycast` down from `probeHeight` with
   `QueryTriggerInteraction.Ignore` — for the arena and test scenes, which have no terrain.
3. Failing both, **refuse**. The spawner reports "not yet" and the caller retries next frame,
   exactly the contract `SpawnPoint` established. In a streamed world a failure means the
   chunk has not loaded, and there is no position better than waiting for one.

The ship is placed at `groundY + shipGroundClearance`, rotated to the point's yaw (or facing
the ring centre).

### Ships: one per team, shared prefab, server-spawned

`VersusShipSpawner` is a `NetworkBehaviour` placed in the VS scene. On the server it grounds
every team point and spawns one ship per team through `IWorldService.Spawn`. The ship prefab
must be registered in `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`
(GUID `c9ad996ef06854049834f7c1c8f95ea3` — confirmed as the list `NetworkManager.prefab`
actually references; the root-level `Assets/DefaultNetworkPrefabs.asset` is not it).

One shared prefab for every team is the symmetric choice, again `GDC-L1-BAL-0003`.

### Seats: a marker component, with a ring fallback

`ShipSeat` is a marker `MonoBehaviour` with a serialized `order`. The spawner collects
`ShipSeat` components in the spawned ship's children and uses them as seat poses. When a ship
has none — which is today, since seats are not defined — it falls back to a ring of positions
of `seatRingRadius` at `seatInteriorOffset` inside the hull.

Wiring real seats later means adding `ShipSeat` markers in `PlayerShipBuilder`, with no
change to the spawner.

**A marker component rather than transforms matched by name.** Name-matching is a known trap
in this repository, and the failure is silent: a renamed anchor stops being found and players
quietly fall back to the ring. A missing component is visible in the Inspector.

Players are placed **at** the seat pose, not parented to the ship. Parenting a player's
`NetworkObject` to a moving vehicle has its own set of hazards, and none of them need
solving while the ship is parked on the ground at match start. That work belongs with real
seats.

### Placing the player

Via `NetworkedTeleport.Move`, never `transform.position`. The player's `NetworkTransform` is
owner-authoritative, so a server-side write to a remote player is overwritten within a tick,
silently. `NetworkedTeleport` already routes the move to the owner and degrades correctly
offline.

The hook is a new branch in `NetworkGameManager.SpawnWhenReady`, symmetric with the
saved-position branch that is already there:

```
if (VersusSession.IsActive && VersusShipSpawner.Instance != null)
    yield return SpawnIntoTeamShip(clientId);
```

That branch preloads chunks around the team's ship point, waits for the spawner to vouch for
a grounded position, spawns the player, and moves them to a free seat. Keeping it in
`NetworkGameManager` rather than in `SpawnManager` keeps team logic out of the plain spawn
path, which the story world still uses unchanged.

## Files

**New — `Gameplay/Versus/Core/`** (assembly `SpaceGame.Versus.Core`, EditMode-testable):

| File | Contents |
| --- | --- |
| `ShipSpawnPoint.cs` | `[Serializable] struct { int Team; Vector2 GroundXZ; float Yaw; }` |
| `VersusShipSpawnConfig.cs` | The ScriptableObject and its tunables |
| `ShipSpawnLayout.cs` | Pure math: ring points, seat ring offsets, team lookup |
| `VersusShipSpawns.cs` | The runtime override static |

**New — `Gameplay/Versus/Runtime/`** (Assembly-CSharp):

| File | Contents |
| --- | --- |
| `ShipSeat.cs` | Seat marker |
| `VersusShipSpawner.cs` | Grounding, ship spawn, seat resolution |
| `VersusTeamRoster.cs` | Server-side team assignment |

**Modified:**

- `Core/Multiplayer/Players/PlayerIdentity.cs` — add a server-write `team` NetworkVariable
- `Core/Multiplayer/Joining/NetworkGameManager.cs` — the VS branch

**Assets:** the config asset; the ship prefab registered in the network prefab list.

## Scope note: team assignment

Team replication does not exist and the feature cannot work without it, so a minimal version
is in scope: `VersusTeamRoster` assigns teams round-robin on the server, seeded from
`VersusSession.TeamCount`, and publishes them through a **server-write** NetworkVariable on
`PlayerIdentity`.

Server-write, not owner-write like the name and suit colour. The server needs a player's team
*before* it places them, and an owner-written value arrives after the spawn it would have to
inform. It also means a client cannot pick its own team.

The VS lobby already knows real team choices (`LobbyTeams.Occupancy`), but carrying those
into the session is a separate integration. `VersusTeamRoster` is the seam: when that handoff
lands, only it changes.

## Non-negotiables

**Multiplayer.** Ships spawn server-only and replicate as `NetworkObject`s. Players are
placed through `NetworkedTeleport`, which routes to the owner. The prefab is registered in
the network prefab list, without which the ship exists for the host and for nobody else.
Verification is on an actual client, not the host.

**Persistence.** A VS match is transient and is deliberately **not** saved: the ships are
spawned into a match scene that no `WorldSaveStore` covers, and a match that resumed
mid-round from a save is not a thing anyone has asked for. This is an explicit decision, not
an omission. Should VS ever gain a save, the ships need registered prefab ids — they already
carry `SaveableEntity` from `PlayerShipBuilder`, so the gap would be the prefab id alone.

**Tests** (`SpaceGame.Versus.Core`, EditMode, roughly three per unit):

- `ShipSpawnLayout` — ring points are evenly spaced and each faces the centre; seat offsets
  fit inside the hull radius; a team index outside the configured count is refused rather
  than throwing.
- `VersusShipSpawns` — the override wins over the asset; `Clear` restores the asset.
- `VersusShipSpawnConfig` — explicit points with a duplicate or missing team fail loudly on
  load rather than silently dropping a team's ship.

Grounding and ship spawning are not unit-tested: both are Unity-API-bound, and the questions
worth asking about them ("does the ship land on the sand") are answered by playing the mode.

## Open question

Nothing blocking. The one thing worth flagging is that this design leaves `MatchManager`
untouched — the minigame arena and VS are still two unconnected systems, and unifying them is
a larger piece of work that should not ride along on this one.
