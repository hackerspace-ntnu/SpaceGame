# Agent Restructure — design

Status: **draft 2, 2026-10-02.** Draft 1 was written from a five-part review of
`Assets/Game/Scripts/agents/` (~38k lines) and the user's brief, then attacked by two independent
reviews — one against the code (4 blockers, 10 majors), one against the architecture (verdict:
*amend, don't replace*). This draft folds both in; §14 records what changed and why. The user's
decisions on the open questions are recorded in §15 (2026-10-02).

Supersedes, once approved: the "never a behaviour tree, never a per-creature state machine" rule in
[AgentSystem.md](../../AI/systems/AgentSystem.md) and
[.claude/skills/spacegame-agent/SKILL.md](../../../.claude/skills/spacegame-agent/SKILL.md), which
is restated, not dropped (§14).

**The brief.** Remove everything that can be removed. Make an agent's behaviour *visible* in one
place. Make adding a behaviour, a goal or a reaction simple. Robust, scalable, modifiable. Agents
can be anything — a villager, a grazing animal, a robot squad, a horse a robot rides, a walking
crawler, the player's ship — and some have complex behaviour. Realistic. Keep the features.

---

## 0. Summary

An agent today is a prefab with **50–70 script components** (`Raxy_poor`: 70 — 17 savers, 6
netcode, ~22 body/presentation, ~15 behaviour) whose behaviour emerges from priority integers,
`enabled` toggling between modules, and frame-stamp side channels. Nothing says what an agent does,
or why it is doing it now.

After the restructure:

```
AgentController   (name kept)  identity · tick · control of the LEGS · body state · event log
├─ Body           motor + request shaping; legs driven by ONE control source
│                 (Puppet · Carrier · Seat · Rider · Brain)
├─ Brain          server-side, optional; built from a BehaviourProfile asset
│    Senses    →  Memory   facts per entity, with source + confidence
│    Reactions →  Mind     anger, fear, grudges, threat        (events in, feelings out)
│    Goals     ←  gates + desire; the winners claim body CHANNELS: Legs · Gaze · Hands · Voice
│                 preempted goals SUSPEND and resume; multi-step work runs as step plans
│    Skills       attacks, speech, gestures, item use, defence
└─ Presence       every machine: replicated stance + focus, chatter, telegraph, dialogue refusal
```

- **What it does** = one `BehaviourProfile` asset, readable as a list. **Why it is doing it now**
  = the inspector's goal table: winner, its current step, and for every other goal the first
  condition that failed ("why not").
- **New reaction** = a row. **New goal** = one file (definition + nested instance class), added to
  a profile. **New multi-step activity** = a step list. **New agent** = model + `AgentController`
  + pick a profile; a validator fixes the rest.
- `Raxy_poor` goes from 70 script components to about 40, of which **one** asset defines its
  behaviour, shared by all 20 Raxy prefabs.

Kept: one owner per decision, damage on the deciding machine, identity-keyed persistence,
`NetAuthority`, Health, `EntityFaction` + goodwill, the legged locomotion assembly, the residents'
deterministic day plans, `AggressionMath`/`GoodwillMath`, `MountModule`/`SteerModule` as plain
components, the `AgentController` class name (≈20 "is this an agent?" checks and ≈30 test files
use it).

## 1. Requirements

| # | Requirement | Measured by |
|---|---|---|
| R1 | Behaviour is visible in one place, at edit time and run time | Profile summary; goal table with "why not" |
| R2 | A new reaction needs no code in the common case | Reaction rows over one condition vocabulary (§4.6) |
| R3 | A new goal is one file and one profile entry | No edits to other goals; no priority bookkeeping |
| R4 | One agent type is defined once | 20 Raxy prefabs share one profile instead of 20 copies |
| R5 | Agents can be anything | Vehicles, mounts, riders, turrets, creatures, people on one model |
| R6 | Every current feature survives | §5 maps every component; §5.1 lists the non-agent call sites |
| R7 | ~200 live agents | Phase-0 benchmark; budget ≤ 2 ms/frame |
| R8 | Host **and** client; save/quit/load | §10, §11, verified each phase |
| R9 | No big bang | Coexistence adapters (§13); each phase ships |

Non-goals: rewriting `NpcWorldSim`, `DayPlanner`, goodwill; a Virtual simulation tier; a node-graph
editor; GOAP; full HTN search.

## 2. Decisions

| Question | Decision | Why |
|---|---|---|
| Selection model | **Tiered goals.** Tier decides precedence. Within Survival/Combat/Alert: **ordered gates, no scores**. Within Social/Routine: gates + a **desire** score from typed curves | Predictable where it matters; scores only where choosing between several routines genuinely needs them. Free utility everywhere was rejected (untunable) |
| A state machine? | **Yes, at the right level.** The winning goals *are* the agent's state; a goal with real phases (Fight, Flee) has its own small FSM; multi-step work is a **step plan**, not hand-written states | No transition edges between goals; no bespoke FSM per chore |
| One goal owns the body? | **No — four channels.** `Legs · Gaze · Hands · Voice`. Highest tier takes what it needs; the next compatible goal fills what is left | Walk-and-talk, seated rider fires, carry-while-fleeing, a turret = a body without legs |
| Interruptions | **Suspend / Resume**, shallow stack (≤ 3, timeout) | A chore survives a fight; today's model would lose it |
| Two-party behaviour | **Encounter** objects (talk, trade, follow, pet, lead) | Generalises `Conversations`; no two goals syncing by messages |
| Group behaviour | **GroupBrain** owned by whatever owns the group, issuing **directives** | `NpcWorldSim`, `Companions`, formation and herd logic stop leaking into members |
| Where the brain runs | **Server (or offline) always.** The body runs on its `NetworkObject` owner | Noise, damage events and the goodwill ledger exist only on the server; a ridden mount is client-owned |
| What stops the brain | **Nothing but death.** Control sources arbitrate the **legs only**; a seated, carried or ridden agent keeps sensing, feeling, aiming and speaking | Draft 1 suspended goals when seated — that turned every mounted NPC into an ornament (Vehicles.md:52) |
| Authoring | **ScriptableObject definitions as sub-assets of a profile**; runtime instances per agent; definitions immutable at runtime | Script refs by GUID survive renames (`StatusReceiver.cs:35-37` rejected `[SerializeReference]` lists for this) |
| Ticking | **One `AgentTicker`**, staggered buckets, two tiers (Live / Dormant) + an importance floor | Near and Virtual tiers deferred until the benchmark asks for them |

## 3. Principles consulted

- **GDC-L1-ARCH-0001** (data-driven, contextual, 4) — applies: this system is retuned constantly.
  Its YAGNI exception is why profiles are flat (no inheritance) and the condition vocabulary is
  fixed.
- **GDC-L1-ARCH-0002** (composition, contextual, 4) — composition of plain C# parts inside one
  agent instead of ~15 MonoBehaviours; its exception names wiring overhead, which 70 components is.
- **GDC-L1-ARCH-0003** (events, contextual, 4) — events at seams (senses → memory, damage →
  reactions, presentation → peers), direct calls inside the brain; traceability restored by the
  event log, as its disagreement section asks.
- **GDC-L1-ARCH-0005** (iteration speed, objective, 4) — the goal table and live tuning are the
  iteration tools.
- **GDC-L1-SYS-0005** (orthogonality, contextual, 3) — one fear model, one combat model, one
  memory, one group mechanism.
- **GDC-L1-SYS-0006** (legibility, contextual, 4) — every telegraph reaches every machine;
  reaction delay and fuzzy memory make NPCs readable as perceiving, not omniscient.
- **GDC-L1-PERF-0001** (measure first, objective, 5) — all review costs are estimates; phase 0
  builds the benchmark, and the Near tier exists only if it says so.

## 4. The model

### 4.1 `AgentController` (kept name, new job)

Owns: identity (`IPersistentEntity`), the tick (via `AgentTicker`), the **leg control stack**, the
**body state**, the per-agent **event log**, the per-instance **personality** (nerve, temper,
speed-drift seed — rolled per instance and saved; a per-prefab seed would march a crowd in step),
and an optional `BehaviourProfile`. A null profile means no brain: PlayerShip, DuneOrnithopter.

**Body state** — one value every goal reads in its `Situation`:
`Free · Seated · Ridden · Carried · Ragdolled · Suppressed (Frozen/Foamed/Swallowed) · Quieted
(ArrivalDirector.QuietHull) · Dead`. Today these are separate flags written by five systems with a
documented race (`AgentRagdoll.cs:440`); after, one owner with transitions in one place.

### 4.2 Body and leg control

Motors stay as actuators — `NavMeshAgentMotor`, the `LeggedDriver` subclasses,
`HoverRigidbodyMotor`, `OrnithopterFlightMotor` — behind a narrowed `IMotor`. Between the request
and the motor:

```csharp
public struct LegsRequest {                 // replaces MoveIntent
    public LegsKind Kind;                   // Hold | GoTo | Drive (rider input)
    public Vector3 Destination; public float ArriveRadius; public bool HoldOnArrival;
    public Pace Pace;                       // Stroll | Walk | Run | Sprint → each motor's gait table
    public RiderInput Drive;
}
```

**Request shaping, not a new steering engine (phase 2).** For the NavMesh motor: pace → speed (one
mapping instead of `IsRunning` × multipliers × drift), repath throttle scaled by distance with a
minimum interval, arrival braking (today forced off), turn-rate in °/s for facing (one number with
the turn clip). `NavMeshAgent` keeps its own path following and avoidance — re-implementing it for
53 prefabs at once would be the big bang this plan forbids. Legged rigs already have full steering
(`WalkerSteering`); they gain `Pace` (today they clamp `SpeedMultiplier` to 1, so wander and chase
look identical).

**Leg control sources** — exactly one drives the legs; the brain never stops:

| Source | Who | Replaces |
|---|---|---|
| Puppet | cutscene / arrival hard control | `ArrivalDirector` toggling `AgentController.enabled` |
| Carrier | rope, rocket, lasso lift (entry and exit decided by the motor, as today) | carry flags in `NavMeshAgentMotor.Carry` |
| Seat | an NPC sitting on a mount or in a vessel | `NpcPassenger` switching movement off |
| Rider | a player steering through `SteerModule` | the `riderDriveFrame` guards in 5 motor files |

**NPC riders keep today's behaviour (§15 Q5).** A mounted NPC is a Seat occupant, never a Rider:
the mount's own brain moves (the outrider's horse chases to `ChaseStopDistance` and stands), and
the seated NPC's `Fight` aims and fires with its Legs request discarded.
| Brain | its own goals | the module stack |

