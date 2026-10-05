# The Striders — a tribe that lives in a walking city

**Date:** 2026-09-23 (revised 2026-09-24: RigWalker habitats + DesertCrawler workers; 3 houses, not 4) ·
**Branch:** `Feat/create-factions` · **Status:** built 2026-09-24 (code + EditMode tests); host/client, save/reload and profiling pending — see [Striders.md](../../AI/systems/Striders.md)
**Parent:** [2026-09-07 faction system design](2026-09-07-faction-system-design.md) §3.6 ("Mechanics")
and [plan](../plans/2026-09-07-faction-system.md) Task 4.3 (Mechanics half). This spec replaces the
Mechanics bullets of Task 4.3; the Outlaw roster in that task is not part of it.

## 1. What we are building

The third tribe. The design doc's working name "Mechanics" is now **The Striders**: a technology-
centric people who live in a **walking city** — a column of six-legged walkers that walks between
salvage sites, stops, works, and walks on. The **houses** are `RigWalker`s (the walker with a house on
its deck); the Strider crew live on them, climb down at every stop to work the site on foot, and board
again before the column moves. **Worker** `DesertCrawler`s walk with the column and, at every stop,
spread out and dig and collect rocks with their tool rigs. Crab walkers ridden by Striders scout the
flanks.

This first pass reuses existing art: the Sand-nomad body recoloured heavily red, the `RigWalker` and
`DesertCrawler` hulls and the `CrabWalker6` body. Distinct Strider art is later work.

### Decisions (user, 2026-09-23/24)

| Question | Decision |
| --- | --- |
| Name | **The Striders**. Files are `Strider*`; "Mechanics" is retired. |
| How the city moves | **Roams as a column** between sites, stops to work, walks on; folds like a caravan. |
| The houses | **`RigWalker`s** — "it has a house on it". The crew live here. |
| The workers | **`DesertCrawler`s** in the mix, uncrewed, collecting rocks at every stop. |
| How the crew live on it | **Posted crew who step off at stops** and re-board before it moves. |
| Crab walkers | **Ridden outriders** (one Strider each), flanking the column; also the war-party mount. |
| Scale | **One larger city**: 3 RigWalker houses (6 crew posts each = 18 crew), 2 DesertCrawler workers, 2 crab outriders → 20 people, 7 machines. *Revised 2026-09-24: 3 houses, not 4 — user: "they are extremely large".* |
| Architecture | **Approach A — the city is an `NpcWorldSim` caravan**, not a moving `SettlementPopulation` (B) or one rigid object (C). |
| Walker damage | **Indestructible in this pass** (as today). A killable walker is its own feature. |
| Out of scope | `TerritoryZone` (plan 4.4), traders (4.5), Strider-specific art, crew disembarking to fight while marching, real resource collection (the crawler's digging is presentation only, as today). |

## 2. Faction, people, roster

- **Faction** `ScriptableObjects/Factions/Core/StriderFaction.asset`: `factionName` "The Striders",
  `defaultStance` **Neutral**, no rows in `GlobalRelationships.asset` (neutral to every tribe and to
  Humans), `hudColor` rust red. Listed in `FactionGoodwillLedger.tribes` on the `NpcWorldSim` object.
  Authored by an idempotent `RosterAuthoring` menu, like `AuthorSkyTribeFaction`.
- **People**: `NomadPrefabBuilder.StriderNomads`, four armed variants of the Sand FBX built exactly as
  `SkyNomads` is (`ArmedNomadVariants.Select(...)`), with a `StriderCloth` `ClothPalette` — heavy rust
  red base, dark iron and brass accents — under its **own `MaterialPrefix`** (`StriderNomadCloth_`),
  so no tribe's build recolours another's. Prefabs `Prefabs/Agents/Characters/Striders/StriderNomad_*`.
  Default `DialogPool`.
- **Roster** `ScriptableObjects/Factions/Rosters/Striders.asset` via `RosterAuthoring.AuthorRoster(...)`:
  - `Warrior` and `Scout` → the four nomads.
  - `Rider` → `StriderCrabOutrider` (the mount; its `NpcPassenger` seats a Strider — the roster's
    existing convention).
  - `handItems` → `GravelBlaster`, `NetGun`, `basicgun` (design §3.6 named the first two).
  - `hostileLines` → `StriderHostileLines.asset`, ~8 mechanical, salvage-flavoured barks.
  - `warPartyTiers` → 0: 2 Riders · 1: 2 Riders + 2 Warriors · 2: 3 Riders + 4 Warriors. A war party
    is an ordinary on-foot/crab party with its own `runtimeOnly` + `bountyHunters` template (skill §5);
    it does **not** come out of the city.
- **Ownership of the machines.** The city's walkers are Strider-owned (`EntityFaction` = Striders on
  the city prefabs below). The shared `DesertCrawler.prefab` moves to `StriderFaction` too (Phase 0
  parked it on Sand), so the lone one in `Chunk_7_4` becomes a Strider worker. The player-pilotable
  `RigWalker.prefab` itself stays a player vehicle, unowned and untouched (see §3.1).

