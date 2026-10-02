---
system: Errands
layer: characters
summary: "Residents' chores (some moving real props), ambles, pairs, patrols, and the jobs decorations bring"
paths:
  - Assets/Game/Scripts/agents/Residents/Body/ErrandRunner.cs
  - Assets/Game/Scripts/agents/Residents/Plan/ChoreRounds.cs
  - Assets/Game/Scripts/agents/Residents/Core/Companions.cs
  - Assets/Game/Scripts/agents/Residents/Core/PerimeterRing.cs
  - Assets/Game/Scripts/agents/Residents/Data/ChoreDefinition.cs
  - Assets/Game/ScriptableObjects/Residents/Chores/
  - Assets/Game/Scripts/agents/Residents/Editor/ResidentErrandContentBuilder.cs
  - Assets/Game/Scripts/agents/Residents/Editor/ResidentErrandContentBuilder.SettlementWork.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Props/
symptoms:
  - "a basket or crate in a settlement never moves, though haulers walk to it"
  - "a settlement prop is in a resident's hand on one machine and on the ground on another"
  - "a prop vanished from a settlement after a fight or a reload"
  - "residents carry an invisible thing and the log says no room in the bag"
  - "the crosshair names a forge or a table but says nobody is there while a resident works at it"
  - "gardeners never water anything and haulers never carry"
  - "guards never patrol, or patrol alone, or one of the pair is left standing behind"
  - "a guard walks the same ring one way and the pair never meet"
  - "a resident carries nothing in its hand during a chore"
  - "the tower guard stands at the foot of the tower instead of on the deck"
  - "two residents talk while walking but stop dead to face each other"
reads_with: [Residents, ResidentReputation, HandTools, NavMeshSystem, Multiplayer]
updated: 2026-10-02
---

# Errands

What residents do INSIDE a plan segment, beyond standing at a place: watering plants, hauling goods or ore (moving the real baskets
and crates), bringing in the harvest, feeding animals, carrying firewood, wandering, walking in pairs, patrolling the perimeter,
standing watch up a tower — and the settlement work the decorations bring: every working decoration (a forge, a loom, a drill rig,
a farm bed) carries its job post and a `SettlementFixture` players can read. The day plan ([Residents.md](Residents.md)) still says only
*when and roughly where*; [ErrandRunner](Assets/Game/Scripts/agents/Residents/Body/ErrandRunner.cs) names the stop.

## Model
A segment whose activity is `Chore`, `Amble` or `Patrol` is still ONE plan segment. Once its walk to the anchor place is over,
`ResidentRoutine` asks the runner (server, one per resident, never saved) for the stop to be at and writes it as the same
holding goal it always writes, so fights and flees preempt an errand by the ordinary priority ladder.

| Idea | Mechanism |
|---|---|
| A chore is data | `ChoreDefinition`: source `SpotUse` → target `SpotUse` (a well → plants, an ore pile → the smelter, a goods pile → another one), targets per round, a carried `InventoryItem`, dwell seconds. Spots are `SpotRole.Errand` → `PlaceKind.Errand`: shared, **never booked** |
| Pairs | `Companions.Pair`: guards pair off in roster order; roamers pair with a friend or relative who is also a free roamer. Derived from the roster every rebuild, never stored. A pair shares one plan seed, so its days draw the same numbers and stay in step; the lower index leads, the other follows it |
| Perimeter | `PerimeterRing`: a point per bearing, `perimeterOffset` (5 m) outside the farthest building box; snapped to the NavMesh and kept only where the heart can walk to it. Gathered where plans are built, as `PlaceKind.Patrol`, after the trip points |
| Real props move | A chore with `carriesProps` fetches a `SettlementProp` resting at a `PropRest` of a source spot and sets it down at a free rest of a target spot; the hand shows the prop's own item. No prop to fetch or nowhere to put it → the ordinary round with `carried` |
| Where a prop is | One short per prop in its building's `SettlementPropSync` (NetworkList): a settlement-wide rest index, `Carried` (-1) or `Taken` (-2). The server writes it, every machine shows it |
| Jobs come with decorations | Every working decoration prefab carries its job post (`SettlementSpot`, a Work `SpotUse`) and a `SettlementFixture` (name, description, scanner class) — placed anywhere, it brings the job along |

