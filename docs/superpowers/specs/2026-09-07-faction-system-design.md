# Factions and Tribes — design

Status: **draft, 2026-09-07** — all decisions taken except the tribes' real names (see
[§11](#11-open-questions)). Implementation plan:
[2026-09-07-faction-system.md](../plans/2026-09-07-faction-system.md).

Two meters, in the user's words: **each faction has its own goodwill meter** (toward each
player) and **each agent has its own aggression meter**. §3.3 and §3.4 are those two meters.

Decisions taken 2026-09-07: goodwill is **per player**; four factions in the first release
(**Sand, Mechanics, Sky, Clankers**); Clankers are hostile to **people only**; tribes are
**neutral to each other**, Sand owns the DuneFoil and Mechanics own the DesertCrawler and
RigWalker; goodwill recovers by **slow decay plus amends**; Sky nomads **wear and deploy** the
wing pack themselves (Option B); bounty hunters exist on **both** sides, as a human **Outlaw**
faction and as Clanker **outriders**; Clankers are **new prefabs**, the patrol robots retired later.

Every humanoid or "smart" thing in the world belongs to a faction. Nomads are split into
tribes, each with its own people, animals, vehicles and gear. Factions hold a stance toward
each other (enemy / neutral / friend), each tribe keeps a running opinion of each player that
the player's actions move, and any single agent that is pushed far enough turns on its
attacker and tells its neighbours. The Clankers — the robot cowboys borrowed from Red Planet
Rampage — shoot every person they meet and take what they carried.

This document explains the whole system: what already exists, what is added, how the pieces
resolve into one answer per pair of entities, and what the player sees. Read the
[AgentSystem](../../AI/systems/AgentSystem.md) reference first if the module stack is
unfamiliar.

---

## 1. Vocabulary

| Word | Meaning here | Backed by |
| --- | --- | --- |
| **Faction** | A side. Anything targeting cares about: Humans, each nomad tribe, Clankers, Fauna, Wildlife, the arena teams. | `FactionDefinition` asset (exists) |
| **Tribe** | A faction that is a *people*: it has a roster of members, mounts, vehicles, gear and a voice. Design word, not a type. | `FactionDefinition` + `FactionRoster` (new) |
| **Stance** | How faction A feels about faction B in general: `Hostile`, `Neutral`, `Allied`. Authored. | `FactionRelationshipTable` row, or a faction's `defaultStance` (new) |
| **Goodwill** | A number per (faction, player): how a tribe currently feels about one player, moved by what that player does. Decides welcome at their settlements and, at the extremes, whether they attack on sight. | `FactionGoodwillLedger` (new) |
| **Aggression** | A number per agent: how close *this* nomad is to drawing on the person in front of it. Fed by damage, gunfire, a weapon pointed at it, trespass. Crosses a threshold into a **grudge** — anger at one attacker, with a leash and a calm-down clock. | `ProvocationModule` (exists; gains the meter) |
| **Alert** | One provoked agent telling nearby friends who to fight. | `AlertBroadcaster` / `AlertReceiverModule` (exist, unwired) |
| **Roster** | What a tribe is made of: prefabs by role, animals, vehicles, item pools, worn-gear pools, chatter, colours, home sites. | `FactionRoster` (new) |
| **Relationship** | The single resolved answer for a pair of *entities* right now, after stance, goodwill and grudge are all consulted. | `EntityFaction.GetRelationshipWith` (exists, gains two lookups) |

The rule that makes the rest simple: **targeting only ever asks one question — "what is my
relationship to that entity right now?" — and every layer below feeds that one answer.** No
module reads goodwill, stance or grudge directly.

---

## 2. What exists today

The faction *identity* layer is complete and persisted. The *behaviour* layer is half-wired.

### 2.1 Working and kept

| Piece | Where | Notes |
| --- | --- | --- |
| `FactionDefinition` | `Assets/Game/Scripts/agents/Faction/` | GUID-stamped id, display name, debug colour. Self-registers in `Registry<T>` for saves. |
| `FactionRelationshipTable` | same | Pair → stance. Same faction = `Allied`. **No row = `Neutral`.** One live table: `GlobalRelationships.asset`. Indexed on first use. |
| `EntityFaction` | same | The targetability marker. `Ensure()` on spawn paths. `SetFaction` at runtime (arena re-teaming). |
| `EntityTargetRegistry` | `agents/Core/` | Every `EntityFaction` registers; `Query` / `ResolveNearest` by relationship. |
| `EntityFactionSaveable` | `Core/Persistence/Adapters/` | Saves faction + table by GUID. |
| `ProvocationModule` + `ProvocationSaveable` | `agents/AI/Targeting/`, adapters | Peaceful-until-hurt. Attributes the hit to the attacker's `EntityFaction` root. Leash 45 m, calm-down 60 s. Re-asserts the target every frame so a `Neutral` faction's target sticks. Persisted. |
| `AgentTargeting` | `agents/AI/Targeting/` | Only ever *acquires* `Hostile` candidates; `ForceTarget` for everything else. `IsFightingWith` (used by dialog to refuse chat mid-fight). |
| `DialogInteraction.CanInteract(interactor)` | `Gameplay/Interaction/` | Refuses conversation with the person the NPC is fighting. |
| `NetDamage.Apply(target, amount, source)` | `Gameplay/Health/` | Carries `source` across the wire, so a **client's** hit still arrives on the server attributed to that client's player root. Attribution for goodwill and grudges works for clients without new netcode. |

### 2.2 Present but unwired or wrong

