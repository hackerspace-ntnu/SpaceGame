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
  - Assets/Game/Editor/Tests/SettlementMusterTests.cs
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
  - "a trader or tinker stands motionless at its stall holding its tool"
  - "a seated resident stands up for a second when it greets"
  - "ResidentValidator says no muster spot"
  - "residents stroll to or sit about at the muster spot"
  - "a tool vanishes from a resident's hand and appears on its belt in one frame"
  - "hidden residents (indoors, offstage) still start gestures and fidgets"
  - "a character prefab (a miner) never moves into a settlement"
  - "Generate says copies made for a role with no place here stayed out"
reads_with: [Errands, HouseVisits, ResidentsBaseline, ResidentReputation, AgentSystem, Multiplayer, Persistence, TerrainGeneration, Seats, Pushables, Stations]
updated: 2026-10-04
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
| What a place is for is data | `SpotUse` asset: role (Work = a job post, Gathering = hearth, Leisure = free time, Errand = a chore stop, Assembly = where a band musters, never planned onto), `holdCue` (the loop held there; one station cue per kind of work, `SettlementSpot.stationCue` where one prop differs: [Stations.md](Stations.md)), `alwaysManned`/`nightManned` |
| Population = beds | Every nomad house has `Dwelling.beds = 5` and `spawnChance` 1 in `NomadSettlement.asset` (a house rolled under 1 left Chunk_6_3 with 12 of 14, 60 beds), so a full settlement is 14 x 5 = 70 people; `NomadSettlement.asset` rolls 18 entries x `count` 4 x `spawnChance` 1 = 72 copies (a roll under 1 starves beds, so keep it at 1 while beds are the target). Generate places one character per bed: `specialCharacters` first (exactly one each, forced), then copies of `characters` in seeded order — with an expedition profile on the culture, the copies certain to hold each quota's role are taken first ([Expeditions.md](Expeditions.md) Extending 5). No dwellings → every rolled copy, sleeping in the open (`campPosition`). `RaxySettlement.asset` is the same layout with every Raxy resident prefab as characters (75 entries, ~86 copies expected for 70 beds; `Tools/SpaceGame/Residents/Author Raxy Settlement Config`) |
| Who they are | `ResidentAssignment` (Generate): index, archetype (**the prefab's own role when its `Resident.archetype` is set** — `Raxy_Job_Miner` is a miner from the start; else job archetypes only where a spot of their post exists, lifestyles toward the culture's shares, drawn from the character prefab's culture profile when one of its archetypes is usable), name, seed, home, bonds (family = same dwelling) |
| A spot is stood at where it was measured | `SettlementPlaces.TryStand`: NavMesh within `standSnapRadius` (1.2 m) across and `standSnapHeight` up of the spot (no 12 m snap), walkable from the heart (a deck needs none). Else the place is **unusable**: kept in the list, skipped by the planner, reported by Generate with building + spot path. A spot that names a target and holds a cue with a measured reach is stood at a point moved toward the target by `StationStand.Derive` (at most `stationMaxShift`), taken only if walkable and no farther from the target: [Stations.md](Stations.md) |
| A day is a precomputed plan | `DayPlanner.BuildAll(seed, day, …)` → one `DayPlan` per resident from places + walking times only. `plan.At(now)` = where it should be. Rebuilt on start, load, day change, `AnchorMoved`. **Never saved.** |
| Errands, ambles, pairs, patrols | a plan segment can be an errand run stop by stop — [Errands.md](Errands.md) |
| Calling in on a house | while a player is inside the house interior, free residents are moved in, sit on its `Seat`s, talk and leave; the routine is handed a segment in a room's places (index >= `HouseRoom.PlaceBase`) — [HouseVisits.md](HouseVisits.md) |
| Friends meet | free time goes first to a spot in a **circle** (spots sharing a `group` under one parent) a friend or family member is already planned into |
| One live deviation | `Resident.SetOverride(Shelter/Approach/Scripted, point, seconds)` beats the plan |
| Out with a band | `Resident.GoAway` / `ComeHome`, driven by the settlement's `SettlementExpeditions`; a band member's body stays home, hidden, while a stand-in walks the road — [Expeditions.md](Expeditions.md) |
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
| What is in the hand | `ResidentHands` (a plain class `ResidentPresence` owns), from the pure `ResidentHandsRule`: the archetype's tool for work, a chore, a patrol or a trip, the band's kit weapon for `Expedition`; the errand's carried item when there is one; **nothing for everything else** (the tool goes on the belt) and **nothing while the hands are on a cart**. The tool's trip between hand and hook is a picture only (`BeltCarrier`, `ResidentTuning.toolTransitSeconds` 0.25 s): the hand counts as empty or full from the first frame |
| Facing a conversation partner | `InteractionFocusModule.FocusOn` |
| Glancing | `ResidentAwareness` (`IFacingModule`, Ambient — loses to focus by the facing rule, A2) |
| Escalation | a hit or shot: an instant, announced fight; a shove: `JostleSensor` → `ProvocationModule` jostle ladder (A1) |
| Answering an ally's alert | `Resident.HandleAllyHurt` on `AlertReceiverModule.HeardAllyHurt` — also how a fighter's call for help arrives (`AlertBroadcaster.CallForHelp`) |
| What to say | `ResidentVoice` — the only sender of `ResidentSaid` |
| What it remembers, and passing it on | `ResidentMemory` (server, saved), `Gossip`, `Rumours` — [ResidentReputation.md](ResidentReputation.md) |

## Key types
| Type | Role |
|---|---|
| `Settlement` (World) | the one component: `Generate`, `Society`, `Seed` (from position), `GeneratedRoot`, `WalkableHeart`, `Culture`, `SettlementId` (the baked id of its own authored `SaveableEntity`; empty, warned once, until **Tools/SpaceGame/Expeditions/Stamp Settlement Identity** has run) |
| `SettlementSpot` / `SpotUse` / `Dwelling` (World/…/Settlement/Spots) | authored on prefabs: one person's place (+Z = facing, optional `face`, `group`); what it is for; beds (door = first `SettlementEntrance`) |
| `SettlementPlaces` (World) | `FindHeart`, `TryDoorStand`, `Problems` — the generator's reachability checks, shared with the society's door places |
| `SettlementCulture` (SO) | lines, names, archetypes, lifestyle shares, `profiles` (character prefab → the archetypes it makes). `NomadCulture.asset` |
| `ResidentArchetype` (SO ×38) | role, `post` (a Work `SpotUse`), trips, chore, duty, nerve, temper, held + belt items. Lifestyle derived: post → Stationed, trips → Outrider, else Roamer. The settlement trades (miner, smelter, barkeep, weaver, potter, beekeeper, healer, butcher, tanner, shrine keeper, mechanic, miller, farmer, stablehand, wood carrier) are authored by `ResidentErrandContentBuilder` — [Errands.md](Errands.md) |
| `ResidentTuning` (one asset, `Resources/Residents/`) | every global knob; durations in **game minutes**; walk speed, trip ring, conversation/rumour pacing, `campSleepCue` |
| `ResidentAssignment` | Generate's population pass (above) |
| `SettlementMuster` (World/…/Settlement/Spots) | the muster spot's rule, pure: `TryChoose(centre, streets, inset)` (road out = the street whose trail reaches farthest from the centre, tie = smaller bearing; the spot stands `ExpeditionTuning.musterInset` back along it from where its PAVING ends — the edge of the built settlement, in view of home — +Z along the last metres of paving), `TraceStreets(centre, pieces, link)` (streets of an already generated settlement, from its laid pieces, paved or stone; least SQUARED steps so a row's last slab is never leapt over), `Find`, `TryPlace` (a `MusterSpot` child of `Generated`, on the NavMesh walkable from the heart). Existing settlements: **Tools/SpaceGame/Expeditions/Place Muster Spots** (idempotent, saves only changed scenes) |
| `SettlementSociety` | runtime: roster (gathered from children), places (doors, camps, spots, trips), `PlaceOf(spot)` / `SpotAt(place)`, `AssemblyPlace(use)` / `TryMusterPose(use, out pose)` (the first usable assembly place of a use; false when there is none), walking times (NavMesh, cached), plans, remark throttle, `Conversations`, `Rumours`. Static `AbsenceQuery(settlementId, residentKey)` (set by the expedition director; null = nobody away) feeds `PlannerResident.away` (an empty day, like the dead, and no companion); `OnAbsenceChanged(settlementId)` re-plans; `ResidentKey(resident)` = `"r:" + index` |
| `SettlementPlace` | one gathered place: kind, use, group, seat index, **stand point** (`Position`), `Usable`, door `Threshold`, face point; `Seated` = its resident needs a `Seat` |
| `Seat` / `ResidentSeating` / `SeatedBodyFit` | the thing sat on, the server's claim of it, and the local lift that rests the hips on its sit point — [Seats.md](Seats.md) |
| `Resident` | identity (≤ 5 per-resident overrides), `home` (Dwelling) or `campPosition`, `sourcePrefab` (the prefab it was placed from: stamped by `ResidentAssignment`, back-filled for older residents by **Tools/SpaceGame/Expeditions/Back-fill Resident Source Prefabs**), derived tuning, quiets stock modules, override (+ `Shelter(seconds)`), offstage + wake, harm → memory, ally alerts. Expeditions: `GoAway` (`ResidentRoutine.LetGo`, then offstage) / `ComeHome(at, facing)` (`NetworkedTeleport.Move`, back onstage) / `IsAway` (server, never saved); `IsStandIn` (the body is a band's stand-in), `IsWithBand` (mustering or walking out/in with its kit), `HeldItem` (the kit weapon in either case, else the archetype's) |
| `ResidentHands` / `ResidentHandsRule` | what the hand holds for the shown activity; re-asserted every frame; `Ready` gates the held loop; `BeltCarrier.CanStow` decides whether the tool may leave the hand. At a station it draws a tool the held cue's clips are made for (`CharacterCue.Tools`: the usual tool if it is one of them, else the first the bag carries) and puts the tool away for a `BareHands` cue |
| `ResidentRoutine` / `ResidentPresence` / `ObserverCheck` | body: goal writing, replicated activity + held place + seat id, "does any player see this" |
| `Attitude` / `PlayerRead` / `ResidentAwareness` | mind (memory, gossip, favor, rumours: [ResidentReputation.md](ResidentReputation.md)) |
| `LineTable` / `LineMatcher` / `SpeechTokens` / `ResidentVoice` / `Conversations` | speech (shown via `Speaker`/`SpeechBubbles`) |

