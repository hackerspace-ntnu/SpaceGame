---
name: spacegame-tribe
description: Use when adding a new tribe to SpaceGame end to end — a FactionDefinition with a FactionRoster, its people, mounts and hand weapons, its war parties, and its caravans. Also use when a tribe's caravan or war party is broken — "no war-party template" in the console, a caravan that never draws a member for a role, a rebuild that leaves nomads without savers or a ragdoll, or a roster that fails RosterAssetTests.
---

# SpaceGame tribes

> **Design check:** a tribe is a faction, a fighting force and a reputation all at once — before
> naming one, sizing its war-party tiers or writing its hostile lines, read the `DESIGN`, `SYS`,
> `BAL` and `MP` principles in `docs/game-development-constitution/INDEX.md` and cite their IDs.

## Overview

A tribe is a `FactionDefinition` that owns a `FactionRoster`: the people it fields, the weapons they
draw, and the war parties it raises against a player it is `AtWar` with. Full model, flows,
multiplayer and persistence: [AgentSystem.md](../../../docs/AI/systems/AgentSystem.md) and
[Persistence.md](../../../docs/AI/systems/Persistence.md). This skill is the checklist for adding
one — read [spacegame-agent](../spacegame-agent/SKILL.md) first if a new creature/prefab is also
needed; this skill assumes the people already exist as agent prefabs.

The Sand Tribe is the worked example throughout: `SandTribeFaction.asset`, `SandTribe.asset`,
[RosterAuthoring.cs](../../../Assets/Game/Editor/Agents/RosterAuthoring.cs),
[NomadPrefabBuilder.cs](../../../Assets/Game/Editor/Agents/NomadPrefabBuilder.cs).

## 1. `FactionDefinition`

Create via `Assets > Create > Factions > Faction Definition`, saved under
`Assets/Game/ScriptableObjects/Factions/Core/`. `defaultStance` **must stay `Neutral`** — a
`Hostile`-default faction can never be a tribe: `FactionGoodwillLedger.Awake` never adds one to its
tracked set (it stays in the serialized `tribes` array untouched; a warning is logged), because
nothing can move a value nobody can read as better than Hostile. Add any relationship rows the tribe needs to
`GlobalRelationships.asset` (`Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset`)
— none at all is the common case; two neutral tribes need no row between them.

## 2. Register it with the goodwill ledger

`FactionGoodwillLedger` lives on the `NpcWorldSim` object in
`Assets/Game/Scenes/world/persistentScene.unity`. Add the new `FactionDefinition` to its
`tribes` array (Inspector, or a `SerializedObject` edit from an editor menu like
`RosterAuthoring.WireWorldSim` does for templates). Skip this and the tribe has no goodwill rows at
all: every hit and kill on its people is silently ignored, and it can never reach `AtWar`.

## 3. `FactionRoster`

One roster per tribe, `Assets/Game/ScriptableObjects/Factions/Rosters/<Tribe>.asset`, built by a new
menu method in [RosterAuthoring.cs](../../../Assets/Game/Editor/Agents/RosterAuthoring.cs) modelled
on `AuthorSandRoster` — copy it, point it at the new faction and prefabs, keep it idempotent
(overwrites what it owns, nothing else). It sets:

- **`members`** — one `RosterMember { role, prefab, weight }` per person/mount. `role = Rider` means
  `prefab` is the **mount**, carrying an `NpcPassenger` — the same convention caravan templates use;
  the mount keeps its own (Fauna) faction and the tribe goes on the rider it seats. Every other role's
  prefab **must already serialize the tribe's `FactionDefinition`** on its own `EntityFaction`,
  because faction is not replicated — a client's copy of a member has only what the prefab shipped
  with, and `RosterValidation` fails a member whose baked faction disagrees with the roster's.
