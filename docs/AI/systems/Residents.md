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
  - "a resident turns to face me and keeps hammering"
  - "an idle resident stands about with its tool in its hand and never fidgets or gestures"
  - "a resident's tool is nowhere on its body after it sat down or stopped working"
  - "[Residents] X stays in the hand of an idle resident"
  - "[Residents] place N is a sit, but there is no Seat within 1.5 m of it"
  - "a resident stands at its sit spot and never sits, with a clean console"
  - "a cook stirs, ploughs or swings a pickaxe at the stove"
  - "a farmer or miner swings the tool short of the bed or the ore, or from a metre too far back"
reads_with: [Errands, HouseVisits, ResidentsBaseline, ResidentReputation, AgentSystem, Multiplayer, Persistence, TerrainGeneration, Seats, Pushables, Stations]
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
| What a place is for is data | `SpotUse` asset: role (Work = a job post, Gathering = hearth, Leisure = free time), `holdCue` (the loop held there; one station cue per kind of work, `SettlementSpot.stationCue` where one prop differs: [Stations.md](Stations.md)), `alwaysManned`/`nightManned` |
| Population = beds | Every nomad house has `Dwelling.beds = 5` and `spawnChance` 1 in `NomadSettlement.asset` (a house rolled under 1 left Chunk_6_3 with 12 of 14, 60 beds), so a full settlement is 14 x 5 = 70 people; `NomadSettlement.asset` rolls 18 entries x `count` 4 x `spawnChance` 1 = 72 copies (a roll under 1 starves beds, so keep it at 1 while beds are the target). Generate places one character per bed: `specialCharacters` first (exactly one each, forced), then copies of `characters` in seeded order. No dwellings → every rolled copy, sleeping in the open (`campPosition`) |
| Who they are | `ResidentAssignment` (Generate): index, archetype (job archetypes only where a spot of their post exists, lifestyles toward the culture's shares, drawn from the character prefab's culture profile when one of its archetypes is usable), name, seed, home, bonds (family = same dwelling) |
| A spot is stood at where it was measured | `SettlementPlaces.TryStand`: NavMesh within `standSnapRadius` (1.2 m) across and `standSnapHeight` up of the spot (no 12 m snap), walkable from the heart (a deck needs none). Else the place is **unusable**: kept in the list, skipped by the planner, reported by Generate with building + spot path. A spot that names a target and holds a cue with a measured reach is stood at a point moved toward the target by `StationStand.Derive` (at most `stationMaxShift`), taken only if walkable and no farther from the target: [Stations.md](Stations.md) |
| A day is a precomputed plan | `DayPlanner.BuildAll(seed, day, …)` → one `DayPlan` per resident from places + walking times only. `plan.At(now)` = where it should be. Rebuilt on start, load, day change, `AnchorMoved`. **Never saved.** |
| Errands, ambles, pairs, patrols | a plan segment can be an errand run stop by stop — [Errands.md](Errands.md) |
| Calling in on a house | while a player is inside the house interior, free residents are moved in, sit on its `Seat`s, talk and leave; the routine is handed a segment in a room's places (index >= `HouseRoom.PlaceBase`) — [HouseVisits.md](HouseVisits.md) |
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
| The loop held at a place | `ResidentPresence` (from `SettlementPlace.HoldCue` = the spot's station cue, else its use's `holdCue`; every machine); a sit is held only on a claimed `Seat` |
| Sitting on a seat | `ResidentSeating` (server, owned by `ResidentRoutine`): claims the nearest free `Seat` at a sit spot and puts the body on it — [Seats.md](Seats.md) |
| Pushing a cart | `ResidentPushing` (server, owned by `ResidentRoutine`): an archetype with `pushesCart` takes the nearest free `Pushable` within `ResidentTuning.cartReach` while it works at a place, and lets go when the work ends — [Pushables.md](Pushables.md) |
| What is in the hand | `ResidentHands` (a plain class `ResidentPresence` owns), from the pure `ResidentHandsRule`: the archetype's tool for work, a chore, a patrol or a trip; the errand's carried item when there is one; **nothing for everything else** (the tool goes on the belt) and **nothing while the hands are on a cart** |
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
| `SettlementPlace` | one gathered place: kind, use, group, seat index, **stand point** (`Position`), `Usable`, door `Threshold`, face point; `Seated` = its resident needs a `Seat` |
| `Seat` / `ResidentSeating` / `SeatedBodyFit` | the thing sat on, the server's claim of it, and the local lift that rests the hips on its sit point — [Seats.md](Seats.md) |
| `Resident` | identity (≤ 5 per-resident overrides), `home` (Dwelling) or `campPosition`, derived tuning, quiets stock modules, override (+ `Shelter(seconds)`), offstage + wake, harm → memory, ally alerts |
| `ResidentHands` / `ResidentHandsRule` | what the hand holds for the shown activity; re-asserted every frame; `Ready` gates the held loop; `BeltCarrier.CanStow` decides whether the tool may leave the hand. At a station it draws a tool the held cue's clips are made for (`CharacterCue.Tools`: the usual tool if it is one of them, else the first the bag carries) and puts the tool away for a `BareHands` cue |
| `ResidentRoutine` / `ResidentPresence` / `ObserverCheck` | body: goal writing, replicated activity + held place + seat id, "does any player see this" |
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
  `depart = arrive − travel`. Place bookings and post coverage are solved at build time — the planner holds no runtime claims; only a physical `Seat` is claimed, at runtime, by [ResidentSeating](Seats.md).
  A night shift belongs to the day it starts; before today's first segment the settlement reads yesterday's plan.
  Free-time candidates prefer a circle a friend (`Resident.CloseTo`: family and friend bonds) is booked into.
