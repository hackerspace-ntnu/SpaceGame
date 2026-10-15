---
system: NpcAnimationPlan
layer: presentation
summary: "What no clip covers yet, and the plan for animating every NPC situation"
paths:
  - Assets/Game/Scripts/Presentation/Animation
symptoms:
  - "an NPC works at a site and shows nothing until the job ends"
  - "an NPC limps or staggers but its feet slide across the ground"
  - "a Hold on a cue plays nothing for a standing state"
  - "an NPC is shot or hit from behind and flinches the same way as from the front"
  - "what animation should an NPC use for X"
reads_with: [HumanoidAnimation, AnimationCatalog, Residents, AgentSystem, Stations]
updated: 2026-10-03
---

# NPC animation plan

Companion to [AnimationCatalog.md](AnimationCatalog.md) and [HumanoidAnimation.md](HumanoidAnimation.md).


## Gaps

What no clip in the project covers (searched across CMU, Quaternius, Mixamo, Iglesias and the five new libraries), so an NPC that needs it today falls back to a neighbour or to nothing:

| Area | Missing | Why it matters |
|---|---|---|
| Combat | **Directional hit reactions** (front, back, left, right, heavy), **block/parry with a weapon**, sword combo chains, shield, spear thrust and guard, surrender (hands up), taunt, a run-away-scared cycle | `hurt` has 5 actions and none depends on where the blow came from; `block` 5, `dodge` 2 |
| Death | One-shot deaths are 6 plus ragdoll; no **death while seated or crouched**, no kneeling-to-prone, no burning/electrocuted/poisoned | Clients despawn corpses instantly (NPC system review, 2026-10-02), so deaths are seen once |
| Social | **Nod / shake head, thumbs up, shush, facepalm, pat on the back, hug, plead, pray**, a second handshake, a real `ask` (1), `beckon` (1), `agree` (1) | The three single-action cues are the ones dialogue reaches for most |
| Sitting | **Seated work** (table, desk, writing, eating at a table, drinking seated), sleeping on one side, sitting down at a fire | `sit` is 12 but all are idles; the Eat/Drink/Cook loops are standing |
| Sleeping | **Getting into a bed**, side sleeper, waking up | `sleep` is 3 (lie down, lying idle, a Mixamo lying loop) |
| Doors, ladders, props | Pushing/pulling a door (one `Opening` clip), **ladder top/bottom transitions**, climbing in and out of a vessel, opening a hatch while climbing | `ladder` is 2 loops |
| Riding | Any riding pose beyond the code-driven rider math | `MountedRiderPose` has no cue |
| Carrying | Carry on the shoulder/back, two-person carry, carry a child, water pail, carry while crouched | `carry` is 10 but 6 are CMU box/suitcase and 4 are the new crate set |
| Swimming | NPC swim (UAL has player swim loops only) | N/A today |
| Children and elders | No child-proportioned rig or clips; one elderly walk/idle in CMU | |
| Hands | **Finger poses** (the KI masked grip poses are unused) — items rely on the hold system | |
| Quality | `Contact`/`Release` marks are not authored on the new throws and strikes; the new locomotion clips have no stride calibration against a real NPC; start/stop and turn-in-place cues exist and nothing raises them | |

Cue vocabulary still thin (answered by one or two actions; add variants before relying on them): agree, ask, beckon, bow, chant, confused, cough, exhausted, farm, music, restrain, salute, sing (1 each); applaud, collapse, cry, dodge, draw, hungry, ladder, read, receive, sitground (2 each).

## NPC animation plan

