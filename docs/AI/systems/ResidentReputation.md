---
system: ResidentReputation
layer: characters
summary: "What a settlement thinks of a player and tells each other: deeds, favor shared by closeness, word of mouth"
paths:
  - Assets/Game/Scripts/agents/Residents/Mind/Gossip.cs
  - Assets/Game/Scripts/agents/Residents/Mind/Favor.cs
  - Assets/Game/Scripts/agents/Residents/Mind/Rumours.cs
  - Assets/Game/Scripts/agents/Residents/Mind/ResidentMemory.cs
  - Assets/Game/Scripts/agents/Residents/Mind/Attitude.cs
symptoms:
  - "only the residents who saw me hit somebody react; the ones next to them never hear about it"
  - "the settlement turns on me neighbour by neighbour for a minute or two after one punch"
  - "a resident I saved is no friendlier to me than one who never met me"
  - "after I hit one villager every resident in the settlement is cold to me"
  - "a resident I defended never thanks me"
  - "residents fighting me run straight past a neighbour who never joins in"
reads_with: [Residents, AgentSystem, Persistence]
updated: 2026-10-02
---

# Resident reputation

How what a player does to (or for) settlement residents becomes what every resident thinks of them. The residents
themselves — plans, routine, speech, provocation — are [Residents.md](Residents.md).

## Model