## Key types
| Type | Role |
|---|---|
| `ChoreDefinition` (SO) / `ResidentDuty` | one round of errands as data · a standing assignment (`Patrol`) that replaces the working day |
| `ErrandRunner` / `ErrandStop` (Body) | the stop inside an errand segment: next chore stop, amble stop, ring point, or a spot beside the leader; a stop's `pick` / `drop` + `dropRest` move a prop when its dwell ends |
| `Companions` / `PerimeterRing` / `ChoreRounds` (Core, Plan) | pure: who walks with whom · the patrol ring · the order a chore visits its targets |
| `SettlementProp` / `PropRest` (World/…/Settlement/Props) | a carriable thing (its `item` = what a hand or bag holds, its `home` rest) · a pose a prop can stand at, served by one errand `spot` |
| `SettlementProps` (World, on `Settlement.Props`) | every prop and rest under `Generated` in hierarchy order, plus the server's transient reservations; `Pick` / `Put` / `FreePropAt` / `IsFree` / `RestsOf(spot)` |
| `SettlementPropSync` (NetworkBehaviour) | the state of one building's props; player take (RMB) is server-decided here; saver key `props` |
| `SettlementNetworking` | `Settlement.Generate` stands a NetworkObject + `NetRelay` (+ `SettlementPropSync`) wrapper round each building with props or a latch (the animal keep's gates) |
| `SettlementFixture` | player side of a working decoration: label, who is working there (from replicated held places), RMB reads the description on the visor, `ScannerRegistry` |
| `CharacterProfile` (on `SettlementCulture.profiles`) | which archetypes a character prefab makes; `ResidentAssignment` draws a newcomer from its profile when one is usable |

## Flows
- **Chore.** Planned in two places: a post holder's break becomes a chore session (`choreSession`) instead of a stroll, and a
  roamer who keeps a chore fills its stroll blocks with sessions and rests. Left out silently when the settlement lacks
  both ends (or only one stop of a same-use chore). The runner takes the nearest source, then `targetsPerRound` targets in
  nearest-neighbour order (the cursor carries on, so every plant is watered in turn), then the nearest source again.
- **Prop round** (`carriesProps`, `PlanPropRound`): nearest source with an unpromised prop on one of its rests (item in `carryItems`),
  nearest other target with a free rest; both promised; stops `pick` then `drop` (carry byte = the prop's item). `FinishProps` acts
  when a dwell ends (a prop a player took first ends the round); `LandCarried` (new segment, `Reset`, routine off, death) puts it down.
- **A player takes a prop:** RMB → `SettlementPropSync.RequestTake` → server: still resting and the bag takes the item → `Taken`.
- **Who works at a fixture:** each of its spots' `PlaceOf`, matched against residents' replicated `ResidentPresence.Place`.
- **Amble.** A roamer's free-time slot is, with `ambleChance` (a paired resident: always), a seeded Stroll/Hearth spot taken
  **unbooked** as the anchor. The runner then alternates spots nobody stands at and points in the street, each held
  `ambleStopSeconds`. Anyone can share a spot's pose; nobody shares a point.
- **Patrol.** One segment for the working day, anchored at ring point `slot * points / slots`; the runner walks the ring
  (direction by slot parity, so pairs meet), advancing on coming within 70 % of the offset of the next point. The follower
  stands beside the leader (`pairGap`), hurries when more than 3 m back, and the leader waits when it is more than
  `pairWaitDistance` ahead (giving up after 20 s). A follower follows only while the leader is also on the same errand.
- **Tower post.** A `SpotUse` with `elevated`: no NavMesh path, so `ResidentRoutine.Hop` puts its worker on the deck and takes
  it down again **only while unwatched**; the planner gives it no breaks.
- **Walking talk.** `Conversations.PairWalkers`: two residents on an amble or patrol within `walkTalkRange` talk without taking
  focus, so neither stops or turns; the talk ends when they drift beyond twice that range, and the pair rests
  `walkTalkRestSeconds` (shorter than a standing talk's rest).
- **Guards challenge.** `archetype.challengesArmed`: an armed or sprinting player in notice range irritates the guard like a
  trespasser — one band per `irritationStepSeconds` (Wary, Drawn, then a fight), never faster. A shove is still the stock ladder
  (≥ 2 shoves for the prickliest), so one push is never a fight.

## Multiplayer
- Chore state, amble stops, the ring and the pairing exist only on the deciding machine. What every machine shows: the
  activity (`Chore`/`Amble`/`Patrol` while on an errand, even walking), the held place **only when it is a spot** (a stop in
  the street or a ring point holds none), and the prop byte.
- A carried item goes through the equipment: the prop byte is `trip rows + 1 + index` into `ResidentTuning.carryItems`;
  every machine finds (or adds) the item in its own bag and equips that slot, and holds the previous slot again when it is put
  down. **`carryItems` is append-only.** **Not seen on a second machine yet.**
- Props: the server writes `SettlementPropSync.states`, every machine shows them, a late joiner gets them with the spawn; the
  NetworkObject is the wrapper's (a loose scene object), a client's take is `TakeServerRpc`. Fixtures send nothing. **Not run on a client yet.**

## Persistence
Nothing is saved: chore rounds restart from their first stop after a load, pairs and the ring are re-derived with the plans.
The carried item is an ordinary bag item, so a save that holds it keeps it.
Props: `SettlementPropSync` (key `props`, `IPersistentEntity` on the wrapper) writes each prop's rest once one has left home; `Carried`
restores home, `Taken` stays taken; a regenerate orphans the record. **Not verified through a reload yet.**

## Gotchas
- **A paired resident's plan is its leader's seed, not its own** (`PlannerResident.shareSeed`). Anything that draws from the
  planner's rng in a way that differs between two residents (a branch on a booking, a draw inside only one path) desynchronises
  the pair; `Strolls` draws the same count whether or not it ambles for that reason.
- **An errand spot must not be a booking.** `PlaceKind.Errand` and `Patrol` are skipped by `Book`; a new unbooked kind belongs in
  that list too, or two residents are planned out of the same well.
- **`PlaceKind.Patrol` points are server-only, like trip points, and come after them.** Never publish one as a held place.
- **A chore whose carried item is missing from `carryItems` shows nothing, silently** (`PropIndexOf` is 0). `ResidentValidator`
  names it; the content builder asserts it.
- **Guard counts come from the culture's `patrolShare`** (rounded down to whole pairs, none below 8 residents) and are handed out
  first in Generate. Existing settlements keep their old archetypes until regenerated.
- **Tower posts and unreachable decks.** `elevated` assumes the deck is on the NavMesh (Generate's reachability check will not tell
  you: the spot is not walked to). Unverified in play.
- **A prop must never carry a spot.** Places are gathered once; a spot riding off on a basket leaves its place where it was.
  The build assembler deactivated the `GoodsPile` spot children of the crates used as props (`Deco_Crate_*`, `HarvestCrates`,
  `MiningToolCrate`) — the same decoration placed as scenery keeps it.
- **A prop must not be static.** A batching-static prop moves its collider but its picture stays put; props carry no static flags.
- **A prop's item needs a free bag slot** (`RaxyToolLoadouts.BagSize = 4`): held + belt + chore item + every distinct prop item
  the resident has carried. Archetypes with a prop chore keep at most one belt tool; a full bag logs "no room in the bag" and the
  carry is invisible, the move still happens.
- **A carried prop is only as safe as `LandCarried`.** Anything new that stops a resident's errands without `ErrandRunner.Reset`
  (or disabling the routine) leaves a prop `Carried` — invisible on every machine until the next load.
- Rests: each prop's authored pose, plus two floor patches beside every `GoodsPile`/`Smelter` stop; a prop with no errand stop within
  3 m got its own `GoodsPile` stop. Job posts take the back or a side where the front was already an errand stop (`Jobs` table).
  Job cues (`cook`, `dig`, `hammer`, `craft`, `tend`) fall back to `work`; `Pen` and `Garden` now hold `tend`.
- Decoration spots stand 1.1 m in front of the prefab's bounds, looking at its middle; they only matter where a settlement's
  config lists the decoration. Re-running `Tools/SpaceGame/Residents/Author Errand Content` is safe.
- **Edit mode has no world NavMesh** (it is added at runtime by `WorldNavMeshProvider`). Generate checks places on its
  own throwaway bake; editor previews (`ResidentsWindow`, the Resident inspector's plan) build a fresh
  `SettlementSociety` inside `WorldNavMeshScope`. Anything else that touches `Settlement.Society` in edit mode caches
  door stands measured on no NavMesh — fine for names, wrong for positions.
- **A spot on a NavMesh island is unusable.** Yard floors, fenced pens and booth interiors are often islands; Generate
  lists each such spot (`… cannot be walked to`). Move the spot onto the open floor. The nomad pen's herders stand at
  the gate side for this reason, and the market's second vendor at the covered wagon.
- **Spot height is the prefab floor the spot was authored on.** Author by raycasting the prefab's own collision (as the
  nomad spots were); a spot on a boulder or under a canopy is fine, inside a closed structure is not.
- Sit loops with `postures: 4` (chair sitting) never play for a standing body; the ground sits answer `sit`.
- Population is beds, but **the characters list caps it**: with fewer rolled copies than beds, beds stay empty and
  Generate says so. Raise `count` on the characters, not beds.
- A job archetype is only handed out where a spot of its `post` exists; a culture with no usable archetype gives
  residents none (warned).
- `Resident.Settlement` is the generated field or a parent only (caravan copies of the prefab belong to none);
  `QuietStockModules` and derived tuning apply to members only, at runtime.
- **Offline, `PlayerIdentity.All` is empty** — every "which players are near" sweep goes through `SessionPlayers.Collect`.
- The world NavMesh is baked from chunk scenes on disk: use **Generate + Bake World NavMesh** before play; a stale bake
  is what made residents walk through walls.
- `HeardAllyHurt` fires only for a target the resident's faction is not Hostile toward; a Hostile intruder is fought
  by everyone the alert reaches, timid or not — the faction stance outranks nerve.
- `Time.timeScale` cannot speed up a hosted day; use `DayNightCycle.JumpToHour`. Never hide, snap or teleport a
  resident while `ObserverCheck` says a player sees the point.
- Pre-2026-10-02 settlements carried `ResidentSettlement`/`SettlementTalk` components, a baked `Places` root, a
  1521-entry travel table and a roster that went empty whenever Generate respawned the characters. All gone; such a
  scene shows two missing scripts — remove them and Generate.

## Extending
- **A chore that moves things:** set `carriesProps`; give the source and target spots `PropRest`s (a rest's `spot` names the stop
  that serves it) and put `SettlementProp`s on some of them, each with an item in `carryItems`. Props live in a building that
  Generate wraps (it does so for any building with a `SettlementProp`).
- **A new working decoration:** a row in `Fixtures` (name, description, scan class) and in `Jobs` (post, side) in
  `ResidentErrandContentBuilder.SettlementWork.cs`, then re-run the menu item.
- **Which person a character prefab makes:** a row in `AuthorProfiles` (or edit `SettlementCulture.profiles` by hand).
- **New chore:** two `SpotUse` assets with role Errand (+ a hold cue: `pickup`/`putdown` exist), a `ChoreDefinition`, the carried hand
  tool appended to `ResidentTuning.carryItems`, an archetype whose `chore` is it, 2-3 line rows with activity `Chore`, and the
  spots on prefabs. No code. Easiest by adding it to `ResidentErrandContentBuilder`.
- **New kind of guard:** an archetype with `duty` Patrol (pairs on the ring) or a post whose `SpotUse` is `elevated` (a tower).
- **New kind of place:** a `SpotUse` asset (role, hold cue, staffing) — no code. Put `SettlementSpot` children on the
  prefabs that offer it.
- **New job:** a Work `SpotUse` + an archetype whose `post` is it + its spots on a prefab + 3–6 TSV rows whose `speaker`
  is the archetype name.
- **New house:** `Dwelling` (beds) on the prefab root and a `SettlementEntrance` where the door is.