- **`handItems`** — every entry must be `EquipKind.Hand` (`RosterValidation` fails otherwise) and, to
  read as a threat before it fires (`MenaceSensor`/`AggressionTelegraphModule`), should have
  `InventoryItem.menacing` set. Baked into every random-weapon member's `NpcRandomLoadout.candidates`
  by the tribe's prefab builder — see §4, and never hand-edit that array.
- **`warPartyTiers`** — `WarPartyTier[]`, each a set of `RoleCount { role, count }`. Every role a tier
  asks for must have at least one `members` entry with positive weight, or `RosterValidation` fails
  and `NpcGroupComposition.Resolve` logs an error and draws nothing for that tier at runtime.
- **`hostileLines`** — a `DialogPool` shouted by `ChatterModule.WarCry` on a war party's first sight
  of its quarry. Without one, `WarPartyDirector.OnQuarrySighted` has nothing to say and stays silent.

Finish by setting `faction.roster = roster` (the back-reference `RosterValidation` checks both ways)
and running `RosterValidation.Problems(roster)` — log every problem, same as `AuthorSandRoster` does.

## 4. Bake weapons into the prefab, and never break the builder's chain

The tribe's prefab builder (copy `NomadPrefabBuilder.BuildSandNomads`) bakes `roster.handItems` into
each random-weapon member's `NpcRandomLoadout.candidates` — this is **generated**, never hand-edited;
`RosterAssetTests`-style tests fail the moment the two arrays disagree (§7), and a rebuild overwrites
the prefab wholesale regardless.

**The build is a chain, and it must run to the end.** `BuildSandNomads` builds every prefab, then
calls `NetworkPrefabRegistrar.SyncMenu()` → `WireSaveables()` → `RagdollWiring.WirePrefabs()`. An
interrupted run (editor reload, exception, a Unity crash mid-build) that stops before the last two
steps leaves finished-looking prefabs with **no savers and no `RagdollRig`** — seen this way in this
session. `AgentController`/`AgentGoal` are auto-added in `Awake` so they are not usually at risk, but
treat a half-finished build as suspect across the board: re-run the whole builder method rather than
patching by hand, and read the prefabs back (`SaveablePolicy`, `RagdollWiring.WirePrefabs`,
`Tools ▸ Save System ▸ Validate Save Wiring`) before trusting them.

## 5. War-party template

A tribe cannot raise a war party without a `runtimeOnly` + `bountyHunters` `NpcGroupTemplate` whose
`tribe` is set, living in the `NpcWorldSim` object's `templates` list — add one from a menu method
modelled on `RosterAuthoring.WireWorldSim` (`Tools/SpaceGame/Agents/Wire War Party Templates`), which
duplicates an existing caravan template's formation, clears `members`/`tasks` (a war party's people
come from `NpcGroupComposition.Resolve` reading the roster's `warPartyTiers`, never a fixed member
list), and sets `runtimeOnly = true`. Skip this and `WarPartyDirector.Raise` logs
`"<Tribe> is at war but the NpcWorldSim has no war-party template for it"` and no party ever comes,
however angry the tribe is. Also ensure a `WarPartyDirector` component sits on the same `NpcWorldSim`
object — `WireWorldSim` adds one if missing.

## 6. Caravan templates

