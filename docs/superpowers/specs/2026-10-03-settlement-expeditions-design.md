# Settlement Expeditions — design

Status: **revision 5, 2026-10-04.** Revision 4 was approved by the user on 2026-10-03. Revision 5 folds in the
corrections the Phase 0–1 plan made and what building Phases 0–1 found (§0.2, §1, §3.3, §4, §10.1, §12, §14.2,
§17). Phases 0–1 are built but not yet verified in play: see
[the progress file](../plans/2026-10-03-settlement-expeditions-progress.md). The system reference is
[Expeditions.md](../../AI/systems/Expeditions.md).
Related: [2026-09-16-rosters-and-war-parties-design.md](2026-09-16-rosters-and-war-parties-design.md)
(the runtime-group and war-party machinery this builds on), [2026-09-13-nomad-settlements-design.md](2026-09-13-nomad-settlements-design.md),
[2026-10-02-agent-restructure-design.md](2026-10-02-agent-restructure-design.md) (stale: its Phase 1 brain was discarded
2026-10-03, so this spec depends on none of it). System docs: [Residents.md](../../AI/systems/Residents.md),
[AgentSystem.md](../../AI/systems/AgentSystem.md), [ResidentReputation.md](../../AI/systems/ResidentReputation.md),
[Hogtie.md](../../AI/systems/Hogtie.md), [CarriedAgent.md](../../AI/systems/CarriedAgent.md),
[LeashSystem.md](../../AI/systems/LeashSystem.md).

A settlement sends **bands of 3–10 of its own residents** out into the world for **several in-game days**:
- to hunt dune rats (bringing them back alive or dead);
- to scout the land;
- to keep up an outpost;
- to raise an antenna;
- or, if someone has killed or hurt one of their own, to **hunt that person down**.

Every band has warriors, and everyone in a band is armed. **One band is always out.** The whole settlement sees each
band off with a ceremony. Members are missing from home while away. They come back as the same people, or they don't
come back, and **the dead are remembered while newcomers keep the settlement alive.**

---

## Decision log

Everything here was decided by the user on **2026-10-03**. The sections that hold each decision are in brackets.

| # | Decision |
|---|---|
| D1 | **Purpose:** bands build up the world and its story, and they are **the way to meet people outside settlements**. Trade and quests with bands come later, and are planned (§0.1). |
| D2 | A band member's real body may be **hidden at home** while away (§4). |
| D3 | The player must be able to **watch a band leave**. Every departure is a **ceremony**: the whole settlement gathers, the band lines up, someone gives a long speech, everyone listens, then everyone says goodbye (§10.1). |
| D4 | **One band is always out** (§3.1). The **kind of band is somewhat random** (§3.1). |
| D5 | Bands **pitch camp every night** with seating, a fireplace, tents and mattresses, and **keep a night watch** (§5.5–5.6). Outposts can be used **as shelter** (§7.3). |
| D6 | **Only band members carry weapons, and all of them do.** Not every member is a warrior: **on the road the others are half-warriors** (§3.2, §6). |
| D7 | **Spears are real melee weapons; bands also carry working electric harpoon guns** (§6.3). |
| D8 | Bands **never attack others, but defend themselves at any cost** (§5.3). The one exception is hunting down a wrongdoer (D11). |
| D9 | **Residents can die, including off-screen.** Deaths are **remembered**, and **newcomers are born or arrive** to keep the population balanced (§3.4). |
| D10 | Warriors per settlement: **6–8 for a small settlement, 12 for a large one** (§3.3). |
| D11 | Bands hunt a player down **only for killing or hurting someone**, and **only that player**. Every player is tracked. **Catching and beating the player settles the grudge.** Such a band is the settlement's **war party** (§7.5). |
| D12 | **Hunting means dune rats for now**: caught and brought home, or killed in the field. More prey later (§7.2). |
| D13 | **There is no settlement economy.** No stores, no needs (§3.1). |
| D14 | Outposts are **shelter**, and sometimes **need maintenance** (§7.3). |
| D15 | **No hiring, joining, trading, lying or looting yet** (§16). |
| D16 | **Full multiplayer support** (§14). |
| D17 | The band leader is **a warrior picked at random** (§3.2). |
| D18 | Trip length: **1–2 real hours is the norm, with shorter and longer trips** (§12). |
| D19 | The design supports **any settlement; only Raxy content is built now** (§1). |
| D20 | Children are born but **do not grow up yet**; arrivals keep the adult balance (§3.4). |
| D21 | **Grudges spread by gossip**, within a settlement and **between settlements when their members meet** (§7.6). |
| D22 | After the top-tier war party is defeated, the settlement **gives up and fears** the player (§7.5). |

---

## 0. Purpose and starting point

### 0.1 What it is for

Bands are how the world outside the walls gets people in it. The player meets them on the road and at night by a
fire. They learn who they are and what they are doing, and see the consequences come back home: a ceremony, a
homecoming, a name remembered. **The measure of success is how often a roaming player meets a band, and whether that
meeting is worth having.** The off-screen simulation exists so that those meetings and the homecomings are true. It
is not an end in itself.

Two consequences:
- **Encounter frequency is a design target, not an accident** (§5.7).
- Every feature is judged by whether it shows up in a meeting or at home. Anything that only exists in a record is cut.

### 0.2 What exists today

| Piece | State | Consequence |
|---|---|---|
| Resident "trips" | Solo, same-day, 60–150 m out, a pose and a prop. Nothing is killed or found. | Unchanged. Everyday life near home. **Residents on these trips are not armed** (D6). |
| `NpcWorldSim` groups | Virtual records moving in straight lines, spawned as bodies within 250 m of a player. Saved and server-only. | The **movement host** for a band. |
| Group member identity | None: members are re-rolled every spawn and dead ones come back (`NpcGroupComposition.Resolve`, `NpcWorldSim.cs:533`). | Bands keep their own member list (§4.2). |
| Off-screen combat | None. | Rolled from real threats (§5.3). |
| Group sim clock | Real seconds; residents run on game minutes. | Bands run on **game minutes** (§5.1). |
| Day length | `Sun.prefab` `cycleDuration: 1600`, so **1 game day ≈ 26.7 real min**. | 3 days ≈ 80 min. |
| The Raxy settlement | One settlement, in `Chunk_6_3`, with 70 residents authored at edit time, each with a stamped `SaveableEntity` id and a `Resident.index`. 8 Guards and 1 Hunter. | Below D10's quota for its size (§3.3). **No resident is ever added or replaced** today (§3.4). |
| Raxy combat | Fists only. All 89 Raxy tools are inert, including the spear and the harpoon gun. | Real weapons (§6.3). |
| World sites | **No** `WorldSiteMarker` in any chunk scene; the only one is on `SkyCityFleet.prefab`, which no scene places (found 2026-10-04). The registry only knows sites in chunks loaded this session. | A baked site catalog (§8.1). It bakes 0 sites until markers are placed. |
| Map | 4000 × 3000 m; the NavMesh is world-wide and always loaded. A day's walk is ~3 km. | Long trips come from stages, not distance (§5.1). |
| Dune rats | Hand-placed only, in `Chunk_6_3`, `Chunk_7_4` and `Chunk_7_5` (`DuneRat`, `DuneRat_Penned`). No spawner. A rope can lift and carry a dune rat (CarriedAgent.md). | Rat colonies are needed (§7.2). |
| Capture tools | `Hogtie` ties a body that is already down; `Leash` ropes anything. Both are player artifacts. | NPC use is new (§7.2). |
| Mounts, wagons | One NPC rider per mount (`NpcPassenger`); no wagons. | Later phases (§11). |

---

## 1. Architecture

