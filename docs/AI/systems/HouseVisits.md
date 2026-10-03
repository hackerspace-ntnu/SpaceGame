---
system: HouseVisits
layer: characters
summary: "Residents call in on the house interior while a player is in it: come in, sit on its Seats, talk, leave"
paths:
  - Assets/Game/Scripts/agents/Residents/Visits/
  - Assets/Game/Scenes/Interiors/NomadHome.unity
  - Assets/Game/Art/Models/_Source~/models/buildings/nomad_interiors_export.py
symptoms:
  - "I walk into a house and nobody is ever there"
  - "the house interior is empty although the settlement is full of idle residents"
  - "residents sit in the house but never talk to each other"
  - "people chasing me stop at the door of a house instead of coming in"
  - "a resident I hurt in the house vanishes and appears outside"
  - "a resident was teleported into a house and is standing outside, trying to walk to a seat"
  - "a resident is left in the house void, or far from the settlement, after I left or reloaded"
  - "a guest sits a metre above its seat in the house"
  - "[HouseRoom] 'X' loaded while 'Y' is: a room's places share one index range"
  - "[HouseRoom] Spot_X is unusable and nobody will sit there"
reads_with: [Residents, Seats, SceneTransitions, Errands, Multiplayer, Persistence]
updated: 2026-10-03
---

# House visits

## Model
Every nomad dwelling's door opens into one shared interior scene, `NomadHome` ([SceneTransitions.md](SceneTransitions.md)),
loaded while somebody is inside. It used to be a still room you were always alone in. Now, while a player is inside, the
settlement's own residents call in: they come in by the door, take a free seat round the hearth, sit and talk for a
while, and leave by the door when they please. Guests are **real residents, not extras**: one that is free to idle is
moved to the doorway, and from then on the routine treats the room as its place.