`MountModule`, `SteerModule`, `PassengerSeat`, `NpcSeating`, `VesselSeats` stay plain components
(≈25 non-agent files depend on them) behind a Rider/Seat adapter. The mount camera stays where it
is. Off-mesh transit (leap, mounted jump, carry arcs) becomes one `OffMeshTransit` helper.

### 4.3 Senses and Memory

Senses run on the server at a staggered rate, write Memory and raise events. Sight (today's rules:
FOV, body *or* head line of sight, sandstorm factor, triggers ignored), Hearing (the `Noise`
registry), Touch (`JostleSensor`'s position-delta rule), Menace (shot first, then aim), Awareness
(residents' `PlayerRead`). Shared services:

- `PlayerSnapshot` — one table refreshed ~10 Hz from `SessionPlayers`: position, flat forward,
  held item + menacing, sprinting, profile id, capsule radius.
- `SightLine` — one non-allocating line-of-sight helper with one layer policy (replaces four).

**Memory** — one table per agent:

```csharp
class Fact {                                   // per entity
    FactSource Source;                         // Seen | Heard | Told | Hit — the alert cascade cap
    float NoticedAt;                           // reaction delay: actionable only after this
    Vector3 Position; float Confidence;        // fuzzed by distance for Heard/Told; decays
    float LastSeen, DamageTaken, GrudgeUntil;
    StimulusSlot[] Stimuli;                    // keyed by StimulusKind — a new sense adds a kind, not a field
}
```

`Source` carries today's cascade cap: only **first-hand** facts (Seen, Hit) are announced to
allies; a target that arrived Told is never passed on (today `TargetAcquired(seen: false)`).
`callForHelp` keeps its leash and interval rule. Long-term per-player memory (today
`ResidentMemory`) lives here too. Replaces `AgentTargeting` last-known/last-attacker, the
provocation aggressor/grudge/gunshot record, `PerceptionModule`'s unused memory, and the
search/noise positions.

### 4.4 Mind

`Anger` (today's meter, unchanged maths — `AggressionMath` bands, jostle ladder, settle steps,
`Announces(cause)`), **`Fear`** (new, same maths), temperament from personality, and **`Threat`**:
target selection as a function of memory using `AgentTargeting`'s scoring. A grudge **pins** the
threat (no per-frame `ForceTarget`). Relationships still resolve through
`EntityFaction.GetRelationshipWith`. Goodwill is **not** a reaction: Hit/Kill reporting stays a
consequence component on the deciding machine (Hit before `damageThreshold`, Kill below the
`IsRestoring` guard), independent of profile.

### 4.5 Goals

```csharp
public abstract class GoalDefinition : ScriptableObject {    // one file: definition + nested instance
    public GoalTier Tier;                 // Puppet-free: Directive(scripted) 5 · Survival 4 · Combat 3 · Alert 2 · Social 1 · Routine 0
    public Channel Needs;                 // Legs | Gaze | Hands | Voice
    public float MinDwell;                // no flapping
    public StanceId Presents;             // what clients show while this runs (§10)
    public Condition[] Gates;             // typed; the first failure is the "why not"
    public Consideration[] Desire;        // curves; empty = 1. Used in Social/Routine only
    public abstract GoalInstance Create(BrainContext brain);
}
public abstract class GoalInstance {
    public virtual void Enter() {}
    public virtual void Suspend(GoalInstance by) {}           // keeps state
    public virtual bool Resume(in Situation s) => true;        // re-check; false → Exit
    public virtual void Exit(ExitReason why) {}
    public virtual bool Interruptible => true;                 // e.g. false mid-swing
    public abstract GoalStatus Tick(in Situation s, BodyChannels held, float dt);  // writes held channels only
    public virtual void Capture(SaveWriter w) {} public virtual void Restore(SaveReader r) {}
}
```

**Selection** (each think tick): goals whose gates pass, ranked by tier, then desire (Social/
Routine), with the active goal kept until `MinDwell` and while not `Interruptible` unless a higher
tier fires. The winner claims its `Needs`; remaining channels go to the best compatible goal
(`Talk` on Gaze+Voice over `FollowPlan` on Legs). A preempted goal **suspends** onto a stack of at
most 3 with a timeout; when the preemptor ends, `Resume` re-checks and either continues or exits
with a reason. A **flap detector** logs more than N switches in T seconds as a fault.

**Starting set — ten goals:**

| Goal | Tier | Needs | Replaces |
|---|---|---|---|
| `Flee` (destination policy: away / shelter / home) | Survival | Legs | `FleeModule`, `FightOrFlightModule`, resident `Shelter` |
| `Fight` (own FSM: approach, position, wind-up, strike/fire, recover; runs seated with Legs ignored) | Combat | Legs Gaze Hands | `Chase`, `CloseCombat`, `AgentRangedCombat`, `NpcItemUse`, `KeepDistance`, `ConjurerCast` |
| `Warn` (Wary faces + barks, Drawn stands + aims gesture) | Alert/Combat | Gaze Voice (+Legs when Drawn) | `AggressionTelegraphModule` |
| `Investigate` (go to a fuzzy position, then sweep) | Alert | Legs Gaze | `SearchModule`, noise investigation |
| `Participate` (in an encounter) | Social | Gaze Voice | `InteractionFocusModule`, `Conversations` pairs, pet/talk focus |
| `FollowDirective` | Directive tier | Legs | `GoalTravelModule` + every outside `AgentGoal` writer |
| `FollowPlan` (read-only, time-indexed `plan.At(now)`; tolerates arriving late) | Routine | Legs Hands | `ResidentRoutine` |
| `Task` (a kind of place + dwell; ordered places = patrol) | Routine | Legs Hands | `NpcTaskModule`, `PatrolModule` |
| `Wander` (radius 0 = idle) | Routine | Legs | `WanderModule` |
| `Sleep` (until disturbed) | Routine | all | `DormantModule` |

Glancing, look-around and watching are not goals: they fill a **free Gaze channel** (today
`WatchModule`, `IdleLookAroundModule`, `ResidentAwareness`'s glance). A goal that claims Gaze
(Fight aiming) always beats them — today's `FacingApplies` rule, made structural.

**Step plans (HTN-lite).** A goal may decompose through a `Method` asset into a linear step list
from a fixed set of primitives: `GoTo · Face · Pick · Drop · Hold(cue, s) · Say · Wait ·
UseSkill`, each with a precondition re-checked when it starts. `ChoreDefinition`/`ErrandRunner`
already are this and are promoted to the general mechanism. A suspended step plan puts a held prop
down at a valid rest (or keeps it in Hands) and saves its step index — the fix for Errands.md's
"a prop vanished after a fight or a reload". The goal table shows
`Chore [GoTo well ✓][Pick bucket ✓][GoTo bed ▶][Water][Return]`.

### 4.6 Reactions and the condition vocabulary

A reaction is a row: **trigger · conditions · effects**. Conditions and goal gates share **one
typed vocabulary** (~15 entries: `HasThreat`, `ThreatWithin(m)`, `FearAbove(nerve)`,
`AngerBand(≥)`, `Relationship(is)`, `BondedTo(victim)`, `FactSource(is)`, `BodyState(is)`,
`InPlanSegment(kind)`, `EscapeExists`, `HoldsItem(menacing)`, …). Designers learn it once. Anything
richer is a code subclass — never a mini-language.

| Trigger | Conditions | Effects |
|---|---|---|
| Hit | damage ≥ threshold | anger → Grudge, pin attacker, alert allies (first-hand) |
| Hit | damage ≥ `enrageDamage` | fear → 0, anger → Grudge (today's FightOrFlight enrage) |
| Hit | status Stagger | brief Legs hold (today's `StatusReactionModule` stagger) |
| Shoved | — | anger one rung (jostle ladder) |
| Gunshot heard | not Allied | anger +15 **or** fear +15 by temperament |
| Ally hurt | nerve ≥ 0.35 · bonded | join (Told fact, pinned) · else fear → shelter |
| Saw ally fleeing *(later)* | — | fear +x (emotional contagion) |

Restoring a save **never** fires reactions (§11).

### 4.7 Encounters and groups

**Encounter** — one shared object for two-party behaviour: participants, roles, turn state, a
break condition. Talk (player or resident pair), Trade, Follow, Pet, Lead-by-rope. Each brain runs
`Participate`. `SettlementSociety.Conversations` is the model and moves onto it.

**GroupBrain** — plain C#, owned by whatever owns the group: `NpcGroup` (caravan, war party),
`Companions` (pairs), a herd, a squad. It issues **directives** (`GoTo`, `FollowLeader`, `Hold`,
`Board(seat)`, each with a tier and an `AllowReflexes` flag); members obey at the Directive tier
and their own Survival/Combat goals still override when allowed. The per-target
**EngagementCoordinator** (shooter tokens, melee ring slots) is the combat half: a Clanker squad
does not all fire at once, and those without a token reposition.

**Skills** — `IAttack` (Melee, Projectile, HeldItem, Spell), `Speak`, `Gesture`
(`CharacterActions`), `UseItem`, `Defend` (`MeleeDefense.Decide`). An NPC gunner reloads (a
natural lull); a defended melee blow does not knock back.

### 4.8 `BehaviourProfile`

A flat list of sense, reaction and goal definitions stored as sub-assets of one profile file
(`+ Add` menu from `TypeCache`). Definitions are **read-only at runtime**; per-agent values
(personality, resident archetype tuning) live on the instance, so play-mode changes never write to
disk by accident. Live tuning edits the asset deliberately through its inspector.

Initial library, from the 53 agent prefabs (values read **from the prefabs**, not class defaults —
Raxy `hitGain` is 1200 on the prefabs against a code default of 300):

| Profile | Prefabs | Notes |
|---|---|---|
| Villager | 20 Raxy | fists, `callForHelp` 1 s; 18 carry `Resident` |
| Drifter | 3 Drifters, 2 SwampMen | fists, no call for help |
| ArmedTribe | 5 Nomads, 4 SkyNomads, Astronaut | held items (Astronaut has `NpcItemUseModule`, no melee) |
| RobotSoldier / RobotRider | Clanker / ClankerOutrider | |
| Predator | DuneRat, Golem, Vrescal | |
| Grazer | Appa, RobotHorse, Sandloper, Ostrich | rideable; Appa adds pet + flee/fight |
| Conjurer | LightningConjurer | `Sleep` + Spell |
| Wanderer | RigWalker, DesertCrawler (+ `CrawlerToolModule` kept as a component) | both wander today |
| *(no brain)* | PlayerShip, DuneOrnithopter | |
| *(deleted, §15 Q2)* | 4 PatrolRobots | removed in phase 0 with their `Chunk_7_0` instance |

After phase 0 there are **49** agent prefabs; every one of them migrates (§15 Q4).

## 5. Every current component → its new home

| Today | New home |
|---|---|
| `AgentController` module loop, `IBehaviourModule`, `BehaviourModuleBase`, `ModulePriority`, `IFacingModule`, `IPresentationModule`, `AgentContext`, `MoveIntent` | `AgentController` (kept name), Brain, channels, `LegsRequest`, Presence |
| `AgentTargeting`, `TargetingProfile`, `TargetResolution` | `Mind.Threat` + Memory; `TargetingProfile` folds into the Sight sense |
| `AgentGoal`, `GoalTravelModule` | Directives + `FollowDirective`; hold/face in `LegsRequest` |
| `ProvocationModule`, `AggressionTelegraphModule`, `MenaceSensor`, `JostleSensor` | `Mind.Anger` + reactions + `Warn` + Menace/Touch senses |
| `PerceptionModule`, `VisionBaseline` | Sight sense (`VisionBaseline` stays as its defaults and its wiring test) |
| `AlertBroadcaster`, `AlertReceiverModule`, `NoiseReceiverModule`, `NoiseEmitter` | Hearing sense, `Fact.Source`, ally reactions; `Noise` registry stays |
| Combat modules (6) | `Fight` + skills + `EngagementCoordinator` |
| `FleeModule`, `FightOrFlightModule`, `StatusReactionModule`'s alarm and stagger | `Flee` + `Mind.Fear` + reactions |
| `SearchModule`, noise investigation | `Investigate` (works, because senses always run) |
| `WanderModule`, `PatrolModule`, `NpcTaskModule` | `Wander`, `Task` |
| `WatchModule`, `IdleLookAroundModule`, `InteractionFocusModule` | free-Gaze fill + `Participate` |
| `DormantModule` | `Sleep` |
| `FormationModule`, `HerdModule` | `GroupBrain` directives + steering cohesion |
| `ChatterModule` | **Presence** (each machine picks its own player's lines, as today) + `Speak` |
| `HealthReactionModule` | slimmed to death consequences: corpse timer **on every machine** (fixes the client vanish), `Despawning` event (loot), Hurt/Death noises; the Died reaction |
| `Resident` | **kept as data-only identity** (22 scene instances in `Chunk_6_3` carry per-instance overrides: settlement, seed, home, archetype, bonds, index, name) |
| `ResidentRoutine`, `ResidentAwareness`, `ResidentVoice` | `FollowPlan`, Awareness sense + kin reactions, `Speak` |
| `MeleeDefense` | `Defend` skill |
| `MotorStateSaveable` type switch | `IMotorState` per motor |
| **Unchanged** | motors' cores, legged drivers + Locomotion assembly, `AgentAnimatorDriver`, `AgentGroundConform`, Health, `EntityFaction` + goodwill + its reporter, ragdoll, inventory/equipment, eyes/mouth/gestures, `ResidentPresence`, `SaddleSocket`, `PettableModule`, `MountModule`/`SteerModule`/seats, `CrawlerToolModule`, `DialogInteraction`, `NpcSpawn`, `AgentActionRelay` (generalised), netcode components |

### 5.1 Non-agent call sites (coexistence and phase 7)

Code outside the stack that reads or writes the old parts and must be moved, mirrored or wrapped:
`DialogInteraction` (reads `AgentTargeting`, `ProvocationModule`), `SettlementAlarm`
(`ForceTarget`), `NpcWorldSim` (writes `AgentGoal`, `NpcTaskModule`, `FormationModule`),
`PettableModule` (serialized `FightOrFlightModule`), `ArrivalDirector` (toggles
`AgentController.enabled`), `AgentRagdoll`, `EntityAudioModule` (`ChaseModule.HasTarget`), the
lasso/snare/blast "is this an agent?" checks, and ≈30 test files. Phase 1 produces the full table
(≈40 sites) by grep; each row says *mirror*, *wrap* or *move* and in which phase.

## 6. Deleted

Verified unused by GUID scan and grep (2026-10-02): `FlyingRigidbodyMotor`, `RigidbodyMotor` +
`SteerModule.EnsureRuntimeMovementPath`, `HorseDriver`, `IMovementMotor.NudgeDestination`/
`SuggestDestination`, `MoveIntent.FacingDirection`, `FightOrFlightModule.SetRoaringFlag`,
`EntityEquipmentController.autoUse` (+ `EntityEquipmentSaveable.autoUseTimer`),
`AgentRangedCombatModule.spawnWeaponModel`, `PerceptionModule` memory + `NotifySpotted`,
`ResidentMemory` death knowledge, `SettlementSociety.FindNeeds`/`HasNeed`,
`Resident.ClearOverride`, `DayPlan.Next`, `TripKindRow` dead fields; `AgentProjectile` +
`TurretProjectile` merged. **Decided in §15**: the four `PatrolRobot` prefabs and their placed
instance in `Chunk_7_0.unity`, and with them `BasePatrolModule` + `BasePatrolSaveable`,
`HerdModule` + `HerdMemberSaveable` and `AgentRangedCombatModule` (no other users; its tactics move
into `Fight`); `Gossip.DawnDeaths` with `ResidentMemory`'s death knowledge (`LearnDeath`,
`KnowsDeath`, `KnownDeaths`, the saved `knownDeaths` list) — it writes a list nothing reads.

Savers replaced by one `BrainSaveable`: `AgentStateSaveable`, `AgentGoalSaveable`,
`PatrolSaveable`, `SearchSaveable`, `AlertResponseSaveable`, `NoiseInvestigationSaveable`,
`WanderSaveable`, `FleeSaveable`, `FormationSaveable`, `PursuitSaveable`, `ProvocationSaveable`,
`CombatCadenceSaveable`, `NpcTaskSaveable`, `HerdMemberSaveable`, `ResidentSaveable` (15).
`AgentPacingSaveable` folds into the `AgentController` saver (drift seed + phase).
`HealthReactionSaveable` goes (all 48 prefabs have empty threshold reactions). `MountSaveable`
stays.

## 7. Creating an agent

1. Model prefab (art pipeline, unchanged).
2. Add `AgentController`. Its inspector runs **`AgentValidator`** — a checklist with a **Fix**
   per row: body kind (NavMesh / legged / hover / flyer) → motor, animator driver, ground conform,
   health, faction, `NetworkObject` + `NetAuthority` + `NetRelay`, network-prefab registration, the
   savers via `SaveablePolicy` (the validator calls it; *Wire Saveable Prefabs* stays the batch
   form). Skips any prefab open in Prefab Mode.
3. Pick a profile, or duplicate one. Personality per instance if needed.
4. Play; read the goal table.

The "half-works with a clean console" gotchas become validator rows: no attack skill on a profile
with `Fight`, no `Speak` with `Warn`, a profile **and** old movement modules on one prefab
(refused during coexistence), `occlusionLayers` = Nothing. A test runs the validator over every
agent prefab. `SculptCharacterBuilder` and `ResidentStackBuilder` shrink to "ensure body set +
assign profile".

## 8. Seeing behaviour

- **Profile summary** (inspector header): "Senses: sight 80 m/180°, hearing, touch. Reacts: hit →
  grudge + alert; gunshot → anger or fear by nerve. Goals: Flee · Fight (fists) · Warn ·
  Investigate · Participate · FollowPlan · Wander." Generated from the gates and rows.
- **Goal table** (play mode): per goal — tier, channels held, state/step, desire, and for every
  loser the **first failed gate** ("Flee ✗ FearAbove(0.6): fear 22"). Suspended stack. Body
  state and leg control source.
- **Mind and memory**: anger/fear bands, threat, facts with source and confidence.
- **Event log**: last 32 events and reactions fired, a fixed ring buffer per agent — cheap enough to
  keep in builds and attach to bug reports.
- **Scene label** (toggle): `Raxy_poor · FIGHT/strike · anger 92 · → Player1`.
- A global Agent Debugger window is deferred until phases 1–5 show the need.

## 9. Scalability

- **`AgentTicker`** — one `Update`, staggered buckets, think at 10 Hz, active goals every frame,
  senses budgeted (raycasts are the real cost, not scoring: ≈200 × 10 goals × 10 Hz is trivial).
  A narrow scheduler — it must never grow game logic. Statics reset on `SubsystemRegistration`
  (domain reload may be off).
- **Two tiers to start**: Live and Dormant (today's `Offstage`: body parked, brain asleep, woken by
  noise or plan). Tier = max(distance tier, **importance floor**): group members, agents with a
  directive, agents in Combat/Alert, and vessel riders are never Dormant — `NpcWorldSim` spawns at
  250 m and war parties stage beyond it, so a distance-only rule would freeze them. Promotion from
  Dormant snaps through the `NetworkedTeleport`/`SaveTeleport` seam when unobserved.
- A Near tier is added only if the phase-0 benchmark shows it is needed.
- Fault barrier without allocations, keyed **per goal**: a faulting goal is quarantined for that
  agent, the brain keeps the rest.
- Every random draw in the brain uses a per-agent `System.Random` seeded from identity, never
  `UnityEngine.Random` (residents' plans and group re-spawns rely on determinism).
- **Benchmark (phase 0)**: headless scene, 200 agents, 4 simulated players; frame ms and GC/frame
  recorded before and after each phase.

## 10. Multiplayer

- **Split authority.** Brain: `Network.Decides` (server or offline) only. Body and leg control:
  the `NetworkObject` owner (a ridden mount is owned by its rider's client; an NPC passenger keeps
  its own object and owner). The brain's leg output is applied only while the server owns the
  body; a client-owned body is driven by its Rider. No brain migration exists.
- **Presence** (every machine): a `NetworkBehaviour` holding **stance** (from the winning goals'
  `Presents`), **focus** (`NetworkObjectId` the agent is engaged with — `DialogInteraction` refuses
  per player with it) and **activity** id. Ticks chatter (each machine its own lines),
  the telegraph's presentation and dwell flags. Replaces the client-side reads of server-only
  module state (`PettableModule`'s always-Calm mood).
- **One presentation channel**: attacks, roar, wake, war cry, death go through a generalised
  `AgentActionRelay`; a goal or skill never plays an animator trigger or sound directly (fixes the
  host-only roar and wake).
- Death presentation on every machine; consequences (loot, ledger) only on the decider.
- Verified on a real client every phase (CLAUDE.md non-negotiable 1).

## 11. Persistence

- **`BrainSaveable`** (key `brain`), an `IDeferredSaveable` loading after `MountSaveable`
  (`Early`): memory (entities as `SaveRef`, kept **pending** until the referenced player binds, as
  `ProvocationSaveable` does today), mind, active and suspended goals with their `Capture`,
  directive. Restore writes state directly — never through events, so no reaction, alert or ledger
  report fires on load.
- `AgentController` saver: drift seed and phase (from `AgentPacingSaveable`). Body: `IMotorState`.
- Not saved, re-derived: tier, channels, plans, encounters, coordinator tokens.
- **Old saves** (§15, decided): transient state is dropped; **grudges** (`provocation`) and **resident
  memory** (`resident`) are migrated, or "a shot Golem forgives on reload" returns.
- Verified by reload and by reading the save JSON each phase (CLAUDE.md non-negotiable 2).

## 12. Testing

- **Pure EditMode**: selection, channels, suspend/resume, every goal's gates and FSM/steps,
  reactions, Memory (source, decay, reaction delay). `AggressionMath`, `GoodwillMath`, `MindTests`,
  `DayPlannerTests`, `AlertChainTests` survive and keep passing.
- **Profile tests**: loads, no missing scripts, every gate/condition known, every `Needs` coverable.
- **Validator test** over all agent prefabs.
- **Scenarios** (headless play harness), each on host + client and across save/load: Appa flees a
  gunshot, fights when cornered, frightened while ridden; timid villager shelters, bonded one
  joins; a Raxy's call for help reaches allies within the leash and stops at the next camp; Clanker
  squad keeps ≤ N shooters and reloads; seated outrider shoots; search after losing a target;
  chore interrupted by a fight resumes with its prop; caravan keeps its task across spawn.
- **Residents baseline first**: residents have never been run in play mode, on a client or across a
  reload (memory: Settlement Residents). That run happens before phase 5, so regressions are
  measurable.

## 13. Migration — every phase ships

| Phase | Work | Done when |
|---|---|---|
| 0 | Fix the verified bugs: Search never runs; caravan re-rolls its task on spawn; clients despawn corpses at once; NPC melee unblockable; `SettlementAlarm` skips goodwill; `autoBraking` forced off. Delete everything in §6, including the PatrolRobots (and the `PatrolRobot 2` row in DEFECTS.md). Hook up bedtime and hearth gossip at the day change (DEFECTS.md row removed). Remove the per-tick closure allocation. Benchmark + baseline. Residents baseline run | Each bug verified host+client; numbers recorded here |
| 1 | Foundations: Brain, Memory, Mind (anger + fear), senses, `PlayerSnapshot`, `SightLine`, `AgentTicker`, channels, selection, suspend/resume, Presence, `BrainSaveable`, goal table. **Coexistence adapters** inside today's `AgentController`: a side-effect *sense* module (always ticks, even while `SteerModule` claims the frame), a movement *goal* module, an `IFacingModule` for Gaze; Threat mirrored into `AgentTargeting`, `AgentGoal` read as a directive. §5.1 table | One test creature on a profile, host+client+reload |
| 2 | Body: `LegsRequest`, `Pace`, request shaping, leg control stack, body state, `OffMeshTransit`, `IMotorState` | Every agent moves through it; no feature lost |
| 3 | **Appa** → Grazer (worst coupling: FightOrFlight, Flee, mount, pet, saddle) | Scenarios pass; old modules gone from Appa |
| 4 | `Fight` + skills + coordinator; Predator, RobotSoldier/Rider, Conjurer, ArmedTribe | Gunners position and reload; melee blockable |
| 5 | People: Villager, Drifter; residents on `FollowPlan`, Encounters, step plans | Settlement day matches the baseline |
| 6 | `GroupBrain` for caravans, war parties, pairs, formations; seats as a leg source (NPC rider behaviour unchanged) | Caravans, war parties, vessels, outrider on host+client |
| 7 | Delete the old stack and savers; rewrite AgentSystem.md, Residents.md, Errands.md, MountSystem.md, the skill, INVARIANTS, Human docs | **All 49 agent prefabs** on a profile or explicitly brainless (a test enumerates them); grep finds no `IBehaviourModule` |
| 8 | Follow-up spec: `NpcWorldSim` split, one schedule format for `DayPlanner` + `NpcTaskPlanner`, a Virtual tier | Separate spec |

## 14. Self-critique — what changed from draft 1, and why

**Draft 1 errors found by review (fixed above):**

1. *Seat above Brain suspended goals* → every mounted NPC an ornament. Now sources control legs
   only (B1).
2. *"Owner runs the brain" + migrating brain state on ownership change.* A ridden mount is
   client-owned while noise, damage events and the ledger are server-only; and the save record
   cannot travel over `NetArg`. Now the brain is always server-side (B2).
3. *A distance-only tier table* would have parked every caravan and war party (spawned at 250 m,
   beyond the 150 m Near tier). Now an importance floor, and only two tiers (B3).
4. *Replacing `Resident`* would orphan 22 scene instances' overrides and their memory. Kept as
   data (B4).
5. *One adapter module* could not keep sensing while ridden, nor coexist with ~40 outside call
   sites. Now three adapters + the call-site table (M1).
6. *A merged memory with a generic "spotted → alert" reaction* would have dropped the alert cascade
   cap. Now `Fact.Source` (M2). Restore never fires reactions (M3); goodwill stays a consequence
   component (M4); presentation that runs everywhere got a home (M5).
7. *Profiles from class defaults, vehicles "only host SteerModule", a full steering rewrite in
   phase 2, a per-prefab drift seed* — all wrong against the prefabs; corrected (M6–M10).

**Architecture amendments from review:**

8. One goal owning the whole body could not express walk-and-talk or a seated gunner → channels.
9. `Exit` on preemption lost interrupted chores → suspend/resume.
10. Hand-written FSMs per chore → step plans from primitives (promoting `ChoreDefinition`).
11. No owner for pairs and groups → Encounters and GroupBrain.
12. "Tier + relevance + reason string" would be as opaque as today → typed gates with a generated
    "why not", scores only in Social/Routine, one condition vocabulary shared with reactions.
13. Cut: Near and Virtual tiers, Soft/Hard commitment + bonus (now `Interruptible` + `MinDwell`),
    14 goals → 10, the debugger window, brain migration, the mount-camera move.

**Standing judgements:**

14. Pure utility AI, behaviour trees, GOAP, full HTN, a node graph, and hiding plumbing with
    `hideFlags` remain rejected (draft 1 reasons hold; HTN-lite covers the useful part of HTN).
15. The old rule "never a per-creature state machine" was right in spirit: *behaviour is profile
    data over shared goals; no per-creature code paths.* That is the restated rule.
16. Shared profiles remove per-prefab tuning freedom on purpose (R4); personality covers the
    differences that matter.
17. Risk that remains: channels + suspend/resume + step plans + encounters are four new concepts.
    Each replaces several existing ones, but phase 1 must prove channels and suspend/resume on a
    real creature before anything else migrates — if they do not hold, phases 3+ stop and this
    spec is revised.

## 15. Decisions (user, 2026-10-02)

| # | Question | Decided |
|---|---|---|
| 1 | Old saves | **Yes**: transient agent state is dropped; grudges (`provocation`) and resident memory (`resident`) are migrated into `brain` |
| 2 | PatrolRobots | **Delete** in phase 0 — the four prefabs, their `Chunk_7_0` instance, and the code only they used (§6) |
| 3 | Dead gossip | **Hook up what does something.** Bedtime (family) and hearth (friends who both sat at the hearth) pass first-hand deeds, which moves favor and attitude: called at the day change for the day that ended (phase 0). `DawnDeaths` only fills a death list nothing reads: deleted with that list (§6) |
| 4 | Order | **Appa first; every agent migrates.** Phase 7 is not done until all 49 agent prefabs are on a profile or explicitly brainless |
| 5 | NPC riders | **Behaviour unchanged.** No NPC pilots through Rider; the mount's brain carries the rider, the seated rider fights (§4.2) |