| Owner | Where | Owns |
|---|---|---|
| **`ExpeditionDirector`** | on the `NpcWorldSim` object in `persistentScene`, beside `WarPartyDirector`. Server-only. | Every band in the world: records, stages, off-screen resolution, the band's group and its stand-ins (`BeginHandOff`). **Every settlement's state too** (rotation, roster snapshot, rest days; later grudges, tales and the known world), keyed by `settlementId` and seeded from the baked site catalog, so "one band always out" holds while the settlement's chunk is unloaded. Saved under `expeditions`. |
| **`SettlementExpeditions`** | on each `Settlement` whose culture has an expedition profile. Server-only; registered with the director only while its chunk is loaded. | Only what needs bodies: ceremonies, muster, walk-out, hand-off, walk-in, applying `Away`. **Keeps no state.** With the chunk unloaded, departures and homecomings run abstractly in the director (§10.1). |
| **`SettlementCitizens`** | on each `Settlement`. Server-only. | The population cycle: remembrance, newcomers, births (§3.4). |
| `NpcWorldSim` | unchanged role | **Moves** bands, as it moves war parties today. |

- **Nothing in the code names Raxy (D19).** Goals, kits, camp kits, speeches, lines and role mappings are data hung
  off the settlement's `SettlementCulture`. Only the Raxy assets are authored now.
- Each class keeps one job, so neither `NpcWorldSim` nor `Settlement` becomes a catch-all.

---

## 2. Principles consulted

- **`GDC-L1-SYS-0003`**: *emergence, bounded* (contextual, 4). A few general stages meet existing systems (fauna,
  reputation, rumours, the player) and produce unauthored stories: "the rat hunters never came back; the stranger was
  seen near their camp". The bounds are §3.3 (never strip the walls), D8 (never the aggressor) and §3.4 (the
  population refills).
- **`GDC-L1-SYS-0006`**: *legibility* (contextual, 4). The ceremony, the evening announcement, the camp fire on the
  horizon, the talk on the road, the homecoming and the remembered names are all the *visible* side of the system. The
  off-screen odds may stay hidden, because the consequences are shown. **One legibility trap is designed out:** if bands
  died only when nobody watched, players would read the system as rigged. So off-screen risk comes **only from threats
  that would also hurt them live** (§5.3).
- **`GDC-L1-DESIGN-0006`**: *legible consequences* (contextual, 4). Hurt a band member and you are hunted. Be caught and
  beaten and it ends, and you hear about it. Help them and it comes home as favour.
- **`GDC-L1-PROTO-0002`**: *riskiest first* (objective, 4). The identity hand-off goes first (Phase 1), and the
  aimed-gun rig is a Phase 0 spike.
- **`GDC-L1-PROD-0002`**: *hidden tails* (contextual, 4). Mounts, wagons, trade, quests and births of growing children
  are each phased later or recorded as later.

None of this has been playtested here. Every number in §12 is a starting guess.

---

## 3. The settlement side

### 3.1 The rotation: one band always out

`SettlementExpeditions` runs a rotation:

1. **When the band that is out turns for home** (enters `ReturnHome`) or is declared `Lost`, the settlement **chooses
   the next band** (below) and announces it that evening (§10.1).
2. **The next morning, the departure ceremony runs and the band leaves** (§10.1). For a while two bands can be out,
   and they may meet on the road and exchange news.
3. With no band out (a fresh world, or a load that finds none), a band is chosen at once and leaves the next morning.
4. If no band can be formed (§3.3), the settlement logs a warning and tries again every morning. "Always one out" is
   a rule the player can see, so failing it is never silent.

**Choosing the kind of band (D4, D13):** a **weighted random draw** over the goals that are possible right now. There
are no needs and no economy:

| Goal | Possible when | Base weight |
|---|---|---|
| **Scout** | always | 3 |
| **Hunt** (dune rats) | a known rat colony (§7.2) | 3 |
| **Outpost** | a known outpost; weight ×2 while one is in poor condition (§7.3) | 2 |
| **Antenna** | a known `AntennaSite` without this settlement's antenna (§8.3) | 1 |
| **Vengeance** | an open grudge (§7.5) | **not drawn: it takes the next slot outright** |

The draw is seeded and saved (§4.2). The goal that just ran gets `varietyPenalty` (×0.5).

### 3.2 Who goes

**Roles.** `ResidentArchetype.expeditionRoles` is an appended flags field:

| Role | Archetypes (Raxy mapping) | Job on the road |
|---|---|---|
| **Warrior** | Guard, TowerGuard, Hunter | Front line; first on watch; one of them leads |
| **Hunter** | Hunter, Butcher, Herder | Tracks, catches and kills prey |
| **Scout** | Scout, Lookout | Finds waypoints and sites; better news |
| **Bearer** | Hauler, Drover, Stablehand, WoodCarrier, OreCarrier | Carries tents, leads captured rats |
| **Builder** | Mechanic, Tinker, Smith | Outpost repairs; raises antennas |
| **Healer** | Healer | Lowers off-screen death odds (§5.3) |
| **any** | every adult not a child | Fills out a band |

**Composition by goal.** Size is clamped to 3–10, and **Warrior min ≥ 2 always**:

| Goal | Warrior | Others | Typical size |
|---|---|---|---|
| Scout | 2 | Scout 1–2 | 3–4 |
| Hunt | 2–3 | Hunter 1–2, Bearer 1 | 4–6 |
| Outpost | 2 | Builder 1–2, any 0–1 | 3–5 |
| Antenna | 2–3 | Builder 1–2, Bearer 1–2 | 5–7 |
| Vengeance | 3–6 by tier (§7.5) | Scout 1, Healer 0–1, any 1–3 | 5–10 |

**The leader (D17)** is a warrior picked at random (seeded) when the band is formed. If the leader dies, another
living warrior is picked; with none left, the member with the highest nerve leads. The leader does the hailing,
lights the fire, sounds the horn and answers for the band in talk.

**Half-warriors (D6).** Members who are not Warriors are half-warriors **on the road only**. It is a property of
expedition mode (§4.1), so it ends at the gate:

| | Warrior | Half-warrior |
|---|---|---|
| Weapon | spear or harpoon gun, in hand on the road | spear, knife, hammer or harpoon gun, in hand on the road |
| Attacked, or an ally attacked | fights to the end (D8) | **fights to the end** (D8): nerve floored at `roadNerveFloor` (0.5) while out |
| Defending the band | front line | second line, `secondLineDistance` (6 m) behind; harpoon carriers shoot from there |
| Encounter | steps forward, weapon Ready | draws, stays behind |
| Night watch | first on the rota | fills the remaining shifts |
| Off-screen strength | 1.0 × health | `halfWarriorWeight` 0.5 × health |
| Work stage | guards the workers | does its trade, weapon stowed |

**Picking members** (`ExpeditionRules.PickMembers`, pure, seeded):
1. Eligible: alive, not away, an adult, has the role, and back from a band more than `restDays` ago.
2. Fill the `min` slots, then the `max` slots, within §3.3.
3. Once the first member is picked, **prefer that member's bonded residents** (`Resident.CloseTo`).
   - *As built (2026-10-04, user):* a slot may set `fillFromAnyAdult`. After every slot has taken its role holders
     (mins, then maxes), such a slot still short of its `min` takes any free adult for the rest, within §3.3. The
     stand-in keeps its archetype but draws the role's kit and plays the role: `MemberRecord.role` is the slot's role
     for every member, and road behaviour keys on it. Never on a Warrior slot. `Goal_Scout`'s Scout slot has it.

### 3.3 Bounds and the warrior quota