An ordinary (non-war-party) `NpcGroupTemplate` for the tribe: set `tribe`, and for any
`NpcGroupMemberSpec` that should draw from the roster instead of naming a fixed prefab, leave
`prefab` empty and set `role` — `NpcGroupComposition.Resolve` draws from `roster.Draw(role, seed,
index)` when `prefab` is null. Seeded by `RosterDraw.StableHash(template.id)`, so the same caravan
re-spawns with the same faces and guns after every fold (`GroupMembership.MemberIndex` feeds
`NpcRandomLoadout`'s own seeded roll).

## 7. Tests

Add a fixture beside [RosterAssetTests.cs](../../../Assets/Game/Editor/Tests/RosterAssetTests.cs),
one per tribe (or parameterise the existing one over a tribe list — either is fine, keep it
readable). Cover, per the Sand fixture:

- `RosterValidation.Problems(roster)` is empty.
- The faction ↔ roster back-reference holds both ways.
- `warPartyTiers` matches what was authored (`RoleCount` per tier).
- Every random-weapon member prefab's baked `NpcRandomLoadout.candidates` equals `roster.handItems`,
  read via `SerializedObject` off the prefab **asset**, not the runtime component (INVARIANTS: read
  what was actually authored, not what `OnValidate` fixed up in memory).
- `hostileLines` is non-null and non-empty.

Run headlessly and expect `FAILED=0` before trusting a new tribe.

## 8. Verification checklist — host **and** a client, then save/reload

A tribe is not done until every line below has been seen, not merely built. CLAUDE.md: a feature
seen only on the host is not finished, and persistence fails silently.

- [ ] Push the tribe to `AtWar` (hurt its people) → the "war party sent" notice appears on the
      hunted player's own screen, tested once as host and once as client, and a party rides out from
      a camp or a fallback point never inside sight of a player.
- [ ] Flee beyond `maxPursuitDistance` → only that party gives up, silently (`Reckoning.Abandoned`
      credits nothing and shows no notice); the war itself stays open and after `partyCooldown` a new
      party rises nearer the player.
- [ ] Wipe a party out → `WarNotice.Weakening`; the next party is one `WarPartyTier` stronger.
- [ ] Get killed by the party → `Reckoning.Caught` credits goodwill (+`caughtCredit`, 15 by
      default), which lifts the band out of `AtWar` and ends the whole war through the same
      `BandChanged` → `EndWar` path fleeing or decay would, once enough credits have landed —
      deep hostility (playtest tuning 2026-09-17 halved the credit) can need a Caught plus a
      Defeated, or several Defeated on their own (`WarPartyDirectorTests.
      Caught_Credits15_AtDeepHostility_TheWarGoesOn`) — "given up" **does** appear once the band
      actually clears, and the party leaves only once out of sight.
- [ ] Save and reload mid-hunt → the same party continues; no second party appears
      (`WarPartyDirector.AdoptRestoredParties` releases a genuine duplicate).
- [ ] Kill a party's mounts and walkers while a dismounted rider still fights → not yet Defeated; walk
      away until it folds → the dismounted rider is despawned with it (`NpcGroup.Fighters`), on the
      client too.
- [ ] Break line of sight with a spawned party for `trailInterval` → it keeps coming toward a fresh,
      fuzzy fix (`NpcWorldSim.SteerSpawned`) rather than stopping.
- [ ] Save mid-hunt, reload, and do not rejoin as the hunted player → the restored party leaves after
      `absentQuarryGrace` with no goodwill change; rejoining later resumes the war at its tier.
- [ ] Two players at war with the tribe → two independent parties (a war is keyed per player).
- [ ] A crewmate helping fight the party loses no goodwill of their own (compare the ledger value
      before and after — `SelfDefenceRules`).
- [ ] Quit and reload during a cooldown → the next party is still the escalated tier
      (`FactionGoodwillSaveable.Standing.warTier`).
- [ ] Walk away from the tribe's caravan until it folds, walk back → the same members carry the same
      weapons, on the client too.
- [ ] The first-sight war cry (`hostileLines`) is heard on the client as well as the host.
- [ ] `git grep` the save JSON for the tribe's `factionId` under `factionGoodwill` and for its
      war-party group under `npcworld` after the checks above, to confirm both actually wrote.

## Related

- [spacegame-agent](../spacegame-agent/SKILL.md) — building the people/mounts this roster fields.
- [spacegame-persistence](../spacegame-persistence/SKILL.md) — `factionGoodwill` and `npcworld`
  record shapes.
- [spacegame-multiplayer](../spacegame-multiplayer/SKILL.md) — network prefab registration for any
  new mount or person prefab.
- [AgentSystem.md](../../../docs/AI/systems/AgentSystem.md) — the source-verified system reference:
  Model, Flows, Multiplayer and Gotchas for rosters and war parties.