- **Routine (0.5 s).** override → plan → goal (hold + face; spots are `Set` unsampled with `exact`; arrive radius 0.6 m at a
  spot, 1.2 m at a door or camp, 3 m at a trip point; an unmeasured or unusable spot is not walked to) → bedtime: walks
  from the door stand into the doorway (`Threshold`), then offstage; a camp sleeper lies down → unobserved snap via
  `NetworkedTeleport` on enable/restore → ladder `Hop` only when `NavMeshReach.CanWalk` says no path (never mid-climb) →
  bedtime backstop → publish presence (`Climbing` while the motor rides a ladder).
- **Presence.** Publishes activity, trip prop, hidden, the held place and the seat id; every machine holds that place's
  `SpotUse.holdCue` (sit, listen, repair …) — a sit only with its seat — a camp sleeper `campSleepCue`, an outrider the trip row's cue.
- **Sitting.** At a `seated` place the routine's `ResidentSeating.Sync` takes the nearest free `Seat` within `ResidentTuning.seatReach` of the authored spot, switches the agent off and puts the body on the seat; no Seat there = the resident stands and one warning per place. A conversation keeps a sitter's place. [Seats.md](Seats.md).
- **Hands.** `ResidentHands` draws the archetype's tool for `Work`, `Patrol`, `Trip`, `Stalking`, a `Chore` only while standing at a spot (and `None`, before the first publish) and puts it on the belt for every other activity — **walking and every commute, errand legs between spots**, sitting, stroll, hearth, amble, sleep, **talking** — so `BodyLanguage` may fidget and gesture and the walk is not bent by a hold pose. A carried errand item wins the hand. The spot loop is held only when `ResidentHands.Ready`: a work activity with no tool, or none that can be drawn, is skipped (logged once), never mimed. Details: [HandTools.md](HandTools.md).

## Multiplayer
- Server: planner, routine, awareness, voice, conversations, rumours, memory, offstage, jostles, alerts. Every
  machine: presence (and the place list it needs, minus trip points), `Speaker`, bubbles, popup.