- **One band out in steady state; two during a hand-over; never more.**
- **Warrior quota (D10).** Each settlement has a `warriorQuota`: **6–8 small, 12 large**. The Raxy settlement
  (70 residents) counts as large, so 12.
  - It has 8 Guards and 1 Hunter today. Phase 0 **reassigns 3 existing adults to Guard** in the scene: their
    archetype and kit change; nobody is regenerated or moved. A full regenerate is avoided because the settlement holds
    hand edits.
    - *As built:* only adults whose body suits a Warrior archetype qualify, highest nerve first. The user approved
      #10 Rasha, #47 and #56 (2026-10-03). They become the settlement's **Guard**, not the first Warrior archetype
      their body suits (TowerGuard, which has one post).
  - From then on, newcomers keep the quota filled (§3.4).
  - *As built (2026-10-04, user):* the warrior quota is one of the profile's quotas; `roleQuotas` adds others in the
    same bed bands. The nomads keep **Scouts 1–2 small, 3 large** (Scout and Lookout archetypes): with one Scout left
    after the warrior picks, no Scout band could form while it rested. `Apply Role Quotas` recasts residents for every
    quota, warriors first; it never recasts a warrior or anybody twice in one run.
- At least `minWarriorsHome` = ⌈quota / 2⌉ (**6** here) warriors stay home.
  - With 12 warriors, 6 can go: enough for a regular band (2–3) and a hand-over band (2–3), or one Vengeance band of
    up to 6.
  - A goal whose warrior minimum cannot be met is skipped for the next one in the draw. Vengeance waits for the
    rotation slot after that.
- Home population never drops below `minHomeShare` (0.8).
- Warrior slots take only real Warriors.

### 3.4 The population cycle (D9)

Residents die: in live fights, off-screen, or by a player's hand. A settlement that only ever loses people empties
out, and the rotation stalls. `SettlementCitizens` keeps it balanced.

**Remembering the dead.**
- A `Remembrance` list on the settlement (saved): `{ name, role, howDied, where, day, killerProfileId? }`.
- **Residents speak of them**: lines with tokens ("Kesh would have liked this weather", "we lost Kesh to the dune
  rats near the red mesa"). They are said most often by bonded residents, and for `mourningDays` after the news.
- **A memorial**: the settlement's shrine gains a marker per name (a stone or candle, as data-driven slots on the
  shrine prefab, shown by count). The ShrineKeeper tends it, and mourners visit it in their free time.
- The departure speech names the remembered dead (§10.1).
- A dead resident's bed becomes a vacancy (`DayPlanner` already skips the dead).

**Newcomers arrive.**
- When the population is below the settlement's bed count, every `arrivalIntervalDays` (2) one newcomer arrives, or
  sometimes a pair or a family.
- They walk in along the road in daylight and are met at the gate by the leader of the guards and a family member of
  the dead person whose bed they take. They are then given the vacant bed.
- **Role:** the one most below quota (warriors first while under `warriorQuota`), else the dead person's trade.
- **Appearance:** drawn from the culture's `profiles` for that role, so newcomers do not wear the face of the dead.

**Births.**
- A bonded adult pair with room in their dwelling may have a child, at most one per `birthIntervalDays` (10) per
  settlement.
- The child appears in the family's home, with a line from the parents, and the settlement hears the news.
- **Children do not grow up in v1** (no ageing). Births add life; **arrivals keep the adult balance.** Ageing is
  recorded for later.

**How newcomers exist.**
- Authored residents are scene objects. **Newcomers are runtime spawns owned by the settlement's `SettlementCitizens`
  record**: prefab id, name, role, bed, bonds, seed and the `ResidentMemory` blob. They are re-spawned from that record
  whenever the settlement's chunk loads.
- This is the same one-owner rule as the band stand-ins (§4): **disowned from the world save, recreated from the
  record.**
- A newcomer is not a child of the settlement transform (NGO parenting rules); it registers with `SettlementSociety`
  explicitly. Today the roster is gathered once from children (`EnsureResidents`), so it must accept registration and
  rebuild the plans.
- A newcomer can go out with a band like anyone else; `Away` hides its runtime body.

---

## 4. Identity: one owner per resident

The Phase 1 risk.

### 4.1 Away, the hand-off and the stand-in

A resident's own body never leaves home. Authored residents cannot be migrated between chunks, would pin chunks
loaded, and would be teleported home by `ResidentRoutine`.

1. **Departure.** After the ceremony (§10.1) the real residents walk out down the road in a `Departing` segment, the
   only writer of their goal. Past `departRadius` (150 m) **and out of every player's sight** (`ObserverCheck`), each
   member goes **`Away`** and the band continues as **stand-ins**.
   - If a player watches all the way, they are swapped in place at `maxHandoffDistance` (400 m): the stand-in spawns
     at the resident's exact pose on the server tick that hides the resident.
   - Whether a client sees a pop is the Phase 1 measurement (§17).
   - A save during departure loads as already departed.
2. **`Away`** is a new `Resident` state beside `Offstage`. It reuses `GoOffstage`'s mechanics (motor parked, faction
   unregistered, `hidden` replicated). It is **not** cleared by the plan, and the routine's settle-teleport and
   bedtime backstop skip it.
3. **The stand-in** is the same prefab: `Resident.sourcePrefab`, stamped at Generate, or the newcomer record's prefab.
   It is spawned by `NpcSpawn.Create` and stamped in `beforeSpawn` with `ExpeditionMember { settlementId,
   residentKey, expeditionId }`. It copies the name and appearance overrides. Its resident stack runs in **expedition
   mode**:
   - the routine and planner stand down;
   - presence, hands, seating, voice and awareness stay on.

   It is disowned from the world save, because the band record is its save.
4. **Homecoming** is the mirror: stand-ins are swapped back at `maxHandoffDistance`, or earlier when unobserved, and
   the real residents walk in (§10.2).

`residentKey` = the authored `Resident.index`, or a newcomer's record id. One key type covers both.

### 4.2 The band record

`ExpeditionRecord`, plain C#, saved by the director:

| Field | Meaning |
|---|---|
| `id`, `settlementId`, `goalId`, `seed` | identity; all rolls use `System.Random` from `seed`, never `UnityEngine.Random` |
| `phase` | `Announced` · `Ceremony` · `Departing` · `Out` · `Returning` · `Home` · `Lost` |
| `stageIndex`, `stageMinutesLeft` | progress, in **game minutes** |
| `members[]` | `{ residentKey, kitId, health01, dead, deathCause, isLeader }` |
| `captives[]` | live dune rats being led home: `{ preyId, health01 }` (§7.2) |
| `carcasses` | count carried home (§7.2) |
| `news[]` | `{ kind, position, subjectProfileId?, day }` (§5.4) |
| `target` | site id, position or quarry profile |
| `camp` | `{ position, seed, pitchedAtMinute, shelterSiteId?, struck }` |
| `watchRota` | seeded watch order |
| `encounters[]` | **per player**: `{ profileId, state, standing, askedQuestion }` (§9) |
| `grudge` | for Vengeance: `{ quarryProfileId, tier, lead, leadAge }` (§7.5) |
| `gossip[]` | the tales the band carries: its settlement's copy at departure, plus whatever it hears on the road (§7.6) |
| `groupId` | the moving `NpcGroup` |

### 4.3 Folding and losses

- **Spawn:** health from the record, the kit, the name. The dead are not spawned; captives spawn on their ropes.
- **Fold** (the player leaves): health, deaths, captives and kills are read back into the record. Unlike other
  groups, **losses persist**: members come from `members[]`, never re-rolled.
  - *As built:* a death is taken from the stand-in's own `OnDeath`, not from the read-back, because a chunk unload
    destroys members with no read-back (and must not kill them). A spawn applies the record's `health01`, so a fold
    does not heal.
- **No loot (D15):** a dead stand-in drops nothing. Its body ragdolls as normal and is removed at fold.

### 4.4 Separate from tribe war parties

`NpcGroup` / `NpcGroup.Record` gain an appended `owner` (`""`, `"war"`, `"expedition"`).
- `WarPartyDirector`'s template lookup, sighting reports and restored-party adoption filter on `owner == "war"`, so
  they never adopt a band.
- `RestoreRecords` keeps a runtime record when a director claims its `owner`. Today it silently skips any
  `runtimeOnly` record without a quarry (`NpcWorldSim.cs:1176`).