Principles applied: [GDC-L1-ANIM-0003](../../game-development-constitution/principles/GDC-L1-ANIM-0003.md) — animation that carries a gameplay meaning (combat tells, hit reactions, a worker's job) must read, so those get distinct, unmistakable clips; purely ambient behaviour (idling, fidgets, chatter) may favour variety and beauty. [GDC-L1-ANIM-0004](../../game-development-constitution/principles/GDC-L1-ANIM-0004.md) — the project chose code-driven, in-place locomotion; every new clip is baked in place and the cost is **stride calibration per archetype** (a measured speed table above), not a switch to root motion. [GDC-L1-ANIM-0005](../../game-development-constitution/principles/GDC-L1-ANIM-0005.md) — life is secondary motion: weight shifts, fidgets, glances; the library now has 16 fidgets (was 4). [GDC-L1-ANIM-0002](../../game-development-constitution/principles/GDC-L1-ANIM-0002.md) — a full-body action never blocks the player's input; NPCs may commit to a full-body clip while standing still, which is the engine's existing rule (`Full` = standing only). SpaceGame's own evidence overrides these: nothing below has been play-tested.

Every case below states what plays it, what exists, what is missing and what has to change. **Data** means assets only (an action, a cue tag, a table row, an override controller on a prefab); **code** means a C# hook.

### 1. Getting around
| Situation | Today | Now available | Needed |
|---|---|---|---|
| Walk / run | Mixamo 8-direction blend, `SpeedX/SpeedY` from the driver | unchanged | — |
| Injured, drunk, swaggering, strolling, heavy, hauling | one style for everyone | 6 locomotion sets + `Walk Injured Belly`, `Walk Drunk Mocap`, `Walk Swagger *`, `Walk Stroll`, `Walk Heavy Hammer`, `Walk Carry Crate` as Full/Moving loop actions | **Data:** pick the archetype per prefab (override controller) and set `animatorSpeedScale` from the speed table. **Dynamic states** (hurt below 30% HP, drunk, hauling) without swapping controllers: **code** — `Hold(injured)` / `Hold(drunk)` / `Hold(carry)` while moving, which plays the Full-slot loop over the base layer; multiply the agent's walk speed by the table ratio through `AgentGoal.SpeedMultiplier` or the feet skate |
| Start / stop | cues `start`, `stop` have 5 and 6 actions; nothing raises them | CMU Walk/Run/Jog Start/Stop, Injured Walk Start/Stop | **Code:** raise `start` when a standing NPC gets a destination and `stop` when a running one arrives, on important NPCs only (each is 1-2 s of committed legs) |
| Turning on the spot | the driver measures turn rate and writes `TurnSpeed`; the humanoid controller has no such parameter | 11 `turn` actions (CMU turn-in-place L/R, injured turns L/R) | **Code:** add `TurnSpeed` to `HumanoidParams.Contract`, or raise `turn` when heading error > ~60° while still; split left/right so the direction matches |
| Climbing | `ResidentPresence` holds cue `ladder` (now 2 actions) | CMU Climb Ladder + a Mixamo ladder loop | **Missing clips:** ladder top/bottom transitions |
| Crouch / sneak / crawl | sneak 11 actions; prone 3 | `Crawl Prone`, `Drop To Prone`, `Rise From Prone` | **Code:** scouts and outriders (`Activity.Stalking`) could use `prone` at their goal |
| Falling over (ice, panic, being shoved) | `fall` 11 | `Slip Fall`, `Stumble Fall`, `Slip And Recover` (fall → lying → get up) | **Code:** a `Stumbled` moment raised by slope/ice/collision, row → cue `stumble` |

### 2. Standing around (ambient life)
| Situation | Today | Now available | Needed |
|---|---|---|---|
| Idle fidgets | `IdleVariation` raises `IdleFidget` at 35% per clock bucket → cue `fidget` | 16 fidget actions: scratch arm, look around, nervous look, cough, wipe sweat, stinky pits, bored watch, sit rub arm… | **Data:** per-body `MomentReactions` tables so a drifter fidgets differently from an astronaut (cough, hungry, disgust, check watch for settlers; shift weight, look around for guards) |
| Waiting | bored 11 | Bored Check Watch, Idle Calm M/F, Heavy Idle, Drunk Idle | **Data:** Resident `Break`/`Stroll` spots tagged `bored` |
| Needs shown in the body | none | `hungry`, `cold` (3), `tired` (8), `exhausted`, `drunk`, `injured` (13) | **Code:** a tiny mood-to-cue map: when a need crosses a threshold, `Hold(cue)` as an idle state; **data** for the cue pools |
| Conversation | `SpeechGestures` → greet, ask, gesture, talk loops | `gesture` pool 6 → 20+ beats (Gesture Speech ×6, Conversation Beat ×4, Talk Beat ×3 + CMU) | **Data:** faction-flavoured `ConversationStarted` row (salute for guards, hat tip / wave for nomads, bow for officials). Still open: dialog gestures are local to the talker |

### 3. Working (residents, caravans, tasks)
`ResidentPresence` already `Hold`s each spot's `holdCue` on every machine, so **new work is mostly tagging spots**, as long as the cue has a loop (last column of the cue table).

| Job | Cue | Held clips | Props the clips assume | Needed |
|---|---|---|---|---|
| Farming | `farm` | Farm Plough (M/F, enter-loop-exit) | a hoe in the right hand (the farmer carries one) | **Done:** the Farm spot holds `farm`; reach measured, stand point derived ([Stations.md](Stations.md)) |
| Fishing | `fish` | Fish Rod (enter-loop-exit M/F), Fish Reel Fight, Fish Pull Out, Fish Cast, Fish Wait Hold | a rod | spot holdCue + rod prop |
| Mining | `mine` | Mine Ground / Mine Wall (L/R × M/F), Drill Low | a pick / a drill | spot holdCue + props |
| Hammering | `hammer`, `craft`, `repair` | Hammer Ground (L/R × M/F, enter-loop-exit), CMU Hammer, Saw, Kneel Work | a hammer (exists as Tool_Hammer) | spot holdCue |
| Cooking | one cue per station: `stir`, `cookpan`, `cookwok`, `grill`, `chop`, `wash`, `blend` (loops); `plate`, `season` (one-shots); `cook` is the family word | Stir, Stir Pot, Cook Pan, Cook Wok, Grill Meat, Chop Food, Chop Vegetables, Wash Vegetables, Blend Fruit | a ladle (stir; the stand-in for a spatula and a pan), a cleaver (chop); **no pan, wok or spatula tool exists** | **Done:** the pot spot holds `stir`, the grill `grill`, the bread oven `cookpan` ([Stations.md](Stations.md)); wok, sink and blender have no prop spot yet; chain a one-shot between loops via `TaskFinished` rows (open) |
| Serving / bartending | `serve` | Bartend (4 sequences), Bartend Hold (loop), Plate Food, Pour Water | bottles, mugs | spot holdCue + props |
| Gathering / foraging | `gather`, `pickup` | Gather Plants (M/F loop), Gather From Ground, Gather Crouch Hold | none | outrider trips: **data:** trip row cue → `gather` |
| Tending / repair | `tend`, `repair`, `rummage`, `wipe`, `weave` | Gather Plants, Kneel Work (tend); Wrench, Screwdriver (repair); Rummage; Wipe Surface; Coil Rope | | **Done** ([Stations.md](Stations.md)); animals and kiln have no clip, the resident stands |
| Operating machines | `operate` | Fax, Button Pushing, Enter Code, Vending ×3 | | **Code:** terminals/shops raise `Operate` moment for the NPC user |
| Music, reading, chanting, singing | `music`, `read`, `chant`, `sing` | Play Piano (stand-sit-play-stand), Spellbook Read, Spellbook Chant, Sing ×3 | piano, book | social spots at settlements: **data** |
| Hauling | `carry` | Carry Idle/Walk/Pickup/Putdown/Hand Over/Receive Crate | a crate | errands: `props.Release` + `Hold(carry)` while a prop is carried (**code**, 5 lines) |
| Task dwell (NpcTask) | only the end one-shot (`TaskFinished`) plays; the dwell shows nothing | everything above | **Code:** `NpcTaskModule` dwell → `Hold(site.holdCue)` (server-side, an Everywhere variant), release on finish |

### 4. Needs and rest
Eating (`eat` 6: hand-bowl loop, long meal), drinking (8), sleeping (3: Lie Down, Lying Idle, Mixamo lying loop), warming (cold 3), exhausted rest (enter-loop-exit), sitting (12). **Missing:** seated eating and drinking, getting into and out of a bed, a side sleeper. **Wiring:** `ResidentPresence` holds `campSleepCue` already; eat and drink have no spot holdCue yet (**data**).

### 5. Combat
| Situation | Today | Now available | Needed |
|---|---|---|---|
| Melee swing | `attackCue` brawl (6) / swordplay (3); Blow timer on the clip's Contact mark | unchanged | **Missing clips:** sword combos, spear thrust; marks must be authored for any new strike |
| Block / dodge | `MeleeDefense` → Block L/R, Duck, Dodge Back (12%/8%) | block 5, dodge 2 | **Missing clips:** weapon parry, side dodges; thin on purpose until more arrive |
| Hit reaction | cue `hurt`, 5 actions, direction-blind | `Hit Damage` (KI) | **Missing clips:** directional hits; **code:** `HurtReaction` passes hit direction into the pick |
| Knockdown / stagger / get up | `fall` 11, `getup` 11 | `Slip And Recover`, `Wounded Collapse`, Get Up ×3 + Rise From Prone | **Code:** a `Knockdown` moment from heavy damage → `Hold(collapse)` then release for the get-up |
| Death | ragdoll (+ `Death Fall`, `Death01`, `Death Fall Short` unused by NPCs) | 6 death/fall one-shots | **Code:** play a death action first, ragdoll after the clip's impact frame, so the corpse does not teleport from standing |
| Draw weapon / escalation | `AggressionTelegraphModule` holds `drawnAction`/`waryAction` | `Draw Pistol`, `Spellbook Draw`, KI Aim Hold, Heavy Idle | **Data:** point `drawnAction` at the new draws for armed factions |
| Ranged | `Pistol Recoil` + Aim Pistol on robots | KI Aim Rifle Hold (3 variants), Rifle Shoot Level | **Never** use a level shot clip as a use action (it snaps an aimed arm); keep recoil additive |
| Throw | 13 `throw` actions; no NPC raises it | Throw Overhand Wide, KI spear and boomerang | **Code:** `NpcItemUseModule` → `Express(throw)`; **marks:** author `Release` before a timer reads it |
| War cry / threaten | `ChatterModule` → `threaten` (4) | Laugh Mad, Threaten Point (were Flex, Cheer) | **Data:** per-faction WarCry row |
| Restrain / arrest | none | `Restrain` (solo clip) | needs a partner clip |

### 6. Group sims
- **Caravans:** Heavy or Carry archetype for porters, Swagger for guards, Stroll for the old; camp scene = sitground ×2 (thin) + cook + bored + sleep + music.
- **War parties:** CMU Walk March + Swagger + `threaten` war cry; draw weapons on band change.
- **Patrol robots:** CMU Walk Robot + existing shoot/aim.
- **Astronauts and drifters (colonies):** Stroll/Formal/Wander archetypes, `operate`/`read` at consoles, `serve` at the bar.
- **Children / elders / animals as humanoids:** no clips; out of scope until a rig exists.

### 7. Order of work
1. **Verify before building more (blocker).** None of the humanoid animation work, old or new, has been run in Play mode, on a host, or on a client (HumanoidAnimation.md already says so). One short session — spawn a settlement, watch 10 residents hold their cues, join a client — will find more than any amount of further authoring.
2. **Data only, no code:** assign archetype override controllers and the speed multipliers per prefab; add holdCues to Settlement spots for the new job cues; give each faction its `MomentReactions` table; point `drawnAction`/`waryAction` at the new draws. Add the missing loop for any cue the plan binds to a state (check the loop column).
3. **Small code hooks:** NpcTask dwell `Hold`; hauling `Hold(carry)`; `Hold(injured/drunk)` plus speed multiplier; Operate/Throw/Stumbled/Knockdown moments; hit direction into `hurt`; death-then-ragdoll; `TurnSpeed` or a raised `turn` cue.
4. **Clips to source** (free libraries: Mixamo, Kevin Iglesias full packs, Quaternius UAL2): directional hits, sword combos and parry, nod/shake/thumbs-up/shush/hug, seated eating and table work, bed in/out, ladder transitions, spear thrust, surrender, pray, carry on the shoulder. Mixamo is the quickest: search the nouns above, download "without skin" FBX at 30 fps, drop them in `Art/Animations/Humanoid/` and add a `cuts.json` row.
5. **Quality pass with the real thing:** calibrate `animatorSpeedScale` per archetype by eye on a flat plane; author `Contact`/`Release` marks (the pose sheet and a clip's hand speed find them); tune fade times on the Full actions that start from locomotion.

## Gotchas

- **Nothing here is play-tested.** The plan is built from the assets, the code paths and the EditMode tests; no Play-mode, host or client run backs any row. Step 1 of the order of work is that run.
- **`Hold` ignores one-shots.** A cue bound to a standing state needs a loop-capable action; the "Of which loop" column in [animation-action-inventory.md](animation-action-inventory.md) is the check (`serve` had none until `Bartend Hold`).
- **A locomotion archetype only fixes the feet if the speed matches.** The ratios in [AnimationCatalog.md](AnimationCatalog.md) are measured per clip, but the default walk's own ground speed is an estimate; calibrate by eye on one NPC first.
- **Swapping an override controller at runtime resets layer states** (and fires a rebind); choose the archetype at spawn, and use the Full-slot locomotion actions (`Walk Injured Belly`, …) for states that come and go.
- **A level shot clip is never a gun's use action** (it snaps an aimed arm); recoil stays additive.
- **Server-decided states need an Everywhere variant** (`ReactEverywhere`, `PlayEverywhere`); a `Hold` made only on the server shows on the host alone.

## Extending

Add a row to the right table when a situation is handled, and delete it from the Gaps table when a clip arrives. A new situation is: find or add a cue ([AnimationCatalog.md](AnimationCatalog.md) → Extending), tag actions, then bind it at the right place — a spot's `holdCue`, a `MomentReactions` row, or one `Hold`/`React` call in the module that owns the state.
