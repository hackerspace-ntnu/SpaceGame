---
system: Residents
layer: characters
summary: "Settlement people from one Settlement component: beds, spots on prefabs, day plans, attitude, speech"
paths:
  - Assets/Game/Scripts/agents/Residents/
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Spots/
  - Assets/Game/ScriptableObjects/Residents/
  - Assets/Game/ScriptableObjects/Settlements/Spots/
  - Assets/Game/Resources/Residents/
  - Assets/Game/Scripts/Presentation/Speech/
symptoms:
  - "settlement NPCs stand where they were placed and never go to work"
  - "a resident walks to its post and then wanders off instead of standing there"
  - "residents pop out of existence in front of me at bedtime"
  - "a resident says nothing when I talk to it, or the same line every time"
  - "one punch and the whole settlement attacks me"
  - "I shoot a settler and the people standing next to him do nothing"
  - "a villager warns me when I punch him instead of fighting back"
  - "bumping into a villager once starts a fight"
  - "residents forget I hit their kin after a reload"
  - "speech bubbles appear for the host but not for a client"
  - "residents answer when I talk to them but never remark on my sprinting or gun, and shoving them does nothing"
  - "Generate reports a spot residents cannot walk to"
  - "Generate says beds stayed empty"
  - "a resident stands next to the stall or fire instead of on its spot"
  - "nobody works at a yard: its job archetype is never handed out"
reads_with: [Errands, ResidentReputation, AgentSystem, Multiplayer, Persistence, TerrainGeneration]
updated: 2026-10-02
---

# Residents

## Model
A settlement needs **one component**: `Settlement` (with a `SettlementConfig`). Its **Generate** lays the
buildings out ([TerrainGeneration.md](TerrainGeneration.md)), then fills the dwellings with people and, when the
config has a `culture`, makes them residents. At runtime the same component hosts the settlement's
`SettlementSociety`: places, plans, conversations and rumours. A resident is a stock agent (`AgentController` +
modules) with the resident component stack; its one new decision is **what to do and where, by the clock**.

| Idea | Mechanism |
|---|---|
| Places come with the prefabs | `SettlementSpot` children on building/decoration prefabs (a `SpotUse` each), `Dwelling` (beds) on houses. Gathered at runtime, never baked |
| What a place is for is data | `SpotUse` asset: role (Work = a job post, Gathering = hearth, Leisure = free time), `holdCue` (the loop held there), `alwaysManned`/`nightManned` |
| Population = beds | Generate places one character per bed: `specialCharacters` first (exactly one each, forced), then copies of `characters` in seeded order. No dwellings → every rolled copy, sleeping in the open (`campPosition`) |
| Who they are | `ResidentAssignment` (Generate): index, archetype (job archetypes only where a spot of their post exists, lifestyles toward the culture's shares, drawn from the character prefab's culture profile when one of its archetypes is usable), name, seed, home, bonds (family = same dwelling) |
| A day is a precomputed plan | `DayPlanner.BuildAll(seed, day, …)` → one `DayPlan` per resident from places + walking times only. `plan.At(now)` = where it should be. Rebuilt on start, load, day change, `AnchorMoved`. **Never saved.** |
| Errands, ambles, pairs, patrols | a plan segment can be an errand run stop by stop — [Errands.md](Errands.md) |
| Friends meet | free time goes first to a spot in a **circle** (spots sharing a `group` under one parent) a friend or family member is already planned into |
| One live deviation | `Resident.SetOverride(Shelter/Approach/Scripted, point, seconds)` beats the plan |
| Server decides, everyone derives | `ResidentPresence` NetworkVariables (activity, prop, hidden, **held place**) + `NetMsg.ResidentSaid` |
| Attitude is a pure function | `Attitude.StanceFor(nerve, temper, regard, personalGrudge, read, band, warmAt, coldAt)` |
| What players did, and what everyone thinks of it | deeds, favor shared by closeness, word of mouth — [ResidentReputation.md](ResidentReputation.md) |
| Lines are rows | the culture's `<Culture>Lines.txt` (tab-separated); `LineMatcher` picks the most specific match; sent by FNV-1a id |