- Spots, dwellings and the generated characters are scene content: identical bytes on every machine, nothing sent.
- The hand slot is **derived on every machine** from the replicated activity and prop; nothing about it is sent.
- `ResidentPresence`'s held place is an **index** into the society's places; it is only meaningful because doors,
  camps and spots are gathered in hierarchy order everywhere. Never insert a runtime-only place before the spots.
  The seat is an **id** (`Seat.Id`, derived from the hierarchy like a `SaveableEntity`'s fallback), resolved on each machine to its own copy of the scenery seat; the cart a pusher holds is the same kind of id (`Pushable.Id`, `ResidentPresence.cart`), and the cart's pose is derived from the body on every machine ([Pushables.md](Pushables.md)).
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
- Not saved: plans, places, walking times, activity, overrides, offstage, **seat claims** — all re-derived on load (a sitter's saved transform is the seat; the plan walks it to its spot and it sits again).
- The hand slot is saved (`EntityEquipmentSaveable`) but re-asserted from the activity every frame, so a save never reloads a tool into a conversation or empty hands to the forge.

## Gotchas
- **A spot is authored where a person STANDS, and a sit spot also has a `Seat` placed on it** (`SeatPlacer`; Deco_Stool_Set's `Spot_Seat` is 1.4 m out, so its seat is a cushion or chair there, not one of the stools). The body goes from the spot's stand point onto the seat, so `AtPlace` counts a sitter as there. `SettlementPlaces.Problems` and `SeatTests` fail a sit spot with no seat.
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
- **Play-verified 2026-10-03 (host, offline):** 70 residents, up to 35 standing within 0.15 m of their spot (Work; a sit was then a hip lift over a sampled surface, replaced by `Seat`s the same day), 3 hidden asleep; a resident sent with `OverrideKind.Scripted` climbed the watchtower ladder; `NomadDoor` interact loaded the NomadHome interior. Client and save/reload: see Testing.md (`settlement-*` modes).
- **A working resident puts its work down when it turns to a player.** `ResidentAwareness.Attending` (its glance window, longer for a threat) → [`ResidentAttention.HandsOff`](Assets/Game/Scripts/agents/Residents/Body/ResidentAttention.cs) → `ResidentPresence.Attending` (replicated), which releases the held spot loop through its exit clip and re-holds it `ResidentTuning.attendResumeSeconds` after the look ends. Only `Work` and `Chore` let go: a sleeper, a sitter and a climber keep their pose, because there the loop IS the pose. A glance recurs every `restateSeconds` while a player lingers, which is why the resume delay exists.

- **A tool that cannot be stowed stays in the hand** (no `BeltMount`, or no anchor left): its holder never gestures while idle, and logs once per item. Every tool but the two carts has a mount (87 of 89); what is left is anyone on `Drifter_RaxyClassic` (no belt) and a profession with more long tools than the prefab's belt has backs. Anchors, classes and the audit: [HandTools.md](HandTools.md) Gotchas. A hidden body changes nothing in the hand; a conversation's first line can precede the stow (opening greet skipped).
- Hands verified in a live host Play session 2026-10-03 (70 residents, stow/draw cycle, gesture gate). **No client run, no save/reload run.**
- **Stations (2026-10-03, edit mode only):** the cue a spot holds is one per kind of work, never a pool of other jobs, and the stand point of a spot with a target moves toward it by the measured reach. Table, tools, reach and what has no clip: [Stations.md](Stations.md). **Not run in Play, on a client, or through a reload**; nothing about it is saved or replicated.

## Extending
- **New people:** a `SettlementCulture` (lines, names, archetypes) on the config. No culture = plain NPCs.
- **A one-of character (quest giver):** a `specialCharacters` entry (prefab + optional archetype); a name set on the
  prefab's `Resident` is kept.
- New reaction: a TSV row — no code. New deed: [ResidentReputation.md](ResidentReputation.md) Extending.
- Editor: the Settlement inspector (Generate, Generate + Bake World NavMesh, Clear, Open Residents Window);
  `Tools/SpaceGame/Residents/Residents Window`.