- A Vengeance band is a war party *in behaviour* (§7.5), but it is owned and saved by the expedition director.

---

## 5. On the road

### 5.1 Time and distance
- Pace on foot 3.5 m/s; bands travel 06:00–19:00 (~870 real s), which is about 3 km a day. **A day's walk crosses most
  of the map**, so long trips come from stages and nights, not distance.
- Stage timers count **game minutes**.
- A clock jump settles missed stage ends in order, with a bounded catch-up loop. It never relies on `Day` changes,
  which skip day-close logic.
- **Second world:** while players are in the other world the sim does not tick, so bands are paused. That is
  intended.

### 5.2 Stages

| Stage | Live | Off-screen | Ends |
|---|---|---|---|
| `Travel(target)` | formation walk, weapons carried | record moves in travel hours | arrived |
| `Camp` | pitch → evening → sleep and watch → strike (§5.5–5.6) | still | dawn, struck |
| `Search(area, n)` | waypoints; the scout's spyglass cue at each | timer | `n` done or `maxMinutes` |
| `Hunt(colony)` | §7.2 | rolled (§5.3) | quota met or `huntMinutes` |
| `Repair(outpost)` / `Build(site)` | builders work, warriors guard | timer | done (§7.3, §8.3) |
| `Track(quarry)` / `Confront(quarry)` | §7.5 | war-party catch-up | caught, beaten, defeated or given up |
| `ReturnHome` | `Travel(home)`, leading captives | — | at the gate |

**Examples:**
- **Hunt**: `Travel(colony) → Camp → Hunt → Camp → Hunt → ReturnHome`.
- **Scout**: `Travel → Search(n) → Camp → Travel → Search(n) → Camp → ReturnHome`, with `n` rolled per band.

Length comes from the stage list plus rolls (D18).

### 5.3 Danger: defend at any cost, never attack (D8)

- **Bands start no fights**, except a Vengeance band against its quarry (§7.5), and the prey they hunt. Stances never
  make another faction hostile.
- **Attacked, they fight to the end.** Members do not flee, and the band does not break while an attacker stands. Only
  once the fight is over do the survivors decide: continue, or turn home (`ReturnHome`) if they are below half
  strength.

**Off-screen risk comes only from real threats.**
- `ThreatMap.Near(route)` sums the **actually hostile** things near the band's path: groups and creatures whose
  relationship to the band's faction resolves Hostile, dangerous fauna sites, and hunted prey that fights back.
- A stage's incident chance = `baseAccident` (0.02) + Σ threat × exposure hours, reduced by the watch at night (§5.6).
  So **if nothing hostile is near, almost nothing happens to them**, which is exactly what a watching player would see.
- **An incident** wounds a member (`health01 −= x`). A `deathChance` roll (0.15, ×0.5 per Healer) makes it fatal, and
  the death is recorded with its cause (`deathCause`: "a crawler", "rat bites").
- **A fight off-screen** compares band strength (warriors 1.0, half-warriors 0.5, × health) with the threat's strength.
  Bands defend to the end, so a lost fight can wipe the band out (`Lost`).
- **A player arriving mid-stage** sees the stage live. The off-screen roll then covers only the unobserved fraction of
  it.

### 5.4 News and losses
- `news[]`: `SiteFound`, `ThreatSeen`, `PlayerSeen`, `PlayerHurtUs` (with the profile), `PlayerHelped`, `PlayerTold`,
  `MemberDied` (who, how, where). All of them come home at the homecoming (§10.2).
- **No survivors** means `Lost`: the members stay `Away`, and after `missingDays` (2) past the expected return they are
  remembered as dead, cause unknown, "they never came back".
- **Deaths** are applied to the resident's own `HealthComponent` (an authored resident) or to the newcomer record, at
  homecoming or when declared missing. They then persist through the existing savers.

### 5.5 The night camp (D5)

**The kit.** Each member carries their own bedroll and seat. Bearers (else warriors) carry the tents, the leader the
fire ring, and warriors the watch-post gear.

| Piece | Count |
|---|---|
| Fireplace | 1 |
| Seats | ⌈n / 2⌉ |
| Tents | ⌈n / 3⌉ |
| Bedrolls | n |
| Watch posts | 1–2 |

The art comes from the decoration library; the bedroll and the rolled back prop are probably new models.

**The spot:**
1. An outpost within `campReach` (400 m) is preferred: shelter (§7.3).
2. Else a catalog `Camp` site within reach.
3. Else a sampled spot: flat (< 12°), reachable, and clear of the `ThreatMap` by `campHazardClearance` (150 m).

**The layout** (`CampLayout.Build(seed, members, kit)`, pure): fire at the centre, seats at 2.5 m, tents and
bedrolls at 5–7 m with doors toward the fire, posts at 10–12 m facing out. Each slot is checked against the NavMesh
and for overlaps; a bad slot is shifted or dropped, never left floating.

**Live:** pitch at dusk (~1 game h: each piece placed on a work cue, fire last) → evening at the fire (sit, eat, band
talk) → night (sleepers on bedrolls; the watch at the posts) → strike at dawn (reverse order).

**Network:** one networked `ExpeditionCamp` carries `seed`, `position`, `pitchedCount`, `fireLit` and `struck`.
Props are built locally on each machine from the layout, and seat ids come from slots so seat claims agree. A camp is
never saved as objects; the record rebuilds it. A player arriving at night finds it in its true state.

### 5.6 The night watch
- Night is split into `watchShifts` (3); each shift has 1 watcher (bands ≤ 4) or 2. Warriors come first in the
  seeded `watchRota`, and half-warriors fill the rest.
- A watcher stands at a post at **Ready**, walks a short beat, and wakes the next watcher at shift change.
- A sighting means a call, the horn, and the camp wakes armed. Then the encounter rules apply (§9), or defence (§5.3).
- Off-screen, a full watch multiplies night incident chance by `watchRiskFactor` (0.5).

### 5.7 Meeting them: encounter frequency (D1)

Bands are the way to meet people outside, so meetings must happen.
- **Routes are drawn toward the world the players use.** When a band picks among equal destinations (a Search
  waypoint, a camp spot, a colony), `ExpeditionRules` weights the choice by recent player presence: a decaying heatmap
  of where players have been, per 500 m chunk, server-side.
- **This is a bias, never a pursuit.** A band never steers toward a player who is currently in view (except a
  Vengeance band).
- **Target:** a player roaming outside meets a band (or its camp fire) about once per band trip, about once every
  1–2 hours. It is measured in playtest; `presenceBias` is the tuning knob.

---

## 6. Gear (D6, D7)

### 6.1 Kits
`ExpeditionKit`: `weapon` (required; the validator fails a kit without one), `tool?`, `beltItems[]`, `campShare`,
`backProps[]`, `garmentOverride?` (a backpack garment for the mount points).

- Kits are **not stock**: there is no economy (D13). A kit is simply the band's gear.
- The bag holds 4 items, so camp pieces ride as worn back props.
- **Only band members are armed.** Everyday trips, chores and posts at home keep the home hand rule, and Guards keep
  their spears.

| Goal | Warrior | Others |
|---|---|---|
| Scout | spear, signal horn | Scout: short spear, spyglass |
| Hunt | spear, knife | Hunter: **harpoon gun**, skinning knife, rope; Bearer: spear, rope, tent |
| Outpost | spear | Builder: hammer (weapon), wrench, parts |
| Antenna | spear | Builder: hammer, wrench; Bearer: spear, antenna parts (back), cable coil |
| Vengeance | spear, horn; **1 in 4 a harpoon gun** + knife | Scout: short spear; Healer: knife, medicine pouch; any: spear |

**The outside hand rule** (stand-ins only):