## 3. The machines

### 3.1 Habitat walker — `StriderHabitatWalker.prefab`

`RigWalker.prefab` is hand-authored (no builder) and is a **player mount**. The city must not hand a
player its houses, and hand edits to the shared prefab would change the player vehicle too. So the city
uses a **prefab variant** of `RigWalker`, created and wholly owned by a new builder
(`StriderCityBuilder`, re-runnable, overwrites only what it adds):

- **Removed** in the variant: the player-piloting path (`MountStation` / `MountModule` /
  `SteerModule`), so nobody can take the helm of a Strider house. Players can still **walk onto the
  deck** — `WalkerPlatformCarrier` stays — and ride along with the city.
- **Added**: `EntityFaction` = Striders; `NpcTaskModule` + `GoalTravelModule` (a leader walks a task
  list) and `FormationModule` (a follower holds a slot). Both steer through `MoveIntent`, which the
  walker's `LeggedDriver` already follows — **proved by a spike before anything else is built**
  (plan Task 1).
- **Six crew posts**: `VesselSeats` with 6 seat markers on the deck and around the house — lookouts at
  the rails, operators seated (`ChairPose`). `VesselSeats` has no dependency on `VesselPilot` (the
  dependency runs the other way), so it is reused, not copied.
- A **`Gangway`** marker at ground level beside the hull, on the NavMesh, where crew get off and on.
- **`CrewShift`** (§4.3).
- Formation radii sized for the hull footprint (`restRadius`, `slotTolerance`, `regroupDistance` —
  existing serialized fields). Indestructible (no `HealthComponent`).

### 3.2 Worker crawler — `DesertCrawler.prefab` (changed in `DesertCrawlerBuilder`)

The crawler already reads as a digger: `CrawlerToolModule` lowers the boom, turns the drum, sweeps the
claw and cycles the bucket whenever the machine is stopped, and stows everything while it walks. The
city uses that as-is:

- **Added** in `DesertCrawlerBuilder` (the prefab is overwritten wholesale, so never by hand):
  `FormationModule` (follows the column while marching) and `EntityFaction` = Striders.
- **At a stop** the worker leaves formation and wanders the site within `workRadius` (serialized,
  ~60 m) with pauses — each pause is the tool rig working, i.e. "collecting rocks". When the column
  moves it rejoins its slot. This is formation's existing rest behaviour widened, plus the crawler's
  existing `WanderModule`; the spike (plan Task 1) confirms which one owns the frame at a stop.
- **Uncrewed**, no crew posts, indestructible.

### 3.3 Crab outrider — `Prefabs/Agents/Characters/Striders/StriderCrabOutrider.prefab`

