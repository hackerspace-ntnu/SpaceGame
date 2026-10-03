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
  - "a resident stands a metre short of its anvil, seat or counter, or faces the wrong way"
  - "a resident sits inside a bench or hovers above the floor when sitting"
  - "nobody works at a post or sits at a spot although the prefab has it (planner skipped it)"
  - "residents vanish in front of a house instead of walking in, or pop out of thin air"
  - "a tower guard is teleported up and down although a ladder leads there"
  - "nobody works at a yard: its job archetype is never handed out"
  - "Generate says 40-odd places residents cannot use after the buildings were authored fine"
  - "residents vanish 1.5 m in front of the door instead of stepping into the doorway"
  - "a client's resident census throws a NullReferenceException on a held place"
  - "the residents baseline shows every resident on Break at its door all day"
reads_with: [Errands, ResidentsBaseline, ResidentReputation, AgentSystem, Multiplayer, Persistence, TerrainGeneration]
updated: 2026-10-03
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
| Population = beds | Every nomad house has `Dwelling.beds = 5` and `spawnChance` 1 in `NomadSettlement.asset` (a house rolled under 1 left Chunk_6_3 with 12 of 14, 60 beds), so a full settlement is 14 x 5 = 70 people; `NomadSettlement.asset` rolls 18 entries x `count` 4 x `spawnChance` 1 = 72 copies (a roll under 1 starves beds, so keep it at 1 while beds are the target). Generate places one character per bed: `specialCharacters` first (exactly one each, forced), then copies of `characters` in seeded order. No dwellings → every rolled copy, sleeping in the open (`campPosition`) |
| Who they are | `ResidentAssignment` (Generate): index, archetype (job archetypes only where a spot of their post exists, lifestyles toward the culture's shares, drawn from the character prefab's culture profile when one of its archetypes is usable), name, seed, home, bonds (family = same dwelling) |
| A spot is stood at where it was measured | `SettlementPlaces.TryStand`: NavMesh within `standSnapRadius` (1.2 m) across and `standSnapHeight` up of the spot (no 12 m snap), walkable from the heart (a deck needs none). Else the place is **unusable**: kept in the list, skipped by the planner, reported by Generate with building + spot path |
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
| Walking + standing at the place | `GoalTravelModule` (hold on arrival, A4); an `ExactStand` goal (every spot) ends with a slow approach to within 0.15 m, then faces the spot's target |
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
| `SettlementPlace` | one gathered place: kind, use, group, seat index, **stand point** (`Position`), `Usable`, door `Threshold`, `SeatSurfaceY`, face point |
| `SeatedBodyFit` | after the animator poses a sitter, lifts the model until the hips rest on the seat surface; local, visual, floor sits untouched |
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
- **Routine (0.5 s).** override → plan → goal (hold + face; spots are `Set` unsampled with `exact`; arrive radius 0.6 m at a
  spot, 1.2 m at a door or camp, 3 m at a trip point; an unmeasured or unusable spot is not walked to) → bedtime: walks
  from the door stand into the doorway (`Threshold`), then offstage; a camp sleeper lies down → unobserved snap via
  `NetworkedTeleport` on enable/restore → ladder `Hop` only when `NavMeshReach.CanWalk` says no path (never mid-climb) →
  bedtime backstop → publish presence (`Climbing` while the motor rides a ladder).
- **Presence.** Publishes activity, trip prop, hidden and the held place; every machine holds that place's
  `SpotUse.holdCue` (sit, listen, repair …), a camp sleeper `campSleepCue`, an outrider the trip row's cue.
- **Offstage.** `AgentController.Offstage` (A5), faction off; presence hides the body everywhere. Woken by plan or noise;
  it reappears in the doorway and walks out.
- **Stand points.** Measured on the server when places are gathered, on every plan rebuild, and every `standRecheckSeconds`
  while any spot is unmeasured or unusable (the world NavMesh streams in); a spot flipping usable/unusable re-plans.
  Derived, never saved; `places` order never changes, so held-place indices agree on every machine.