## Flows
- **Generate (edit time, inspector button).** `SettlementEditor.Generate`: add the resident stack to the config's
  character prefabs (`ResidentStackBuilder.EnsureStack`, prefab assets, skips one open in Prefab Mode) →
  `Settlement.Generate` (layout, terrain, decorations, `SpawnChance` rolls, then **Populate**: beds → newcomers,
  throwaway NavMesh, `walkableHeart`, muster spot (`SettlementMuster`, if the culture's profile has a `musterUse`), `SettlementPlaces.Problems`
  → place characters → `ResidentAssignment`) → `ResidentValidator` (lines, archetypes, roster, expeditions, quotas, muster spot). **Generate + Bake World NavMesh** does the same and then
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
- **Presence.** Publishes activity, trip prop, hidden, the held place and the seat id; every machine holds that place's A `Hidden` body (indoors, offstage) has its renderers and hit capsule off (`ShowHidden`) and is also `BodyLanguage.Dormant` (no gesture, fidget, speech gesture or loop starts) with its Animator culled completely until it is seen again: 539 of the 817 gesture and fidget starts of the 2026-10-03 Play day came from such bodies; the gate is **not yet measured**.
  `SpotUse.holdCue` (sit, listen, repair …) — a sit only with its seat — a camp sleeper `campSleepCue`, an outrider the trip row's cue.
- **Sitting.** At a `seated` place the routine's `ResidentSeating.Sync` takes the nearest free `Seat` within `ResidentTuning.seatReach` of the authored spot, switches the agent off and puts the body on the seat; no Seat there = the resident stands and one warning per place. A conversation keeps a sitter's place. [Seats.md](Seats.md).
- **Hands.** `ResidentHands` draws `Resident.HeldItem` — the archetype's tool, or the band's kit weapon for `Expedition` (a stand-in, or a resident with its band at home) — for `Work`, `Patrol`, `Trip`, `Stalking`, `Expedition`, a `Chore` only while standing at a spot (and `None`, before the first publish) and puts it on the belt for every other activity — **walking and every commute, errand legs between spots**, sitting, stroll, hearth, amble, sleep, **talking** — so `BodyLanguage` may fidget and gesture and the walk is not bent by a hold pose. A carried errand item wins the hand. The spot loop is held only when `ResidentHands.Ready`: a work activity with no tool, or none that can be drawn, is skipped (logged once), never mimed. Details: [HandTools.md](HandTools.md).

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
- What the server publishes before a resident's `NetworkObject` spawns (sent offstage or `GoAway` as its chunk loads) is
  kept in the presence's own copy and written into its NetworkVariables in `OnNetworkSpawn`, so it reaches clients with
  the spawn; nobody has to wait for `IsSpawned` before acting on a resident.

## Persistence
- Saved: `ResidentSaveable` (key `"resident"`, memory per player profile); `DayNightSaveable` (`anchorDay`);
  `AgentGoalSaveable` (hold + face).
- The settlement itself carries an authored `SaveableEntity` (Stamp Settlement Identity; on a prefab-placed settlement the scene instance, never the prefab) only so its id names it in records that outlive the chunk: scoped `External`, no record of its own (Gotchas). Its bands are saved by the expedition director ([Expeditions.md](Expeditions.md)); `Resident.away` is not saved, the settlement's performer re-applies it after a load.
- A generated character is a scene-placed entity: its save identity is derived from scene + hierarchy path.
  Regenerating with the same position and config spawns the same characters in the same order, so memories find
  the same people again; changing the config or moving the settlement makes new people (old records orphaned).
- Not saved: plans, places, walking times, activity, overrides, offstage, away, **seat claims** — all re-derived on load (a sitter's saved transform is the seat; the plan walks it to its spot and it sits again).
- The hand slot is saved (`EntityEquipmentSaveable`) but re-asserted from the activity every frame, so a save never reloads a tool into a conversation or empty hands to the forge.

## Gotchas
- **A character prefab made for a role moves in only where that role has its place** (2026-10-04). `Populate` skips a copy whose prefab names an archetype the settlement cannot host (`ResidentAssignment.Places.CanHost`: a post with no spot of it, a chore missing an end) and takes the next copy instead; Generate names the skipped roles only when beds stay empty. Roles set on prefabs bypass the culture's lifestyle and patrol shares (those still steer the role-less copies) and need not be in the culture's `archetypes` list. Expedition role-quota picks (Apply Role Quotas) never recast them: they have no culture profile. The 52 `Raxy_{Type,Job,Casual}_*` prefabs carry roles, every one of the 38 archetypes at least once; the hand-dressed `Raxy_*`/`Drifter_Raxy*` prefabs do not, and are still dealt one from their profile.
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
- **A tool that cannot be stowed stays in the hand** (no `BeltMount`, or no anchor left): its holder never gestures while idle, and logs once per item. Every tool but the two carts has a mount (87 of 89); what is left is a profession with more long tools than the prefab's belt has backs (`Drifter_RaxyClassic` and three other beltless prefabs were dressed with a belt 2026-10-04). Anchors, classes and the audit: [HandTools.md](HandTools.md) Gotchas. A hidden body changes nothing in the hand; a conversation's first line can precede the stow (opening greet skipped).
- **Play-verified 2026-10-03 (host offline, 600 s day, all 70 residents sampled every 0.5 s, 85,000 samples):** walkers at `HoldStyle` 0 except patrol guards (spear) and errand-basket bearers, hips to head 88 degrees median; 160 hand changes by 49 residents, none thrashing (never 6 in 2 s); a talking resident's tool is on the belt about 0.1 s after `Talking` is published and it then gestures; 99.8 percent of Work samples at a spot play a loop of the spot's cue; workers stand within 0.13 m of their stand point on average; 0 errors, 0 exceptions logged after the world loaded. Found and fixed from it: a trader holding its scales stood motionless at the stall (`explain` needs free hands: the scales are now stowed), gestures played on through a tool or basket drawn a moment later, a greeting stood a seated resident up. What is left: the three `Drifter_RaxyClassic` residents cannot stow (no belt) and talk without gesturing; the Drover works empty-handed (its archetype holds nothing); 49 of 275 places are unusable on the world NavMesh. **No client run, no save/reload run.** **2026-10-04, compiled and type-checked only (the Editor was blocked; no render, no Play):** seats carry a pose ([Seats.md](Seats.md)), a tool travels between hand and belt over `toolTransitSeconds`, hidden bodies start nothing, `SkinnedGarment` binds a belt to the Classic once **Equip Residents** dresses it (not run), the pen's Drover still has no cart within `cartReach` of its Herd spots (pen prefab not edited).
- **The settlement's `SaveableEntity` must stay `External`.** Left on `World`, it is captured like any world entity: its record holds its pose, so moving the settlement in the editor after a save exists is undone on the next load, and any `ISaveable` under it with no `SaveableEntity` of its own between them goes into the settlement's record. Re-running Stamp Settlement Identity corrects the scope. A saver that belongs to the settlement itself needs this revisited.
- **A new `SpotRole` needs its own `KindOf` arm**: unmapped roles fall through to `Stroll`, so the muster would have been a bench everyone strolled to. `DayPlanner` skips `Assembly` places everywhere; `HouseRoom` refuses one. **The muster spot is placed by rule** (no settlement has a gate): the stone trail picks the lane, the paving end places the spot — inset from the trail end, Chunk_6_3's south lane (slabs to ~87 m, stones to ~166 m) put it ~160 m out, and `departRadius`/`maxHandoffDistance` are measured from it. Facing follows the slab row (~200° there), not the bearing from the centre (~182°). The validator checks it on the BAKED world NavMesh (stale right after a plain Generate). Edit mode only so far: no Play, client or reload run.
- **Away IS offstage, so every existing offstage exclusion covers it** (`GoAway` = `ResidentRoutine.LetGo` + `GoOffstage`). What differs: nothing but `ComeHome` brings it back — `ComeOnstage` and `Wake` are no-ops while away, and the routine's settle-teleport and bedtime backstop skip it. Two readers walk the roster *regardless* of offstage and so check `IsAway` themselves: `Gossip` (`Witness`, `IsListening`) and `SettlementSociety.EnsureCompanions`. Anything new that iterates residents must ask the same. The society's own "away" (the director's `AbsenceQuery`) runs from the hand-off until the walk-in completes, so a resident walking back in still has no plan while its band's performer moves it.
- **A stand-in is a resident component on a body with no `Settlement`** (`Resident.Settlement` returns null when `IsStandIn`), which is what stands the routine and the planner down; audited 2026-10-03, nothing reachable from a stand-in dereferences a settlement or society unguarded. `FormationModule` is on every resident prefab **disabled** on purpose ([AgentSystem.md](AgentSystem.md)).
- **Role-quota recruits keep their old tools in a world saved before Apply Role Quotas** (formerly Apply Warrior Quota; `ResidentCarry` fills only an empty bag, and the saved bag is not empty), and keep their old coworker bonds.
- **Stations (2026-10-03):** the cue a spot holds is one per kind of work, never a pool of other jobs, and the stand point of a spot with a target moves toward it by the measured reach. Table, tools, reach and what has no clip: [Stations.md](Stations.md). **Run in Play on the host (a day, 13,035 Work samples at a cue, 99.8 percent playing the right loop); not on a client or through a reload**; nothing about it is saved or replicated.

## Extending
- **New people:** a `SettlementCulture` (lines, names, archetypes) on the config. No culture = plain NPCs.
- **A character that is always one role:** set `Resident.archetype` on its prefab. It keeps that role in every settlement and stays out of one without the role's place; add it to the config's `characters` (no culture profile needed). If its archetype has an expedition role, Generate counts it toward that quota first; run Prepare Resident Prefabs after adding it ([Expeditions.md](Expeditions.md) Extending 4).
- **A one-of character (quest giver):** a `specialCharacters` entry (prefab + optional archetype); a name set on the prefab's `Resident` is kept.
- New reaction: a TSV row — no code. New deed: [ResidentReputation.md](ResidentReputation.md) Extending. Editor: the Settlement inspector (Generate, Generate + Bake World NavMesh, Clear, Open Residents Window);
  `Tools/SpaceGame/Residents/Residents Window`.