Built by a new builder modelled on `RobotHorseBuilder`'s outrider path, from the `CrabWalker6` body:
`AgentController` (with the crab's `LeggedDriver` as motor), health + reactions, `EntityFaction` =
Striders, the netcode stack (`NetworkObject`, `ClientNetworkTransform`, `NetRelay`, `NetAuthority`,
`NetworkedHealthComponent`), savers, `SceneTracked`, `FormationModule`, and an `NpcPassenger` seating a
Strider nomad. **The mount carries, the rider shoots** — the crab has no attack (existing rule,
`AgentSystem.md`). The wild `CrabWalker6.prefab` is not touched.

## 4. The city and the crew cycle

### 4.1 The group

One permanent `NpcGroupTemplate`, id `strider-city`, written by an idempotent editor menu (modelled on
`RosterAuthoring.WireWorldSim`): `tribe` = Striders; members = 1 lead habitat walker (`isLeader`),
2 follower habitat walkers, 2 worker crawlers, 2 `Rider`s drawn from the roster, and **crew** (6 per
habitat walker) drawn from the roster's Warrior/Scout roles; tasks = travel to `SiteKind.Ruin` /
`SiteKind.ScrapField`, dwell 90–180 s. `NpcTaskPlanner` already discourages repeating a stop. It
folds/unfolds at the sim's global `spawnRadius`/`despawnRadius` and travels while folded, like every
caravan. Formation shape: lanes/rows ~30 m apart (as built: two lanes; nothing assigns a lane per kind —
houses, workers and crabs share one follower order).

### 4.2 Seating crew on a ground carrier

Before this work only the sky path seated riders into a carrier (`NpcWorldSim.BoardTransport` refuses
a prefab without `VesselPilot`, and `seated` was true only for a vessel). **As built (2026-09-24), no
seating helper was extracted:** the shared piece already existed — `NpcSpawn.Create(..., seated: true)`
spawns a member with its `NavMeshAgent` off — and both paths then call `VesselSeats.Seat` themselves
(the vessel through `VesselPilot.Begin`, a house through `CrewShift.Take(member, aboard: true)`). Crew
members do not name their carrier: a `crew: true` member takes the first house already spawned in the
same group with a free post (`CrewShift.FirstWithRoom`), so templates list the houses before the crew.
An unfolding city seats each house's six crew on its posts when the group is marching, and spawns them
on foot at their gangways when its saved `crewAshore` says they were ashore.

### 4.3 `CrewShift` — server-only, one per habitat walker

A component on the habitat walker with pure decision logic in `CrewShiftLogic` (tested without a
scene):

| State | What happens | Leaves when |
| --- | --- | --- |
| **Aboard** | Crew on posts. They keep their own targeting and shoot from the deck if provoked (`RidesAsPassenger`). Nobody disembarks while marching. | The leader starts a dwell. |
| **Disembarking** | One crew member unseated every `disembarkInterval` (0.5 s) at the `Gangway` (`VesselSeats.Unseat(seat, gangway)`). | All living crew are ashore. |
| **Ashore** | Crew wander and work within `ashoreRadius` (40 m) of their walker, like nomads at camp. | The leader's dwell ends. |
| **Recalling** | Crew are sent to the gangway; each is seated on arrival within `boardRadius`. **Held while any crew member is fighting.** After `recallTimeout` (60 s) any living straggler is seated directly. | All living crew are aboard. |

The **leader holds its dwell** until every habitat's `CrewShift` reports Aboard, so the column never
leaves its crew behind. As built (2026-09-24) the gate waits for the houses' crew only: whichever
house `FormationModule.LeaderOf` names installs it on its own `NpcTaskModule` (re-checked every tick),
and it is consulted both when a stay ends and before a new stop is chosen. The worker crawlers need no
gate — the leader's 2.7 m/s is under their 3.2, and formation's regroup brings them back. Dead crew
stay dead; the walker walks on with empty posts. All numbers above are serialized on `CrewShift`.

## 5. Multiplayer

Every decision is server-side: `NpcWorldSim`/`WarPartyDirector` (`Network.Decides`) and `CrewShift`
(authority only). Seating needs no new message: crew are parented under the habitat walker's
`NetworkObject` by `NpcSeating.Attach`, exactly like sky-vessel riders, and the seated pose is
presentation derived from what each machine sees. New network prefabs (four `StriderNomad_*`,
`StriderCrabOutrider`, `StriderHabitatWalker`) are registered by `NetworkPrefabRegistrar.Sync(out _,
out _)` at the end of their builders — never `SyncMenu()`. The crawler is already registered. A player
standing on a habitat deck is carried by `WalkerPlatformCarrier` exactly as on today's RigWalker.

**Verify on a client:** the column marches with crew on the decks; at a stop the crew climb down and
wander while the crawlers dig; they re-board and the column leaves; a late joiner arriving mid-stop
sees the crew ashore; a crab outrider's rider shoots from the saddle; a client can ride on a habitat
deck but cannot pilot it.

## 6. Persistence

Seated crew are never saved individually — the group record decides, the rule the sky vessels already
follow. `NpcGroup.Record` gains **`crewAshore`** (appended; an older save reads `false`):
reload mid-march → crew come back seated; reload mid-stop → crew come back on foot around their
gangways; the dead stay dead through the existing `GroupMembership`. The faction/roster/prefabs are
authored assets, not save state.

**Verify by reloading**, and check the save JSON: the city under `npcworld` (with `crewAshore`) and the
Striders' `factionId` under `factionGoodwill` once a Strider has been hurt.

## 7. Performance

27 agents when loaded (20 people + 7 machines) — above the Sky City's 16. Per
**GDC-L1-PERF-0004** (budget the frame) and **GDC-L1-PERF-0001** (measure, don't guess), the plan
profiles frame time beside the city in play before calling it done; crew-per-habitat and the worker
count are tunables on the template if it is over budget.

## 8. Testing

EditMode, test first:

- `CrewShiftLogicTests` — dwell start → disembark; release only when all living crew are aboard; timeout
  seats stragglers; a fight holds the recall; dead crew do not block.
- `NpcGroup` record back-compat — an old record reads `crewAshore = false`.
- Striders roster fixture beside `RosterAssetTests` (skill §7): validation clean, faction ↔ roster
  back-reference, tiers as authored, baked hand items match, hostile lines present.
- `StriderHabitatWalker` prefab: a variant of `RigWalker`; no `MountStation`/`MountModule`/`SteerModule`;
  faction Striders; 6 seats; a gangway; task/goal/formation modules; `CrewShift`; network hash; carrier
  kept. And `RigWalker.prefab` itself unchanged (still mountable).
- `DesertCrawler` prefab: faction Striders, `FormationModule`, tool rig intact.
- `StriderCrabOutrider` prefab: brain, netcode hash, savers, `NpcPassenger` rider, no attack module.
- `strider-city` template wiring read back from `persistentScene.unity`.

## 9. Documentation

- New `docs/AI/systems/Striders.md` in the standard shape (Model → Key types → Flows → Multiplayer →
  Persistence → Gotchas → Extending), modelled on `SkyTribe.md`.
- `docs/Human/the-systems.md` entry (the validator requires it for a new system).
- `AgentSystem.md` (walkers as group members, crew cycle), `Vehicles.md` (the shared carrier-seating
  helper; habitat variant vs the player RigWalker; crew posts and gangway), the faction plan's status
  table (Task 4.3), and the `spacegame-tribe` skill (a tribe whose home is a moving carrier).
- `python3 tools/docs_check.py --index` clean.

## 10. Design principles consulted

- **GDC-L1-PERF-0004 / PERF-0001** (objective/contextual) — the agent budget in §7 is measured, not
  assumed.
- **GDC-L1-LEVEL-0001** (contextual) — a red column of walkers on the horizon pulls the eye to the city
  without a marker; the palette choice serves that.
- **GDC-L1-PROD-0002** (contextual) — scope: indestructible walkers, presentation-only digging, no
  territory, no traders and no disembark-to-fight while marching are deliberate cuts, each with a known
  later home.

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| `NpcTaskModule`/`FormationModule` do not drive a `LeggedDriver` walker cleanly | Spike first (plan Task 1) on a habitat and a crawler; stop and re-plan if it fails. |
| A prefab variant cannot drop the RigWalker's mount components cleanly (NGO, saved references) | The spike builds the variant too; if removal breaks it, the builder instead disables them and marks the station non-interactable, recorded in Gotchas. |
| Machines collide in formation | Wide shape + large per-prefab radii; watch it in the spike. |
| The gangway is off the NavMesh on rough ground | Sample the NavMesh around the marker at unseat time (`NpcSeating.Restore` already warps onto it). |
| Seated crew mis-pose or unparent on clients | Same path as sky riders, which is client-verified; check in the client pass. |
| A worker crawler wanders off and falls behind | No gate (as built): the crawler outpaces the leader (3.2 vs 2.7 m/s) and formation's regroup brings it back. |
| Agent cost | Profile (§7); crew count and worker count are tunables. |