| Finding | Evidence | Consequence |
| --- | --- | --- |
| **`AlertBroadcaster.Broadcast` has zero callers.** | `grep '\.Broadcast(' Assets/Game/Scripts/agents` → nothing outside `AgentActionRelay`. | "Alert allies when hostile" does not happen today. Receivers exist only on the five robot prefabs. |
| **Alert delivery is physics-based and faction-blind on the receiver side.** | `Physics.OverlapSphereNonAlloc(..., receiverLayers)`; `receiverLayers = Nothing` only warns. `ReceiveAlert` does a bare `ForceTarget`. | A `Neutral`-faction receiver (any nomad) would drop the alerted target within seconds — the documented "gunshot target does not stick" failure. |
| **Hurt noise aggro is faction-blind.** | `HealthReactionModule` emits `NoiseType.Hurt` with `LastDamageSource` as instigator; `NoiseReceiverModule.aggroOn` defaults to `Alert \| Hurt` and never checks relationship. | Any listener in radius targets the attacker — a Clanker would "avenge" a nomad. Nomads carry no `NoiseReceiverModule` today, so it is latent. |
| **Two faction assets are misnamed.** | `PlayerFaction.asset` and `NPCFaction.asset` both have `factionName: Robots`. | Any UI that prints the faction name says "Robots" for the player. |
| **`PatrolRobot 2` is on `PlayerFaction`.** | prefab `faction` GUID `e60e1b…` | It is allied with players and neutral to its siblings. |
| **No `EntityFaction` on** `BountyHunter.prefab`, `Ostrich.prefab`, `HumanoidRobot.prefab`, `CrabWalker6.prefab`, `DesertCrawler.prefab`. | prefab grep | Invisible to every targeting module, silently. The "Bounty Hunters" caravan hunts players it cannot be targeted by. |
| **`BountyHunterFaction` has no rows.** | table grep | Neutral to everyone, including the crew it hunts. |
| **Prefab folder casing drifts.** | `find` reports `Prefabs/agents/…`, prefab references say `Prefabs/Agents/…`. | Known project invariant: fix references, never the folder. Every path in this design uses `Agents` as the builders do. |
| **Only one tribe exists.** | `Nomad.prefab` + `Nomad_{Umber,Tan,Maroon,StrawHat}` all on `NPCFaction`; two caravan templates (`Nomad Caravan`, `Sand Nomads`) hard-code prefab lists. | Nothing ties prefabs, mounts, items or vehicles to a tribe. |

### 2.3 The faction roster as it stands

| Asset | Display name | Rows in `GlobalRelationships` | Used by |
| --- | --- | --- | --- |
| `PlayerFaction` | "Robots" (wrong) | Hostile↔Robot, Neutral↔NPC (explicit), Hostile↔Wildlife | `PlayerCharacter.prefab`, `SpawnManager.playerFaction`, `PatrolRobot 2` (wrong) |
| `NPCFaction` | "Robots" (wrong) | Neutral↔Player, Neutral↔Robot | all five nomads, `NomadOstrich` |
| `RobotFaction` | Robots | Hostile↔Player, Neutral↔NPC | `PatrolRobot`, `1`, `3`, `DeathmatchBot` |
| `WildlifeFaction` | Wildlife | Hostile↔Player | DuneRat, Sandloper, Vrescal |
| `FaunaFaction` | Fauna | none (peaceful by construction) | Appa, Golem |
| `BountyHunterFaction` | Bounty Hunters | none | nothing |
| `TeamRed/Blue/Green/Yellow`, `Solo1..16` | arena | all-pairs hostile | `MatchManager` only |

---

## 3. The model

Three layers, one resolver. Each layer answers a different question and lives on a different
object, so none of them needs to know about the others.

```
                who is asking?  →  EntityFaction.GetRelationshipWith(other)
                                           │
              ┌────────────────────────────┼─────────────────────────────┐
              ▼                            ▼                             ▼
   3.3 AGGRESSION (per agent)  3.4 GOODWILL (per faction, per player) 3.2 STANCE (per faction pair)
   ProvocationModule           FactionGoodwillLedger                  FactionRelationshipTable
   "that one pushed ME too far" "that player has been hurting US"     "we have always hated them"
   → Hostile to that entity    → Hostile/Allied to that player        → the authored default
   leash + calm-down           thresholds + decay                     + defaultStance fallback
              │                            │                             │
              └──── first non-null wins, top to bottom ─────────────────┘
```

The resolver is a pure static, `FactionRelations.Resolve(EntityFaction self, EntityFaction other)`,
so it can be unit-tested with no scene. It does not change `AgentTargeting`, any module, or the
registry: they keep calling `GetRelationshipWith`, which now delegates to it.

### 3.1 Identity — unchanged

`FactionDefinition` stays exactly what it is: a GUID-stamped asset that saves and the arena can
hand out at runtime. It gains **two fields**:

| Field | Type | Purpose |
| --- | --- | --- |
| `defaultStance` | `FactionRelationship` (default `Neutral`) | Stance toward any faction the table has no row for. `Hostile` for Clankers. **Nothing else sets this**: the "peaceful = zero rows" rule in the agent doc stays true for every faction that leaves it at `Neutral`. |
| `roster` | `FactionRoster` (optional) | The tribe's people and things. Null for Fauna, Wildlife and the arena factions. |

The four misnamed or misplaced assets are fixed, not recreated (GUIDs are in prefabs and saves):

| Asset | Change |
| --- | --- |
| `PlayerFaction.asset` | rename file → `HumansFaction.asset`, `factionName` → "Humans" |
| `NPCFaction.asset` | rename file → `SandTribeFaction.asset`, `factionName` → "Sand Tribe" (keeps the five nomads and both nomad caravans on the right side with no prefab edits) |
| `RobotFaction.asset` | rename file → `ClankerFaction.asset`, `factionName` → "Clankers", `defaultStance = Hostile` |
| `BountyHunterFaction.asset` | rename file → `OutlawFaction.asset`, `factionName` → "Outlaws", one row Outlaws↔Humans `Hostile` (§3.7) |

### 3.2 Stance — one table plus a default

`FactionRelationshipTable.Get(a, b)` today: same → `Allied`; row → row; else `Neutral`. It
becomes:

1. same faction → `Allied`
2. explicit row → that row (unchanged)
3. otherwise, if either side's `defaultStance` is not `Neutral`: `Hostile` if **either** says
   `Hostile`; `Allied` only if **both** say `Allied` (nobody is your friend unilaterally)