| Activity | In the hand | Stance |
|---|---|---|
| Walking, ceremony walk-out, homecoming | weapon | one-arm Carry |
| Encounter, watch, defence | weapon | Ready |
| Work stage | tool; weapon on the back or belt | the tool's |
| Pitching or striking | the piece | Carry |
| Sitting, eating, sleeping | nothing; weapon within reach | — |

Members **draw their kits at the ceremony**, so the hand-off to a stand-in shows no change of gear.

### 6.2 Stage stances
`ExpeditionStance` swaps targeting for a stage and restores it after:
- `Hunt`: the target prey only (dune rats).
- `Confront`: the quarry player only.
- Everything else: the peaceful default, with defence (D8).

**No stance ever makes a faction hostile.**

### 6.3 Weapons that work (D7)
- **Melee:** `MeleeToolStats { damage, reach, cooldown, cue }` on the tool asset drives `CloseCombatModule`. Without
  stats, the fist values apply. Spear ~30 damage, 3.5 m; knife ~18, 2 m; hammer ~22, 2.2 m.
- **Electric harpoon gun:** a real ranged weapon. It fires a projectile that deals shock damage and a **stun**
  (`stunSeconds` 1.5).
  - The stun knocks a dune rat down, and a downed animal can be tied (§7.2).
  - Raxy gain `NpcItemUseModule`; shots go over `NetMsg.ItemUsed`. No ammo, `reloadSeconds` 4, `preferredRange` 12–25 m,
    a knife inside 3 m.
  - The player version is a separate artifact task (`spacegame-artifact` skill).
  - **Unproven on the Raxy rig**: Phase 0 spike.

---

## 7. The goals

### 7.1 Scout
Visits `n` waypoints in an area the settlement knows little about (§8.2). `SiteFound` and `ThreatSeen` news grows the
known world. A Scout band is the cheapest fallback (3 members).

### 7.2 Hunt: dune rats (D12)

**Rat colonies.**
- A new world-sim group template, `dune-rat-colony`: wild, Fauna, one per catalog `AnimalGround` site tagged for dune
  rats. Each has a saved `count` that regrows by `regrowPerDay` up to `capacity`.
- Live, the colony's rats spawn and roam the site; off-screen it is just the count. This also gives the world its first
  self-sustaining wildlife.
- Rats are peaceful until provoked, and **fight back when hunted**. That is the hunt's real risk (§5.3).
- `PreyDefinition` (data) keeps hunting generic for the prey that comes later.