| Idea | Mechanism |
|---|---|
| A deed is a remembered fact | `ResidentMemory.Deed {profile, act, victim, day, expires, heard}` — harm (`Hit`, `Threatened`, `Killed`, kin readings) or kindness (`Defended`); `victim` is whoever it was done to |
| Residents know what they saw or were told | `Gossip.Witness` (the subject + everyone in `noticeRadius` with line of sight), then word of mouth |
| Word of mouth, no hop limit | [`Rumours`](Assets/Game/Scripts/agents/Residents/Mind/Rumours.cs) (ticked by the settlement's `SettlementSociety`): whoever knows tells every resident within `earshot`; listeners pass it on. News moves favor, never starts a fight |
| Fights spread by calling for help, not by news | the character's own `AlertBroadcaster.callForHelpEvery` (every Raxy: 1 s, in or out of a settlement) — [AgentSystem.md](AgentSystem.md). A resident answers by temperament (`Resident.HandleAllyHurt`) |
| Favor is shared by closeness | [`Favor`](Assets/Game/Scripts/agents/Residents/Mind/Favor.cs): a deed is worth `favorFor*` to its subject and a share to each resident who learns it — family 0.75, friend 0.5, coworker 0.35, anyone else 0.2 — counted once per resident |
| Stance reads regard | regard = familiarity (talks) + favor (deeds); [`Attitude.StanceFor`](Assets/Game/Scripts/agents/Residents/Mind/Attitude.cs) is Warm at ≥ `warmAt`, Cold/Afraid at ≤ −`coldAt` or under a **personal** grudge |
| A grudge is personal only up close | `HoldsPersonalGrudge`: harm the resident saw, or harm to its kin however learned. Harm to anyone else it only heard of moves favor and nothing more |

## Key types

| Type | Role |
|---|---|
| [`ResidentMemory`](Assets/Game/Scripts/agents/Residents/Mind/ResidentMemory.cs) | per resident, keyed by save profile: familiarity, favor, deeds, known deaths; `Regard`, `Holds`, `HoldsPersonalGrudge`, `KinHarmedBy` |
| [`Gossip`](Assets/Game/Scripts/agents/Residents/Mind/Gossip.cs) | `Witness`, `Pass` (word of mouth), `Learn` (remember + weigh once), `ObservationOf` (the line a deed is told with) |
| `Favor` | pure: `ValueOf(act)`, `ShareOf(listener, subject)` (closest bond either holds), `Apply` (±`favorLimit`), `IsHarm` |
| `Rumours` | news: who is within earshot of whom, every `rumourSweepSeconds` (0.5), one hop per `rumourTellSeconds` |
| `ResidentTuning` | `favorFor{Hit,Threat,Killing,Defending}`, `{family,friend,coworker,neighbour}Share`, `favorLimit`, `warmAt`, `coldAt`, `rumourTellSeconds` |

## Flows

1. **Harm.** A hit or kill on a resident → `Resident.ReportHarm` → `Gossip.Witness(victim, attacker, Hit|Killed)` (the
   fight itself is [Residents.md](Residents.md)'s).
2. **Kindness.** Any damage (`HealthComponent.AnyDamaged`, server) to an entity whose `AgentTargeting.Target` is a
   resident, dealt by a player → `Gossip.Witness(resident, player, Defended)`. The resident thanks them (`Remark` ×
   `Defending`) the first time that day.
3. **Learning a deed** (`Gossip.Learn`). Re-read through the learner's bond (a sister's hit is `HarmedKin`), merged into
   memory (first-hand wins), and — only if new — favor moves by `ValueOf(act) × ShareOf(learner, subject)`. A deed is one
   per (player, act, subject, day): defending or hitting the same resident again that day is no new news.
4. **Word of mouth** (server, every 0.5 s). Each onstage resident holding a deed tells every onstage resident within
   `earshot` (20 m) who lacks it (`Gossip.Pass`). A resident who just learned something passes it on
   `rumourTellSeconds` (1.5 s) later; a witness, or anyone after a reload, at once. So news walks the settlement
   neighbour to neighbour and reaches whoever later comes within earshot of somebody who knows.
5. **Calling for help** is the character's, not the settlement's: a Raxy fighting calls every ally in 30 m each second
   (`AlertBroadcaster.CallForHelp`, [AgentSystem.md](AgentSystem.md)). A resident it reaches answers by temperament —
   nerve ≥ 0.35 joins, anyone bonded to the CALLER joins, the rest shelter; one already fighting is never sent home
   (`HandleAllyHurt` returns while provoked). Whoever joins calls in turn. Covered by `MindTests.ACallForHelp_…`.
6. **Speech.** The teller says a `Rumour` line naming the subject when a player can hear, it is not mid-line, not
   fighting and not the subject. `Conversations` opens a pair's talk with today's deed (`Gossip` × its observation).

## Multiplayer

All of it runs where the server decides (`Network.Decides`). Its consequences reach clients the ways they already do:
lines are `ResidentSaid`, a fight is server-owned targeting, a shelter is a goal on the replicated body. Clients never
hold favor or deeds.

## Persistence

`ResidentSaveable` (key `"resident"`) saves the whole memory: familiarity, **favor**, deeds (`heard` included), deaths.
When each resident learned a deed and when an attack happened are runtime only, so a reload keeps what is known and
starts nobody mid-alarm. Witnessed-hit timestamps are runtime too.

## Gotchas

- **Fighters re-announce, on purpose** (user decisions 2026-10-02: "broadcast every second while aggro", then "no matter
  the settlement"). It lived here as `SettlementRumours.CallToArms` for an hour; it is `AlertBroadcaster.callForHelpEvery`
  on the prefab now, so a Raxy calls in a caravan or alone too, and any allied faction hears it. The positive loop
  GDC-L1-SYS-0004 warns about is braked by temperament (the timid never join) and the leash (calls stop when the enemy
  leaves 45 m). A new species that should call: set the field on its prefab (or its builder).
- **News no longer calls anyone to arms** (it did for 120 s after an attack, 2026-10-01 → 02). Two routes into a fight
  were one too many; fights spread through fighters, opinion through news.
- **Heard harm to a non-relative is not personal** (2026-10-02). Before, any grudge made a resident Cold, and with word of
  mouth that was the whole settlement after one punch. Coldness now spreads as lost favor, graded by closeness; tune
  the `*Share` knobs, not the grudge rule.
- **Favor is never forgiven by time.** A grudge expires after its forgive days; the favor a deed cost does not come back
  on its own. Talks (familiarity) and good deeds are the way back — regard is the sum.
- **`ActKind` and `Observation` are saved and compared by number** (Newtonsoft writes enums as ints). Append only —
  `Defended` and `Defending` went at the end.
- **Word of mouth is part of the settlement, not a component on it.** It used to be a `SettlementRumours` component that
  had to be added to the root; one added from the Editor was never saved into `Chunk_6_3`, so in play nobody told
  anybody, with a clean console. Since 2026-10-02 `Rumours` is a plain class every `SettlementSociety` owns, so a
  settlement with a culture always has it. Offstage (indoor) residents neither tell nor hear.
- **Bedtime, hearth and dawn gossip never run** — `Gossip.SpreadAtBedtime`/`SpreadAtHearth`/`DawnDeaths` have no caller
  ([DEFECTS](../DEFECTS.md)). Word of mouth covers everyone onstage.
- Only players have a save profile, so only players' deeds are remembered; an NPC defending a resident counts for nothing.
- Never run in play mode, on a client or through a reload yet; the rules are covered by `MindTests` (EditMode).

## Extending

**New deed** (gift, help with a need, trade): append an `ActKind`; give it a `favorFor*` in `ResidentTuning` and an arm in
`Favor.ValueOf` (and in `Favor.IsHarm` if it is kindness); append an `Observation` and an arm in `Gossip.ObservationOf`;
call `Gossip.Witness(subject, player, act, noticeRadius)` on the server where it happens; add `Remark`, `Gossip` and
`Rumour` rows for it in the culture's lines. Spreading, favor, stance and saving then follow with no further code.
