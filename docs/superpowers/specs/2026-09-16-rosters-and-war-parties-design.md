# Rosters and War Parties — design

Status: **approved in brainstorming 2026-09-16**, awaiting spec review.
Parent design: [2026-09-07-faction-system-design.md](2026-09-07-faction-system-design.md).
Parent plan: [2026-09-07-faction-system.md](../plans/2026-09-07-faction-system.md) (Phase 4).

This is **sub-project 1 of Phase 4**: plan Tasks 4.1 and 4.2, plus the war parties that make a
tribe's war with a player mean something. It **supersedes** two passages in the parent documents,
which disagreed with each other:

- Design §3.4, *At war*: "caravan records of that tribe route toward that player's last known
  position". Every caravan diverting is replaced by **dedicated war parties**; ordinary caravans
  keep travelling and trading.
- Plan Task 4.2's `quarry` field on `bountyHunters` templates. Replaced by runtime war-party
  groups carrying a quarry on the **group**, not the template.

---

## 0. Phase 4, decomposed

Phase 4 is four separate pieces of work, each with its own spec → plan → implementation cycle:

| # | Sub-project | Plan tasks | Depends on |
|---|---|---|---|
| **1** | **Rosters, roster-drawn caravans, war parties** — this document | 4.1, 4.2 | Phase 3 |
| 2 | Territory — settlement ownership, the Trespass input | 4.4 | Phase 3 |
| 3 | Mechanics and Sky tribes | 4.3 | 1; needs names and art decisions |
| 4 | Traders | 4.5 | 1; starts with a spike |

## 1. Decisions

| Question | Decided |
|---|---|
| Who comes after an at-war player? | **Dedicated war parties.** Ordinary caravans carry on. (They still shoot you on sight: the goodwill layer already resolves `AtWar` as hostile.) |
| How does a party know where the player is? | **Tracks, but never loses the trail.** Sighting and last-known position, plus a periodic fuzzy fix on the player's real position when contact is lost. |
| What ends a war? | **Each war party is a reckoning.** Self-defence never costs goodwill; every resolved encounter moves the player toward peace. |
| Where does a party start? | **The nearest camp**, with an off-screen fallback when none is in range. |
| Strength over a war? | **Escalates, then the tribe gives up.** Each party beaten raises the next one's tier. |
| Two players at war with one tribe? | **One party per hunted player.** |
| Player far away but not very far? | **The party catches up while unobserved**, never arriving inside visible range. |
| Player incredibly far away? | **The party gives up without resolving anything**; a new one rises nearer the player after the cooldown. |
| Architecture | **Runtime world-sim groups plus a separate `WarPartyDirector`.** |

## 2. Principles consulted

- **`GDC-L1-SYS-0004`** — *Know your feedback loops* (objective, confidence 4). Before this design,
  fighting back against hunters cost goodwill, which deepened the war, which justified more
  hunters: an **unintended reinforcing loop** with no exit. The reckoning turns it into a deliberate
  **balancing** loop. Escalation (beat a party, the next is stronger) is a local reinforcing loop,
  bounded by the reckoning. The principle's own caveat applies — catch-up must be legible, hence §5.3.
- **`GDC-L1-DESIGN-0006`** — *Legible consequences* (contextual, 4). Names "faction and reputation
  systems" explicitly. A reckoning the player cannot perceive is, to them, no consequence at all.
  Phase 7's UI is not built, so this design includes a minimal tell (§5.3) rather than leaving the
  mechanic invisible.
- **`GDC-L1-DESIGN-0004`** — *Challenge that tracks skill* (contextual, 4). Escalate-then-give-up is a
  rising curve with a designed rest beat when the war ends.
- **`GDC-L1-CONTENT-0004`** — *Validate content on ingest* (contextual, 4). Applied to rosters in §3,
  including its exception: validate what breaks, but do not block work in progress.

---

## 3. Rosters

`FactionRoster` — a ScriptableObject, one per tribe, under
`Assets/Game/ScriptableObjects/Factions/Rosters/`. Menu `Assets ▸ Create ▸ Factions ▸ Roster`.