| Decision | Single owner |
|---|---|
| Who lives here, and who they are | `Settlement.Generate` → `ResidentAssignment` (edit time) |
| Today's plan | `DayPlanner` (pure) |
| Where now | `ResidentRoutine` → `AgentGoal.TrySetSampled(…, hold: true, facePoint)`; nothing else writes a resident's goal |
| Walking + standing at the place | `GoalTravelModule` (hold on arrival, A4) |
| The loop held at a place | `ResidentPresence` (from the place's `SpotUse.holdCue`, every machine) |
| Facing a conversation partner | `InteractionFocusModule.FocusOn` |
| Glancing | `ResidentAwareness` (`IFacingModule`, Ambient — loses to focus by the facing rule, A2) |
| Escalation | a hit or shot: an instant, announced fight; a shove: `JostleSensor` → `ProvocationModule` jostle ladder (A1) |
| Answering an ally's alert | `Resident.HandleAllyHurt` on `AlertReceiverModule.HeardAllyHurt` — also how a fighter's call for help arrives (`AlertBroadcaster.CallForHelp`) |
| What to say | `ResidentVoice` — the only sender of `ResidentSaid` |
| What it remembers, and passing it on | `ResidentMemory` (server, saved), `Gossip`, `Rumours` — [ResidentReputation.md](ResidentReputation.md) |

## Key types
| Type | Role |
|---|---|
| `Settlement` (World) | the one component: `Generate`, `Society`, `Seed` (from position), `GeneratedRoot`, `WalkableHeart`, `Culture` |
| `SettlementSpot` / `SpotUse` / `Dwelling` (World/…/Settlement/Spots) | authored on prefabs: one person's place (+Z = facing, optional `face`, `group`); what it is for; beds (door = first `SettlementEntrance`) |
| `SettlementPlaces` (World) | `FindHeart`, `TryDoorStand`, `Problems` — the generator's reachability checks, shared with the society's door places |
| `SettlementCulture` (SO) | lines, names, archetypes, lifestyle shares, `profiles` (character prefab → the archetypes it makes). `NomadCulture.asset` |
| `ResidentArchetype` (SO ×38) | role, `post` (a Work `SpotUse`), trips, chore, duty, nerve, temper, held + belt items. Lifestyle derived: post → Stationed, trips → Outrider, else Roamer. The settlement trades (miner, smelter, barkeep, weaver, potter, beekeeper, healer, butcher, tanner, shrine keeper, mechanic, miller, farmer, stablehand, wood carrier) are authored by `ResidentErrandContentBuilder` — [Errands.md](Errands.md) |
| `ResidentTuning` (one asset, `Resources/Residents/`) | every global knob; durations in **game minutes**; walk speed, trip ring, conversation/rumour pacing, `campSleepCue` |
| `ResidentAssignment` | Generate's population pass (above) |
| `SettlementSociety` | runtime: roster (gathered from children), places (doors, camps, spots, trips), `PlaceOf(spot)` / `SpotAt(place)`, walking times (NavMesh, cached), plans, remark throttle, `Conversations`, `Rumours` |
| `SettlementPlace` | one gathered place: kind, use, group, seat index, position, face point |
| `Resident` | identity (≤ 5 per-resident overrides), `home` (Dwelling) or `campPosition`, derived tuning, quiets stock modules, override (+ `Shelter(seconds)`), offstage + wake, harm → memory, ally alerts |
| `ResidentRoutine` / `ResidentPresence` / `ObserverCheck` | body: goal writing, replicated activity + held place, "does any player see this" |
| `Attitude` / `PlayerRead` / `ResidentAwareness` | mind (memory, gossip, favor, rumours: [ResidentReputation.md](ResidentReputation.md)) |
| `LineTable` / `LineMatcher` / `SpeechTokens` / `ResidentVoice` / `Conversations` | speech (shown via `Speaker`/`SpeechBubbles`) |

## Flows
- **Generate (edit time, inspector button).** `SettlementEditor.Generate`: add the resident stack to the config's
  character prefabs (`ResidentStackBuilder.EnsureStack`, prefab assets, skips one open in Prefab Mode) →
  `Settlement.Generate` (layout, terrain, decorations, `SpawnChance` rolls, then **Populate**: beds → newcomers,
  throwaway NavMesh, `walkableHeart`, `SettlementPlaces.Problems` → place characters → `ResidentAssignment`) →
  `ResidentValidator` (culture lines, archetypes, roster). **Generate + Bake World NavMesh** does the same and then
  re-bakes the world NavMesh. Generate's summary line names every door or spot nobody can walk to, and empty beds.
- **Places (runtime, lazily).** Doors (one per `Dwelling`, at the first point out from the doorway reachable from
  `walkableHeart`), camps (one per resident without a dwelling), spots (every active `SettlementSpot` under
  `Generated`, hierarchy order), then — only where plans are built — seeded trip points on a ring
  (`tripDistance`) the heart can walk to. Index order is the same on every machine except trips, which come last.
- **Walking times.** `TravelMinutes(a, b)` = NavMesh path length (else straight line × `detourFactor`) ÷
  (`walkSpeed` × seconds per game minute), measured on first use and cached until plans are rebuilt.
- **Plan.** Segments `{depart, arrive, leave, activity, place}` in absolute game minutes since day 0;
  `depart = arrive − travel`. Seats and post coverage are solved at build time — no runtime claims, nothing leaks.
  A night shift belongs to the day it starts; before today's first segment the settlement reads yesterday's plan.
  Free-time candidates prefer a circle a friend (`Resident.CloseTo`: family and friend bonds) is booked into.
- **Routine (0.5 s).** override → plan → goal (hold + face; arrive radius 0.6 m at a spot, 1.2 m at a door or
  camp, 3 m at a trip point) → offstage at the door when unobserved (or after 10 s) — a camp sleeper lies down
  instead → unobserved snap via `NetworkedTeleport` on enable/restore → bedtime backstop → publish presence.
- **Presence.** Publishes activity, trip prop, hidden and the held place; every machine holds that place's
  `SpotUse.holdCue` (sit, listen, repair …), a camp sleeper `campSleepCue`, an outrider the trip row's cue.
- **Offstage.** `AgentController.Offstage` (A5), faction off; presence hides the body everywhere. Woken by plan or noise.
- **Awareness (0.5 s).** players within `noticeRadius` → `PlayerRead` → one `Observation` → consequence lane
  (never throttled), ambient lane (per player / per resident gaps), body lane (glance). Irritation →
  `ProvocationModule.Escalate(player, Trespass)`. Unforgiven kin grudge + player near → override Shelter.
- **Hit or shot.** Any blow ≥ `damageThreshold` is `Raise(Grudge)` → `Provoke(attacker)` **with** the alert
  (`AlertBroadcaster`, 30 m); the deed's spread is [ResidentReputation.md](ResidentReputation.md). **Shove:**
  `JostleSensor` → `ProvocationModule.Jostled`, one rung per shove (≤ 1 per 1.5 s), `jostlesToFight` 3 warm → 2 prickly.
- **Ally alert** (`HeardAllyHurt`, a non-Hostile target): nerve ≥ 0.35 joins at once (`allyHurtGain` = `attackAt`);
  bonded to the victim → `Raise(Grudge, attacker, AllyHurt)`; anyone else → `ClearAlert` + `Shelter(settleSeconds)`.
- **Talk.** `DialogInteraction` → `ResidentVoice`; a client sends `ResidentAddressed`, the server answers with `ResidentSaid`.
- **Resident pairs.** `Conversations` (every `pairInterval`): two residents holding at one place **or two spots of one
  circle**, with a player in earshot, exchange up to `maxTurns` lines, each reply matched on the previous line's register.

## Multiplayer
- Server: planner, routine, awareness, voice, conversations, rumours, memory, offstage, jostles, alerts. Every
  machine: presence (and the place list it needs, minus trip points), `Speaker`, bubbles, popup.
- Spots, dwellings and the generated characters are scene content: identical bytes on every machine, nothing sent.
- `ResidentPresence`'s held place is an **index** into the society's places; it is only meaningful because doors,
  camps and spots are gathered in hierarchy order everywhere. Never insert a runtime-only place before the spots.
- `JostleSensor` reads replicated player positions, never collisions: on the server a client's body is not simulated.
- `ResidentAddressed` (117, player → server) and `ResidentSaid` (118, server → all, host included) on the resident's relay.
- Late joiners read activity and place from the NetworkVariables; speech is not replayed. `ResidentPresence` is a
  NetworkBehaviour: the stack is on the prefab (Generate adds it), then Sync Network Prefabs.

## Persistence
- Saved: `ResidentSaveable` (key `"resident"`, memory per player profile); `DayNightSaveable` (`anchorDay`);
  `AgentGoalSaveable` (hold + face).
- A generated character is a scene-placed entity: its save identity is derived from scene + hierarchy path.
  Regenerating with the same position and config spawns the same characters in the same order, so memories find
  the same people again; changing the config or moving the settlement makes new people (old records orphaned).
- Not saved: plans, places, walking times, activity, overrides, offstage — all re-derived on load.

## Gotchas
- Chores, ambles, walking pairs, the perimeter patrol and tower posts live in [Errands.md](Errands.md) — read it before touching `DayPlanner.Strolls`, `ResidentRoutine.Evaluate` or `Conversations`.
## Extending
- **New people:** a `SettlementCulture` (lines, names, archetypes) on the config. No culture = plain NPCs.
- **A one-of character (quest giver):** a `specialCharacters` entry (prefab + optional archetype); a name set on the
  prefab's `Resident` is kept.
- New reaction: a TSV row — no code. New deed: [ResidentReputation.md](ResidentReputation.md) Extending.
- Editor: the Settlement inspector (Generate, Generate + Bake World NavMesh, Clear, Open Residents Window);
  `Tools/SpaceGame/Residents/Residents Window`.