| Idea | Mechanism |
|---|---|
| A room has places | `HouseRoom` (on the scene root) gathers the `SettlementSpot`s below it (one per `Seat`, one circle) and a `Door_Stand`. Their places live in an index range of their own, `HouseRoom.PlaceBase` (1 << 20) and up; `SettlementSociety.Place(i)` and `SpotAt(i)` hand any such index to the loaded room |
| A visit is a plan segment | While a resident is a guest, `HouseVisits.TryGetSegment` gives `ResidentRoutine` a segment of its own — a seat (`Break`, so it sits) until it is time to go, then the doorway — in place of its day plan. Everything else is the stock resident: the routine walks it there, `ResidentSeating` puts it on the `Seat` ([Seats.md](Seats.md)), `ResidentPresence` holds the sit loop, `Conversations` talk between two spots of one circle |
| Who comes | the host settlement is the one whose dwelling door is nearest the first player's recorded return position. A resident qualifies when its plan says `Break`, `Hearth`, `Stroll` or `Amble`, it is calm, not offstage, not talking, carrying nothing, under no override, and **unwatched where it stands**. `GuestPick.Choose`: those whose bed is in the house first, then family/friends of them, then anyone, with a draw to break ties |
| When | on entry 1-2 guests are already seated (`guestsOnEntry`); then one invitation every `arrivalGapSeconds` (20-60 s) while a seat is free and no player stands within `arrivalClearance` of the door. A guest stays `staySeconds` (90-240 s), then walks to the door |
| Arriving | `NetworkedTeleport.Move` to the doorway (inside the closed leaf), then the routine walks it to the seat |
| Leaving | at the doorway it is put back outside the dwelling's door (`SettlementPlaces.TryDoorStand`) when nobody there sees it, or after `watchedWaitSeconds`; its day plan walks it on to wherever it was due |
| A fight in the room | a guest that is hurt or provoked **stays in the room**: it gets up (its segment becomes the doorway), fights, and walks out only once it is calm again; a guest that dies stays where it fell. An alarm (any override, e.g. an ally's shelter order) also sends a guest to the door, and a visitor ignores the override itself (it has no street to shelter in) |
| Chasers follow you in | a resident whose grudge (`ProvocationModule.Aggressor`) is the player now inside, within `pursuitRange` (30 m) of the dwelling's door, steps through the door `distance / pursuitSpeed` seconds later and carries on the fight in the room, whether it was idle or hunting. It is a guest on its way out, so it leaves by the door once calm |
| Going home | every guest when the last player leaves or the interior is about to unload (`InteriorManager.OnInteriorWillUnload`) is stood up and put outside the door at once |

## Key types
| Type | Role |
|---|---|
| `HouseRoom` | the room's seat places and doorway; static `PlaceAt` / `SpotAt` by global index; measures stand points on the interior's own NavMesh (server) |
| `HouseVisits` | server driver on the same object: who is a guest, arrivals, departures, `TryGetSegment` |
| `GuestPick` / `GuestCandidate` | pure ordering of who is invited; unit-tested (`HouseVisitTests`) |
| `ResidentRoutine.StandUp()` | gets a sitter up where it is, ahead of a move |

## Flows
1. Player enters (`SceneTransition` → `InteriorManager`) → scene loads → `HouseRoom` gathers, `Settle` measures the stands once the NavMesh exists → `HouseVisits` finds the host, seats the initial guests, then ticks every `thinkSeconds`.
2. Invite: pick a free usable seat and a candidate → move it to the doorway → `Admit` (its segment is now the seat) → it walks, `Seat.NearestFree` within `seatReach`, claims, sits.
3. Leave: `leaving` flips its segment to the doorway → walks → `AtDoor` and `MayStepOut` → `SendHome`: `StandUp`, move outside the dwelling's door, drop the visit.
4. Last player leaves → `TryFindHost` fails → everyone sent home; the scene unloads.

## Multiplayer
- **Server decides; nothing is replicated of its own.** Clients see a resident that moved (its transform sync), sat (`ResidentPresence`'s held place and seat id) and spoke (`ResidentSaid`). The room's places resolve on every machine because the interior scene is loaded on every machine (NGO scene sync) and `Seat.Id` / the place index derive from the scene's own hierarchy.
- A late joiner into a house others are in is the existing open item in [SceneTransitions.md](SceneTransitions.md); guests are not placed for them specially.
- **Not run on a client.** Only a host-offline Play run exists (2026-10-03): initial guests seated, an invitation arriving at the door and walking to a seat, two guests in conversation, a guest leaving and resuming its plan outside, everyone sent home when the player walked out, a hurt guest and the ally who joined staying in the room fighting, and two grudge-holders outside following the player in through the door.

## Persistence
Nothing is saved: a visit, a seat claim and the room's places are derived. A quit or a reload while guests are inside leaves residents saved at interior coordinates (around -6000, 0, -6000); on load the routine's settle step moves a resident that far from its plan back while nobody is looking. **Not run: a save made with guests inside, reloaded.**

## Gotchas
- **A seat must not have a collision hull.** A sitter's body is inside the seat it sits on. A solid hull there (a) hides the sitter from every `ObserverCheck` ray — `Conversations` only open when a player is in earshot with line of sight, so two guests side by side never spoke — and (b) leaves a small walkable NavMesh island on its top that a re-enabled agent snaps to (the body stood on the stool, hips a stool-height too high). `nomad_interiors_export.py` leaves `Root_Seat_*` out of the collision mesh.
- **The stand spot is beside the seat, the body goes onto the seat.** The spot is 1 m out from the seat, away from the hearth (the gap between the hearth and a stool is narrower than two agent radii), on the rim; `ResidentSeating` teleports the body to `Seat.FeetPosition`. A spot with no `Seat` within `ResidentTuning.seatReach` makes nobody sit there.
- **`SeatedBodyFit` and the NavMesh must agree about the floor.** A sitter's feet are in the hearth pit (y -0.2 to -0.4); the interior's NavMesh covers the pit, so the agent re-enabled by the teleport lands within ~0.1 m of the feet. Without NavMesh in the pit it snaps to the rim and the hips float. If seats are added, rebake and check `NavMesh.SamplePosition(seat.FeetPosition)` is within ~0.2 m.
- **Arrivals are visible on purpose.** Line of sight sees the whole round room, so "nobody is looking" would never be true; only a player standing within `arrivalClearance` of the doorway delays one. A guest does appear in the doorway; the leaf is closed.
- **One interior at a time.** All rooms share the `PlaceBase` range; a second loaded `HouseRoom` logs an error and replaces the first. Per-instance interiors would need a scene key in the index.
- **The host settlement is guessed from the return position.** The nearest dwelling door of any loaded settlement with residents wins; entering by another route (a script, a teleport) picks a wrong or no house.
- **A chaser's live target is gone the moment you enter**: its path target is a world its NavMesh does not reach, so `AgentTargeting.Target` reads none within a tick. Pursuit therefore reads the grudge (`Provocation.Aggressor` / `Provoker`), which lasts.
- **Always stand a resident up before moving it** (`PutAt`): `ResidentSeating` puts a sitter that was moved while seated back at its old stand point when it gets up, i.e. in the wrong world.
- **Residents saved while visiting come back far from their plan** (see Persistence); the settle step only runs while nobody sees the resident.
- Guests need free residents: at night nearly everyone is asleep, so the room stays empty; around 19:00 there are many `Hearth`/`Amble` residents.

## Extending
- **More seats:** in the interior blend add the model, export, then in the scene add a `Seat` (sit point at the hips' rest, +Z toward the middle, `footDrop` to the floor) and a `SettlementSpot` (use `Seat`, group `hearth`) 1 m beside it on walkable floor; rebake the NavMesh. Leave seats out of collision.
- **Another house interior:** a `HouseRoom` + `HouseVisits` on its root, a `Door_Stand` (+Z toward the door), seats and spots. Only one may be loaded at a time.
- **Different tempo:** `HouseVisits` fields (`arrivalGapSeconds`, `staySeconds`, `guestsOnEntry`) are serialized on the scene object.
- **Re-export the interior:** `blender --background --python Assets/Game/Art/Models/_Source~/models/buildings/nomad_interiors_export.py` (`INTERIOR_SCALE=1.25` by default, the shipped size). A different scale needs the scene's anchors, door box and lights scaled by the same factor and a NavMesh rebake.