4. otherwise `Neutral`

Step 3 is what makes "robots hate everyone" one checkbox instead of one row per faction — and,
more importantly, one row per *future* faction that would otherwise be forgotten. Fauna stays
peaceful: it has no rows and a `Neutral` default, so nothing acquires it and it acquires nothing,
exactly as the agent doc describes.

**Clankers attack people, not animals** (decided). Their default is `Hostile`, and two explicit
`Neutral` rows — Clankers↔Fauna and Clankers↔Wildlife — override it, because a row beats the
default (step 2 before step 3). Any *people* faction added later is hostile to Clankers with no
row at all; only a new *animal* faction needs a Neutral row, and the test in the plan pins both.

Authored matrix (rows are explicit table entries; blank = falls to default/Neutral; tribes are
neutral to one another, decided):

| | Humans | Outlaws | Sand | Mechanics | Sky | Clankers | Fauna | Wildlife |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Humans** | — | Hostile | | | | *default* | | Hostile (exists) |
| **Outlaws** | | — | | | | *default* | | |
| **Sand** | | | — | | | *default* | | |
| **Mechanics** | | | | — | | *default* | | |
| **Sky** | | | | | — | *default* | | |
| **Clankers** | | | | | | — | **Neutral** (row) | **Neutral** (row) |

Explicit `Player↔NPC Neutral` and `Robot↔NPC Neutral` rows exist today and are deleted: they
say what the default already says. The `Player↔Robot Hostile` row goes too, once the default
takes over (plan Task 1.1). Tribe–tribe feuds are a lever for later; the goodwill system
already lets the crew *become* a tribe's enemy, which is the feud that matters first.

### 3.3 Aggression — the per-agent meter

`ProvocationModule` today is binary: any hit above `damageThreshold` → hostile at once, with a
leash and a calm-down clock, persisted. It becomes a **meter**, `aggression` in `[0, 100]` per
agent, and "hostile" is the meter crossing `attackAt` (default 100). The existing grudge —
aggressor, leash, calm-down — is what the meter *becomes* at the top; nothing about it changes.

**Inputs** (serialized per prefab; a roster can override them per tribe):

| What the player does | Δ aggression | Notes |
| --- | --- | --- |
| Damages this agent | +`hitGain` × damage fraction (default enough that one solid hit is 100) | The old behaviour is the default: a real hit still means a fight. Chip damage does not. |
| Damages an ally within the alert radius | +40 | Arrives through the alert (§3.5). |
| Fires a gun within `NoiseReceiverModule` range | +15 per shot | Through the noise path; needs no line of sight. |
| Points a held weapon at this agent inside `menaceRange` (12 m) for more than `menaceDelay` (1.5 s) | +20 per second | Read off the player's aim: `EntityEquipmentController` already knows who is aimed at what. |
| Stands inside this tribe's territory while unwelcome (§3.10) | +10 per second | The settlement guard case. |
| Time | −`calmRate` per second (default 10) while below `attackAt` | Above it, the old leash rule applies instead: no cooling while the aggressor is inside the leash. |

**Bands** the agent passes through on the way up, and what each one shows (the telegraph the
old binary version could not give):

| Aggression | State | What it does |
| --- | --- | --- |
| 0 … 40 | calm | Errand as usual. |
| 40 … 80 | wary | `WatchModule` tracks the player; a `hostileLines` "warning" bark once; dialog prompt stays. |
| 80 … 100 | drawn | Weapon up (`IsAiming`), `StopAndFace` the player, a "last warning" bark; dialog refuses. |
| 100 | grudge | The existing `Provoke`: target handed to `AgentTargeting`, leash, calm-down, alert raised. |

The per-tribe flavour is in the numbers: Sand nomads have a high `calmRate` and forgive a
gunshot; a Clanker's `attackAt` is irrelevant because its stance already says Hostile; an
Outlaw's `menaceRange` is long. `GDC-L1-SYS-0006`: the rule is hidden, the feedback (posture,
bark, aim) is not.

Two additions on top of the meter:

- **It raises the alert** (§3.5). Reaching `attackAt` calls `AlertBroadcaster.Broadcast` if one
  is present, once per new aggressor, not per re-assertion.
- **It reports to the ledger** (§3.4). The damage event goes to `FactionGoodwillLedger` with
  the amount and whether it killed. The meter itself stays local to the agent and is saved by
  `ProvocationSaveable` (one appended field; the key `"provocation"` never changes).

Aggression meters go on **every tribe member, every Outlaw and every Clanker**, not only on
peaceful creatures. A Clanker is already hostile, so its meter is redundant most of the time,
but it makes `lastAttackerBias` coherent and costs nothing.

### 3.4 Goodwill — the aggression meter

**One number per (faction, player)** — decided: per player, not per crew. A signed float in
`[-100, +100]`, `0` = the authored stance applies unchanged. It is owned by the server, lives in
the persistent scene next to `NpcWorldSim`, and is saved as **player-scoped** state (the
persistence skill's PATH C, keyed by the player's profile), so a player's reputation follows
them into any world session they rejoin and one astronaut can be an outlaw to the Sand Tribe
while their crewmate trades with it.

What "per player" means for the rest of the system:

- **Resolution is per entity pair already.** `Resolve(nomad, playerA)` and `Resolve(nomad,
  playerB)` consult different ledger rows; `AgentTargeting` acquires only the player whose band
  says `Hostile`; `DialogInteraction.CanInteract(interactor)` is already per person. No module
  needs to know.
- **A tribe member fighting player A is still neutral to player B** stood next to them.
  The per-agent grudge covers B the moment B joins in (damage attribution), and the alert
  carries only the aggressor. This is the intended reading of "the player who fired is chased".
- **Identity.** The ledger key is the player's persisted profile id (whatever PATH C already
  uses), resolved from the player root's `EntityFaction` through the same component that binds
  player-scoped saves. Plan Task 3.2 pins the exact type.