**The hunt, live:**
1. The band reaches the colony. Warriors spread to cut off retreat; the Hunter closes in.
2. **Catch** (`catchShare` of the quota, ~0.5):
   - stun with the harpoon gun (or a spear blow at low health);
   - **tie** the downed rat (`Hogtie`'s NPC entry point: the body is down, so `CanTie` holds);
   - **rope it** (`Leash.Create`) to a Bearer.

   The rat is led home on the rope. CarriedAgent already lifts and carries a dune rat on a rope, and the rat keeps
   walking with its brain on.
3. **Kill** (the rest): the carcass is field-dressed (skinning cue, ~10 game min) and carried on a pole between two
   members as a back prop.
4. The quota is met, or `huntMinutes` runs out, then `Camp` or `ReturnHome`.

**Off-screen:** caught and killed counts are rolled from the colony count, the hunters and the time spent. The colony
count drops by the same amount.

**At home:**
- Live rats are released into the settlement's pen (`DuneRat_Penned` behaviour). The pen's rats are a saved count on
  `SettlementCitizens`'s pen record, spawned from it, the same one-owner pattern again.
- Carcasses go to the Butcher's post as an errand prop. There is no economy, so this is purely a visible story.

**Network and save:** a captive is a runtime-spawned rat (a registered network prefab) on a rope. Its record lives in
`captives[]` until it reaches the pen. The tie and the rope replicate as they already do for players.

### 7.3 Outposts: shelter and maintenance (D14)
- Outposts (`RelayOutpost`, `LatticeOutpost`, `Outpost3`) are placed in chunks as `TradePost`-kind sites, renamed to
  an appended `SiteKind.Outpost`.
- **Shelter:** any band near an outpost at dusk camps there. The fire goes outside, bedrolls inside, and no tents are
  pitched. This needs an interior floor with NavMesh, which is checked per outpost prefab.
- **Condition:** each outpost has a saved `condition` (0–1) that decays by `outpostDecayPerDay`. It shows in 3 visible
  states: working lights and antenna dish; lights out; dish down and a door hanging.
- An **Outpost band** travels there and runs `Repair`: builders work, warriors guard. On completion `condition = 1`,
  and the visual state updates for everyone.
- Bands sheltering there **notice** a poor condition and bring it home as news, which raises the Outpost weight.
- `OutpostState` is a scene object in its chunk with a `SaveableEntity`, a networked state and a server-side decay. Its
  decay is computed from elapsed game time when the chunk loads, so it does not need to tick while unloaded.

### 7.4 Antenna
`Travel(site) → Camp → Build(site) → ReturnHome`. Builders build, warriors guard, and bearers carry the parts. On
completion the `SettlementAntenna` is spawned (§8.3).

### 7.5 Vengeance: the settlement's war party (D11)

**The grudge.**
- **Trigger:** a player kills a resident, or **hurts** one: a deliberate hit that lands, at home or on the road (a band
  survivor's `PlayerHurtUs` news counts).
- Accidental contact does not count (`ProvocationModule` already tells a push from a blow).
- A grudge is **per player** (`grudges[]` on the settlement: `{ profileId, severity, tier }`). Severity: **hurt** = 1,
  **kill** = 3.
- **Only the wrongdoer is targeted** (D11). The band tracks every player and talks to the others as normal (§9).
  Anyone who attacks the band is fought in self-defence (D8), and gets their own grudge for hurting someone.

**The war party.**
- A grudge takes the **next rotation slot** (§3.1).
- Size by severity and tier: hurt is 5–6 (3 warriors); kill is 7–10 (4–6 warriors); each tier adds warriors, within
  §3.3.
- It behaves as a war party: it uses `WarPartyRules` for the trail fix every `trailInterval` (with fuzz) and for
  catch-up only while unobserved.
- `Track(quarry)`, then on contact `Confront(quarry)`. The leader names the deed ("You killed Kesh."), then the band
  attacks the quarry only. A short warning beat lets the player know why.

**Endings:**

| Ending | When | Then |
|---|---|---|
| **Caught and beaten** | the band downs the quarry (health to the downed/death threshold) | **The grudge is settled.** The band goes home and tells it ("we found the one who killed Kesh"). The deed is forgiven in the settlement's memory. |
| **Defeated** | the quarry wipes out or breaks the band | The grudge **deepens** (tier +1, up to `maxTier` 2), and the next war party is larger. The fallen are remembered with the quarry named. |
| **Gave up** | the quarry is beyond `maxPursuitDistance`, offline, or in another world for `giveUpDays` | The band goes home; the grudge stays open for a later party. |

- **Beyond `maxTier`:** after a war party at the top tier is defeated, the settlement **gives up and fears** that player.
  Residents shun them and bands avoid them. Only time (`fearDecayDays`) lifts it. This bounds the
  kill-the-war-party-forever loop, which otherwise drains the settlement.
- **Not a tribe war, but it travels.** The Drifters do not join `FactionGoodwillLedger`. A grudge belongs to the
  settlements that **know** of the deed, and the knowledge spreads by gossip (§7.6).
- **Opening a grudge.** A grudge opens when a deed **reaches the settlement**: a witness at home, a band survivor's
  news, or gossip carried in (§7.6). Within the settlement it then spreads as deeds already do (`Rumours`, hearth and
  bedtime gossip). The war party is raised once the deed is **heard by the settlement** at the next evening gathering,
  not the instant it happens.
- **Settled and feared spread too.** "They caught and beat the one who killed Kesh" and "the stranger who broke three
  war parties" travel the same way as the deed itself (§7.6).
- **Respawn camping:** a band never waits at or approaches a player's ship. A downed-then-respawned quarry is beyond
  the band's reach by rule.

### 7.6 Gossip between settlements (D21)

What a settlement knows about players travels with its people. Bands are how it gets from one settlement to another.

**What travels: `Tale` records.** A tale is a compact, saved piece of knowledge:

| Field | Meaning |
|---|---|
| `subjectProfileId` | the player it is about |
| `kind` | `Killed` · `Hurt` · `Helped` · `Told` · `GrudgeSettled` · `Feared` |
| `victimName`, `originSettlementId`, `place`, `day` | for the lines: "the stranger who killed Kesh of the Red Mesa camp" |
| `severity` | for `Killed`/`Hurt`: the grudge weight (§7.5) |
| `hops` | how many mouths it has passed through |

Tales are deduplicated by `(subject, kind, victimName, originSettlementId, day)`, and each keeps its lowest `hops`.

**How it spreads.**
- **Inside a settlement:** the existing machinery. Residents' `ResidentMemory` hears deeds through `Rumours`, hearth
  and bedtime gossip. The settlement's tale list (on `SettlementExpeditions`) is the shared version of what its people
  know, and the source for grudges and standing.
- **Departure:** a band carries **a copy of its settlement's tales** (`gossip[]` on the record).
- **Meeting:** two bands meet when their groups come within `meetRadius` (200 m) of each other. This covers both live,
  as bodies, and folded, as records on a sim tick, and also bands sharing an outpost for the night.
  - They **exchange tales**, both ways, with `hops + 1`.
  - Live, the band halts, the two leaders walk to each other and talk for a few lines ("You've heard of the one in
    the grey suit?"), then both bands carry on.
  - Off-screen, the exchange is silent; each band records a `MetBand` news line for its homecoming.
  - Two bands from the **same** settlement (the hand-over) also exchange: fresh news reaches home early.
- **Homecoming:** the band's new tales merge into its settlement's list. The leader tells the most severe one aloud
  among the news (§10.2), and residents' memories hear it as a deed (an appended `ActKind.HeardTale`, with the origin
  settlement).
- **Newcomers** (§3.4) arrive carrying a few tales from wherever they came from, at `hops + 1`. That is a second road
  for gossip, and it needs no meeting.

**How spread grudges behave.** Hearsay is weaker than what a settlement saw itself:
- **Severity drops per hop** (`hopDecay` 1): a kill (3) becomes 2 after one hop, then 1, then nothing.
- **Severity ≥ 2:** the receiving settlement **opens its own grudge** and sends its own war party (§7.5). A murder
  heard first-hand from the band that witnessed it is enough to bring another settlement after you.
- **Severity 1:** **cold**. Residents and that settlement's bands treat the player as hostile in talk: they warn off,
  refuse the question, and give curt lines. They do not hunt.
- **`GrudgeSettled` closes** a matching grudge wherever it arrives. **`Feared` sets fear** (§7.5) at severity ≥ 2 and
  coldness below that.
- **`Helped` / `Told`** spread too, as favour at the same per-hop discount. A good name travels as well as a bad one.
- Tales older than `taleExpiryDays` (20) stop spreading and fade from lines. A grudge already opened from one stays
  until it ends.

**Bounds.** Hop decay means no tale crosses more than two hand-offs at full weight, so a single killing cannot turn
every settlement in the world into hunters. A settlement never re-opens a grudge for a deed it has already seen
settled.

**Today there is one settlement.** Cross-settlement spread is built and tested on the data side (EditMode tests and
the debug panel's *inject a foreign band*, §14.4). It shows in play only once a second settlement runs bands.
Same-settlement spread (hand-over bands bringing news home early) works from day one.

---

## 8. Sites and the known world

### 8.1 Site catalog
- A `WorldSiteCatalog` asset is baked at edit time from every marker in every chunk scene and merged into the registry
  at startup. A test fails on any marker missing from it.
- **Phase 0 content:**
  - `AnimalGround` (dune-rat colonies) ×4–6;
  - `Outpost` ×2–3, placing the existing outpost prefabs;
  - `Camp` ×6;
  - `AntennaSite` ×3–4 on high ground (not named "Relay");
  - `Ruin` / `ScrapField` ×4 as scouting targets.

### 8.2 What a settlement knows
`knownSites` and `knownChunks` on the settlement are saved. They start within `homeKnownRadius` (800 m). Scouting
grows them, and Hunt, Outpost and Antenna goals only pick known sites.

### 8.3 The antenna
- `SettlementAntenna` is a runtime-spawned, networked and saved structure (with a `prefabId`), built by an Antenna band.
- The settlement **knows everything within `antennaRadius`** (600 m): players seen there become rumours, and threats
  join the `ThreatMap`.
- A player can destroy it; that is a deed. The site then reopens for a new Antenna band.

---

## 9. Encounters and talk (D1, D11, D15)

**Per player.** `encounters[]` holds a state for each player near the band.

**The band's posture is the most cautious state among the players present.** For example, a friendly player standing
next to the quarry still finds the band at Ready.

| State | Enters | The band does |
|---|---|---|
| **Wary** | a player within `noticeRange` (60 m) | Halts. Warriors forward, weapons Ready; others draw and stay behind. |
| **Hail** | within `hailRange` (25 m) | The leader calls out, the line chosen by standing. |
| **Talk** | the player approaches calmly and interacts | Up to three beats (below). Warriors relax to Carry and keep watching. |
| **Warn-off** | the player aims a weapon, sprints at them, or comes inside `personalSpace` (6 m) uninvited | Provocation warnings, backing off. A blow that lands means defence (D8) and a grudge (§7.5). |
| **Confront** | the player is this band's quarry | §7.5 |

**Where the state starts:** the snapshot of the settlement's standing with that player (including what it has heard
by gossip, §7.6), plus `PlayerRead`. Friendly skips Wary; **cold** (a severity-1 tale) goes to Warn-off and refuses
talk; feared means the band keeps its distance; the quarry goes to Confront.

**Talk** goes through the residents' voice machinery (`ResidentVoice`, line tables, `NetMsg.ResidentSaid`; the
player's reply goes through `ResidentAddressed`):
1. **Who we are:** the leader names the band and the settlement.
2. **What we're doing:** the goal, place, days out and progress ("Two rats roped already; one more and we turn home").
3. **One question** per player per band, from the goal's table:
   - the player chooses **"Tell them"** (the true answer, computed from the world) or **"Keep it to yourself"**;
   - **there is no lying** (D15);
   - telling helps the band and comes home as `PlayerTold` favour.

| Goal | Asks | The true answer, from | "Tell them" does |
|---|---|---|---|
| Hunt | "Seen any dune rats?" | a colony or rats within `answerRadius` (300 m) | the hunt moves there |
| Scout | "What lies that way?" | the next catalog site on that bearing | `SiteFound` |
| Outpost | "Is the outpost still standing?" | the outpost's condition | the band knows what to bring |
| Vengeance | "Have you seen *name*?" | the quarry's real position | a trail fix. Telling on a crewmate is a real choice. |

- Small talk afterwards uses the existing remark lines plus band lines.
- **Not in v1:** trade, joining, orders, lying, looting.

---

## 10. Ceremonies and the home side (D3)

### 10.1 The departure ceremony

**The evening before:** the chosen members and their families talk about it at the hearth. Any resident asked says
who is going, where and why.

**The morning** runs as a settlement-wide plan block: `DayPlanner` gets a `Ceremony` segment for every resident at home,
and the night shift joins after its shift.
1. **The call** (`ceremonyStart`, 07:30): the leader sounds the horn, and everyone walks to the gathering place, an
   authored `SettlementSpot` with `SpotUse.Gathering` (the plaza).
2. **The line-up:** the band stands in a row facing the crowd, kits drawn and weapons in hand. The crowd stands in arcs
   from `CeremonyLayout.Build(seed, crowd, band)` (pure, NavMesh-checked stand points), children at the front.
3. **The speech:** the **speaker** is the Elder, else the Storyteller, else the oldest bonded resident not going.
   - The speaker faces the crowd and gives **a long speech of 8–12 lines** (`CeremonySpeech` table, tokens for the
     members' names, the goal, the place and the remembered dead).
   - Everyone holds the `listen` cue and faces the speaker (the existing `SpotUse` `listen` hold).
4. **Goodbyes:** families step forward to their member (a farewell cue and a line). Then the whole crowd says goodbye:
   a staggered burst of short lines, so many speak at once.
5. **The walk-out:** the band forms up and walks to the gate and down the road. The crowd follows to the gate, waves,
   watches them go, and disperses back to its day.

**Duration:** about 1.5 game hours (~100 s real). With the settlement's chunk loaded, the ceremony runs whether or not
anyone watches, so a player arriving late sees it in progress. With the chunk unloaded (no player anywhere near), the
departure happens **abstractly** in the director: the band's group is created folded at the road point and the
members are `Away` when the chunk next loads. Homecomings are the same: performed with bodies when the chunk is
loaded, abstract when it is not.

**Phase 1 (built):** no ceremony yet. The band musters for `musterMinutes` (30 game min) from `departHour` (07:30) at
the settlement's **muster spot**, a `SettlementSpot` with role `Assembly` (never planned as a stroll or leisure place).
Settlements have no gate, so the spot is **placed by rule**: on the lane whose trail reaches farthest from the centre,
`musterInset` (5 m) back from where its paving ends, facing out along it. Generate places it; existing settlements
get it from a menu. Phase 2's ceremony moves to the plaza.

**Multiplayer:** everything is ordinary resident movement, cues and `ResidentSaid` speech. Nothing new replicates
except the line-up stand points, which come from the seed.

### 10.2 Homecoming
- The band walks in, weapons carried, with rats on ropes and carcasses on poles. The horn sounds; families come to the
  gathering place.
- The leader **tells the news aloud**: the haul, sightings, help from strangers, the dead by name and cause, and the
  gravest tale heard from other bands.
  Grief lines follow, and the names join the remembrance (§3.4).
- Captives go to the pen and carcasses to the butcher. Members rest (`restDays`).
- **A war party's homecoming** tells the ending (§7.5).
- **A missing band:** after `missingDays`, the evening gathering remembers them.

### 10.3 While away
Empty beds, fewer guards on the walls, residents answering "where is X?", and a fire on the horizon at night.

---

## 11. Later phases: animals and wagons
- **Mounts:** a Raxy-ridden RobotHorse variant with `NpcPassenger` and a seat measured for Raxy scale. The stable's
  horses go `Away` as members do. Doubles the pace.
- **Pack animals:** a saddled Appa or Sandloper (its `WallInventory` is real cargo) led on a rope.
- **Wagons:** a separate spec. A networked, saved, hitched, NavMesh-aware towed vehicle is the largest tail in this
  document.

---

## 12. Tunables: starting values

| Tunable | Default |
|---|---|
| bands out | 1; 2 during a hand-over (rule) |
| goal weights / `varietyPenalty` | Scout 3 · Hunt 3 · Outpost 2 (×2 if poor) · Antenna 1 / ×0.5 |
| `warriorQuota` | small 6–8 · large 12 |
| `minWarriorsHome` / `minHomeShare` / `restDays` | ⌈quota/2⌉ / 0.8 / 2 days |
| `arrivalIntervalDays` / `birthIntervalDays` / `mourningDays` | 2 / 10 / 3 |
| `ceremonyStart` / ceremony length / speech lines | 07:30 / ~1.5 game h / 8–12 |
| `departRadius` / `maxHandoffDistance` | 150 / 400 m |
| travel hours / pace | 06:00–19:00 / 3.5 m/s |
| trip length (game days) | Scout 1–5 · Hunt 1–4 · Outpost 2–4 · Antenna 2–4 · Vengeance ≤ 5 (mostly 1–2 h real) |
| `baseAccident` / `deathChance` / Healer | 0.02 per stage / 0.15 / ×0.5 each |
| `watchShifts` / `watchersPerShift` / `watchRiskFactor` | 3 / 1–2 / 0.5 |
| `campReach` / `campHazardClearance` | 400 / 150 m |
| `roadNerveFloor` / `halfWarriorWeight` / `secondLineDistance` | 0.5 / 0.5 / 6 m |
| spear · knife · hammer | 30 dmg 3.5 m · 18 dmg 2 m · 22 dmg 2.2 m |
| harpoon | range 12–25 m, reload 4 s, stun 1.5 s |
| colony `capacity` / `regrowPerDay` / hunt quota / `catchShare` | 8 / 2 / 3–5 / 0.5 |
| `outpostDecayPerDay` | 0.1 |
| `noticeRange` / `hailRange` / `personalSpace` / `answerRadius` | 60 / 25 / 6 / 300 m |
| `presenceBias` | 0.5 |
| Vengeance `maxTier` / `giveUpDays` / `fearDecayDays` | 2 / 2 / 10 |
| `meetRadius` / `hopDecay` / `taleExpiryDays` | 200 m / 1 severity per hop / 20 |
| `missingDays` / `homeKnownRadius` / `antennaRadius` | 2 / 800 m / 600 m |
| *added in Phase 1:* `departHour` / `musterMinutes` / `musterInset` / `musterSpacing` | 07:30 / 30 game min / 5 m / 1.2 m |
| `handoffObserveRadius` / `observeRadius` / `walkLimitMinutes` | 400 m (capped by the sim's despawn radius) / 150 m / 240 game min |
| `travelReach` / `searchRing` / `arriveRadius` / `destinationSampleDistance` | 800–1600 m / 150–300 m / 15 m / 25 m |
| small-settlement warrior quota | equal bands of bed count across 6–8 (0–13 → 6, 14–26 → 7, 27–39 → 8, ≥ 40 → 12) |

---

## 13. Phases

| Phase | Delivers | Proves |
|---|---|---|
| **0. Groundwork** | Site catalog and placed sites (colonies, outposts, camps, antenna sites); `NpcGroup.owner` plus the restore rule; `settlementId`, `sourcePrefab` and `residentKey`; 3 adults reassigned to Guard (quota 12); Gathering and Muster spots; **spike: a Raxy fires the harpoon gun, host + client**; **debug tools** (§14.4) | Destinations exist; runtime records survive load; the gun risk is known; trips can be tested without waiting hours |
| **1. Round trip** (riskiest) | Rotation; announcement; a simple muster (no speech yet); visible departure → hand-off → stand-ins → `Travel`/`Search` → visible homecoming. Scout only, 3 members, on foot, kits as props | Identity across save, reload, chunk unload and a client joining; no duplicates, no vanished residents, no visible pop |
| **2. Ceremony and the road** | The full departure ceremony and homecoming; camp; watch; melee and harpoon; the outside hand rule; per-player encounters and talk; presence bias | It reads as people leaving home and travelling a dangerous land, and players meet them |
| **3. Hunt** | Rat colonies, the live hunt (stun, tie, rope, kill), captives home to the pen, carcasses, threat-based off-screen risk | Bands achieve things that show at home |
| **4. Life and death** | Off-screen deaths, `Lost` bands, remembrance (lines, memorial, speech), newcomers, births | The settlement lives through losses and stays balanced |
| **5. Places** | Outposts (shelter, condition, repair), antennas, known-world growth | Bands change the world |
| **6. Vengeance and gossip** | Grudges, war parties, endings, fear; tales, band meetings, hop decay, cold standing | Hurting someone has a consequence that comes for you, and travels |
| **7. Animals** | Ridden horses, pack animals | — |
| **8. Wagons** | own spec | — |

Every phase ends with its part of the §14 checklist on **host, a real client and a reload**, and with its system doc
updated. Phase 1 creates `docs/AI/systems/Expeditions.md` and the `docs/Human/the-systems.md` entry.

---

## 14. Multiplayer, persistence, testing (D16)

### 14.1 Authority
- **Server only:** both directors' worth of logic (`ExpeditionDirector`, `SettlementExpeditions`, `SettlementCitizens`),
  stages, encounters, grudges, colonies, outpost decay and threat maps.
- **What clients receive, and how:**
  - stand-ins, newcomers, captives, camps and antennas as networked spawns (all registered prefabs; every Raxy profile
    prefab is verified to be in `DefaultNetworkPrefabs`);
  - `Away` as the replicated `hidden` flag;
  - weapons as the hand slot. **Belt gear is bag state and does not replicate today** (HandTools.md:75), so the plan
    must decide how clients see belts;
  - speech through `ResidentSaid`, and answers back through `ResidentAddressed`;
  - shots through `ItemUsed`;
  - ties and ropes through the existing artifact paths;
  - outpost condition as a NetworkVariable.
- `EntityFaction` is not replicated, so nothing a client renders may depend on it.
- **Players are tracked by profile id** (encounters, grudges, standing), so a rejoining player is still the same
  person to the band.

### 14.2 Persistence
| Key / owner | Holds |
|---|---|
| `expeditions` (director, `persistentScene`) | `bands[]`: the band records |
| `expeditions` (director, `settlements[]` by `settlementId`) | rotation count, roster snapshot, `restUntilDay`, last goal (Phase 1); later grudges, **tales**, `knownSites` / `knownChunks`, news, remembrance |
| `SettlementCitizens` (settlement, chunk) | Phase 4: newcomer records, the pen count |

- **Not saved:** stand-ins (the record rebuilds them), `Resident.away` (the director answers it), and a kit weapon
  lent to a resident at home (`ILentSlots`: its bag slot saves empty).
- After a load the rotation waits for the persistent scene's hydrate (`WorldSaveStore.OnSceneHydrated`), not for
  `SaveManager.OnLoadApplied`, which only fires when a player had a saved record. A band saved mid-muster loads as
  departed (Out, folded at the hand-off point); a band saved mid-walk-in loads as Home.
- The settlement's own `SaveableEntity` exists for identity only and is `External`: it writes no record.
| `NpcGroup.Record.owner` | appended |
| colony records | in the `npcworld` group records |
| `OutpostState` | in the outpost's chunk |

- The director hydrates before chunks, so `Away` is known before the settlement wakes.
- Rolls are seeded per stage, so reloading cannot farm outcomes.
- A reload with a band spawned leaves no stand-in in the world and no resident shown at home.

### 14.3 Verification checklist (host + real client + reload)
1. Evening: residents name who goes. Morning: the horn, the whole settlement gathers, the line-up, the speech
   (everyone listening), the goodbyes, and the walk-out. The client sees it all. Follow the band: no pop at hand-off.
2. On the road: the names match, everyone is armed. Wary → Hail → Talk; one question; "Tell them" changes their plan.
   Aim a weapon: warn-off.
3. Night: the camp is pitched (at an outpost if near one), the watch hails you. Reload: same camp, same watcher.
4. Hunt live: a rat is stunned, tied and roped, and a carcass goes on a pole. Home: the rat in the pen, the carcass to
   the butcher.
5. Hit a member: they fight to the end. Survivors tell it at home, and a war party comes for **you only**. Your
   crewmate can still talk to it. Get beaten: the grudge is settled and the story is told. Beat it twice at max tier:
   fear.
6. Save mid-trip, quit, load: the same stage, nobody duplicated, `expeditions` present in the JSON.
7. Kill one member and reload: still one short. Homecoming: the dead person is named, remembered at the shrine, and
   stays dead. Two days later a newcomer arrives and takes the bed.
8. Unload the settlement while a band is out, and come back after it is due: it has returned, and a new band is out.
9. Two clients on opposite sides see the same band, and each gets their own encounter.

### 14.4 Debug tools (Phase 0)
Trips last real hours, so verification needs a **server debug panel**. It can:
- force a goal;
- advance or jump a band's stage;
- simulate N off-screen days;
- kill or wound a member;
- trigger an arrival or a birth;
- open or settle a grudge;
- inject a foreign band carrying given tales (to test cross-settlement gossip with one settlement);
- print a band record.

All commands go through the server, so they work from a client too.

### 14.5 Tests (EditMode, pure where possible)
- `ExpeditionRulesTests`: weighted draw and gating, picks, bounds, bonds, the leader and succession, stage resolution,
  threat-only risk, the unobserved fraction, catch-up after a clock jump.
- `RotationTests`: always one out, never more than two, a warning when none can be formed.
- `PopulationTests`: arrivals fill the warrior quota first; births limited by interval and room; remembrance entries.
- `CampLayoutTests`, `CeremonyLayoutTests`: deterministic, reachable, no overlaps.
- `EncounterRulesTests`: per-player state, the band's posture is the most cautious, the quarry goes to Confront.
- `VengeanceRulesTests`: severity, tiers, endings, fear, never near a ship.
- `GossipRulesTests`: dedup, hop decay (kill → 2 → 1 → 0), ≥ 2 opens a grudge and 1 means cold, settled closes,
  expiry, a settled deed never re-opens, the exchange is symmetric.
- `ExpeditionPersistenceTests`: record, newcomer and pen round-trips; `owner` survives restore; no war-party adoption.
- `ExpeditionAssetTests`: Warrior min ≥ 2, every kit armed, kits fit, catalog complete, Gathering and Muster spots
  exist.

---

## 15. Open questions

None. Every question raised in review was answered on 2026-10-03 (D1–D22). The spec is ready for an implementation
plan once the user approves it.

## 16. Out of scope, recorded for later
- Hiring, joining, escorting or giving orders to a band; trading with a band; quests through bands (planned: D1).
- Lying to bands; looting band members (D15).
- Ageing (children growing up), and starvation or any economy (D13).
- Other prey than dune rats; bands meeting other settlements' bands; other settlements' content.
- Camp remains left in the world; burying the dead in the field; a map UI for bands and sites.

## 17. Risks
- **The identity hand-off**: a resident shown at home while `Away`, or a stand-in saved into a chunk (DEFECTS.md:41).
  This is Phase 1's whole purpose.
- **The visible pop** at an in-place swap. Fallback: swap only while unobserved. Stand-ins can only exist within the
  sim's despawn radius (360 m), so a watcher farther away counts as not watching, as it does for every group's fold.
  Not yet measured on a client.
- **The routine fights `Away`** (settle-teleport, bedtime backstop): both must check `Away` first.
- **Runtime residents** (newcomers): roster registration, plan rebuilds, and every system that gathered the roster
  once (`EnsureResidents`, `HouseVisits`, `Conversations`, `Rumours`) must see them.
- **The aimed harpoon on the Raxy rig**: the Phase 0 spike. If it fails, spears only, and the stun comes from a spear
  knockdown.
- **NPC tie and rope**: `Hogtie` and `Leash` are player artifacts; an NPC entry point is new code on the authority path.
- **The ceremony at scale**: up to 70 residents walking to one plaza. Crowding, NavMesh islands and stand-point
  reachability are checked by `CeremonyLayout`'s validator and the residents baseline harness.
- **Gossip snowball**: a tale that never decays would turn every settlement against one player. `hopDecay` and
  `taleExpiryDays` bound it, and `GossipRulesTests` pin both.
- **Clock jumps** skip day-close logic. Stage catch-up, arrivals and decay are computed from elapsed game minutes,
  never from `Day` ticks.
- **Inherited group-sim bugs** (wiped-out non-war groups respawn full; folds heal). Bands avoid them by using the record;
  the shared paths are checked in Phase 0.