- **Sitting.** A `SpotUse.seated` spot reads the highest upward-facing collider under it (`SeatSurfaceY`, every machine);
  `SeatedBodyFit` lifts the model only when that is ≥ `seatMinLift` above the feet. Floor spots hold `sitground`
  (Sit Ground / Cross Leg only), never the bench-height loops.
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
  `SettlementSociety.ConversationsOpened` counts the talks opened (standing and walking) for the baseline.

- **Baseline.** One recorded settlement day to hold rewrites to: [ResidentsBaseline.md](ResidentsBaseline.md).

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
- **A spot is authored where a person STANDS, on the floor in front of the seat** (Deco_Stool_Set's `Spot_Seat` is 1.4 m out): the
  sit is a ground sit. A spot on the seat top gets the hip lift; one inside a closed structure is unusable.
- **`SettlementPlace.Position` is the measured stand point, not the spot's transform.** Use `SpotAt(i).Position` for the authored one.
  Never insert a place or reorder the gather: unusable places keep their index precisely for this.
- Chores, ambles, walking pairs, the perimeter patrol and tower posts live in [Errands.md](Errands.md) — read it before touching `DayPlanner.Strolls`, `ResidentRoutine.Evaluate` or `Conversations`.
- **A baseline over a stale world NavMesh measures nothing.** `Assets/Game/Settings/WorldNavMesh.asset` is baked separately
  from Generate; regenerate a settlement without **Generate + Bake World NavMesh** and at runtime every spot logs "its NavMesh
  is an island the settlement cannot walk to", doors log "nothing walkable out from the door", and the planner, left with
  doors only, fills the whole day with `Break` at the door — the CSV reads 100 % reached because nobody goes anywhere.
  Check the summary's `places … (usable N)` before trusting a run.
- **The NavMesh keeps an agent radius (0.5 m) clear of every solid, so a spot authored beside its prop is 0.6 to 1.2 m from any mesh.** `standSnapRadius` was 0.6 and made 66 of 229 spots unusable on Chunk_6_3; 1.2 leaves 41. `doorThresholdSnap` is 1.5 for the same reason: at 1 m eleven of the fourteen doorways had no `Threshold`, so their residents went indoors at the door stand, in plain sight. What remains is not the radius: spots in a walled courtyard (an island) or in decor packed under two radii apart.
- **Generate does not remove what an older Generate left beside `Generated`.** Chunk_6_3 still held a `Places` child with 39 `ResidentPlace` components whose script was deleted; Generate only clears `Generated`. Delete such a leftover by hand (`GameObjectUtility.RemoveMonoBehavioursWithMissingScript` for the missing components on the Settlement itself).
- **A client gathers no trip points**, so `SettlementSociety.Place(heldIndex)` is null there for a resident held at one: null-check it before measuring (the settlement autotest census did not and threw on the client).
- **Play-verified 2026-10-03 (host, offline):** 70 residents, up to 35 standing within 0.15 m of their spot (Work, Sitting at hearth stones with the hips on the seat), 3 hidden asleep; a resident sent with `OverrideKind.Scripted` climbed the watchtower ladder; `NomadDoor` interact loaded the NomadHome interior. Client and save/reload: see Testing.md (`settlement-*` modes).
## Extending
- **New people:** a `SettlementCulture` (lines, names, archetypes) on the config. No culture = plain NPCs.
- **A one-of character (quest giver):** a `specialCharacters` entry (prefab + optional archetype); a name set on the
  prefab's `Resident` is kept.
- New reaction: a TSV row — no code. New deed: [ResidentReputation.md](ResidentReputation.md) Extending.
- Editor: the Settlement inspector (Generate, Generate + Bake World NavMesh, Clear, Open Residents Window);
  `Tools/SpaceGame/Residents/Residents Window`.