- **Clients each receive their own bands**, not everyone's (§5).

**Events and deltas** (all serialized on the ledger so they can be tuned; numbers are starting
points):

| Player action | Δ goodwill with *that* faction | Δ with factions **Hostile** to it | Δ with factions **Allied** to it |
| --- | --- | --- | --- |
| Damaged a member (per hit, scaled by fraction of max health) | −2 … −10 | +1 | −1 |
| Killed a member | −25 | +5 | −10 |
| Killed a member's mount / vehicle | −15 | | |
| Killed a Clanker within 60 m of a tribe member who could see it | +3 | | |
| Traded with a member (barter system exists in code, unplaced) | +5 | | |
| Time (decay toward 0, per in-game hour) | ±2 toward 0 | | |

**Recovery is slow decay plus amends** (decided): goodwill drifts back toward 0 over hours of
play, and trading with the tribe or killing Clankers in front of them moves it faster. Decay
runs on the server clock while the world is loaded, from the saved `lastChangeTime` when it is
not, so an absence counts.

**Thresholds** on the ledger, per faction (overridable per `FactionDefinition`):

| Band | Goodwill | Effect on that player |
| --- | --- | --- |
| **Allied** | ≥ +60 | Resolves `Allied`. Members alert *for* the player, defend them, chatter warmly. |
| **Friendly** | +20 … +60 | Resolves whatever the stance says (usually `Neutral`), but trades/dialog open up. No targeting change. |
| **Wary** | −20 … +20 | Stance unchanged. |
| **Hostile-on-sight** | ≤ −40 | Resolves `Hostile`. `AgentTargeting` acquires that player on sight like Wildlife does. |
| **At war** | ≤ −80 | As above, plus caravan records of that tribe route *toward* that player's last known position (the bounty-hunter lead logic, reused). |

Hysteresis is mandatory: a band is entered at its threshold and left 10 points past it in the
other direction, or a player hovering at −40 sees every nomad flip between talking and shooting.

**Feedback-loop check** (`GDC-L1-SYS-0004`, objective/4). Damage → hostility → the player
defends themselves → more damage is a **positive loop**; without a brake, one accidental shot ends
with a whole tribe permanently at war with one person. The brakes are: decay toward 0, the hit
delta scaled by health fraction (a graze is not a murder), the leash on grudges so a player who
*leaves* is forgiven locally, and amends as an explicit way back.

### 3.5 Alerting — "tell the others"

When an agent is provoked it broadcasts once. The broadcaster is rebuilt on the registry, not
physics:

- `AlertBroadcaster.Broadcast(target, position)` → `EntityTargetRegistry.Query(self, Allied,
  position, alertRadius, buffer)` → for each result with an `AlertReceiverModule`,
  `ReceiveAlert(target, position)`. No layer mask to forget; `Allied` already means "same
  faction or an authored ally", and a goodwill-Allied tribe helps the crew for free.
- `AlertReceiverModule.ReceiveAlert` hands the target to **`ProvocationModule.Provoke`** when the
  receiver has one, and to `AgentTargeting.ForceTarget` only when it does not (robots). This is
  the fix for a `Neutral` receiver losing the target within seconds: the grudge is what holds it.
- **Cascade cap.** A received alert does **not** re-broadcast. One shot wakes one camp, not the
  whole map. (Second positive loop, capped by construction.)
- `NoiseReceiverModule`'s aggro branch gains a relationship check: it targets the instigator
  of a `Hurt` noise only if the hurt entity is `Allied` to the listener and the instigator is
  not. A Clanker hearing a nomad scream keeps shooting the nomad.
- Alert radius and duration are per prefab (they are already fields); the roster carries
  nothing about them.

Every tribe member prefab gets `AlertBroadcaster` + `AlertReceiverModule` in its builder.
`AlertResponseSaveable` already exists for the receiver's investigation timer.

### 3.6 Rosters — what a tribe is made of

`FactionRoster` is a new ScriptableObject (`Assets > Create > Factions > Roster`), one per
tribe, under `Assets/Game/ScriptableObjects/Factions/Rosters/`:

| Field | Type | Notes |
| --- | --- | --- |
| `faction` | `FactionDefinition` | Back-reference; the roster is spawn-time authoring, the definition is the runtime identity. Kept separate so the twenty arena factions do not grow empty roster fields. |
| `members` | `RosterMember[]` { `role`, `prefab`, `weight` } | `role` is an enum: `Elder`, `Warrior`, `Trader`, `Rider`, `Pilot`, `Scout`. A caravan template asks for "2 Warriors" and the roster answers with weighted prefabs. |
| `mounts` | `GameObject[]` | Animal prefabs carrying `NpcPassenger` (the `NomadOstrich` pattern: the mount is the agent, the rider is cargo). |
| `vehicles` | `GameObject[]` | Vehicle prefabs the tribe travels in. What "travels in" means per vehicle is in §3.8. |
| `handItems` | `InventoryItem[]` | What goes in the hand at spawn — today's `WeaponArtifactPaths` list from `NomadPrefabBuilder`, moved into data. Ranged only, per the builder's own note about `NpcItemUseModule`. |
| `wornGear` | `InventoryItem[]` | Gauntlets and back gear drawn at spawn (§3.8). |
| `chatter` | `DialogPool` | Idle and errand lines in the tribe's voice. |
| `hostileLines` | `DialogPool` | Barks on provocation and on the crew crossing a goodwill threshold. |
| `clothPalette` | `Material[]` / colour set | Handed to the prefab builder so a tribe reads at a glance (`GDC-L1-NARR-0006`: the world is told through what people wear and ride, not a lore dump). |
| `homeSites` | `SiteKind[]` | Where caravans of this tribe start and rest. |
| `goodwillOverrides` | thresholds | Optional per-tribe thresholds; a proud tribe forgives less. |