| Field | Type | Purpose |
|---|---|---|
| `faction` | `FactionDefinition` | Back-reference. `FactionDefinition.roster` points the other way. |
| `members` | `RosterMember[]` `{ role, prefab, weight }` | A template asks for a role; the roster answers with a weighted prefab. |
| `mounts` | `GameObject[]` | Animals a `Rider` sits on (`NomadOstrich`, saddled Appa). |
| `handItems` | `InventoryItem[]` | Weapons drawn at spawn. Replaces `NomadPrefabBuilder.WeaponArtifactPaths`, which is **deleted**. |
| `chatter`, `hostileLines` | `DialogPool` | The tribe's voice. `hostileLines` supplies the war party's first-sight bark. |
| `warPartyTiers` | `WarPartyTier[]` `{ RoleCount[] }` | Composition per escalation tier. **New; not in the parent design.** |

`RosterRole`: `Scout`, `Warrior`, `Rider`, `Trader`, `Elder`.

**Deliberately absent** — each arrives with the sub-project that needs it: `vehicles`,
`clothPalette`, `homeSites` (sub-project 3); `wornGear` (Phase 5); per-tribe aggression and
goodwill overrides (held for the user's planned review of what makes NPCs aggressive). Unity
tolerates fields added later.

**Deterministic draws.** Seeded by group `rosterSeed` and member index, so a group that folds and
re-spawns returns with the same prefabs.

**Validation on ingest** (`GDC-L1-CONTENT-0004`), in two strengths, because the principle's own
exception warns that over-strict validation blocks legitimate work in progress:

- **`OnValidate` warns, and never blocks.** It fires on every Inspector edit, so a designer half-way
  through adding a member slot must not get an error per keystroke. An empty slot is *unfinished*,
  not broken.
- **`RosterAssetTests` fail hard** for the shipped roster — that is where "broken and shippable" is
  caught, at the build rather than at runtime.

What both check:
- `roster.faction.roster == roster`.
- Every member prefab carries `AgentController` and `EntityFaction`.
- Every `handItems` entry has `equipKind == Hand`.

At runtime, drawing a role with no members logs an error and returns null. It never substitutes a
different role.

**`SandTribe.asset`** is authored from what the sand nomads already are: the five nomad prefabs,
`NomadOstrich` and saddled Appa as mounts, the seven weapon artifacts, the default dialog pool.
Its war-party tiers (starting values, tunable):

| Tier | Composition |
|---|---|
| 0 | 2 Scout |
| 1 | 3 Warrior, 1 Scout |
| 2 | 3 Warrior, 2 Rider |

---

## 4. Runtime groups and roster-drawn caravans

### 4.1 Runtime groups

`NpcWorldSim` gains:

- `NpcGroup CreateGroup(NpcGroupTemplate template, string groupId, Vector3 start)`
- `void DisbandGroup(string groupId)` — despawns live members through the netcode path and removes
  the group and its record.

Authored templates keep seeding exactly one group each at startup, unchanged. A runtime group's id
is distinct from its template's (`warparty:<tribeId>:<profileId>:<n>`). The save record already
separates `id` and `templateId`, and `RestoreRecords` already recreates a group whose id is not a
seeded one provided its template exists — so runtime groups persist without a format change beyond
§4.2.

### 4.2 The group record

`NpcGroup` and `NpcGroup.Record` gain three fields, **appended** so older saves read defaults:

| Field | Meaning | Default (old saves, ordinary caravans) |
|---|---|---|
| `rosterSeed` | Which prefabs and weapons this group draws | 0 — a valid, deterministic seed |
| `quarryProfileId` | Whose war this party is fighting | empty — not a war party |
| `tier` | War-party escalation tier | 0 |

### 4.3 Templates learn their tribe

- `NpcGroupTemplate.tribe` (`FactionDefinition`).
- `NpcGroupMemberSpec.role` (`RosterRole`). If `prefab` is set it is spawned exactly as today; if only
  `role` is set, the prefab is drawn from `tribe.roster`.
- Spawning applies `tribe` as each member's faction via `EntityFaction.Ensure`.
- A war party's composition comes from `roster.warPartyTiers[group.tier]`, not from `template.members`.

In `persistentScene`: *Nomad Caravan* and *Sand Nomads* get `tribe = SandTribe`. *Bounty Hunters*
gets `tribe = Outlaws` and **keeps its explicit prefabs** — Outlaws are not a tribe and have no roster.
A new authored template, *Sand War Party*, holds the formation, speed and `bountyHunters = true`
for Sand's war parties, so `RestoreRecords` can always resolve a restored party.

### 4.4 Same guns after a refold

`NpcRandomLoadout` currently rolls with `Random.Range` inside `OnNetworkSpawn`, so **a caravan that
folds and re-spawns comes back differently armed**. The roll becomes seeded from the group's
`rosterSeed` combined with the member's index. A nomad with no group — placed by hand — still rolls
randomly.

**Ordering risk, to be resolved in the plan:** the roll fires inside the network spawn, so the seed
must be applied before `NetworkObject.Spawn`, not after `NpcSpawn.Create` returns.

### 4.5 Where the weapon list lives

The roster owns it. `NomadPrefabBuilder` bakes `roster.handItems` into each nomad's
`NpcRandomLoadout.candidates` at build time, so hand-placed nomads keep working standalone. A test
fails if the baked list and the roster ever differ.

Chosen over reading the roster at runtime because Clankers use the same component with their own
list and no roster: one runtime path plus a drift guard is simpler than precedence rules between two
sources.

---

## 5. War parties

### 5.1 Lifecycle

`WarPartyDirector`, a MonoBehaviour on the `NpcWorldSim` object beside `FactionGoodwillLedger`.
**Server-only** (`Network.Decides`). Its own class so `NpcWorldSim` does not become a catch-all: the
director decides *when* a party exists; the world sim moves it.

- Subscribes to `FactionGoodwillLedger.BandChanged`.
- A player entering `AtWar` with a tracked tribe **opens a war** for that (tribe, profile) pair.
- A player leaving `AtWar` **closes the war**: their party is disbanded and their tier resets to 0.
- At most **one active party per war**. After a party resolves, the next is raised once `partyCooldown`
  has elapsed.
- Wars are keyed per player, so two players at war with Sand have two independent wars and two parties.

### 5.2 Origin, catch-up and tracking

**Origin.** The director searches `WorldSiteRegistry` for the nearest `SiteKind.Camp` within
`campSearchRadius` of the quarry's last known position and creates the party there. Tribes do not
own camps until sub-project 2; any camp counts until then. With no camp in range, the party is
created `fallbackDistance` from the last known position, outside visible range.

**Why it travels for free.** A group outside every player's spawn radius is a **folded record** that
the world sim advances off-screen. A party created at a distant camp is therefore already riding
toward the player, cheaply and unseen, and becomes bodies only on approach.

**Catch-up while unobserved.** While folded, a party may jump along its path toward its lead, up to
a **staging distance** = the world sim's spawn radius + `stagingMargin`. It **never** jumps:
- to any point within any player's spawn radius, and
- closer than the staging distance to its lead.

So the player always watches a party arrive at normal speed over the final stretch. It never appears
on top of them.

**Abandoning.** If the quarry is more than `maxPursuitDistance` from the party, the party is
disbanded **without resolution** — no goodwill change, no tier change — and the cooldown starts. The
next party rises from the camp nearest the player's new location.

**Tracking.** `RefreshLead` becomes quarry-aware for groups with a `quarryProfileId`:

1. A member can see the quarry → lead is the quarry's position.
2. Contact lost → head for the last known position.
3. No contact for `trailInterval` → the server sets the lead to the quarry's **real position offset by
   up to `trailFuzz`**. The party knows roughly where the player went, never exactly where they stand.

Outlaw bounty hunters are unchanged: they chase whoever they see and still respond to
`ReportSighting`. `ReportSighting` stays scoped to them, because it carries a position but not whose
— a war party fed it could be sent after the wrong player.

### 5.3 The reckoning

| Outcome | Condition | Goodwill (quarry, tribe) | Tier for next party |
|---|---|---|---|
| **Caught** | The quarry is killed and `LastDamageSource` resolves to a member of this party | **+`caughtCredit`** | unchanged |
| **Defeated** | Every member of the party is dead, while it was unfolded | **+`defeatedCredit`** | `min(tier + 1, maxTier)` |
| **Abandoned** | Quarry beyond `maxPursuitDistance` | none | unchanged |

- The quarry dying to **anything else** — a Clanker, a fall, another tribe — is not a reckoning.
- A folded party cannot be *defeated*; nobody can reach it.
- Both credits report through `FactionGoodwillLedger` as amends events, so hysteresis, spread rules
  and `BandChanged` behave exactly as for any other event.

With the default thresholds (enter `AtWar` at −80, leave above −70) a player at −85 is out of war
after **one Caught** (−55) or **two Defeated** (−73, then −61).

**Playtest tuning, 2026-09-17:** wars were ending far too soon, so `caughtCredit` dropped 30 → 15
and `defeatedCredit` 12 → 4 (§9). With the new values a player at −85 is *not* out of war after one
Caught (−70, still inside the sticky `AtWar` edge) — it now takes a Caught plus at least one
Defeated, or several Defeated on their own, to get back out. See the tuning brief:
`.superpowers/sdd/2026-09-16-rosters-and-war-parties/tuning-brief.md`.

### 5.4 Self-defence

Hits **and** kills on a war-party member are **not reported** to the ledger when the attacker is:

- the party's quarry, or
- on the quarry's side — **same `EntityFaction`**, the rule the hunt spill already uses: the crew in
  the open world, the team in a versus match.

Hits are exempt as well as kills because every hit costs 2–10 points; exempting only kills would
still push a defending player deeper into war. An attacker **not** on the quarry's side receives no
exemption. Local aggression is unaffected — nearby tribe members still react to a firefight through
their own aggression meters, which are separate from goodwill.

Each spawned member carries its group id so the exemption can be decided where damage is reported.

### 5.5 Telling the player

Through `SystemMessages.Post` — the existing single visor channel — shown only to the hunted player:

| When | Message intent | Severity |
|---|---|---|
| A party is raised | A war party has been sent after you | Warning |
| A party is **Defeated** | The tribe's resolve is weakening | Notice |
| The war ends | The tribe has given up the war | Notice |

A party's first sight of its quarry triggers a bark from the roster's `hostileLines` through
`ChatterModule.TrySayNow`.

---

## 6. Multiplayer

- The director runs only where the world is decided. War-party members are ordinary world-sim
  members and spawn through the existing networked path; no new network prefabs.
- The seeded loadout roll happens on the server. `NpcRandomLoadout` already replicates the result
  through its network variable, so clients are unaffected by *how* the server picked.
- The self-defence exemption and every reckoning are decided on the server, against server-side factions.
- **Notifications and the host.** `FactionGoodwillNetwork` deliberately skips sending bands to its own
  host, because the host holds the rows. Notifications must not inherit that rule, or the host would
  never learn a party is coming. A notification posts locally when the hunted player is this machine's
  own, and travels by targeted `[Rpc]` otherwise.
- **The quarry disconnects.** Their party is disbanded without resolution. The war itself persists in
  their player-scoped save and resumes when they rejoin.
- Arena modes have no world sim and no ledger, so no war parties occur there. Wherever both exist, the
  same-faction rule scopes everything to teams with no mode check.

## 7. Persistence

- **Active parties** persist as world-sim group records carrying `quarryProfileId`, `tier` and
  `rosterSeed` (§4.2).
- **Escalation between parties.** A party's tier is on its record only while it exists. During the
  cooldown it would live solely in the director's memory, and a reload would silently reset the tribe
  to sending scouts. The tier is therefore appended to `FactionGoodwillSaveable.Standing` as `warTier`
  — player-scoped, beside the band it belongs to.
- **The cooldown is not saved.** It restarts from full on load, which can only delay the next party, so
  reloading is not an exploit.
- **No duplicate after a reload.** On load the director **adopts** every restored group with a
  `quarryProfileId` before reconciling wars against the ledger. Reconciling first would see "at war, no
  party" and raise a second one.

## 8. Testing

EditMode, pure where the rule allows:

- **`RosterDrawTests`** — seeded draws are stable; weights hold over 10 000 draws; an empty role logs and
  returns null.
- **`RosterAssetTests`** — the shipped Sand roster validates; every member prefab carries the required
  components; `handItems` are hand items; the builder's baked candidates equal `roster.handItems`.
- **`WarPartyRulesTests`** (pure) — each outcome's goodwill step and next tier; escalation caps at
  `maxTier`; tier resets when the war ends; catch-up never lands within a player's spawn radius or
  inside staging distance; beyond `maxPursuitDistance` the party abandons; a trail fix stays within
  `trailFuzz` of the real position.
- **`SelfDefenceRulesTests`** (pure) — exempt for the quarry and same-faction attackers; not exempt for
  a different faction.
- **`WarPartyPersistenceTests`** — a runtime group's `tier`, `quarryProfileId` and `rosterSeed`
  round-trip; a restored war party is adopted, not duplicated; `warTier` round-trips on the player save.

**Play checklist** (host and client):
1. Reach `AtWar` → a warning appears and a party rides out from a camp.
2. Flee beyond max pursuit → it gives up; after the cooldown a new party rises nearer you.
3. Wipe one out → "resolve weakening"; the next party is a tier stronger.
4. Get caught → the war ends and "given up" appears.
5. Reload mid-hunt → the same party continues and no second party appears.
6. Two players at war → two parties.
7. A crewmate helping you fight a party loses no goodwill.
8. Quit and reload during a cooldown → the next party is still the escalated tier.

## 9. Tunables — starting values

All serialized on the director or the roster; nothing here is final.

| Tunable | Default | Where |
|---|---|---|
| `partyCooldown` | 60 s (was 180 s; playtest tuning 2026-09-17) | director |
| `campSearchRadius` | 1000 m | director |
| `fallbackDistance` | staging distance + 100 m | director |
| `stagingMargin` | 30 m (was 50 m; playtest tuning 2026-09-17) | director |
| `maxPursuitDistance` | 1500 m | director |
| `trailInterval` | 10 s (was 60 s; playtest tuning 2026-09-17) | director |
| `trailFuzz` | 30 m (was 80 m; playtest tuning 2026-09-17) | director |
| `caughtCredit` | +15 (was +30; playtest tuning 2026-09-17) | director |
| `defeatedCredit` | +4 (was +12; playtest tuning 2026-09-17) | director |
| `maxTier` | 2 | derived: `warPartyTiers.Length − 1` |

`FactionGoodwillLedger.decayPerGameHour` also dropped 2 → 0.5 in the same pass (design
§3.4/faction-system-design.md), for the same "wars end too soon" feedback.

## 10. Out of scope, recorded for later

- **Quest-granted goodwill.** Completing a quest for a tribe is an amends event into the same ledger,
  the way trading is. The quest system is future work; the ledger needs only a new `GoodwillEvent` when
  it arrives. *Requested by the user 2026-09-16; plan only, not built now.*
- **Mount kills.** With rosters and groups, a mount belonging to a tribe's group is now attributable,
  so design §3.4's `MountKill` becomes buildable. Not done here.
- **Per-tribe aggression and goodwill tuning** — waits on the user's review of what makes NPCs aggressive.
- Territory (sub-project 2), new tribes (3), traders (4).

## 11. Risks

- **Loadout seed ordering** (§4.4) — if the seed cannot be applied before the network spawn, weapons will
  not survive a refold. The plan must prove this with a test before building on it.
- **Catch-up and world-sim ticking.** Catch-up assumes folded groups advance on the world sim's tick. If
  folded groups advance only in coarse steps, staging distance and the visibility check must allow for a
  full step, or a party could land inside visible range between ticks.
- **Abandon thrash.** A player hovering around `maxPursuitDistance` could make parties abandon and re-rise
  repeatedly. The cooldown bounds it; watch for it in play.