Draws are **deterministic per group**: seeded by group id + member index, the same way the
world sim seeds formation jitter, so a caravan that folds into a record and re-spawns comes back
with the same faces and the same guns.

### 3.7 The factions

Every people-faction **lives in settlements and moves between them** (decided). Settlements
are not truly implemented yet — today's tile settlements spawn robots — so §3.10 records the
rules they will follow and the plan builds the hook, not the towns.

**Humans.** The crew, and their own settlements of buildings in the long run.
`PlayerCharacter.prefab` is the only member today. No roster; the field exists so a future
NPC astronaut can join it. Neutral to every tribe by default.

**Sand Tribe** (exists as `NPCFaction`; real name pending). Nomads of the open dunes:
scavengers who keep livestock. They live in houses — some of rock, some simple enough to be
packed up and carried, because they move — and ride ostriches and Appa between them.
Members: `Nomad` (staff), `Nomad_Umber/Tan/Maroon/StrawHat` (random ranged artifact). Mounts:
`NomadOstrich`, Appa with a saddle. Vehicle: the `DuneFoil` sand sailer (decided). Tasks:
graze the herd, scavenge a wreck site, strike camp and move. Voice: the current `DialogPool`
defaults.

**Mechanics** (working name; real name pending). They live in **walking houses — a walking
city**. The `DesertCrawler` (already an AI walking station) is the seed of that: a Mechanics
"settlement" is a column of crawlers and `RigWalker`s that stops, works, and walks on. Members:
nomad body variants with tool belts and a wrench palette, built from the same FBX pipeline.
Hand items: the `GravelBlaster`, `NetGun`. Worn: `Repulsor Gauntlet`, `Grappling Hook`. They
are the tribe that would trade for salvage, which ties them to the game's spine (hull
modules). The walking-city habitat vehicle itself is new art (backlog, §8).

**Sky Tribe** (working name; real name pending). They live in **floating vehicles in the sky**
(not created yet — backlog, §8) and come down on wing packs. Members: lighter cloth, goggles
palette. Worn: `Wing Pack` on every member; `Item Scanner` gauntlets on scouts. Mounts: none —
they fly, by wearing and deploying the pack themselves (§3.8, decided). Until the floating
homes exist, a Sky caravan's "settlement" is a high mesa camp. How an NPC flies is the one
genuinely new mechanic in this design.

No further tribes in the first release (decided). The Salt, Cave and Glasser ideas are kept
in the plan's backlog as "a roster asset and a builder recipe, not new code" for later.

**Clankers.** The robot cowboys, and the one faction with a settled backstory (from Red Planet
Rampage, carried over): they were sent to terraform Mars, but western movies were uploaded in
place of the terraforming instructions. They left Mars for this planet and built settlements
of their own here. They are after **artifacts and lost technology**, and they attack people to
take what they carry. Their settlements admit nobody — they shoot on sight — so a Clanker town
is a place to raid, never to visit. `defaultStance = Hostile` plus two Neutral rows toward
Fauna and Wildlife: hostile to every *people* faction, indifferent to animals (decided). They
keep no goodwill (the ledger ignores factions whose default is `Hostile` — you cannot befriend
them), they keep aggression meters (so `lastAttackerBias` works) and they alert each other.
Their roster: patrol bodies, **outriders** — a bounty-hunting role that tracks the player
instead of patrolling (decided; the existing `bountyHunters` template logic, on a Clanker
template) — and, as the lore asks, a **scavenging** behaviour: a Clanker that wins a fight walks
to what the loser dropped and takes it (the `ScavengeModule` worked example in the agent skill,
plus the missing "NPC picks up a `PickupableItem`" path it names). Your artifacts are what they
came for; losing to them costs you the thing in your hand.
Today's `PatrolRobot`, `PatrolRobot 1..3` and `DeathmatchBot` sit on this faction and keep
working unchanged; the imported bodies arrive as **new prefabs alongside**, and the patrol
robots are retired in a later PR once Clankers are verified on a client (decided).

**Outlaws** (decided). Other humans, hostile to the crew, neutral to the tribes. Today's
`BountyHunterFaction` and `BountyHunter.prefab` become this faction, and the `Bounty Hunters`
caravan template keeps its hunter logic. Outlaws keep no goodwill (their default toward Humans
is Hostile by row, and they are not a tribe) but do keep grudges and alert each other. They
are the crew's mirror: the same body, the same guns, the wrong side.

**Fauna / Wildlife.** Unchanged. Not "smart", so no roster, no goodwill, no alerts beyond what
`FightOrFlightModule` and herds already do.

### 3.8 Gear: the same inventory the player has

Nomads already hold and fire **the same `InventoryItem` assets and `UsableItem` prefabs the
player uses**, through `EntityInventoryComponent` + `EntityEquipmentController` +
`NpcItemUseModule`, and drop them through `EntityLootTable`. That covers the hand slot.

Worn gear does not exist for NPCs. `BodyEquipmentController`, the three `BodySlot`s, `EquipKind`,
`BodySlotRules` and `WornSeat.Apply` are all player-side, but only the *controller* assumes a
player; the seats, rules and visuals take any humanoid `Animator`. The design adds
**`EntityBodyEquipment`**: an NPC counterpart holding three `BodySlot`s, seating worn items
with the same `WornSeat.Apply` on the same bones, obeying the same `BodySlotRules`, and handing
its contents to `EntityLootTable` on death — so a sky nomad's wing pack is a wing pack you can
take. Firing a gauntlet at a target is a side-effect module like `NpcItemUseModule`
(`NpcGauntletUseModule`, one per worn slot, `ClaimsMovement = false`), only for gauntlets whose
use is meaningful without a cursor: the Repulsor, the Grappling Hook, the Flashlight at night.

**Flying — Option B, the worn wing pack (decided).** `DuneOrnithopter.prefab` has no
`AgentController`; it is a player vehicle with a `MountSaveable`, and it stays that way. A
Sky nomad *wears* the Wing Pack through `EntityBodyEquipment` and an `NpcFlightModule`
(Override priority) deploys it:

1. **Trigger.** A `Fly` task from the tribe's `NpcTask[]` (travel between sites by air), or a
   threat: provoked with the aggressor closer than `takeOffDistance`, or goodwill-Hostile
   player in sight. Sky nomads flee *upward* where a Sand nomad would run.
2. **Deploy.** The worn pack's `WornVisual` opens (the player's wing pack already has a folded
   and a spread form); `AgentController.Motor` is swapped from `NavMeshAgentMotor` to an
   `OrnithopterFlightMotor` instance on the same body; the `NavMeshAgent` is disabled. The swap
   is the one place two transform writers could overlap — the motor swap must disable the old
   writer before enabling the new one (INVARIANTS: "something else already owns that
   transform").
3. **Fly.** `AirWanderModule` / `GoalTravelModule` drive the flight motor toward the goal;
   `FormationModule` gets a flying shape (wider lanes, vertical stagger).
4. **Land.** A `Land` step samples a NavMesh point under the goal, descends, re-enables the
   `NavMeshAgent` with `Warp`, swaps the motor back, folds the pack.
5. **Replication.** Deploy and stow are `AgentAction.Deploy` / `Stow` through
   `AgentActionRelay`, presentation only; the body's transform is already replicated. A late
   joiner reads the current form off the worn item's saved/replicated state, not off a missed
   message.
6. **Persistence.** In flight, the saver stores `flying = true` plus the transform; on load the
   nomad comes back airborne with the pack deployed, or on the ground if `flying` is false.
7. **Loot.** Shoot one down and the pack is on the corpse; take it and fly it yourself. Nothing
   about the item changes — it is the same `InventoryItem`.

The mounted-aircraft alternative (an AI ornithopter with the nomad as `NpcPassenger`, the
`NomadOstrich` pattern) is **not** the plan; it is recorded in the implementation plan only as
the fallback if the motor-swap spike fails, and switching to it needs a sign-off. Either way
the wing pack is *worn as a visual* from day one, because that is free with `EntityBodyEquipment`.

### 3.9 Clankers from Red Planet Rampage

Repo: `github.com/hackerspace-ntnu/Red-Planet-Rampage` (Unity, Mirror netcode — the netcode
is irrelevant, only the art moves). License **BSD-4-Clause**, which carries an advertising
clause: *"All advertising materials mentioning features or use of this software must display
the following acknowledgement: This product includes software developed by Prosjekt: Spill, of
Hackerspace NTNU."* Permission has been given verbally; the license terms still need to be met
in-repo (a `THIRD_PARTY_NOTICES.md` with the full text and the asset list) and in the credits.

Assets located:

| Path in RPR | What | Use |
| --- | --- | --- |
| `Assets/Models/Player/YiiHaw.fbx` | The robot cowboy body ("Y11-H4W") | The Clanker body |
| `Assets/Models/Player/YiiHawArm.fbx`, `YiiHawArmRight.fbx` | First-person arms | Not needed |
| `Assets/Models/Player/Player.fbx` | Earlier/other body | Compare; possibly a second variant |
| `Assets/Animation/Y11-H4W/Y11-H4W.controller` + `rig.001_{idle,Walk,back,SideStepLeft,SideStepRight,CrouchForward,CrouchIdle,CrouchRight,Leap}.anim` | Its animation set | Walk/idle/strafe map onto the `SpeedX`/`SpeedY` blend contract; no `Hurt`/`Die`/shoot clips — reuse the robot controller's or author in Blender |
| `Assets/Models/PlayerSelect/`, `Assets/Animation/Auctioneer/` | Other characters | Candidate Clanker *variants* (an auctioneer-bot elder), inspect before deciding |
| `Assets/Models/GunParts/` | Modular gun parts | Tempting for Clanker weapons; out of scope for this design |

Pipeline: the FBX goes into the `.blend` source library through the `blender-model` skill (it
is the only route the art pipeline doc allows — Unity never sees a `.blend`), gets the
project's material palette and a humanoid rig check, exports through its own export script,
and a `ClankerBuilder` (copy of `NomadPrefabBuilder`) assembles the prefab: `NavMeshAgent` +
`NavMeshAgentMotor`, `EntityFaction` = Clankers, ranged combat via `EntityEquipmentController`
+ a Clanker hand-item pool (the robot pistol definitions that exist under
`ScriptableObjects/Weapons/`), `AlertBroadcaster` + `AlertReceiverModule`, `PatrolModule`
fallback, the full saveable and netcode set. `CowBotRocket.prefab`, the scenery rocket, is the
obvious Clanker landmark and goes into their roster's `vehicles` as set dressing.

### 3.10 Settlements and territory

Decided: tribes live in settlements and move between them, and **welcome at a tribe's
settlement is decided by that player's goodwill with the tribe**. Settlements are not truly
implemented — the tile generator emits buildings and `SettlementConfig.robotPrefabs` — so this
design adds the *ownership hook* now and leaves the towns to the settlement work:

| Rule | Mechanism |
| --- | --- |
| A settlement has an owning faction. | `SettlementConfig.owner` (`FactionDefinition`); the emitted settlement root carries a `TerritoryZone` component with the owner and a radius. Long-run settlement *kinds* (mining, market, …) are a separate `SettlementKind` field the generator reads; the faction system only needs the owner. |
| A player is welcome inside if their goodwill band with the owner is **Wary or better**. | `TerritoryZone` tracks players inside it (registry query on an interval, no physics). |
| An unwelcome player inside raises every member's aggression (§3.3, +10/s) — guards draw, bark, then attack. Leave and it cools. | `TerritoryZone` calls `ProvocationModule.AddAggression` on members within it. Nothing else changes: the meter does the rest. |
| At **Hostile-on-sight** goodwill the zone is irrelevant — they attack anywhere. | Already true from §3.4. |
| Clanker settlements admit nobody. | Their stance is Hostile; the zone adds nothing. A Clanker town is a raid target. |
| Human settlements welcome the crew. | Same faction → Allied; the zone adds nothing. |
| A moving settlement (Sand camp being struck, the Mechanics' walking city) is still a settlement. | `TerritoryZone` is a component on whatever moves — a camp root, the lead crawler — not a fixed world position. |

**Built 2026-09-07, stance half only:** `SettlementAlarm` on the Clanker town root scans the
registry for entities the owner is *Hostile* toward inside its radius, raises and holds, sirens
on every machine from local information (no message needed: every machine sees the same
intruder), and on the deciding machine hands the nearest intruder to every idle owner-faction
agent inside through the alert receiver. `TerritoryZone`'s goodwill gate and the aggression-meter
trespass input replace the "Hostile" test when Phase 3 lands; the component stays.

What stays for the settlement work itself: the buildings per tribe (rock houses and movable
shelters for Sand, the walking city for Mechanics, floating homes for Sky, Clanker and Human
towns), settlement kinds, and the tribe's "strike camp and move" task. None of it blocks the
faction system; all of it plugs into `owner` + `TerritoryZone`.

---

## 4. What the player sees

The system is a promise of agency ("what you do to a tribe matters"), and `GDC-L1-DESIGN-0006`
(contextual/4) is explicit that such a promise is empty unless consequences are *perceivable and
attributable*. `GDC-L1-SYS-0006` (contextual/4) allows hiding the rule but not the feedback.
So the *numbers* stay hidden and the *state* is shown, at four moments:

1. **Looking at someone.** The visor reticle's info box already shows label / value / prompt
   for whatever the `Interactor` arbitrated. An `EntityFaction` readout supplies the faction
   name as the label and the resolved relationship as the value: `SAND TRIBE — WARY`,
   `CLANKER — HOSTILE`. Colour from `debugColor` (rename to `hudColor`; it was never only for
   gizmos).
2. **Crossing a threshold.** One bark from the nearest member of that tribe in earshot, from
   the roster's `hostileLines` ("You've drawn blood on the Sand Tribe. We'll remember."), plus a
   visor notice in the existing warning-banner slot. Once per crossing, never repeated per
   frame — the ledger raises an event only on band change.
3. **Before an attack.** A provoked member that was `Neutral` a second ago says a line and
   raises its weapon for `warningDuration` (0.8 s, on the prefab) *before* the first shot.
   This is the humanoid equivalent of the documented "creature charges without telegraphing"
   fix. Clankers give no warning; that is their character.
4. **In the world.** Tribes are told by cloth colour, mount and vehicle, not by a codex. A
   crawler on the horizon with figures on its deck *is* the Mechanics.

What is deliberately **not** shown: the goodwill number, the delta per action, or a faction
screen. If playtesting shows players misattributing why a tribe turned on them, the first
knob is the bark text, not a meter (`GDC-L1-SYS-0006`, "playtest for superstition").

---

## 5. Multiplayer

- **Stance and rosters are assets** — identical on every machine, nothing to replicate.
- **Grudges and alerts run on the agent's authority only**, because `AgentController` gates the
  whole tick and `ProvocationModule.Update` returns early off-authority. Every agent in the
  open world is server-owned. Nothing new here.
- **Goodwill is server-authoritative.** The ledger only mutates behind `Network.Simulates`.
  Attribution needs no new wire: `NetDamage.Apply` already carries `source` and the server's
  `HealthComponent.Damage` records it as `LastDamageSource`, and the crew's `EntityFaction` is
  on the player root. A **client's** shot therefore lowers goodwill exactly as the host's does —
  this is verified on a real client in the plan, not assumed.
- **Each client needs its *own* bands for the visor**, not the number and not other players'.
  One new message, `NetMsg.FactionGoodwill` (append-only id, next free after 69), carrying
  faction id hash + band, sent **to the affected player's client** on change (`NetSendTo`) and
  replayed to that client on a late join (the ledger sends that player's bands when their
  player object binds — the invariant "bind, and read the state as it is now"). The host reads
  the ledger directly.
- Threshold barks are presentation and go through `AgentActionRelay`'s existing
  `AgentActed` path (a new `AgentAction.Bark` value) so every machine hears the same nomad say
  it once.

## 6. Persistence

| State | Where it lives | Saver |
| --- | --- | --- |
| Which faction an agent is on | `EntityFaction` | `EntityFactionSaveable` (exists) |
| An agent's grudge | `ProvocationModule` | `ProvocationSaveable` (exists) |
| An agent's alert investigation | `AlertReceiverModule` | `AlertResponseSaveable` (exists) |
| Its target / last attacker | `AgentTargeting` | `AgentStateSaveable` (exists) |
| **Goodwill per faction, per player** | `FactionGoodwillLedger` (persistent scene) | **`FactionGoodwillSaveable`** (new): **player-scoped** (persistence PATH C, keyed by profile), payload `{ factionId (GUID) → { goodwill, band, lastChangeTime } }`, key `"factionGoodwill"`. References assets only, so not deferred; restored when the player binds, which is before any agent can reason about them. |
| A caravan's tribe and drawn members | `NpcGroup.Record` gains `tribeId` + `rosterSeed` | `NpcWorldSimSaveable` (exists, fields appended) |
| What an NPC wears | `EntityBodyEquipment` | **`EntityBodyEquipmentSaveable`** (new), the NPC twin of the player's body-slot saver; item by `InventoryItem` GUID per slot |

Renaming faction *files* is safe: saves and prefabs hold GUIDs. Renaming `factionName` is safe:
nothing keys on it (that was the point of `ID`). The new `defaultStance` field is a fresh name
so every existing asset takes the C# default `Neutral` — the "renamed field gets the new
default" invariant, used deliberately.

## 7. Spawning and the world sim

`NpcGroupTemplate` gains a `tribe` (`FactionDefinition`) and `NpcGroupMemberSpec` gains an
optional `role`. Resolution when a record spawns: a spec with a `prefab` spawns that prefab
(today's behaviour, untouched); a spec with a `role` and no prefab draws from
`tribe.roster.members` with the group's seed. Every spawned member goes through
`EntityFaction.Ensure(member, tribe, table)` so a prefab shared by two tribes lands on the right
side. `bountyHunters` stays a template flag; it gains a `quarry` choice — *any crew member* or
*the player with the worst goodwill toward this tribe* — so an Outlaw posse hunts anyone and a
tribe at war hunts the one who earned it. The existing `Bounty Hunters` template moves to the
Outlaws faction; a Clanker outrider template is added with the same flag.

Caravan authoring stays where it is (inlined on the `NpcWorldSim` object in
`persistentScene.unity`); one template per tribe is added, and the two existing nomad templates
get `tribe = Sand Tribe`.

## 8. Not in this design

- A faction *screen* or journal. Legibility is in-world (§4) until a playtest says otherwise.
- The settlements themselves: Sand rock houses and packable shelters, the Mechanics' walking
  city habitat, the Sky tribe's floating vehicles, Clanker and Human towns, settlement kinds
  (mining, …). The faction system provides `owner` + `TerritoryZone` (§3.10) and stops there.
- Further tribes (Salt, Cave, Glassers) — backlog; each is a roster asset and a builder recipe.
- Tribe-versus-tribe feuds — a table row each, when the world-building asks for one.
- Clanker modular guns from RPR's `GunParts`.
- Tribe-versus-tribe *warfare* as a simulated thing. Two hostile caravans that meet will fight
  because targeting is symmetric; nobody plans it.
- Trading UI placement. The barter code exists; placing a trader is a content task.

## 9. Risks and gotchas to carry into the plan

- **`FactionRelationshipTable.Get` is on the hot path** (per candidate per query per agent). The
  default-stance fallback adds two enum compares to a cache miss; the ledger lookup is a
  dictionary probe keyed by faction id only when one side is Humans. Measure with a full arena
  (16 solo factions) before merging: the table comment records why the index exists.
- **Goodwill must never make Fauna acquirable by accident.** The ledger is consulted only when
  one side's faction *has a roster*; Fauna and Wildlife have none. Test it.
- **A restored grudge and a restored goodwill must agree.** Load order: assets (registry) →
  ledger (`RestoreState`, immediate) → agents (`ProvocationSaveable`, deferred). The ledger is
  not deferred, so it is in force before any agent reasons about the crew.
- **The alert must go through `Provoke` on a `Neutral` receiver**, or the alerted target is dropped
  within seconds (the "gunshot target does not stick" gotcha, already documented).
- **`AlertBroadcaster` on the registry, not `OverlapSphere`**: the layer-mask field is the kind of
  silent `Nothing` the project has been bitten by three times.
- **Casing.** Builders write `Assets/Game/Prefabs/Agents/…`; disk says `agents`. Use the builders'
  casing in every new reference and never rename the folder.
- **Every `Nomad*` prefab is builder-owned.** New components go into `NomadPrefabBuilder`
  recipes, never onto the prefab by hand, or the next rebuild drops them.

## 10. Principles consulted

| Id | Type / conf. | Where it bit |
| --- | --- | --- |
| `GDC-L1-DESIGN-0006` — agency needs legible consequences | contextual / 4 | §4: hidden numbers, shown state, barks on band change |
| `GDC-L1-SYS-0006` — legible systems; hide the rule, keep the feedback | contextual / 4 | §4: no meter UI; visor readout and tells instead |
| `GDC-L1-SYS-0002` — author rules, not outcomes | objective / 5 | §3: three small rules resolve into one answer; tribe/Clanker fights emerge from symmetric targeting rather than scripting |
| `GDC-L1-SYS-0004` — know your feedback loops | objective / 4 | §3.4, §3.5: two positive loops (retaliation, alert cascade) each given a brake (decay + scaled deltas + leash; no re-broadcast) |
| `GDC-L1-NARR-0006` — build worlds by implication | contextual / 4 | §3.6, §4: tribes told by cloth, mounts, vehicles and voice; no codex |

The corpus is decision support. Where a playtest disagrees with a threshold in §3.4, the
playtest wins; the numbers are serialized for exactly that reason.

---

## 11. Open questions

Decisions taken on 2026-09-07 are struck through with the answer. Only the tribes' real names
are still open, and nothing waits on them: the assets are created under the working names and
a `factionName` is a display string, not a key.

1. ~~Goodwill per crew or per player?~~ **Per player.**
2. ~~Tribe names and count.~~ **Sand, Mechanics, Sky, plus Clankers.** No extras. Real names for
   "Mechanics" and "Sky" still wanted (see Q10).
3. ~~Should Clankers attack animals?~~ **People only.**
4. ~~Tribe–tribe stances and vehicle ownership.~~ **All neutral; Sand = DuneFoil, Mechanics =
   DesertCrawler + RigWalker.**
5. ~~How forgiving?~~ **Slow decay plus amends.**
6. ~~Sky flight A or B?~~ **B: the nomad wears and deploys the pack.**
7. ~~Bounty hunters?~~ **Both: a human Outlaw faction and Clanker outriders.**
8. ~~Clanker bodies?~~ **New prefabs alongside; patrol robots retired later.**
9. ~~Where do tribes live?~~ **In settlements, moving between them; welcome is by goodwill.**
   Sand: rock houses and packable shelters, ostrich and Appa riders, scavengers with livestock.
   Mechanics: a walking city. Sky: floating vehicles (not built). Clankers and Humans: towns
   of buildings; Clanker towns shoot on sight. Settlement kinds (mining, …) later. See §3.10.
10. ~~World-building hooks.~~ **Clankers answered** (§3.7): Mars terraformers who received
    westerns instead of instructions, moved here, built settlements, and hunt artifacts and
    lost technology. **Still open: the real names of the Sand, Mechanics and Sky tribes**, and
    a sentence of voice each for their chatter — the default pool ships until then.
