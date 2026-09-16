# Rosters and War Parties Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each tribe has a roster that caravans draw from, and a player at war with a tribe is hunted by a dedicated war party whose outcome — caught, defeated or abandoned — moves the war toward peace.

**Architecture:** A `FactionRoster` asset per tribe feeds `NpcWorldSim`, which gains runtime groups (created and released at runtime, saved as ordinary group records). A separate server-only `WarPartyDirector` on the same object watches `FactionGoodwillLedger.BandChanged`, keeps a pure `WarBook` of wars, raises parties, tracks and catches them up while unobserved, and settles each one through the ledger. Members carry a `GroupMembership` stamped before their network spawn, which seeds their loadout, counts fighters and decides the self-defence exemption.

**Tech Stack:** Unity 6000.3.11f1, C#, NGO (`[Rpc]`, `NetMessaging`/`AgentActionRelay`), Newtonsoft save adapters, NUnit EditMode.

**Spec:** [docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md](../specs/2026-09-16-rosters-and-war-parties-design.md). Read it in full first; the *why* of every rule below is there.

## Global Constraints

- **Do not touch the sky city.** `SkyCity*`, `sky_city*` and `SkyCityBuilder.cs` belong to another agent working in parallel. Nothing in this plan needs them. Stage only the files each task names — the working tree holds unrelated changes.
- **Commit only when the user has said to.** Each task ends with a commit step; skip it unless commits are authorised for this run.
- Server-only decisions go behind `Network.Decides`. Offline `Network.Server` is **false** and `Network.Decides` is **true**.
- Save formats are append-only: new fields are appended to `NpcGroup.Record` and `FactionGoodwillSaveable.Standing`; save keys never change.
- Every tunable is serialized; spec §9 defaults: `partyCooldown` 180 s, `campSearchRadius` 1000 m, `stagingMargin` 50 m, fallback = staging + 100 m, `maxPursuitDistance` 1500 m, `trailInterval` 60 s, `trailFuzz` 80 m, `caughtCredit` +30, `defeatedCredit` +12, `maxTier` = `warPartyTiers.Length − 1`.
- Sand war-party tiers: 0 = 2 Scout; 1 = 3 Warrior + 1 Scout; 2 = 3 Warrior + 2 Rider.
- Builder-owned prefabs (`Nomad*`) change only through `NomadPrefabBuilder`; rebuild and read back.
- Verification commands:
  - Type-check: `python tools/typecheck.py --editor` → `No errors.`
  - Tests (needs the Editor): `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("<Fixture>")` via Unity MCP, or menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`; then read `Temp/headless_tests.txt` until it holds `DONE`, expect `FAILED=0`. Queue only from a clean scene.
  - Docs: `python tools/docs_check.py --index` → `0 errors`.

### Decisions this plan takes where the spec left room

| Question | Resolution |
|---|---|
| What is a `Rider` member's prefab? | The **mount** prefab carrying an `NpcPassenger` (e.g. `NomadOstrich`) — the convention caravan templates already use. So the spec's separate `mounts` list is **not added**: nothing would read it. The mount keeps its own (Fauna) faction; the tribe is applied to the **rider** it seats. |
| `chatter` on the roster | **Not added.** Nothing in this sub-project reads it and no `DialogPool` asset exists yet. `hostileLines` is added and authored. |
| Faction on clients | `EntityFaction` is not replicated, so a clients' copy of a member has only its prefab's faction. Validation therefore requires every non-Rider member prefab to **already serialize** the roster's faction. |
| Loadout seed ordering (spec §11) | `NpcSpawn.Create` gains a `beforeSpawn` callback invoked after `Instantiate` and **before** `NetworkObject.Spawn`, so `OnNetworkSpawn`'s roll already sees the membership. Offline the roll is in `Start`, later still. |
| A party released while players can see it | It is never popped out of view. Its quarry is cleared and it is removed the moment it folds (`DisbandWhenFolded`). `DisbandGroup` (immediate) is used only for folded groups. |
| A war party wiped out | Marked `WipedOut` and never re-spawned; the director resolves it as Defeated. Ordinary caravans keep today's behaviour. |
| Party origin when the nearest camp is in view | Use the fallback point instead, so a party never appears inside visible range. |
| Catch-up target | `staging + trailFuzz` from the lead, so a fuzzed lead can never pull the jump inside a player's staging distance. |
| War-end detection after a load | `RestoreRow` raises no event, so the director also **reconciles** wars against the ledger each tick, after adopting restored parties. |

## File structure

| File | Responsibility |
|---|---|
| Create `Assets/Game/Scripts/agents/faction/FactionRoster.cs` | `RosterRole`, `RosterMember`, `RoleCount`, `WarPartyTier`, the `FactionRoster` asset and its `Draw`. |
| Create `Assets/Game/Scripts/agents/faction/RosterDraw.cs` | Pure deterministic hashing and weighted picks. |
| Create `Assets/Game/Scripts/agents/faction/RosterValidation.cs` | The one list of roster problems, shared by `OnValidate` (warn) and tests (fail). |
| Create `Assets/Game/Scripts/agents/faction/SelfDefenceRules.cs` | Pure: is this attacker exempt? |
| Modify `Assets/Game/Scripts/agents/faction/FactionDefinition.cs` | `+ roster`. |
| Modify `Assets/Game/Scripts/agents/faction/FactionGoodwillLedger.cs` | `Report` takes the victim entity and applies the exemption; `+ Credit`. |
| Modify `Assets/Game/Scripts/agents/faction/FactionGoodwillNetwork.cs` | `+ Notify` (war notices, host-local or targeted Rpc). |
| Create `Assets/Game/Scripts/agents/World/GroupMembership.cs` | Which group an NPC was spawned for; fighter counting; attribution helpers. |
| Create `Assets/Game/Scripts/agents/World/NpcGroupComposition.cs` | Pure-ish: which prefabs a group spawns. |
| Create `Assets/Game/Scripts/agents/World/WarPartyRules.cs` | `Reckoning`, `WarPartySettings`, pure rules. |
| Create `Assets/Game/Scripts/agents/World/WarBook.cs` | `War`, `WarBook`: pure war state. |
| Create `Assets/Game/Scripts/agents/World/WarPartyDirector.cs` | The MonoBehaviour glue. |
| Create `Assets/Game/Scripts/agents/World/WarNotice.cs` | `WarNotice` + `WarNoticeText`. |
| Modify `Assets/Game/Scripts/agents/World/NpcGroup.cs` | Template `tribe`/`runtimeOnly`, member `role`; group `RosterSeed`/`QuarryProfileId`/`Tier` + runtime flags. |
| Modify `Assets/Game/Scripts/agents/World/NpcWorldSim.cs` | Runtime groups, composition, quarry-aware ticking, events. |
| Modify `Assets/Game/Scripts/agents/Core/NpcSpawn.cs` | `beforeSpawn` callback. |
| Modify `Assets/Game/Scripts/agents/entity/NpcRandomLoadout.cs` | Seeded roll for group members. |
| Modify `Assets/Game/Scripts/agents/Modules/Riding/NpcPassenger.cs` | Stamp the seated rider. |
| Modify `Assets/Game/Scripts/agents/AI/Targeting/ProvocationModule.cs`, `Assets/Game/Scripts/agents/entity/HealthReactionModule.cs` | Pass the victim to `Report`. |
| Modify `Assets/Game/Scripts/agents/Modules/Personality/ChatterModule.cs`, `Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/AgentAction.cs` | Replicated war cry. |
| Modify `Assets/Game/Scripts/Core/Persistence/Adapters/FactionGoodwillSaveable.cs` | `Standing.warTier`. |
| Create `Assets/Game/Editor/Agents/RosterAuthoring.cs` | Menu: author the Sand roster, hostile lines, scene templates and director. |
| Modify `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` | Bake `roster.handItems`; delete `WeaponArtifactPaths`. |
| Tests in `Assets/Game/Editor/Tests/` | `RosterDrawTests`, `RosterAssetTests`, `GroupRecordTests`, `NpcGroupCompositionTests`, `RuntimeGroupTests`, `WarPartyRulesTests`, `SelfDefenceRulesTests`, `WarBookTests`, `WarPartyPersistenceTests`; `FactionGoodwillLedgerTests` extended. |

---

### Task 1: Roster data and deterministic draws

**Files:**
- Create: `Assets/Game/Scripts/agents/faction/RosterDraw.cs`, `FactionRoster.cs`, `RosterValidation.cs`
- Modify: `Assets/Game/Scripts/agents/faction/FactionDefinition.cs`
- Test: `Assets/Game/Editor/Tests/RosterDrawTests.cs`

**Interfaces:**
- Produces: `RosterDraw.Hash(int seed, int index) : uint`, `RosterDraw.Roll01(int, int) : double`, `RosterDraw.IndexFor(int seed, int index, int count) : int`, `RosterDraw.PickWeighted(IReadOnlyList<float>, double) : int`, `RosterDraw.StableHash(string) : int`; `enum RosterRole { Scout, Warrior, Rider, Trader, Elder }`; `FactionRoster.Draw(RosterRole, int seed, int index) : GameObject`, `.TierAt(int) : WarPartyTier`, `.MaxTier : int`, fields `faction`, `members`, `handItems`, `hostileLines`, `warPartyTiers`; `RoleCount { role, count }`; `WarPartyTier { roles }`; `RosterValidation.Problems(FactionRoster) : List<string>`; `FactionDefinition.roster`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Rosters: deterministic draws and weights. The shipped Sand roster is RosterAssetTests' job.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class RosterDrawTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private GameObject Prefab(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            return go;
        }

        private FactionRoster Roster(params RosterMember[] members)
        {
            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            roster.members = members;
            junk.Add(roster);
            return roster;
        }

        [Test]
        public void Hash_IsStableForTheSameInputs()
        {
            Assert.AreEqual(RosterDraw.Hash(42, 7), RosterDraw.Hash(42, 7));
        }

        [Test]
        public void Hash_SpreadsAcrossIndices()
        {
            var seen = new HashSet<uint>();
            for (int i = 0; i < 100; i++) seen.Add(RosterDraw.Hash(42, i));
            Assert.Greater(seen.Count, 95);
        }

        [Test]
        public void IndexFor_StaysInRange_AndRefusesEmpty()
        {
            for (int i = 0; i < 500; i++)
            {
                int index = RosterDraw.IndexFor(9, i, 7);
                Assert.That(index, Is.InRange(0, 6));
            }
            Assert.AreEqual(-1, RosterDraw.IndexFor(9, 0, 0));
        }

        [Test]
        public void PickWeighted_HoldsWeightsOverTenThousandDraws()
        {
            var weights = new List<float> { 1f, 3f, 0f };
            int second = 0;
            for (int i = 0; i < 10000; i++)
            {
                int pick = RosterDraw.PickWeighted(weights, RosterDraw.Roll01(1234, i));
                Assert.AreNotEqual(2, pick, "a zero weight must never be drawn");
                if (pick == 1) second++;
            }
            Assert.That(second / 10000f, Is.InRange(0.72f, 0.78f));
        }

        [Test]
        public void PickWeighted_AllZero_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, RosterDraw.PickWeighted(new List<float> { 0f, 0f }, 0.5));
        }

        [Test]
        public void StableHash_IsStable_AndDistinguishesIds()
        {
            Assert.AreEqual(RosterDraw.StableHash("warparty:a:p:1"), RosterDraw.StableHash("warparty:a:p:1"));
            Assert.AreNotEqual(RosterDraw.StableHash("warparty:a:p:1"), RosterDraw.StableHash("warparty:a:p:2"));
        }

        [Test]
        public void Draw_SameSeedAndIndex_ReturnsTheSamePrefab()
        {
            GameObject a = Prefab("A"), b = Prefab("B"), c = Prefab("C");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = a, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = b, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = c, weight = 1f });

            for (int i = 0; i < 20; i++)
                Assert.AreSame(roster.Draw(RosterRole.Warrior, 77, i), roster.Draw(RosterRole.Warrior, 77, i));
        }

        [Test]
        public void Draw_OnlyReturnsTheAskedRole()
        {
            GameObject warrior = Prefab("W"), scout = Prefab("S");
            FactionRoster roster = Roster(
                new RosterMember { role = RosterRole.Warrior, prefab = warrior, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = scout, weight = 1f });

            for (int i = 0; i < 50; i++)
                Assert.AreSame(scout, roster.Draw(RosterRole.Scout, 5, i));
        }

        [Test]
        public void Draw_RoleWithNoMembers_LogsAndReturnsNull()
        {
            FactionRoster roster = Roster(new RosterMember { role = RosterRole.Warrior, prefab = Prefab("W") });

            LogAssert.Expect(LogType.Error, new Regex("no Elder members"));
            Assert.IsNull(roster.Draw(RosterRole.Elder, 1, 0));
        }

        [Test]
        public void TierAt_ClampsToTheLastTier()
        {
            FactionRoster roster = Roster();
            roster.warPartyTiers = new[] { new WarPartyTier(), new WarPartyTier() };

            Assert.AreEqual(1, roster.MaxTier);
            Assert.AreSame(roster.warPartyTiers[1], roster.TierAt(5));
            Assert.AreSame(roster.warPartyTiers[0], roster.TierAt(-1));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/typecheck.py --editor`
Expected: errors naming `RosterDraw`, `FactionRoster`, `RosterMember`, `RosterRole`, `WarPartyTier`.

- [ ] **Step 3: Write `RosterDraw.cs`**

```csharp
// Deterministic randomness for rosters: the same group draws the same people and the same guns
// every time it spawns. A caravan that folds and re-spawns while you watch must come back as the
// caravan you saw. Pure, so it is tested without a scene.
using System.Collections.Generic;

namespace SpaceGame.Agents
{
    public static class RosterDraw
    {
        /// <summary>A well-mixed 32-bit hash of (seed, index). Not cryptographic; stable across runs and platforms.</summary>
        public static uint Hash(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ ((uint)index + 0x7F4A7C15u) * 0x85EBCA77u;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>A roll in [0, 1).</summary>
        public static double Roll01(int seed, int index) => Hash(seed, index) / 4294967296.0;

        /// <summary>An index in [0, count), or -1 when there is nothing to pick from.</summary>
        public static int IndexFor(int seed, int index, int count) =>
            count <= 0 ? -1 : (int)(Hash(seed, index) % (uint)count);

        /// <summary>
        /// The slot <paramref name="roll01"/> lands in, weighting each slot by its value. Zero and
        /// negative weights are never picked; -1 when no weight is positive.
        /// </summary>
        public static int PickWeighted(IReadOnlyList<float> weights, double roll01)
        {
            double total = 0d;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0f) total += weights[i];

            if (total <= 0d) return -1;

            double target = roll01 * total;
            double accumulated = 0d;

            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;

                accumulated += weights[i];
                if (target < accumulated) return i;
            }

            // roll01 is below 1, so only floating-point rounding reaches here: the last positive slot.
            for (int i = weights.Count - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;

            return -1;
        }

        /// <summary>
        /// FNV-1a over a string. Used instead of <c>string.GetHashCode</c>, which .NET is free to
        /// randomise per process — and a seed that changes between sessions is no seed at all.
        /// </summary>
        public static int StableHash(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        h ^= c;
                        h *= 16777619u;
                    }
                }
                return (int)h;
            }
        }
    }
}
```

- [ ] **Step 4: Write `FactionRoster.cs`**

```csharp
// A tribe's people: who it fields in each role, what they carry, and what a war party sent after a
// player looks like at each step of a war.
//
// Design: docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md §3.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public enum RosterRole
    {
        Scout,
        Warrior,
        Rider,
        Trader,
        Elder,
    }

    [Serializable]
    public class RosterMember
    {
        public RosterRole role;

        [Tooltip("What to spawn. For a Rider this is the MOUNT prefab carrying an NpcPassenger — the " +
                 "same convention as a caravan template. The mount keeps its own faction; the tribe " +
                 "is applied to the rider it seats.")]
        public GameObject prefab;

        [Min(0f)]
        public float weight = 1f;
    }

    [Serializable]
    public class RoleCount
    {
        public RosterRole role;

        [Min(1)]
        public int count = 1;
    }

    [Serializable]
    public class WarPartyTier
    {
        public RoleCount[] roles = Array.Empty<RoleCount>();
    }

    [CreateAssetMenu(menuName = "Factions/Roster")]
    public class FactionRoster : ScriptableObject
    {
        [Tooltip("The tribe this roster fields. Its FactionDefinition.roster must point back here.")]
        public FactionDefinition faction;

        public RosterMember[] members = Array.Empty<RosterMember>();

        [Tooltip("Weapons a member may draw at spawn. Baked into each nomad's NpcRandomLoadout by " +
                 "NomadPrefabBuilder; a test fails if the two differ.")]
        public InventoryItem[] handItems = Array.Empty<InventoryItem>();

        [Tooltip("Shouted by a war party on first sight of the player it is hunting.")]
        public DialogPool hostileLines;

        [Tooltip("War-party composition per escalation tier. Each party beaten raises the next one tier.")]
        public WarPartyTier[] warPartyTiers = Array.Empty<WarPartyTier>();

        private static readonly List<GameObject> CandidateBuffer = new();
        private static readonly List<float> WeightBuffer = new();

        public int MaxTier => Mathf.Max(0, warPartyTiers.Length - 1);

        public WarPartyTier TierAt(int tier) =>
            warPartyTiers.Length == 0 ? null : warPartyTiers[Mathf.Clamp(tier, 0, warPartyTiers.Length - 1)];

        /// <summary>
        /// A prefab for <paramref name="role"/>, the same one every time for the same seed and index.
        /// Never substitutes another role: an empty role is an authoring error, logged, and null.
        /// </summary>
        public GameObject Draw(RosterRole role, int seed, int index)
        {
            CandidateBuffer.Clear();
            WeightBuffer.Clear();

            foreach (RosterMember member in members)
            {
                if (member == null || member.prefab == null || member.role != role) continue;

                CandidateBuffer.Add(member.prefab);
                WeightBuffer.Add(member.weight);
            }

            int pick = RosterDraw.PickWeighted(WeightBuffer, RosterDraw.Roll01(seed, index));
            if (pick >= 0) return CandidateBuffer[pick];

            Debug.LogError($"[FactionRoster] {name} has no {role} members with a positive weight; " +
                           "nothing was drawn.", this);
            return null;
        }

        /// <summary>Warns and never blocks: a half-filled slot is unfinished, not broken (CONTENT-0004).</summary>
        private void OnValidate()
        {
            foreach (string problem in RosterValidation.Problems(this))
                Debug.LogWarning($"[FactionRoster] {name}: {problem}", this);
        }
    }
}
```

- [ ] **Step 5: Write `RosterValidation.cs`**

```csharp
// Everything that makes a roster broken, in one list. OnValidate prints it as warnings while a
// designer works; RosterAssetTests fails on it for the shipped roster (spec §3, CONTENT-0004).
using System.Collections.Generic;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public static class RosterValidation
    {
        public static List<string> Problems(FactionRoster roster)
        {
            var problems = new List<string>();
            if (roster == null)
            {
                problems.Add("there is no roster.");
                return problems;
            }

            if (roster.faction == null)
                problems.Add("faction is not set.");
            else if (roster.faction.roster != roster)
                problems.Add($"{roster.faction.factionName}.roster does not point back at this roster.");

            for (int i = 0; i < roster.members.Length; i++)
                CheckMember(roster, i, problems);

            for (int i = 0; i < roster.handItems.Length; i++)
            {
                InventoryItem item = roster.handItems[i];
                if (item == null)
                    problems.Add($"handItems[{i}] is empty.");
                else if (item.equipKind != EquipKind.Hand)
                    problems.Add($"handItems[{i}] '{item.name}' is a {item.equipKind}, not a hand item.");
            }

            if (roster.warPartyTiers.Length == 0)
                problems.Add("warPartyTiers is empty; this tribe cannot raise a war party.");

            for (int t = 0; t < roster.warPartyTiers.Length; t++)
            {
                WarPartyTier tier = roster.warPartyTiers[t];
                if (tier == null || tier.roles == null) continue;

                foreach (RoleCount wanted in tier.roles)
                {
                    if (wanted != null && !HasRole(roster, wanted.role))
                        problems.Add($"warPartyTiers[{t}] asks for {wanted.role}, which has no members.");
                }
            }

            return problems;
        }

        private static void CheckMember(FactionRoster roster, int i, List<string> problems)
        {
            RosterMember member = roster.members[i];
            if (member == null || member.prefab == null)
            {
                problems.Add($"members[{i}] has no prefab.");
                return;
            }

            string label = $"members[{i}] '{member.prefab.name}'";

            if (member.prefab.GetComponent<AgentController>() == null)
                problems.Add($"{label} has no AgentController.");

            EntityFaction entityFaction = member.prefab.GetComponent<EntityFaction>();
            if (entityFaction == null)
                problems.Add($"{label} has no EntityFaction.");

            if (member.role == RosterRole.Rider)
            {
                if (member.prefab.GetComponent<NpcPassenger>() == null)
                    problems.Add($"{label} is a Rider but carries no NpcPassenger to seat one.");
                return;
            }

            // Faction is not replicated: a client only ever sees the prefab's own. So it must already
            // be the tribe, or the two machines disagree about who this person is.
            if (entityFaction != null && roster.faction != null && entityFaction.Faction != roster.faction)
                problems.Add($"{label} serializes faction '{entityFaction.Faction?.factionName}', not " +
                             $"'{roster.faction.factionName}'.");
        }

        private static bool HasRole(FactionRoster roster, RosterRole role)
        {
            foreach (RosterMember member in roster.members)
                if (member != null && member.prefab != null && member.role == role && member.weight > 0f)
                    return true;

            return false;
        }
    }
}
```

`EquipKind` lives in `SpaceGame.Items` (`Assets/Game/Scripts/Items/Core/EquipKind.cs`); `NpcPassenger` and `AgentController` in `SpaceGame.Agents`.

- [ ] **Step 6: Add the back-reference to `FactionDefinition.cs`**, after `hudColor`:

```csharp
        [Tooltip("This faction's people, if it is a tribe. Empty for Humans, Outlaws, Clankers and " +
                 "animals — only a tribe fields caravans and war parties from a roster.")]
        public FactionRoster roster;
```

- [ ] **Step 7: Type-check, then run `RosterDrawTests`**

Run: `python tools/typecheck.py --editor` → `No errors.`; then `HeadlessTestRunner.RunEditModeDeferred("RosterDrawTests")`.
Expected: 10 passed, `FAILED=0`.

- [ ] **Step 8: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/faction/RosterDraw.cs* Assets/Game/Scripts/agents/faction/FactionRoster.cs* Assets/Game/Scripts/agents/faction/RosterValidation.cs* Assets/Game/Scripts/agents/faction/FactionDefinition.cs Assets/Game/Editor/Tests/RosterDrawTests.cs*
git commit -m "feat(factions): FactionRoster asset with deterministic draws"
```

---
### Task 2: The Sand Tribe roster, and the builder baking weapons from it

**Files:**
- Create: `Assets/Game/Editor/Agents/RosterAuthoring.cs`
- Create (by running the menu): `Assets/Game/ScriptableObjects/Factions/Rosters/SandTribe.asset`, `SandTribeHostileLines.asset`
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` (delete `WeaponArtifactPaths` at ~94–106; `ConfigureRandomWeapon` at ~1360; the doc comment at ~51)
- Modify (by rebuilding): the five `Assets/Game/Prefabs/Agents/Characters/Nomad*.prefab`
- Test: `Assets/Game/Editor/Tests/RosterAssetTests.cs`

**Interfaces:**
- Consumes: Task 1's `FactionRoster`, `RosterValidation.Problems`, `FactionDefinition.roster`.
- Produces: `RosterAuthoring.SandRosterPath` (`const string`), `RosterAuthoring.AuthorSandRoster()` (menu `Tools/SpaceGame/Agents/Author Sand Tribe Roster`). Tasks 4 and 8 add scene-wiring methods to this same class.

- [ ] **Step 1: Write the failing tests**

```csharp
// The shipped Sand Tribe roster: valid, wired both ways, and the only source of the nomads' guns.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class RosterAssetTests
    {
        private const string SandFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SandTribeFaction.asset";

        private static readonly string[] NomadPrefabs =
        {
            "Assets/Game/Prefabs/Agents/Characters/Nomad.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_Maroon.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_StrawHat.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_Tan.prefab",
            "Assets/Game/Prefabs/Agents/Characters/Nomad_Umber.prefab",
        };

        private static FactionRoster Sand => AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SandRosterPath);

        [Test]
        public void SandRoster_Exists_AndValidates()
        {
            Assert.IsNotNull(Sand, $"No roster at {RosterAuthoring.SandRosterPath}. Run Tools/SpaceGame/Agents/Author Sand Tribe Roster.");
            var problems = RosterValidation.Problems(Sand);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void SandFaction_PointsAtItsRoster()
        {
            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(SandFactionPath);
            Assert.AreSame(Sand, faction.roster);
            Assert.AreSame(faction, Sand.faction);
        }

        [Test]
        public void SandRoster_HasTheSpecTiers()
        {
            Assert.AreEqual(3, Sand.warPartyTiers.Length);
            Assert.AreEqual(2, Sand.MaxTier);

            AssertTier(0, (RosterRole.Scout, 2));
            AssertTier(1, (RosterRole.Warrior, 3), (RosterRole.Scout, 1));
            AssertTier(2, (RosterRole.Warrior, 3), (RosterRole.Rider, 2));
        }

        private static void AssertTier(int index, params (RosterRole role, int count)[] expected)
        {
            RoleCount[] roles = Sand.warPartyTiers[index].roles;
            CollectionAssert.AreEqual(expected, roles.Select(r => (r.role, r.count)).ToArray(), $"tier {index}");
        }

        [Test]
        public void SandRoster_HandItems_AreTheBakedCandidatesOnEveryNomad()
        {
            foreach (string path in NomadPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);

                var loadout = prefab.GetComponent<NpcRandomLoadout>();
                Assert.IsNotNull(loadout, $"{path} has no NpcRandomLoadout");

                SerializedProperty candidates = new SerializedObject(loadout).FindProperty("candidates");
                var baked = Enumerable.Range(0, candidates.arraySize)
                    .Select(i => candidates.GetArrayElementAtIndex(i).objectReferenceValue as InventoryItem)
                    .ToArray();

                CollectionAssert.AreEqual(Sand.handItems, baked,
                    $"{path}: re-run Tools/SpaceGame/Agents/Build Sand Nomad NPCs after changing the roster.");
            }
        }

        [Test]
        public void SandRoster_HostileLines_AreAuthored()
        {
            Assert.IsNotNull(Sand.hostileLines);
            Assert.IsNotEmpty(Sand.hostileLines.lines);
        }
    }
}
```

Before relying on `NomadPrefabs`, confirm each prefab currently carries `NpcRandomLoadout` (grep the prefabs for the GUID in `NpcRandomLoadout.cs.meta`). If a recipe has `RandomWeapon = false`, drop that prefab from the array and say why in a comment.

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/typecheck.py --editor`
Expected: error that `RosterAuthoring` does not exist.

- [ ] **Step 3: Write `RosterAuthoring.cs`**

```csharp
// Authors the Sand Tribe roster from what the sand nomads already are, and (Tasks 4 and 8) wires the
// world sim to it. Idempotent: re-run it any time; it overwrites what it owns and nothing else.
//
// Run from: Tools > SpaceGame > Agents > Author Sand Tribe Roster
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public static class RosterAuthoring
    {
        private const string RosterDir = "Assets/Game/ScriptableObjects/Factions/Rosters";
        public const string SandRosterPath = RosterDir + "/SandTribe.asset";
        private const string SandHostileLinesPath = RosterDir + "/SandTribeHostileLines.asset";
        public const string SandFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SandTribeFaction.asset";

        private const string CharacterDir = "Assets/Game/Prefabs/Agents/Characters";
        private const string NomadOstrichPath = "Assets/Game/Prefabs/Agents/Caravan/NomadOstrich.prefab";

        private static readonly string[] SandPeople =
        {
            CharacterDir + "/Nomad.prefab",
            CharacterDir + "/Nomad_Maroon.prefab",
            CharacterDir + "/Nomad_StrawHat.prefab",
            CharacterDir + "/Nomad_Tan.prefab",
            CharacterDir + "/Nomad_Umber.prefab",
        };

        // Moved here from NomadPrefabBuilder.WeaponArtifactPaths, which is deleted: the roster owns the
        // list now. Ranged artifacts only: NpcItemUseModule fires the held item at a target between
        // minRange and maxRange, so a gauntlet or a scanner in the hand would be "used" at nothing.
        private static readonly string[] SandHandItemPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/basicgun.asset",
            "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset",
            "Assets/Game/Resources/Items/Artifacts/NetGun.asset",
            "Assets/Game/Resources/Items/Artifacts/LaserStaff.asset",
            "Assets/Game/Resources/Items/Artifacts/BallLightningWeapon.asset",
            "Assets/Game/Resources/Items/Artifacts/LightningSpell.asset",
            "Assets/Game/Resources/Items/Artifacts/DragonBazooka.asset",
        };

        private static readonly string[] SandHostileLines =
        {
            "There you are.",
            "The tribe remembers you.",
            "You should not have come back.",
            "Sand take you!",
        };

        [MenuItem("Tools/SpaceGame/Agents/Author Sand Tribe Roster")]
        public static void AuthorSandRoster()
        {
            var faction = Load<FactionDefinition>(SandFactionPath);
            if (faction == null) return;

            Directory.CreateDirectory(RosterDir);

            DialogPool hostile = LoadOrCreate<DialogPool>(SandHostileLinesPath);
            hostile.lines = SandHostileLines.ToArray();
            EditorUtility.SetDirty(hostile);

            FactionRoster roster = LoadOrCreate<FactionRoster>(SandRosterPath);
            roster.faction = faction;
            roster.hostileLines = hostile;
            roster.handItems = SandHandItemPaths.Select(Load<InventoryItem>).Where(i => i != null).ToArray();

            GameObject[] people = SandPeople.Select(Load<GameObject>).Where(p => p != null).ToArray();
            GameObject ostrich = Load<GameObject>(NomadOstrichPath);

            // Every nomad can scout or fight; nothing tells one from the other yet. Role-specific
            // people arrive with sub-project 3's art.
            roster.members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .Concat(ostrich != null ? new[] { Member(RosterRole.Rider, ostrich) } : Array.Empty<RosterMember>())
                .ToArray();

            roster.warPartyTiers = new[]
            {
                Tier((RosterRole.Scout, 2)),
                Tier((RosterRole.Warrior, 3), (RosterRole.Scout, 1)),
                Tier((RosterRole.Warrior, 3), (RosterRole.Rider, 2)),
            };
            EditorUtility.SetDirty(roster);

            faction.roster = roster;
            EditorUtility.SetDirty(faction);

            AssetDatabase.SaveAssets();

            foreach (string problem in RosterValidation.Problems(roster))
                Debug.LogError($"[RosterAuthoring] Sand roster: {problem}", roster);

            Debug.Log($"[RosterAuthoring] Wrote {SandRosterPath}: {roster.members.Length} members, " +
                      $"{roster.handItems.Length} hand items, {roster.warPartyTiers.Length} tiers.", roster);
        }

        private static RosterMember Member(RosterRole role, GameObject prefab) =>
            new RosterMember { role = role, prefab = prefab, weight = 1f };

        private static WarPartyTier Tier(params (RosterRole role, int count)[] roles) => new WarPartyTier
        {
            roles = roles.Select(r => new RoleCount { role = r.role, count = r.count }).ToArray(),
        };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError($"[RosterAuthoring] Missing {typeof(T).Name} at {path}.");
            return asset;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
```

- [ ] **Step 4: Make `NomadPrefabBuilder` bake from the roster**

Delete the `WeaponArtifactPaths` array and the comment above it (~94–106). In the `NomadRecipe.RandomWeapon` doc comment (~51), replace `<see cref="WeaponArtifactPaths"/>` with `the Sand Tribe roster's <c>handItems</c>`. In `ConfigureRandomWeapon`, replace the whole `candidates` block with:

```csharp
                var candidates = so.FindProperty("candidates");
                if (candidates != null)
                {
                    // The roster owns the weapon list (rosters spec §4.5). Baked rather than read at
                    // runtime so a hand-placed nomad works standalone; RosterAssetTests fails if the
                    // bake and the roster ever differ.
                    var roster = AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.SandRosterPath);
                    if (roster == null)
                    {
                        Debug.LogError($"[NomadPrefabBuilder] No roster at {RosterAuthoring.SandRosterPath}; " +
                                       "run Tools/SpaceGame/Agents/Author Sand Tribe Roster first. The " +
                                       "nomads are built unarmed.");
                        candidates.arraySize = 0;
                    }
                    else
                    {
                        InventoryItem[] items = roster.handItems.Where(item => item != null).ToArray();
                        candidates.arraySize = items.Length;
                        for (int i = 0; i < items.Length; i++)
                            candidates.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
                    }
                }
```

Add `using SpaceGame.Agents;` if missing. Then grep `Assets/Game/Editor` for `WeaponArtifactPaths` → no matches.

- [ ] **Step 5: Type-check** → `No errors.`

- [ ] **Step 6: Run the tools in the Editor**

1. `Tools ▸ SpaceGame ▸ Agents ▸ Author Sand Tribe Roster` → console: `Wrote …SandTribe.asset: 11 members, 7 hand items, 3 tiers.`, no errors.
2. `Tools ▸ SpaceGame ▸ Agents ▸ Build Sand Nomad NPCs`, then `Build Nomad NPC (prefab only)`. Dismiss "Scene(s) Have Been Modified" only when the scene is Untitled.
3. `git diff --stat -- Assets/Game/Prefabs/Agents/Characters`: the candidates must hold the same seven items as before. Read any other churn and confirm it is only the rebuild.

- [ ] **Step 7: Run `RosterAssetTests`** → 5 passed. Re-run `FactionAssetTests` and `MenacingItemTests` → still green.

- [ ] **Step 8: Commit** (only if authorised)

```bash
git add Assets/Game/Editor/Agents/RosterAuthoring.cs* Assets/Game/Editor/Agents/NomadPrefabBuilder.cs Assets/Game/ScriptableObjects/Factions/Rosters* Assets/Game/ScriptableObjects/Factions/Core/SandTribeFaction.asset Assets/Game/Prefabs/Agents/Characters/Nomad*.prefab Assets/Game/Editor/Tests/RosterAssetTests.cs*
git commit -m "feat(factions): Sand Tribe roster owns the nomads' weapon list"
```

---

### Task 3: Group records, membership and seeded loadouts

**Files:**
- Modify: `Assets/Game/Scripts/agents/World/NpcGroup.cs`
- Create: `Assets/Game/Scripts/agents/World/GroupMembership.cs`
- Modify: `Assets/Game/Scripts/agents/Core/NpcSpawn.cs`, `Assets/Game/Scripts/agents/entity/NpcRandomLoadout.cs`, `Assets/Game/Scripts/agents/Modules/Riding/NpcPassenger.cs`
- Test: `Assets/Game/Editor/Tests/GroupRecordTests.cs`

**Interfaces:**
- Consumes: `RosterDraw.IndexFor`.
- Produces:
  - `NpcGroup`: `int RosterSeed`, `string QuarryProfileId` (never null), `int Tier`, `bool IsWarParty`; runtime-only `int FightersSpawned`, `int FightersDead`, `bool WipedOut`, `bool DisbandWhenFolded`, `bool QuarrySeenThisSpawn`.
  - `NpcGroup.Record`: appended `int rosterSeed`, `string quarryProfileId`, `int tier`.
  - `GroupMembership` (MonoBehaviour): `NpcGroup Group`, `int MemberIndex`, `FactionDefinition Tribe`, `bool IsFighter`; `static GroupMembership Stamp(GameObject member, NpcGroup group, int memberIndex, FactionDefinition tribe)`, `static void StampRider(GameObject mount, GameObject rider)`, `static string GroupIdOf(Transform source)`, `static GameObject FighterOf(GameObject member)`, `const int RiderIndexOffset = 1000`.
  - `NpcSpawn.Create(GameObject prefab, Vector3 position, Quaternion rotation, Object context = null, Action<GameObject> beforeSpawn = null)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// A group's record carries the roster seed, the quarry and the tier through a save, and a member
// stamped before its spawn is who its loadout roll and the ledger think it is.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class GroupRecordTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        [Test]
        public void Record_RoundTripsTheWarPartyFields_ThroughTheSaveSerializer()
        {
            var group = new NpcGroup
            {
                Id = "warparty:sand:p1:1",
                TemplateId = "sand-war-party",
                RosterSeed = 918273,
                QuarryProfileId = "p1",
                Tier = 2,
            };

            JObject json = JObject.FromObject(group.ToRecord(), SaveSerializer.Serializer);
            NpcGroup.Record back = json.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var restored = new NpcGroup { Id = back.id, TemplateId = back.templateId };
            restored.ApplyRecord(in back);

            Assert.AreEqual(918273, restored.RosterSeed);
            Assert.AreEqual("p1", restored.QuarryProfileId);
            Assert.AreEqual(2, restored.Tier);
            Assert.IsTrue(restored.IsWarParty);
        }

        [Test]
        public void Record_FromAnOlderSave_ReadsDefaults()
        {
            var old = JObject.Parse("{\"id\":\"nomad-caravan\",\"templateId\":\"nomad-caravan\",\"taskIndex\":1}");
            NpcGroup.Record record = old.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var group = new NpcGroup { Id = record.id, TemplateId = record.templateId };
            group.ApplyRecord(in record);

            Assert.AreEqual(0, group.RosterSeed);
            Assert.AreEqual(string.Empty, group.QuarryProfileId);
            Assert.AreEqual(0, group.Tier);
            Assert.IsFalse(group.IsWarParty);
        }

        [Test]
        public void Stamp_OnFootMember_IsAFighter_CountsAndTakesTheTribe()
        {
            var tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(tribe);

            var member = new GameObject("Nomad");
            junk.Add(member);
            member.AddComponent<EntityFaction>();

            var group = new NpcGroup { Id = "g" };
            GroupMembership membership = GroupMembership.Stamp(member, group, 3, tribe);

            Assert.IsTrue(membership.IsFighter);
            Assert.AreEqual(3, membership.MemberIndex);
            Assert.AreEqual(1, group.FightersSpawned);
            Assert.AreSame(tribe, member.GetComponent<EntityFaction>().Faction);
            Assert.AreEqual("g", GroupMembership.GroupIdOf(member.transform));
        }

        [Test]
        public void GroupIdOf_ClimbsFromAChildTransform_AndIsNullForStrangers()
        {
            var member = new GameObject("Nomad");
            junk.Add(member);
            var hand = new GameObject("Hand");
            hand.transform.SetParent(member.transform);
            member.AddComponent<EntityFaction>();

            GroupMembership.Stamp(member, new NpcGroup { Id = "g" }, 0, null);

            Assert.AreEqual("g", GroupMembership.GroupIdOf(hand.transform));
            Assert.IsNull(GroupMembership.GroupIdOf(null));

            var stranger = new GameObject("Stranger");
            junk.Add(stranger);
            Assert.IsNull(GroupMembership.GroupIdOf(stranger.transform));
        }

        [Test]
        public void NpcSpawn_RunsBeforeSpawn_OnTheInstance_BeforeReturning()
        {
            var prefab = new GameObject("Template");
            junk.Add(prefab);

            GameObject seen = null;
            GameObject instance = NpcSpawn.Create(prefab, Vector3.zero, Quaternion.identity, null,
                                                  go => seen = go);
            junk.Add(instance);

            Assert.AreSame(instance, seen);
        }

        [Test]
        public void SeededPick_IsTheSameForTheSameGroupAndMember()
        {
            const int seed = 55, member = 2, candidates = 7;
            Assert.AreEqual(RosterDraw.IndexFor(seed, member, candidates),
                            RosterDraw.IndexFor(seed, member, candidates));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/typecheck.py --editor`
Expected: errors on `RosterSeed`, `QuarryProfileId`, `GroupMembership`, and the five-argument `NpcSpawn.Create`.

- [ ] **Step 3: Extend `NpcGroup`**

In `class NpcGroup`, after `public float LeadAge;`:

```csharp
        /// <summary>Which prefabs and weapons this group draws. Same seed, same people, after every refold.</summary>
        public int RosterSeed;

        /// <summary>The profile this war party is hunting. Empty for every group that is not one.</summary>
        public string QuarryProfileId = string.Empty;

        /// <summary>War-party escalation tier: which row of the roster's warPartyTiers it spawns.</summary>
        public int Tier;

        public bool IsWarParty => !string.IsNullOrEmpty(QuarryProfileId);

        // Runtime only, never saved. Counted by GroupMembership while the group is spawned and reset on
        // every spawn, which is why a folded party cannot be "defeated": nobody can reach it.
        [NonSerialized] public int FightersSpawned;
        [NonSerialized] public int FightersDead;

        /// <summary>A war party whose members all died. It never re-spawns; the director resolves it.</summary>
        [NonSerialized] public bool WipedOut;

        /// <summary>Released while somebody could see it: removed the moment it folds, never popped out of view.</summary>
        [NonSerialized] public bool DisbandWhenFolded;

        /// <summary>First sight of the quarry since this spawn has been announced.</summary>
        [NonSerialized] public bool QuarrySeenThisSpawn;
```

Append to `struct Record`, after `leadAge`:

```csharp
            // Appended 2026-09-16 (rosters spec §4.2). Older saves read 0, null, 0: a valid seed, not a
            // war party, tier 0.
            public int rosterSeed;
            public string quarryProfileId;
            public int tier;
```

In `ToRecord()` add `rosterSeed = RosterSeed, quarryProfileId = QuarryProfileId, tier = Tier,`; in `ApplyRecord` add:

```csharp
            RosterSeed = record.rosterSeed;
            QuarryProfileId = record.quarryProfileId ?? string.Empty;
            Tier = Mathf.Max(0, record.tier);
```

- [ ] **Step 4: Write `GroupMembership.cs`**

```csharp
// Which world-sim group an NPC was spawned for.
//
// Stamped by NpcWorldSim BEFORE the member's network spawn (NpcSpawn.Create's beforeSpawn), so its
// NpcRandomLoadout roll in OnNetworkSpawn is already seeded by the group. Server-side bookkeeping
// only: clients never have one, and nothing on a client asks.
//
// Three readers: the loadout roll (seed), the war-party director (fighters spawned and dead,
// who dealt a killing blow) and the goodwill ledger (the self-defence exemption).
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class GroupMembership : MonoBehaviour
    {
        /// <summary>Added to the mount's index for the rider it seats, so the two never share a roll.</summary>
        public const int RiderIndexOffset = 1000;

        public NpcGroup Group { get; private set; }
        public int MemberIndex { get; private set; }
        public FactionDefinition Tribe { get; private set; }

        /// <summary>False for a mount: a mount carries, the rider fights (AgentSystem.md).</summary>
        public bool IsFighter { get; private set; }

        private HealthComponent health;

        public static GroupMembership Stamp(GameObject member, NpcGroup group, int memberIndex,
                                            FactionDefinition tribe)
        {
            if (member == null || group == null) return null;

            if (!member.TryGetComponent(out GroupMembership membership))
                membership = member.AddComponent<GroupMembership>();

            membership.Group = group;
            membership.MemberIndex = memberIndex;
            membership.Tribe = tribe;
            membership.IsFighter = !member.TryGetComponent(out NpcPassenger _);

            if (membership.IsFighter) membership.Enlist();
            return membership;
        }

        /// <summary>Called by <see cref="NpcPassenger"/> for the rider it seats, before that rider spawns.</summary>
        public static void StampRider(GameObject mount, GameObject rider)
        {
            if (mount == null || !mount.TryGetComponent(out GroupMembership membership) || membership.Group == null)
                return;

            Stamp(rider, membership.Group, membership.MemberIndex + RiderIndexOffset, membership.Tribe);
        }

        /// <summary>The group id of whoever <paramref name="source"/> belongs to, or null.</summary>
        public static string GroupIdOf(Transform source)
        {
            if (source == null) return null;

            GroupMembership membership = source.GetComponentInParent<GroupMembership>();
            return membership != null && membership.Group != null ? membership.Group.Id : null;
        }

        /// <summary>The one who fights for this member: the seated rider on a mount, else the member.</summary>
        public static GameObject FighterOf(GameObject member) =>
            member != null && member.TryGetComponent(out NpcPassenger passenger) && passenger.HasRider
                ? passenger.Rider
                : member;

        private void Enlist()
        {
            // The mount keeps its own faction; only a fighter takes the tribe. Null table keeps the
            // prefab's relationships (EntityFaction.SetFaction).
            if (Tribe != null) EntityFaction.Ensure(gameObject, Tribe, null);

            Group.FightersSpawned++;

            if (health != null) health.OnDeath -= OnDied;
            health = GetComponent<HealthComponent>();
            if (health != null) health.OnDeath += OnDied;
        }

        private void OnDied()
        {
            // A restore replaying a death is not a death in this fight.
            if (health != null && health.IsRestoring) return;
            if (Group != null) Group.FightersDead++;
        }

        private void OnDestroy()
        {
            if (health != null) health.OnDeath -= OnDied;
        }
    }
}
```

- [ ] **Step 5: Add the callback to `NpcSpawn.Create`**

Replace the signature and the lines up to `DisownFromWorldSave(instance);` with:

```csharp
        /// <param name="context">Logged as the object to select when a spawn fails.</param>
        /// <param name="beforeSpawn">
        /// Runs on the new instance after Instantiate and BEFORE the network spawn — the only moment
        /// where anything read in <c>OnNetworkSpawn</c> (a seeded loadout roll) can still be set.
        /// </param>
        public static GameObject Create(GameObject prefab, Vector3 position, Quaternion rotation,
                                        UnityEngine.Object context = null,
                                        Action<GameObject> beforeSpawn = null)
        {
            if (prefab == null) return null;

            GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            DisownFromWorldSave(instance);
            beforeSpawn?.Invoke(instance);
```

The rest of the method is unchanged.

- [ ] **Step 6: Seed the roll in `NpcRandomLoadout`**

In `Roll()`, replace `InventoryItem pick = candidates[Random.Range(0, candidates.Length)];` with:

```csharp
            // A group member draws from its group's seed, so a caravan that folds and re-spawns comes
            // back carrying the same guns. A hand-placed NPC has no group and still rolls freely.
            int index = TryGetComponent(out GroupMembership membership) && membership.Group != null
                ? RosterDraw.IndexFor(membership.Group.RosterSeed, membership.MemberIndex, candidates.Length)
                : Random.Range(0, candidates.Length);

            InventoryItem pick = candidates[index];
```

In the header comment, replace the sentence *"Caravan members are not saved at all (NpcSpawn.Create disowns them), so they roll afresh every time they walk into range -- which is the "random per spawn" that was asked for."* with *"Caravan members are not saved at all (NpcSpawn.Create disowns them), so a group member's roll is seeded by its group (GroupMembership) instead: the same caravan comes back with the same guns after every refold. A hand-placed NPC still rolls at random."*

- [ ] **Step 7: Stamp the rider in `NpcPassenger.SpawnRider`**

```csharp
            GameObject rider = NpcSpawn.Create(riderPrefab, position, rotation, this,
                                               spawned => GroupMembership.StampRider(gameObject, spawned));
```

- [ ] **Step 8: Type-check, then run `GroupRecordTests`** → 6 passed. Re-run `AgentCarryTests` and `NomadAlertWiringTests` (riders and nomads) and compare against their pre-existing results.

- [ ] **Step 9: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/World/NpcGroup.cs Assets/Game/Scripts/agents/World/GroupMembership.cs* Assets/Game/Scripts/agents/Core/NpcSpawn.cs Assets/Game/Scripts/agents/entity/NpcRandomLoadout.cs Assets/Game/Scripts/agents/Modules/Riding/NpcPassenger.cs Assets/Game/Editor/Tests/GroupRecordTests.cs*
git commit -m "feat(world-sim): group membership stamped before spawn seeds loadouts"
```

---

### Task 4: Templates learn their tribe; runtime groups in the world sim

**Files:**
- Modify: `Assets/Game/Scripts/agents/World/NpcGroup.cs` (template + member spec)
- Create: `Assets/Game/Scripts/agents/World/NpcGroupComposition.cs`
- Modify: `Assets/Game/Scripts/agents/World/NpcWorldSim.cs`
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (+ scene wiring menu)
- Modify (by running the menu): `Assets/Game/Scenes/world/persistentScene.unity`
- Test: `Assets/Game/Editor/Tests/NpcGroupCompositionTests.cs`, `Assets/Game/Editor/Tests/RuntimeGroupTests.cs`

**Interfaces:**
- Consumes: Task 1 `FactionRoster.Draw/TierAt`, `RosterDraw.StableHash`; Task 3 `NpcGroup` fields, `GroupMembership.Stamp/FighterOf`, `NpcSpawn.Create(..., beforeSpawn)`.
- Produces:
  - `NpcGroupTemplate.tribe : FactionDefinition`, `NpcGroupTemplate.runtimeOnly : bool`; `NpcGroupMemberSpec.role : RosterRole`.
  - `readonly struct PlannedMember { GameObject Prefab; bool Leads; }`; `NpcGroupComposition.Resolve(NpcGroup, NpcGroupTemplate) : List<PlannedMember>`.
  - `NpcWorldSim`: `NpcGroup CreateGroup(NpcGroupTemplate template, string groupId, Vector3 start)`, `void DisbandGroup(string groupId)`, `void ReleaseGroup(string groupId)`, `NpcGroup FindGroup(string groupId)`, `NpcGroupTemplate WarPartyTemplateFor(FactionDefinition tribe)`, `FactionDefinition TribeOf(NpcGroup group)`, `float SpawnRadius`, `void CollectPlayerPositions(List<Vector3> into)`, `event Action<NpcGroup, GameObject> QuarrySighted` (group, the fighter who saw).
  - `RosterAuthoring.WireWorldSim()` — menu `Tools/SpaceGame/Agents/Wire War Party Templates`; `RosterAuthoring.SandWarPartyTemplateId = "sand-war-party"`.

- [ ] **Step 1: Write the failing tests**

`NpcGroupCompositionTests.cs`:

```csharp
// Which prefabs a group spawns: explicit prefabs as authored, roles from the tribe's roster, and a
// war party's people from its tier.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class NpcGroupCompositionTests
    {
        private readonly List<Object> junk = new();
        private GameObject scoutA, scoutB, warrior, explicitPrefab;
        private FactionDefinition tribe;

        [SetUp]
        public void SetUp()
        {
            scoutA = Make("ScoutA");
            scoutB = Make("ScoutB");
            warrior = Make("Warrior");
            explicitPrefab = Make("Explicit");

            tribe = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(tribe);

            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            junk.Add(roster);
            roster.faction = tribe;
            roster.members = new[]
            {
                new RosterMember { role = RosterRole.Scout, prefab = scoutA, weight = 1f },
                new RosterMember { role = RosterRole.Scout, prefab = scoutB, weight = 1f },
                new RosterMember { role = RosterRole.Warrior, prefab = warrior, weight = 1f },
            };
            roster.warPartyTiers = new[]
            {
                new WarPartyTier { roles = new[] { new RoleCount { role = RosterRole.Scout, count = 2 } } },
                new WarPartyTier { roles = new[]
                {
                    new RoleCount { role = RosterRole.Warrior, count = 3 },
                    new RoleCount { role = RosterRole.Scout, count = 1 },
                } },
            };
            tribe.roster = roster;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            junk.Add(go);
            return go;
        }

        [Test]
        public void WarParty_TakesItsTiersComposition_AndTheFirstLeads()
        {
            var template = new NpcGroupTemplate { tribe = tribe };
            var group = new NpcGroup { QuarryProfileId = "p", Tier = 1, RosterSeed = 4 };

            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);

            Assert.AreEqual(4, plan.Count);
            Assert.IsTrue(plan[0].Leads);
            Assert.IsTrue(plan.Skip(1).All(p => !p.Leads));
            Assert.AreEqual(3, plan.Take(3).Count(p => p.Prefab == warrior));
            Assert.That(plan[3].Prefab, Is.SameAs(scoutA).Or.SameAs(scoutB));
        }

        [Test]
        public void WarParty_SameSeed_SamePeople()
        {
            var template = new NpcGroupTemplate { tribe = tribe };
            var a = NpcGroupComposition.Resolve(new NpcGroup { QuarryProfileId = "p", RosterSeed = 99 }, template);
            var b = NpcGroupComposition.Resolve(new NpcGroup { QuarryProfileId = "p", RosterSeed = 99 }, template);

            CollectionAssert.AreEqual(a.Select(p => p.Prefab), b.Select(p => p.Prefab));
        }

        [Test]
        public void Caravan_ExplicitPrefabWins_RoleOnlyIsDrawn()
        {
            var template = new NpcGroupTemplate
            {
                tribe = tribe,
                members = new[]
                {
                    new NpcGroupMemberSpec { prefab = explicitPrefab, isLeader = true, count = 1 },
                    new NpcGroupMemberSpec { role = RosterRole.Warrior, count = 2 },
                },
            };

            List<PlannedMember> plan = NpcGroupComposition.Resolve(new NpcGroup(), template);

            Assert.AreEqual(3, plan.Count);
            Assert.AreSame(explicitPrefab, plan[0].Prefab);
            Assert.IsTrue(plan[0].Leads);
            Assert.AreSame(warrior, plan[1].Prefab);
            Assert.AreSame(warrior, plan[2].Prefab);
        }

        [Test]
        public void WarParty_WithoutARoster_LogsAndPlansNothing()
        {
            var template = new NpcGroupTemplate { tribe = null };
            LogAssert.Expect(LogType.Error, new Regex("has no tier"));

            Assert.IsEmpty(NpcGroupComposition.Resolve(new NpcGroup { Id = "w", QuarryProfileId = "p" }, template));
        }
    }
}
```

`RuntimeGroupTests.cs`:

```csharp
// Runtime groups: created, found, released and disbanded; never seeded at startup; restored from a
// save without duplicates; and a war party's lead never goes cold.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class RuntimeGroupTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<Object> junk = new();
        private NpcWorldSim sim;
        private NpcGroupTemplate caravan, warParty;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            junk.Add(sand);

            caravan = new NpcGroupTemplate { id = "caravan", tribe = sand, useStartPosition = true };
            warParty = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };

            var go = new GameObject("Sim");
            junk.Add(go);
            sim = go.AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { caravan, warParty });
            Call("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private object Call(string method, params object[] args) =>
            typeof(NpcWorldSim).GetMethod(method, Private).Invoke(sim, args);

        [Test]
        public void Seeding_SkipsRuntimeOnlyTemplates()
        {
            Call("Start");

            CollectionAssert.AreEqual(new[] { "caravan" }, sim.Groups.Select(g => g.Id));
            Assert.AreNotEqual(0, sim.Groups[0].RosterSeed, "a seeded group takes a seed from its id");
        }

        [Test]
        public void CreateGroup_AddsAFindableGroup_AndRefusesADuplicateId()
        {
            NpcGroup group = sim.CreateGroup(warParty, "warparty:sand:p:1", new Vector3(10f, 0f, 20f));

            Assert.AreSame(group, sim.FindGroup("warparty:sand:p:1"));
            Assert.AreEqual("sand-war-party", group.TemplateId);
            Assert.AreEqual(RosterDraw.StableHash("warparty:sand:p:1"), group.RosterSeed);

            LogAssert.Expect(LogType.Error, new Regex("already exists"));
            Assert.IsNull(sim.CreateGroup(warParty, "warparty:sand:p:1", Vector3.zero));
        }

        [Test]
        public void ReleaseGroup_OfAFoldedGroup_RemovesItAtOnce()
        {
            sim.CreateGroup(warParty, "w", Vector3.zero);
            sim.ReleaseGroup("w");

            Assert.IsNull(sim.FindGroup("w"));
        }

        [Test]
        public void ReleaseGroup_OfASpawnedGroup_ClearsTheQuarry_AndWaitsForTheFold()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Spawned = true;

            sim.ReleaseGroup("w");

            Assert.AreSame(group, sim.FindGroup("w"));
            Assert.IsFalse(group.IsWarParty);
            Assert.IsTrue(group.DisbandWhenFolded);
        }

        [Test]
        public void WarPartyTemplateFor_FindsTheTribesRuntimeHunterTemplate()
        {
            Assert.AreSame(warParty, sim.WarPartyTemplateFor(sand));
            Assert.AreSame(sand, sim.TribeOf(sim.CreateGroup(warParty, "w", Vector3.zero)));
        }

        [Test]
        public void RestoreRecords_DropsRuntimeGroupsTheSaveDoesNotHave_AndSkipsReleasedRecords()
        {
            Call("Start");
            sim.CreateGroup(warParty, "stale", Vector3.zero).QuarryProfileId = "p";

            var hunting = new NpcGroup { Id = "warparty:sand:p:3", TemplateId = "sand-war-party", QuarryProfileId = "p", Tier = 1 };
            var released = new NpcGroup { Id = "warparty:sand:p:2", TemplateId = "sand-war-party" };

            sim.RestoreRecords(new[] { sim.Groups[0].ToRecord(), hunting.ToRecord(), released.ToRecord() });

            CollectionAssert.AreEquivalent(new[] { "caravan", "warparty:sand:p:3" }, sim.Groups.Select(g => g.Id));
            Assert.AreEqual(1, sim.FindGroup("warparty:sand:p:3").Tier);
        }

        [Test]
        public void WarParty_LeadNeverGoesCold_WhileFolded()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";
            group.Lead = new Vector3(5000f, 0f, 0f);
            group.HasLead = true;

            for (int i = 0; i < 400; i++) Call("TickGroup", group, 1f);

            Assert.IsTrue(group.HasLead);
            Assert.Greater(group.LeadAge, 300f);
            Assert.Greater(group.Position.x, 0f, "it walked toward the lead");
        }

        [Test]
        public void ReportSighting_DoesNotSteerAWarParty()
        {
            NpcGroup group = sim.CreateGroup(warParty, "w", Vector3.zero);
            group.QuarryProfileId = "p";

            sim.ReportSighting(new Vector3(1f, 0f, 1f));

            Assert.IsFalse(group.HasLead);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `python tools/typecheck.py --editor`
Expected: errors on `tribe`, `runtimeOnly`, `role`, `NpcGroupComposition`, `PlannedMember`, `CreateGroup`, `ReleaseGroup`, `FindGroup`, `WarPartyTemplateFor`, `TribeOf`.

- [ ] **Step 3: Extend the template types in `NpcGroup.cs`**

In `NpcGroupMemberSpec`, after `prefab`:

```csharp
        [Tooltip("Used only when prefab is empty: the prefab is drawn from the template tribe's roster.")]
        public RosterRole role;
```

In `NpcGroupTemplate`, after `displayName`:

```csharp
        [Tooltip("The tribe this group belongs to. Members take its faction, and roles draw from its " +
                 "roster. Leave empty for groups that are not a tribe's (Outlaws have no roster but " +
                 "still set this so their members are stamped).")]
        public FactionDefinition tribe;

        [Tooltip("Never seeded at startup; only created at runtime (war parties). Kept in this list so " +
                 "a saved runtime group can always find its template on load.")]
        public bool runtimeOnly;
```

- [ ] **Step 4: Write `NpcGroupComposition.cs`**

```csharp
// Which prefabs a group spawns, in spawn order. Kept out of NpcWorldSim so the rule — explicit prefab,
// else the roster's draw for the role, and a war party's people from its tier — is tested without
// spawning anything.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public readonly struct PlannedMember
    {
        /// <summary>Null when nothing could be drawn; the slot is skipped but its index is still spent.</summary>
        public readonly GameObject Prefab;
        public readonly bool Leads;

        public PlannedMember(GameObject prefab, bool leads)
        {
            Prefab = prefab;
            Leads = leads;
        }
    }

    public static class NpcGroupComposition
    {
        public static List<PlannedMember> Resolve(NpcGroup group, NpcGroupTemplate template)
        {
            var plan = new List<PlannedMember>();
            FactionRoster roster = template.tribe != null ? template.tribe.roster : null;

            if (group.IsWarParty)
            {
                WarPartyTier tier = roster != null ? roster.TierAt(group.Tier) : null;
                if (tier == null)
                {
                    Debug.LogError($"[NpcWorldSim] War party '{group.Id}' has no tier {group.Tier} to draw " +
                                   $"from: {(template.tribe != null ? template.tribe.factionName : "no tribe")} " +
                                   "has no roster or no warPartyTiers.");
                    return plan;
                }

                foreach (RoleCount wanted in tier.roles)
                {
                    if (wanted == null) continue;

                    for (int i = 0; i < Mathf.Max(1, wanted.count); i++)
                        plan.Add(new PlannedMember(roster.Draw(wanted.role, group.RosterSeed, plan.Count), plan.Count == 0));
                }

                return plan;
            }

            if (template.members == null) return plan;

            foreach (NpcGroupMemberSpec spec in template.members)
            {
                if (spec == null) continue;

                for (int i = 0; i < Mathf.Max(1, spec.count); i++)
                {
                    GameObject prefab = spec.prefab != null
                        ? spec.prefab
                        : roster != null ? roster.Draw(spec.role, group.RosterSeed, plan.Count) : null;

                    plan.Add(new PlannedMember(prefab, spec.isLeader));
                }
            }

            return plan;
        }
    }
}
```

- [ ] **Step 5: Runtime groups in `NpcWorldSim`**

Add `using System;` at the top. After `public IReadOnlyList<NpcGroup> Groups => groups;` add:

```csharp
        /// <summary>A war party's fighter saw its quarry for the first time since it spawned. Server only.</summary>
        public event Action<NpcGroup, GameObject> QuarrySighted;

        public float SpawnRadius => spawnRadius;
```

In `Update`, after the tick loop:

```csharp
            // A released group leaves once nobody can see it go (rosters plan: never popped out of view).
            groups.RemoveAll(g => g.DisbandWhenFolded && !g.Spawned);
```

Note: `TickGroup` is only reached after the `tickTimer` early return, so place the `RemoveAll` inside the same block, after the `for` loop.

In `SeedGroups`, skip runtime templates and seed deterministically:

```csharp
                if (template == null || string.IsNullOrWhiteSpace(template.id) || template.runtimeOnly) continue;

                var group = new NpcGroup
                {
                    Id = template.id,
                    TemplateId = template.id,
                    Position = ResolveStart(template),
                    RosterSeed = RosterDraw.StableHash(template.id),
                };
```

Add a new region after `ResolveStart`:

```csharp
        // ── Runtime groups ───────────────────────────────────────────────────────

        /// <summary>
        /// A group that no template seeded — a war party. Its template must be in <c>templates</c>
        /// (normally <c>runtimeOnly</c>), or a save could never restore it. Server only.
        /// </summary>
        public NpcGroup CreateGroup(NpcGroupTemplate template, string groupId, Vector3 start)
        {
            if (template == null || string.IsNullOrWhiteSpace(groupId)) return null;

            if (FindGroup(groupId) != null)
            {
                Debug.LogError($"[NpcWorldSim] A group with id '{groupId}' already exists; ids key both " +
                               "the save record and the formation.", this);
                return null;
            }

            if (!templatesById.ContainsKey(template.id ?? string.Empty))
            {
                Debug.LogError($"[NpcWorldSim] Template '{template.id}' is not in this sim's list, so a " +
                               "save could not restore the group. Add it (runtimeOnly).", this);
                return null;
            }

            var group = new NpcGroup
            {
                Id = groupId,
                TemplateId = template.id,
                Position = start,
                RosterSeed = RosterDraw.StableHash(groupId),
            };

            groups.Add(group);
            Log($"{template.displayName} created as '{groupId}' at {start:F0}");
            return group;
        }

        public NpcGroup FindGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return null;

            foreach (NpcGroup group in groups)
                if (group.Id == groupId) return group;

            return null;
        }

        /// <summary>Remove a group now, despawning any live members through the netcode path.</summary>
        public void DisbandGroup(string groupId)
        {
            NpcGroup group = FindGroup(groupId);
            if (group == null) return;

            foreach (GameObject member in group.Live)
                DestroyMember(member);

            group.Live.Clear();
            groups.Remove(group);
            Log($"'{groupId}' disbanded");
        }

        /// <summary>
        /// Stop a group being anyone's war party. Folded: removed at once. Spawned: its quarry is cleared
        /// and it is removed when it folds, so players never watch it vanish.
        /// </summary>
        public void ReleaseGroup(string groupId)
        {
            NpcGroup group = FindGroup(groupId);
            if (group == null) return;

            if (!group.Spawned)
            {
                DisbandGroup(groupId);
                return;
            }

            group.QuarryProfileId = string.Empty;
            group.DisbandWhenFolded = true;
        }

        /// <summary>The template a tribe's war parties are created from: runtime-only, hunting, that tribe's.</summary>
        public NpcGroupTemplate WarPartyTemplateFor(FactionDefinition tribe)
        {
            if (tribe == null || templates == null) return null;

            foreach (NpcGroupTemplate template in templates)
                if (template != null && template.runtimeOnly && template.bountyHunters && template.tribe == tribe)
                    return template;

            return null;
        }

        public FactionDefinition TribeOf(NpcGroup group) => TemplateFor(group)?.tribe;

        public void CollectPlayerPositions(List<Vector3> into)
        {
            into.Clear();
            foreach (Transform player in players)
                if (player != null) into.Add(player.position);
        }
```

- [ ] **Step 6: Tick war parties by their own rules**

In `TickGroup`, first line after the template null check:

```csharp
            // Wiped out: waits for the director to resolve it rather than re-spawning at full strength.
            if (group.WipedOut) return;
```

In `TickSpawned`, replace the wiped-out branch and the lead refresh:

```csharp
            if (group.Live.Count == 0)
            {
                group.Spawned = false;
                if (group.IsWarParty) group.WipedOut = true;
                Log($"{template.displayName} was wiped out");
                return;
            }

            group.Position = Centroid(group);

            if (group.IsWarParty) RefreshQuarryLead(group, delta);
            else if (template.bountyHunters) RefreshLead(group, delta);
```

In `TickVirtual`, before the `bountyHunters` branch:

```csharp
            if (group.IsWarParty)
            {
                TickWarPartyVirtual(group, template, delta);
                return;
            }
```

Add beside `TickHunterVirtual`:

```csharp
        /// <summary>
        /// A war party's lead is the director's (rosters spec §5.2): it never goes cold, and reaching it
        /// is not the end of the search — the director will refresh it. With no lead yet it waits.
        /// </summary>
        private static void TickWarPartyVirtual(NpcGroup group, NpcGroupTemplate template, float delta)
        {
            if (!group.HasLead) return;

            group.LeadAge += delta;
            group.GoalPosition = group.Lead;
            group.ArriveRadius = 20f;
            group.HasGoal = true;
            group.AdvanceToward(template.travelSpeed, delta);
        }
```

Add beside `RefreshLead`:

```csharp
        /// <summary>
        /// A spawned war party follows only its quarry. Anybody else its members shoot at is a fight on
        /// the way, not a lead — and ReportSighting cannot say whose position it carries.
        /// </summary>
        private void RefreshQuarryLead(NpcGroup group, float delta)
        {
            foreach (GameObject member in group.Live)
            {
                GameObject fighter = GroupMembership.FighterOf(member);
                if (fighter == null || !fighter.TryGetComponent(out AgentTargeting targeting)) continue;
                if (!targeting.HasTarget || !targeting.CanSeeTarget || !IsQuarry(group, targeting.Target)) continue;

                group.Lead = targeting.Target.position;
                group.HasLead = true;
                group.LeadAge = 0f;

                if (!group.QuarrySeenThisSpawn)
                {
                    group.QuarrySeenThisSpawn = true;
                    QuarrySighted?.Invoke(group, fighter);
                }

                return;
            }

            if (group.HasLead) group.LeadAge += delta;
        }

        private static bool IsQuarry(NpcGroup group, Transform target)
        {
            EntityFaction entity = target != null ? target.GetComponentInParent<EntityFaction>() : null;
            SpaceGame.Core.Persistence.PlayerSaveService players = SpaceGame.Core.Persistence.SaveManager.Instance?.Players;

            return entity != null && players != null
                   && players.TryGetProfileFor(entity.gameObject, out string profileId)
                   && profileId == group.QuarryProfileId;
        }
```

(Use `using SpaceGame.Core.Persistence;` instead of the qualified names if it introduces no ambiguity; `SaveManager` and `PlayerSaveService` live there — confirm with grep.)

In `ReportSighting`, skip war parties and released groups:

```csharp
                if (template == null || !template.bountyHunters || group.IsWarParty || group.DisbandWhenFolded) continue;
```

- [ ] **Step 7: Spawn from the composition and stamp members**

Replace `Spawn` and `SpawnMember`:

```csharp
        private void Spawn(NpcGroup group, NpcGroupTemplate template)
        {
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);
            if (plan.Count == 0) return;

            group.Live.Clear();
            group.FightersSpawned = 0;
            group.FightersDead = 0;
            group.QuarrySeenThisSpawn = false;

            Vector3 heading = group.Heading;
            int followerIndex = 0;
            bool leaderTaken = false;

            for (int index = 0; index < plan.Count; index++)
            {
                PlannedMember planned = plan[index];
                if (planned.Prefab == null) continue;

                bool leads = planned.Leads && !leaderTaken;

                Vector3 slot = leads
                    ? group.Position
                    : FormationMath.SlotPosition(followerIndex, group.Position, heading,
                                                 template.formation, followerIndex * 7919, 0f);

                if (!leads) followerIndex++;

                // Stamped before the network spawn, so the loadout roll in OnNetworkSpawn is seeded.
                int memberIndex = index;
                GameObject member = SpawnMember(planned.Prefab, slot, heading,
                    instance => GroupMembership.Stamp(instance, group, memberIndex, template.tribe));
                if (member == null) continue;

                leaderTaken |= leads;
                group.Live.Add(member);

                Configure(member, group, template, leads);
            }

            if (group.Live.Count == 0) return;

            // Nobody was flagged, so the first spawned leads. FormationModule falls back the same
            // way, but making it explicit here means the task list lands on the right member.
            if (!leaderTaken && group.Live[0].TryGetComponent(out FormationModule first))
                first.SetFormation(group.Id, true);

            group.Spawned = true;
            Log($"{template.displayName} spawned ({group.Live.Count} members)");
        }

        private GameObject SpawnMember(GameObject prefab, Vector3 position, Vector3 heading,
                                       Action<GameObject> beforeSpawn)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, spawnSampleDistance, NavMesh.AllAreas))
                position = hit.position;

            Quaternion rotation = heading.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(heading, Vector3.up)
                : Quaternion.identity;

            return NpcSpawn.Create(prefab, position, rotation, this, beforeSpawn);
        }
```

- [ ] **Step 8: Restore without duplicates**

In `RestoreRecords`, after `BuildTemplateIndex();`:

```csharp
            // A runtime group from the session being replaced has no business in the loaded one. Seeded
            // groups are matched by id below; runtime groups exist only if the save has them.
            var savedIds = new HashSet<string>();
            foreach (NpcGroup.Record record in records)
                if (!string.IsNullOrEmpty(record.id)) savedIds.Add(record.id);

            groups.RemoveAll(g => IsRuntime(g) && !savedIds.Contains(g.Id));
```

In the record loop, before `var restored = …`:

```csharp
                // A released war party saved mid-fold: it was already leaving. Restoring it would leave a
                // hunter with nobody to hunt.
                if (templatesById[record.templateId].runtimeOnly && string.IsNullOrEmpty(record.quarryProfileId))
                    continue;
```

Add the helper:

```csharp
        private bool IsRuntime(NpcGroup group) => TemplateFor(group) is { runtimeOnly: true };
```

- [ ] **Step 9: Run both fixtures** → `NpcGroupCompositionTests` 4 passed, `RuntimeGroupTests` 8 passed.

- [ ] **Step 10: Wire the scene templates** — add to `RosterAuthoring.cs`:

```csharp
        public const string SandWarPartyTemplateId = "sand-war-party";
        private const string WorldScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        private const string OutlawFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/OutlawFaction.asset";

        /// <summary>
        /// Gives the caravans their tribe and adds the Sand War Party template. Copies the sand nomads'
        /// formation; members and tasks are emptied because a war party's people come from its tier.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire War Party Templates")]
        public static void WireWorldSim()
        {
            var sand = Load<FactionDefinition>(SandFactionPath);
            var outlaws = Load<FactionDefinition>(OutlawFactionPath);
            if (sand == null || outlaws == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty templates = so.FindProperty("templates");

                int sandNomads = -1, warParty = -1;
                for (int i = 0; i < templates.arraySize; i++)
                {
                    SerializedProperty template = templates.GetArrayElementAtIndex(i);
                    string id = template.FindPropertyRelative("id").stringValue;

                    if (id == "nomad-caravan" || id == "sand-nomads")
                        template.FindPropertyRelative("tribe").objectReferenceValue = sand;
                    if (id == "bounty-hunters")
                        template.FindPropertyRelative("tribe").objectReferenceValue = outlaws;
                    if (id == "sand-nomads") sandNomads = i;
                    if (id == SandWarPartyTemplateId) warParty = i;
                }

                if (warParty < 0)
                {
                    if (sandNomads < 0)
                    {
                        Debug.LogError("[RosterAuthoring] No 'sand-nomads' template to copy the formation from.");
                        return;
                    }

                    templates.GetArrayElementAtIndex(sandNomads).DuplicateCommand();
                    warParty = sandNomads + 1;
                }

                SerializedProperty party = templates.GetArrayElementAtIndex(warParty);
                party.FindPropertyRelative("id").stringValue = SandWarPartyTemplateId;
                party.FindPropertyRelative("displayName").stringValue = "Sand War Party";
                party.FindPropertyRelative("tribe").objectReferenceValue = sand;
                party.FindPropertyRelative("runtimeOnly").boolValue = true;
                party.FindPropertyRelative("bountyHunters").boolValue = true;
                party.FindPropertyRelative("useStartPosition").boolValue = false;
                party.FindPropertyRelative("travelSpeed").floatValue = 3f;
                party.FindPropertyRelative("members").arraySize = 0;
                party.FindPropertyRelative("tasks").arraySize = 0;

                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        /// <summary>
        /// Open the world scene additively if needed, edit its NpcWorldSim, save, and leave the editor as
        /// it was found — the same dance NomadPrefabBuilder.AddSandNomadCaravan does, for the same reasons.
        /// </summary>
        private static void WithWorldSim(Action<NpcWorldSim> edit)
        {
            Scene scene = SceneManager.GetSceneByPath(WorldScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Additive);

            NpcWorldSim sim = scene.GetRootGameObjects()
                .Select(g => g.GetComponentInChildren<NpcWorldSim>(true))
                .FirstOrDefault(s => s != null);

            if (sim == null)
                Debug.LogError($"[RosterAuthoring] No NpcWorldSim in {WorldScenePath}.");
            else
            {
                edit(sim);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
        }
```

Add `using UnityEditor.SceneManagement;` and `using UnityEngine.SceneManagement;`.

- [ ] **Step 11: Run the menu and read the scene back**

Run `Tools ▸ SpaceGame ▸ Agents ▸ Wire War Party Templates`. Then:

```bash
grep -n "id: sand-war-party" -A4 Assets/Game/Scenes/world/persistentScene.unity
grep -n "runtimeOnly: 1" Assets/Game/Scenes/world/persistentScene.unity
git diff --stat -- Assets/Game/Scenes/world/persistentScene.unity
```

Expected: one `sand-war-party` template with `runtimeOnly: 1`, `bountyHunters: 1`, `members: []`; `tribe:` set on the three caravans. The scene diff touches only the `NpcWorldSim` block. If `persistentScene.unity` already carries unrelated uncommitted edits (it does at the time of writing), read the diff and stage only this task's hunk, or tell the user.

- [ ] **Step 12: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/World/NpcGroup.cs Assets/Game/Scripts/agents/World/NpcGroupComposition.cs* Assets/Game/Scripts/agents/World/NpcWorldSim.cs Assets/Game/Editor/Agents/RosterAuthoring.cs Assets/Game/Editor/Tests/NpcGroupCompositionTests.cs* Assets/Game/Editor/Tests/RuntimeGroupTests.cs*
git add -p Assets/Game/Scenes/world/persistentScene.unity
git commit -m "feat(world-sim): tribe-aware templates and runtime groups"
```

---

### Task 5: War-party rules and the self-defence rule (pure)

**Files:**
- Create: `Assets/Game/Scripts/agents/World/WarPartyRules.cs`, `Assets/Game/Scripts/agents/faction/SelfDefenceRules.cs`
- Test: `Assets/Game/Editor/Tests/WarPartyRulesTests.cs`, `Assets/Game/Editor/Tests/SelfDefenceRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum Reckoning { None, Caught, Defeated, Abandoned }`
  - `[Serializable] struct WarPartySettings` with `partyCooldown, campSearchRadius, stagingMargin, fallbackExtra, maxPursuitDistance, trailInterval, trailFuzz, caughtCredit, defeatedCredit` and `static WarPartySettings Default`.
  - `WarPartyRules.CreditFor(Reckoning, in WarPartySettings) : float`, `NextTier(Reckoning, int tier, int maxTier) : int`, `StagingDistance(float spawnRadius, float margin) : float`, `ShouldAbandon(Vector3 party, Vector3 quarry, float maxPursuit) : bool`, `IsUnobserved(Vector3 point, IReadOnlyList<Vector3> players, float staging) : bool`, `TryCatchUp(Vector3 party, Vector3 lead, float standoff, IReadOnlyList<Vector3> players, float staging, out Vector3 moved) : bool`, `TrailFix(Vector3 real, Vector2 disc, float fuzz) : Vector3`, `FallbackOrigin(Vector3 lastKnown, Vector2 direction, float distance) : Vector3`, `IsDefeated(int fightersSpawned, int fightersDead, bool wipedOut) : bool`, `IsCaughtBy(string killerGroupId, string partyGroupId) : bool`.
  - `SelfDefenceRules.IsExempt(string quarryProfileId, FactionDefinition quarrySide, string attackerProfileId, FactionDefinition attackerSide) : bool`.

- [ ] **Step 1: Write the failing tests**

`WarPartyRulesTests.cs`:

```csharp
// The war-party rules from rosters spec §5, without a scene.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class WarPartyRulesTests
    {
        private static readonly WarPartySettings S = WarPartySettings.Default;

        [Test]
        public void Credits_MatchTheSpec()
        {
            Assert.AreEqual(30f, WarPartyRules.CreditFor(Reckoning.Caught, S));
            Assert.AreEqual(12f, WarPartyRules.CreditFor(Reckoning.Defeated, S));
            Assert.AreEqual(0f, WarPartyRules.CreditFor(Reckoning.Abandoned, S));
            Assert.AreEqual(0f, WarPartyRules.CreditFor(Reckoning.None, S));
        }

        [Test]
        public void OnlyDefeat_RaisesTheTier_AndItCaps()
        {
            Assert.AreEqual(1, WarPartyRules.NextTier(Reckoning.Defeated, 0, 2));
            Assert.AreEqual(2, WarPartyRules.NextTier(Reckoning.Defeated, 2, 2));
            Assert.AreEqual(1, WarPartyRules.NextTier(Reckoning.Caught, 1, 2));
            Assert.AreEqual(1, WarPartyRules.NextTier(Reckoning.Abandoned, 1, 2));
        }

        [Test]
        public void Abandons_OnlyBeyondMaxPursuit_OnTheFlat()
        {
            Assert.IsFalse(WarPartyRules.ShouldAbandon(Vector3.zero, new Vector3(1500f, 900f, 0f), 1500f));
            Assert.IsTrue(WarPartyRules.ShouldAbandon(Vector3.zero, new Vector3(1501f, 0f, 0f), 1500f));
        }

        [Test]
        public void CatchUp_JumpsToTheStandoff_AlongThePath()
        {
            var players = new List<Vector3> { Vector3.zero };
            float staging = WarPartyRules.StagingDistance(250f, 50f);

            Assert.IsTrue(WarPartyRules.TryCatchUp(new Vector3(1000f, 5f, 0f), Vector3.zero, staging + 80f,
                                                   players, staging, out Vector3 moved));
            Assert.AreEqual(380f, moved.x, 0.01f);
            Assert.AreEqual(0f, moved.z, 0.01f);
            Assert.AreEqual(5f, moved.y, 0.01f, "height is the record's own");
        }

        [Test]
        public void CatchUp_NeverMovesAPartyAlreadyInside()
        {
            Assert.IsFalse(WarPartyRules.TryCatchUp(new Vector3(350f, 0f, 0f), Vector3.zero, 380f,
                                                    new List<Vector3>(), 300f, out Vector3 moved));
            Assert.AreEqual(new Vector3(350f, 0f, 0f), moved);
        }

        [Test]
        public void CatchUp_NeverLandsWhereAnyPlayerCouldSee()
        {
            // A second player stands right where the jump would land.
            var players = new List<Vector3> { Vector3.zero, new Vector3(400f, 0f, 0f) };

            Assert.IsFalse(WarPartyRules.TryCatchUp(new Vector3(1000f, 0f, 0f), Vector3.zero, 380f,
                                                    players, 300f, out _));
        }

        [Test]
        public void CatchUp_WithAFuzzedLead_StillLandsOutsideTheQuarrysStaging()
        {
            Vector3 quarry = Vector3.zero;
            Vector3 lead = WarPartyRules.TrailFix(quarry, new Vector2(1f, 0f), 80f);  // 80 m toward the party
            var players = new List<Vector3> { quarry };
            const float staging = 300f;

            Assert.IsTrue(WarPartyRules.TryCatchUp(new Vector3(2000f, 0f, 0f), lead, staging + 80f,
                                                   players, staging, out Vector3 moved));
            Assert.GreaterOrEqual(Vector3.Distance(moved, quarry), staging);
        }

        [Test]
        public void TrailFix_StaysWithinFuzz()
        {
            for (int i = 0; i < 200; i++)
            {
                Vector2 disc = Random.insideUnitCircle * 3f;  // deliberately out of range: must be clamped
                Vector3 fix = WarPartyRules.TrailFix(new Vector3(10f, 2f, 10f), disc, 80f);
                Assert.LessOrEqual(Vector3.Distance(new Vector3(10f, 2f, 10f), fix), 80.001f);
            }
        }

        [Test]
        public void FallbackOrigin_IsAtTheDistance_EvenForAZeroDirection()
        {
            Vector3 origin = WarPartyRules.FallbackOrigin(Vector3.zero, Vector2.zero, 400f);
            Assert.AreEqual(400f, new Vector2(origin.x, origin.z).magnitude, 0.01f);
        }

        [Test]
        public void Defeat_NeedsEveryFighterDown_OrAWipe()
        {
            Assert.IsFalse(WarPartyRules.IsDefeated(0, 0, false), "no fighters seated yet is not a defeat");
            Assert.IsFalse(WarPartyRules.IsDefeated(4, 3, false));
            Assert.IsTrue(WarPartyRules.IsDefeated(4, 4, false));
            Assert.IsTrue(WarPartyRules.IsDefeated(0, 0, true));
        }

        [Test]
        public void Caught_OnlyByThisParty()
        {
            Assert.IsTrue(WarPartyRules.IsCaughtBy("w1", "w1"));
            Assert.IsFalse(WarPartyRules.IsCaughtBy("w2", "w1"));
            Assert.IsFalse(WarPartyRules.IsCaughtBy(null, "w1"));
            Assert.IsFalse(WarPartyRules.IsCaughtBy(null, ""));
        }
    }
}
```

`SelfDefenceRulesTests.cs`:

```csharp
// Rosters spec §5.4: fighting off a war party never costs goodwill for the quarry or anyone on their side.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SelfDefenceRulesTests
    {
        private FactionDefinition crew, redTeam;

        [SetUp]
        public void SetUp()
        {
            crew = ScriptableObject.CreateInstance<FactionDefinition>();
            redTeam = ScriptableObject.CreateInstance<FactionDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(crew);
            Object.DestroyImmediate(redTeam);
        }

        [Test] public void TheQuarry_IsExempt() =>
            Assert.IsTrue(SelfDefenceRules.IsExempt("p1", crew, "p1", crew));

        [Test] public void ACrewmate_IsExempt() =>
            Assert.IsTrue(SelfDefenceRules.IsExempt("p1", crew, "p2", crew));

        [Test] public void AnotherTeam_IsNot() =>
            Assert.IsFalse(SelfDefenceRules.IsExempt("p1", crew, "p2", redTeam));

        [Test] public void NotAWarParty_NobodyIs() =>
            Assert.IsFalse(SelfDefenceRules.IsExempt("", crew, "p1", crew));

        [Test] public void QuarryOffline_OnlyTheQuarryIs()
        {
            Assert.IsTrue(SelfDefenceRules.IsExempt("p1", null, "p1", crew));
            Assert.IsFalse(SelfDefenceRules.IsExempt("p1", null, "p2", null));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `python tools/typecheck.py --editor` → errors on `WarPartySettings`, `WarPartyRules`, `Reckoning`, `SelfDefenceRules`.

- [ ] **Step 3: Write `WarPartyRules.cs`**

```csharp
// The rules of a war party, pure: what each outcome is worth, when to give up, where a party may be
// moved while nobody is watching, and what counts as caught or defeated.
// Design: docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md §5.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>How a war party ended. None: it did not end by any rule (its quarry left the game).</summary>
    public enum Reckoning
    {
        None,
        Caught,
        Defeated,
        Abandoned,
    }

    [Serializable]
    public struct WarPartySettings
    {
        [Tooltip("Seconds after a party resolves before the next one is raised.")]
        public float partyCooldown;

        [Tooltip("How far from the quarry to look for a camp to raise the party at.")]
        public float campSearchRadius;

        [Tooltip("Metres beyond the world sim's spawn radius a party is kept while it catches up unseen. " +
                 "Covers a player moving for one world-sim tick.")]
        public float stagingMargin;

        [Tooltip("With no usable camp, the party starts this far beyond the staging distance.")]
        public float fallbackExtra;

        [Tooltip("Further than this from its quarry, a party gives up. No goodwill, no tier change.")]
        public float maxPursuitDistance;

        [Tooltip("Seconds without contact before the party gets a fresh, fuzzy fix on its quarry.")]
        public float trailInterval;

        [Tooltip("How far from the quarry's real position a fix may be. Tracks, not telepathy.")]
        public float trailFuzz;

        [Tooltip("Goodwill returned when the party kills its quarry.")]
        public float caughtCredit;

        [Tooltip("Goodwill returned when the party is wiped out. The next party is a tier stronger.")]
        public float defeatedCredit;

        public static WarPartySettings Default => new WarPartySettings
        {
            partyCooldown = 180f,
            campSearchRadius = 1000f,
            stagingMargin = 50f,
            fallbackExtra = 100f,
            maxPursuitDistance = 1500f,
            trailInterval = 60f,
            trailFuzz = 80f,
            caughtCredit = 30f,
            defeatedCredit = 12f,
        };
    }

    public static class WarPartyRules
    {
        public static float CreditFor(Reckoning outcome, in WarPartySettings settings) => outcome switch
        {
            Reckoning.Caught   => Mathf.Abs(settings.caughtCredit),
            Reckoning.Defeated => Mathf.Abs(settings.defeatedCredit),
            _                  => 0f,
        };

        /// <summary>Beating a party makes the next one stronger, up to the roster's last tier.</summary>
        public static int NextTier(Reckoning outcome, int tier, int maxTier)
        {
            int cap = Mathf.Max(0, maxTier);
            int next = outcome == Reckoning.Defeated ? tier + 1 : tier;
            return Mathf.Clamp(next, 0, cap);
        }

        public static float StagingDistance(float spawnRadius, float margin) => spawnRadius + Mathf.Max(0f, margin);

        public static bool ShouldAbandon(Vector3 party, Vector3 quarry, float maxPursuit) =>
            FlatDistance(party, quarry) > maxPursuit;

        /// <summary>No player is within <paramref name="staging"/> of <paramref name="point"/>.</summary>
        public static bool IsUnobserved(Vector3 point, IReadOnlyList<Vector3> players, float staging)
        {
            for (int i = 0; i < players.Count; i++)
                if (FlatDistance(point, players[i]) < staging) return false;

            return true;
        }

        /// <summary>
        /// Move a folded party along its path to <paramref name="standoff"/> from its lead, if it is
        /// further than that and no player could see where it lands. The last stretch is always walked.
        /// </summary>
        public static bool TryCatchUp(Vector3 party, Vector3 lead, float standoff,
                                      IReadOnlyList<Vector3> players, float staging, out Vector3 moved)
        {
            moved = party;

            Vector3 fromLead = party - lead;
            fromLead.y = 0f;

            float distance = fromLead.magnitude;
            if (distance <= standoff) return false;

            Vector3 candidate = lead + fromLead / distance * standoff;
            candidate.y = party.y;

            if (!IsUnobserved(candidate, players, staging)) return false;

            moved = candidate;
            return true;
        }

        /// <summary>The quarry's real position, off by up to <paramref name="fuzz"/> on the flat.</summary>
        public static Vector3 TrailFix(Vector3 real, Vector2 disc, float fuzz)
        {
            Vector2 offset = Vector2.ClampMagnitude(disc, 1f) * Mathf.Max(0f, fuzz);
            return real + new Vector3(offset.x, 0f, offset.y);
        }

        public static Vector3 FallbackOrigin(Vector3 lastKnown, Vector2 direction, float distance)
        {
            Vector2 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.up;
            return lastKnown + new Vector3(dir.x, 0f, dir.y) * distance;
        }

        public static bool IsDefeated(int fightersSpawned, int fightersDead, bool wipedOut) =>
            wipedOut || (fightersSpawned > 0 && fightersDead >= fightersSpawned);

        public static bool IsCaughtBy(string killerGroupId, string partyGroupId) =>
            !string.IsNullOrEmpty(partyGroupId) && killerGroupId == partyGroupId;

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            Vector3 d = a - b;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
```

- [ ] **Step 4: Write `SelfDefenceRules.cs`**

```csharp
// Rosters spec §5.4. Hits and kills on a war-party member cost nothing when the attacker is the party's
// quarry or on the quarry's side — the same same-EntityFaction rule the hunt spill uses, which is the
// crew in the open world and the team in a versus match. Anyone else pays as usual.
namespace SpaceGame.Agents
{
    public static class SelfDefenceRules
    {
        /// <param name="quarrySide">The quarry's faction, or null when they are not in the game.</param>
        public static bool IsExempt(string quarryProfileId, FactionDefinition quarrySide,
                                    string attackerProfileId, FactionDefinition attackerSide)
        {
            if (string.IsNullOrEmpty(quarryProfileId)) return false;
            if (attackerProfileId == quarryProfileId) return true;

            return quarrySide != null && attackerSide == quarrySide;
        }
    }
}
```

- [ ] **Step 5: Run both fixtures** → `WarPartyRulesTests` 11 passed, `SelfDefenceRulesTests` 5 passed.

- [ ] **Step 6: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/World/WarPartyRules.cs* Assets/Game/Scripts/agents/faction/SelfDefenceRules.cs* Assets/Game/Editor/Tests/WarPartyRulesTests.cs* Assets/Game/Editor/Tests/SelfDefenceRulesTests.cs*
git commit -m "feat(factions): pure war-party and self-defence rules"
```

---

### Task 6: The ledger exempts self-defence and accepts credits

**Files:**
- Modify: `Assets/Game/Scripts/agents/faction/FactionGoodwillLedger.cs` (`Report` ~382–398; add `Credit` and `IsSelfDefence`)
- Modify: `Assets/Game/Scripts/agents/AI/Targeting/ProvocationModule.cs:225`, `Assets/Game/Scripts/agents/entity/HealthReactionModule.cs:264`
- Test: `Assets/Game/Editor/Tests/FactionGoodwillLedgerTests.cs` (extend)

**Interfaces:**
- Consumes: Task 3 `GroupMembership`, Task 5 `SelfDefenceRules`.
- Produces: `FactionGoodwillLedger.Report(EntityFaction victim, EntityFaction attacker, GoodwillEvent kind, float magnitude = 1f)` (replaces the `FactionDefinition victimFaction` overload — there is no other caller), `FactionGoodwillLedger.Credit(FactionDefinition faction, string profileId, float amount)`.

- [ ] **Step 1: Write the failing tests** — append to `FactionGoodwillLedgerTests`:

```csharp
        [Test]
        public void Credit_MovesTowardPeace_AndCanEndAWar()
        {
            Move(sand, PlayerA, -85f);
            Assert.AreEqual(GoodwillBand.AtWar, ledger.BandFor(sand, PlayerA));

            ledger.Credit(sand, PlayerA, 30f);

            Assert.AreEqual(-55f, ledger.ValueFor(sand, PlayerA), 0.001f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));
        }

        [Test]
        public void Credit_TwoDefeats_EndTheWarToo()
        {
            Move(sand, PlayerA, -85f);

            ledger.Credit(sand, PlayerA, 12f);
            Assert.AreEqual(GoodwillBand.AtWar, ledger.BandFor(sand, PlayerA), "-73 is inside the sticky edge");

            ledger.Credit(sand, PlayerA, 12f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, PlayerA));
        }

        [Test]
        public void Credit_IgnoresNonPositiveAmounts()
        {
            Move(sand, PlayerA, -50f);

            ledger.Credit(sand, PlayerA, 0f);
            ledger.Credit(sand, PlayerA, -10f);

            Assert.AreEqual(-50f, ledger.ValueFor(sand, PlayerA), 0.001f);
        }
```

The `IsSelfDefence` path needs a bound player (SaveManager), so like `Report`'s attacker resolution it is covered by `SelfDefenceRulesTests` plus play-checklist item 7, not here.

- [ ] **Step 2: Run to verify it fails** — type-check → `Credit` does not exist.

- [ ] **Step 3: Change `Report` and add `Credit`**

Replace the `Report` method (keep its doc comment, updating the parameter name) with:

```csharp
        public void Report(EntityFaction victim, EntityFaction attacker, GoodwillEvent kind, float magnitude = 1f)
        {
            if (!Network.Decides || victim == null || attacker == null) return;
            if (!TryGetProfile(attacker.gameObject, out string profileId)) return;

            // Fighting off a war party is not a fresh offence (rosters spec §5.4). Hits as well as
            // kills: every hit costs points, so exempting only kills would still push a defender deeper.
            if (IsSelfDefence(victim, attacker, profileId)) return;

            FactionDefinition victimFaction = victim.Faction;

            float delta = DeltaFor(kind, magnitude);
            if (Mathf.Approximately(delta, 0f)) return;

            Move(victimFaction, profileId, delta);
            SpreadToOthers(victimFaction, profileId, delta);
        }

        /// <summary>
        /// Amends that no attacker performed — a war party resolved (rosters spec §5.3), later a quest.
        /// Runs through the same hysteresis, spread and BandChanged as any other event.
        /// </summary>
        public void Credit(FactionDefinition faction, string profileId, float amount)
        {
            if (!Network.Decides || amount <= 0f) return;

            Move(faction, profileId, amount);
            SpreadToOthers(faction, profileId, amount);
        }

        private static bool IsSelfDefence(EntityFaction victim, EntityFaction attacker, string attackerProfileId)
        {
            if (!victim.TryGetComponent(out GroupMembership membership) || membership.Group == null)
                return false;

            string quarry = membership.Group.QuarryProfileId;
            if (string.IsNullOrEmpty(quarry)) return false;

            FactionDefinition quarrySide = null;
            PlayerSaveService players = SaveManager.Instance?.Players;
            if (players != null && players.TryGetBoundPlayer(quarry, out GameObject quarryBody)
                && quarryBody.TryGetComponent(out EntityFaction quarryFaction))
                quarrySide = quarryFaction.Faction;

            return SelfDefenceRules.IsExempt(quarry, quarrySide, attackerProfileId, attacker.Faction);
        }
```

Keep whatever comments the old body had between the lines; drop only those that described the old `victimFaction` parameter.

- [ ] **Step 4: Update the two callers**

`ProvocationModule.cs` (~225):

```csharp
                FactionGoodwillLedger.Instance?.Report(mine, attacker, GoodwillEvent.Hit, fraction);
```

`HealthReactionModule.cs` (~264):

```csharp
            ledger.Report(mine, killer, GoodwillEvent.Kill);
```

Then grep `Assets/Game/Scripts` for `\.Report(` on the ledger → exactly these two.

- [ ] **Step 5: Run `FactionGoodwillLedgerTests`, `AlertChainTests`, `AggressionMathTests`** → all green (ledger fixture +3).

- [ ] **Step 6: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/faction/FactionGoodwillLedger.cs Assets/Game/Scripts/agents/AI/Targeting/ProvocationModule.cs Assets/Game/Scripts/agents/entity/HealthReactionModule.cs Assets/Game/Editor/Tests/FactionGoodwillLedgerTests.cs
git commit -m "feat(goodwill): self-defence exemption and reckoning credits"
```

---

### Task 7: The war book (pure war state)

**Files:**
- Create: `Assets/Game/Scripts/agents/World/WarBook.cs`
- Test: `Assets/Game/Editor/Tests/WarBookTests.cs`

**Interfaces:**
- Consumes: Task 5 `Reckoning`, `WarPartyRules.NextTier`.
- Produces:
  - `sealed class War`: `FactionDefinition Tribe`, `string ProfileId`, `int Tier`, `string PartyGroupId` (never null), `float Cooldown`, `bool HasParty`.
  - `sealed class WarBook`: `IReadOnlyList<War> Wars`, `List<War> Snapshot()`, `War Find(FactionDefinition, string)`, `War FindByGroup(string)`, `War Open(FactionDefinition, string)`, `void Close(War)`, `void Tick(float)`, `bool ReadyToRaise(War)`, `string AssignParty(War, Func<string, bool> taken)`, `void Resolve(War, Reckoning, int maxTier, float cooldown)`, `void ClearParty(War, float cooldown)`, `War Adopt(FactionDefinition, string, string groupId, int tier)`, `int TierFor(FactionDefinition, string)`, `void RestoreTier(FactionDefinition, string, int)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// The war book: one war per (tribe, player), one party per war, escalation that survives the gap
// between parties, and ids that never collide with a restored party.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class WarBookTests
    {
        private FactionDefinition sand, sky;
        private WarBook book;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = "sand";
            sky = ScriptableObject.CreateInstance<FactionDefinition>();
            sky.ID = "sky";
            book = new WarBook();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(sand);
            Object.DestroyImmediate(sky);
        }

        [Test]
        public void Open_IsIdempotent_PerTribeAndPlayer()
        {
            War a = book.Open(sand, "p1");
            Assert.AreSame(a, book.Open(sand, "p1"));
            Assert.AreNotSame(a, book.Open(sand, "p2"), "two players at war are two wars");
            Assert.AreNotSame(a, book.Open(sky, "p1"));
            Assert.AreEqual(3, book.Wars.Count);
        }

        [Test]
        public void ANewWar_IsReadyToRaiseAtOnce()
        {
            War war = book.Open(sand, "p1");
            Assert.IsTrue(book.ReadyToRaise(war));
            Assert.AreEqual(0, war.Tier);
        }

        [Test]
        public void AssignParty_NamesItAfterTheWar_AndSkipsTakenIds()
        {
            War war = book.Open(sand, "p1");
            string id = book.AssignParty(war, taken => taken == "warparty:sand:p1:1");

            Assert.AreEqual("warparty:sand:p1:2", id);
            Assert.AreEqual(id, war.PartyGroupId);
            Assert.IsFalse(book.ReadyToRaise(war), "one party per war");
            Assert.AreSame(war, book.FindByGroup(id));
        }

        [Test]
        public void Resolve_Defeated_RaisesTier_AndStartsTheCooldown()
        {
            War war = book.Open(sand, "p1");
            book.AssignParty(war, _ => false);

            book.Resolve(war, Reckoning.Defeated, maxTier: 2, cooldown: 180f);

            Assert.AreEqual(1, war.Tier);
            Assert.IsFalse(war.HasParty);
            Assert.IsFalse(book.ReadyToRaise(war));

            book.Tick(179f);
            Assert.IsFalse(book.ReadyToRaise(war));
            book.Tick(1f);
            Assert.IsTrue(book.ReadyToRaise(war));
        }

        [Test]
        public void Cooldown_DoesNotRun_WhileAPartyIsOut()
        {
            War war = book.Open(sand, "p1");
            book.ClearParty(war, 50f);
            book.AssignParty(war, _ => false);

            book.Tick(100f);
            Assert.AreEqual(50f, war.Cooldown);
        }

        [Test]
        public void Close_ForgetsTheTier()
        {
            War war = book.Open(sand, "p1");
            book.AssignParty(war, _ => false);
            book.Resolve(war, Reckoning.Defeated, 2, 0f);

            book.Close(war);

            Assert.IsNull(book.Find(sand, "p1"));
            Assert.AreEqual(0, book.TierFor(sand, "p1"));
            Assert.AreEqual(0, book.Open(sand, "p1").Tier, "a new war starts from scouts");
        }

        [Test]
        public void RestoredTier_SeedsTheNextWarOpened()
        {
            book.RestoreTier(sand, "p1", 2);
            Assert.AreEqual(2, book.TierFor(sand, "p1"));
            Assert.AreEqual(2, book.Open(sand, "p1").Tier);
        }

        [Test]
        public void Adopt_TakesTheRestoredParty_WithoutASecondWar()
        {
            War adopted = book.Adopt(sand, "p1", "warparty:sand:p1:4", 1);

            Assert.AreSame(adopted, book.Open(sand, "p1"));
            Assert.AreEqual(1, book.Wars.Count);
            Assert.AreEqual("warparty:sand:p1:4", adopted.PartyGroupId);
            Assert.AreEqual(1, adopted.Tier);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — type-check → `WarBook`, `War` do not exist.

- [ ] **Step 3: Write `WarBook.cs`**

```csharp
// Every war currently on: which tribe, which player, how escalated, which party is out, how long until
// the next. Pure C# so the lifecycle is tested without a scene; WarPartyDirector is the glue that acts
// on it. A war is keyed per player (rosters spec §5.1): two players at war with Sand are two wars.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public sealed class War
    {
        public War(FactionDefinition tribe, string profileId)
        {
            Tribe = tribe;
            ProfileId = profileId;
        }

        public FactionDefinition Tribe { get; }
        public string ProfileId { get; }
        public int Tier { get; internal set; }
        public string PartyGroupId { get; internal set; } = string.Empty;
        public float Cooldown { get; internal set; }

        public bool HasParty => !string.IsNullOrEmpty(PartyGroupId);

        internal int PartiesRaised;
    }

    public sealed class WarBook
    {
        private readonly List<War> wars = new();

        // The tier a war stands at, kept between parties and across saves (FactionGoodwillSaveable's
        // warTier). Cleared when the war ends: peace resets escalation.
        private readonly Dictionary<(string, string), int> tiers = new();

        public IReadOnlyList<War> Wars => wars;

        /// <summary>A copy to iterate while resolving, which may close wars.</summary>
        public List<War> Snapshot() => new(wars);

        public War Find(FactionDefinition tribe, string profileId)
        {
            foreach (War war in wars)
                if (war.Tribe == tribe && war.ProfileId == profileId) return war;

            return null;
        }

        public War FindByGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return null;

            foreach (War war in wars)
                if (war.PartyGroupId == groupId) return war;

            return null;
        }

        public War Open(FactionDefinition tribe, string profileId)
        {
            War war = Find(tribe, profileId);
            if (war != null) return war;

            war = new War(tribe, profileId) { Tier = TierFor(tribe, profileId) };
            wars.Add(war);
            return war;
        }

        public void Close(War war)
        {
            if (war == null) return;

            wars.Remove(war);
            tiers.Remove(Key(war.Tribe, war.ProfileId));
        }

        /// <summary>Cooldowns run only between parties.</summary>
        public void Tick(float deltaTime)
        {
            foreach (War war in wars)
                if (!war.HasParty) war.Cooldown = Mathf.Max(0f, war.Cooldown - deltaTime);
        }

        public bool ReadyToRaise(War war) => war != null && !war.HasParty && war.Cooldown <= 0f;

        /// <summary>
        /// Name the next party. <paramref name="taken"/> is asked because the counter restarts after a
        /// load while a restored party may still hold "…:1".
        /// </summary>
        public string AssignParty(War war, Func<string, bool> taken)
        {
            string id;
            do
            {
                war.PartiesRaised++;
                id = $"warparty:{war.Tribe.ID}:{war.ProfileId}:{war.PartiesRaised}";
            }
            while (taken(id));

            war.PartyGroupId = id;
            return id;
        }

        public void Resolve(War war, Reckoning outcome, int maxTier, float cooldown)
        {
            war.Tier = WarPartyRules.NextTier(outcome, war.Tier, maxTier);
            tiers[Key(war.Tribe, war.ProfileId)] = war.Tier;
            ClearParty(war, cooldown);
        }

        public void ClearParty(War war, float cooldown)
        {
            war.PartyGroupId = string.Empty;
            war.Cooldown = Mathf.Max(0f, cooldown);
        }

        /// <summary>A party restored from a save: its war resumes with it rather than raising a second.</summary>
        public War Adopt(FactionDefinition tribe, string profileId, string groupId, int tier)
        {
            War war = Open(tribe, profileId);
            war.PartyGroupId = groupId;
            war.Tier = Mathf.Max(0, tier);
            tiers[Key(tribe, profileId)] = war.Tier;
            return war;
        }

        public int TierFor(FactionDefinition tribe, string profileId)
        {
            War war = Find(tribe, profileId);
            if (war != null) return war.Tier;

            return tiers.TryGetValue(Key(tribe, profileId), out int tier) ? tier : 0;
        }

        public void RestoreTier(FactionDefinition tribe, string profileId, int tier)
        {
            int restored = Mathf.Max(0, tier);
            if (restored == 0)
            {
                tiers.Remove(Key(tribe, profileId));
                return;
            }

            tiers[Key(tribe, profileId)] = restored;

            War war = Find(tribe, profileId);
            if (war != null && !war.HasParty) war.Tier = restored;
        }

        private static (string, string) Key(FactionDefinition tribe, string profileId) =>
            (tribe != null ? tribe.ID : string.Empty, profileId ?? string.Empty);
    }
}
```

- [ ] **Step 4: Run `WarBookTests`** → 8 passed.

- [ ] **Step 5: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/World/WarBook.cs* Assets/Game/Editor/Tests/WarBookTests.cs*
git commit -m "feat(factions): war book tracks wars, parties and escalation"
```

---

### Task 8: Telling the player — notices and the war cry

**Files:**
- Create: `Assets/Game/Scripts/agents/World/WarNotice.cs`
- Modify: `Assets/Game/Scripts/agents/faction/FactionGoodwillNetwork.cs`
- Modify: `Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/AgentAction.cs`
- Modify: `Assets/Game/Scripts/agents/Modules/Personality/ChatterModule.cs`
- Test: `Assets/Game/Editor/Tests/WarNoticeTextTests.cs`

**Interfaces:**
- Produces: `enum WarNotice { Raised, Weakening, GaveUp }`; `WarNoticeText.For(WarNotice, FactionDefinition) : (string Id, string Text, MessageSeverity Severity)`; `FactionGoodwillNetwork.Notify(FactionDefinition tribe, WarNotice notice)` (server side); `AgentAction.WarCry = 3`; `ChatterModule.WarCry(int lineIndex)` (server side).

- [ ] **Step 1: Write the failing test**

```csharp
// Rosters spec §5.5: what the hunted player is told, and how loudly.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class WarNoticeTextTests
    {
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = "sand";
            sand.factionName = "Sand Tribe";
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(sand);

        [Test]
        public void Raised_IsAWarning_NamingTheTribe()
        {
            var (_, text, severity) = WarNoticeText.For(WarNotice.Raised, sand);
            Assert.AreEqual(MessageSeverity.Warning, severity);
            StringAssert.Contains("Sand Tribe", text);
        }

        [Test]
        public void Weakening_AndGaveUp_AreNotices()
        {
            Assert.AreEqual(MessageSeverity.Notice, WarNoticeText.For(WarNotice.Weakening, sand).Severity);
            Assert.AreEqual(MessageSeverity.Notice, WarNoticeText.For(WarNotice.GaveUp, sand).Severity);
        }

        [Test]
        public void OneSlotPerTribe_SoTheLatestReplacesTheLast()
        {
            Assert.AreEqual(WarNoticeText.For(WarNotice.Raised, sand).Id, WarNoticeText.For(WarNotice.GaveUp, sand).Id);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — type-check → `WarNotice`, `WarNoticeText` do not exist.

- [ ] **Step 3: Write `WarNotice.cs`**

```csharp
// What a hunted player is told about their war (rosters spec §5.5, GDC-L1-DESIGN-0006): a reckoning
// the player cannot perceive is no consequence at all. Phase 7's visor readout will say more; this is
// the minimum tell, through the one visor channel there is.
using SpaceGame.Presentation;

namespace SpaceGame.Agents
{
    /// <summary>Sent as an int over the wire. Append only.</summary>
    public enum WarNotice
    {
        Raised,
        Weakening,
        GaveUp,
    }

    public static class WarNoticeText
    {
        /// <summary>One message id per tribe, so "given up" replaces "a war party is coming" in place.</summary>
        public static (string Id, string Text, MessageSeverity Severity) For(WarNotice notice, FactionDefinition tribe)
        {
            string name = tribe != null ? tribe.factionName : "The tribe";
            string id = $"war:{(tribe != null ? tribe.ID : string.Empty)}";

            return notice switch
            {
                WarNotice.Raised    => (id, $"{name} have sent a war party after you.", MessageSeverity.Warning),
                WarNotice.Weakening => (id, $"{name}: their resolve is weakening.", MessageSeverity.Notice),
                _                   => (id, $"{name} have given up the war.", MessageSeverity.Notice),
            };
        }
    }
}
```

- [ ] **Step 4: Add `Notify` to `FactionGoodwillNetwork`**

Add `using SpaceGame.Presentation;`. After `Send(...)`:

```csharp
        /// <summary>
        /// Server side. Tell this player something about their war.
        ///
        /// Deliberately NOT <see cref="Send"/>'s rule. Send skips the owner because the host holds the
        /// rows already; a notice is an event with nothing to hold, so skipping the host would mean
        /// the host is never told a party is coming (rosters spec §6). Posted here when this machine
        /// is that player — the host, or offline where nothing is spawned — and sent to them otherwise.
        /// </summary>
        public void Notify(FactionDefinition tribe, WarNotice notice)
        {
            if (Ledger == null) return;

            int index = Ledger.IndexOf(tribe);
            if (index < 0) return;

            if (IsOwner || !IsSpawned)
            {
                PresentNotice(index, notice);
                return;
            }

            NoticeRpc(index, (int)notice, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void NoticeRpc(int tribeIndex, int notice, RpcParams rpcParams) =>
            PresentNotice(tribeIndex, (WarNotice)notice);

        private void PresentNotice(int tribeIndex, WarNotice notice)
        {
            // Unknown index: an older client against a newer server. Say nothing rather than name the
            // wrong tribe.
            FactionDefinition tribe = Ledger?.TribeAt(tribeIndex);
            if (tribe == null) return;

            (string id, string text, MessageSeverity severity) = WarNoticeText.For(notice, tribe);
            SystemMessages.Post(id, text, severity);
        }
```

- [ ] **Step 5: Add `AgentAction.WarCry`** after `Band`:

```csharp
        /// <summary>
        /// A war party's first sight of the player it is hunting: a shout from the tribe's roster
        /// <c>hostileLines</c>, index in <see cref="NetArg.B"/>. The index, not the text, so the
        /// message stays small and every machine reads the line from its own copy of the roster.
        /// </summary>
        public const int WarCry = 3;
```

- [ ] **Step 6: Replicated war cry on `ChatterModule`**

Replace `private void Awake() => taskModule = GetComponent<NpcTaskModule>();` and `private void OnEnable() => speakTimer = RollInterval();` with:

```csharp
        private AgentAuthority authority;

        private void Awake()
        {
            taskModule = GetComponent<NpcTaskModule>();
            authority = new AgentAuthority(this);
        }

        // A rider is re-parented onto its mount, which can change who simulates it.
        private void OnTransformParentChanged() => authority?.Invalidate();

        private void OnEnable()
        {
            speakTimer = RollInterval();
            this.NetOn(NetMsg.AgentActed, OnAgentActed);
        }

        private void OnDisable() => this.NetOff(NetMsg.AgentActed, OnAgentActed);
```

(If `ChatterModule` already has an `OnDisable`, add the `NetOff` line to it instead.) Add `using SpaceGame.Core;` if missing. Then add:

```csharp
        /// <summary>
        /// Server side. Shout line <paramref name="lineIndex"/> of this agent's tribe's hostile lines,
        /// here and on every watching machine. Called by WarPartyDirector on a party's first sight.
        /// </summary>
        public void WarCry(int lineIndex)
        {
            PresentWarCry(lineIndex);

            if (Network.Server)
                AgentActionRelay.Broadcast(this, AgentAction.WarCry, transform.position, transform.forward, lineIndex);
        }

        private void OnAgentActed(in NetArg arg, ulong sender)
        {
            if (arg.A != AgentAction.WarCry) return;

            // The deciding machine already shouted while deciding to.
            if (authority == null || authority.SimulatedHere) return;

            PresentWarCry(arg.B);
        }

        private void PresentWarCry(int lineIndex)
        {
            FactionRoster roster = TryGetComponent(out EntityFaction faction) && faction.Faction != null
                ? faction.Faction.roster
                : null;

            string[] lines = roster != null && roster.hostileLines != null ? roster.hostileLines.lines : null;
            if (lines == null || lineIndex < 0 || lineIndex >= lines.Length) return;

            TrySayNow(lines[lineIndex]);
        }
```

- [ ] **Step 7: Type-check; run `WarNoticeTextTests`** → 3 passed. Re-run `AgentActionBroadcastTests` → green.

- [ ] **Step 8: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/World/WarNotice.cs* Assets/Game/Scripts/agents/faction/FactionGoodwillNetwork.cs Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/AgentAction.cs Assets/Game/Scripts/agents/Modules/Personality/ChatterModule.cs Assets/Game/Editor/Tests/WarNoticeTextTests.cs*
git commit -m "feat(factions): war notices and a replicated war cry"
```

---

### Task 9: The war-party director

**Files:**
- Create: `Assets/Game/Scripts/agents/World/WarPartyDirector.cs`
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (the wiring menu also adds the director)
- Modify (by running the menu): `Assets/Game/Scenes/world/persistentScene.unity`
- Test: `Assets/Game/Editor/Tests/WarPartyDirectorTests.cs`

**Interfaces:**
- Consumes: Task 4 `NpcWorldSim` runtime-group API and `QuarrySighted`; Task 5 rules; Task 6 `FactionGoodwillLedger.Credit`; Task 7 `WarBook`; Task 8 `FactionGoodwillNetwork.Notify`, `ChatterModule.WarCry`; Task 3 `GroupMembership.GroupIdOf`.
- Produces: `WarPartyDirector.Instance`, `WarPartyDirector.Book : WarBook`, `int WarTierFor(FactionDefinition, string)`, `void RestoreWarTier(FactionDefinition, string, int)`. Private, reached by tests through reflection: `Raise(War, Vector3)`, `Resolve(War, Reckoning)`, `AdoptRestoredParties()`, `Reconcile(FactionGoodwillLedger)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// The director against a real ledger and world sim, without a session: wars open on AtWar, parties
// are raised with their quarry and tier, and each reckoning moves goodwill and escalation per spec §5.3.
// Anything needing a bound player (tracking, catching, notices) is the play checklist's.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class WarPartyDirectorTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Player = "profile-a";

        private readonly List<Object> junk = new();
        private FactionGoodwillLedger ledger;
        private NpcWorldSim sim;
        private WarPartyDirector director;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            WorldSiteRegistry.Clear();

            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = sand.factionName = "Sand";
            junk.Add(sand);

            var roster = ScriptableObject.CreateInstance<FactionRoster>();
            roster.faction = sand;
            roster.warPartyTiers = new[] { new WarPartyTier(), new WarPartyTier(), new WarPartyTier() };
            sand.roster = roster;
            junk.Add(roster);

            var table = ScriptableObject.CreateInstance<FactionRelationshipTable>();
            junk.Add(table);

            var ledgerGo = new GameObject("Ledger");
            junk.Add(ledgerGo);
            ledger = ledgerGo.AddComponent<FactionGoodwillLedger>();
            var so = new SerializedObject(ledger);
            so.FindProperty("tribes").arraySize = 1;
            so.FindProperty("tribes").GetArrayElementAtIndex(0).objectReferenceValue = sand;
            so.FindProperty("relationships").objectReferenceValue = table;
            so.ApplyModifiedPropertiesWithoutUndo();
            Invoke(ledger, "Awake");

            var simGo = new GameObject("Sim");
            junk.Add(simGo);
            sim = simGo.AddComponent<NpcWorldSim>();
            var template = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Invoke(sim, "Awake");

            director = simGo.AddComponent<WarPartyDirector>();
            Invoke(director, "Awake");
            Invoke(director, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
            WorldSiteRegistry.Clear();
        }

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        private void Move(float delta) => Invoke(ledger, "Move", sand, Player, delta);

        private War AtWarWithAParty()
        {
            Move(-85f);
            War war = director.Book.Find(sand, Player);
            Invoke(director, "Raise", war, Vector3.zero);
            return war;
        }

        [Test]
        public void EnteringAtWar_OpensAWar()
        {
            Move(-85f);
            Assert.IsNotNull(director.Book.Find(sand, Player));
        }

        [Test]
        public void Raise_CreatesAPartyHuntingTheQuarry_AtTheFallback_WhenNoCampExists()
        {
            War war = AtWarWithAParty();
            NpcGroup party = sim.FindGroup(war.PartyGroupId);

            Assert.IsNotNull(party);
            Assert.AreEqual(Player, party.QuarryProfileId);
            Assert.AreEqual(0, party.Tier);
            Assert.AreEqual(250f + 50f + 100f, new Vector2(party.Position.x, party.Position.z).magnitude, 0.5f);
            Assert.IsTrue(party.HasLead);
            Assert.LessOrEqual(new Vector2(party.Lead.x, party.Lead.z).magnitude, 80.001f);
        }

        [Test]
        public void Raise_UsesTheNearestCamp_WhenNobodyCanSeeIt()
        {
            WorldSiteRegistry.Register(SiteKind.Camp, new Vector3(700f, 0f, 0f), 10f, "Camp");

            War war = AtWarWithAParty();

            Assert.AreEqual(700f, sim.FindGroup(war.PartyGroupId).Position.x, 0.01f);
        }

        [Test]
        public void Defeated_Credits12_RaisesTheTier_AndTheWarGoesOn()
        {
            War war = AtWarWithAParty();
            string partyId = war.PartyGroupId;

            Invoke(director, "Resolve", war, Reckoning.Defeated);

            Assert.AreEqual(-73f, ledger.ValueFor(sand, Player), 0.001f);
            Assert.AreEqual(1, war.Tier);
            Assert.IsNull(sim.FindGroup(partyId), "a folded party is removed at once");
            Assert.AreSame(war, director.Book.Find(sand, Player));
            Assert.AreEqual(180f, war.Cooldown);
        }

        [Test]
        public void Caught_Credits30_AndEndsTheWar()
        {
            War war = AtWarWithAParty();

            Invoke(director, "Resolve", war, Reckoning.Caught);

            Assert.AreEqual(-55f, ledger.ValueFor(sand, Player), 0.001f);
            Assert.AreEqual(GoodwillBand.HostileOnSight, ledger.BandFor(sand, Player));
            Assert.IsNull(director.Book.Find(sand, Player));
            Assert.AreEqual(0, director.WarTierFor(sand, Player), "peace resets escalation");
        }

        [Test]
        public void Abandoned_ChangesNothing_ButTheParty()
        {
            War war = AtWarWithAParty();
            string partyId = war.PartyGroupId;

            Invoke(director, "Resolve", war, Reckoning.Abandoned);

            Assert.AreEqual(-85f, ledger.ValueFor(sand, Player), 0.001f);
            Assert.AreEqual(0, war.Tier);
            Assert.IsNull(sim.FindGroup(partyId));
        }

        [Test]
        public void EscalationCaps_AtTheRostersLastTier()
        {
            War war = AtWarWithAParty();
            for (int i = 0; i < 5; i++)
            {
                Move(-24f);   // keep the war going despite the credits
                if (!war.HasParty) Invoke(director, "Raise", war, Vector3.zero);
                Invoke(director, "Resolve", war, Reckoning.Defeated);
            }

            Assert.AreEqual(2, war.Tier);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — type-check → `WarPartyDirector` does not exist.

- [ ] **Step 3: Write `WarPartyDirector.cs`**

```csharp
// Decides when a tribe's war with one player has a war party in the field, and settles the score
// every time one is resolved.
//
// The director decides WHEN a party exists; NpcWorldSim moves it. Kept apart so the world sim does not
// become a catch-all. Server-only, on the NpcWorldSim object beside FactionGoodwillLedger.
//
// Design: docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md §5–§7.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.World;
using Random = UnityEngine.Random;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcWorldSim))]
    public class WarPartyDirector : MonoBehaviour
    {
        public static WarPartyDirector Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [SerializeField] private WarPartySettings settings = WarPartySettings.Default;

        [Tooltip("Seconds between decisions. Kept below the world sim's tick so a wiped-out party is " +
                 "resolved before anything else looks at it.")]
        [SerializeField] private float decisionInterval = 0.5f;

        [Tooltip("Directions tried for a fallback origin before accepting one a player might see.")]
        [SerializeField] private int fallbackAttempts = 8;

        private sealed class QuarryWatch
        {
            public HealthComponent Health;
            public Action Handler;
        }

        private readonly WarBook book = new();
        private readonly Dictionary<War, QuarryWatch> watches = new();

        // Wars whose quarry has been in the game this session. A quarry absent since load is still
        // binding; one that was here and is gone has left, and only then is their party released.
        private readonly HashSet<War> present = new();

        private readonly List<Vector3> playerPositions = new();
        private readonly HashSet<FactionDefinition> reportedMissingTemplate = new();

        private NpcWorldSim sim;
        private FactionGoodwillLedger subscribedLedger;
        private float timer;

        public WarBook Book => book;

        private float Staging => WarPartyRules.StagingDistance(sim.SpawnRadius, settings.stagingMargin);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[WarParty] A second WarPartyDirector. One world, one director.", this);
                enabled = false;
                return;
            }

            Instance = this;
            sim = GetComponent<NpcWorldSim>();
        }

        // Start rather than OnEnable: the ledger on this same object may not have run Awake yet.
        private void Start()
        {
            subscribedLedger = FactionGoodwillLedger.Instance;
            if (subscribedLedger != null) subscribedLedger.BandChanged += OnBandChanged;
            if (sim != null) sim.QuarrySighted += OnQuarrySighted;
        }

        private void OnDestroy()
        {
            if (subscribedLedger != null) subscribedLedger.BandChanged -= OnBandChanged;
            if (sim != null) sim.QuarrySighted -= OnQuarrySighted;

            foreach (War war in book.Snapshot()) Unwatch(war);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!Network.Decides) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            float elapsed = decisionInterval - timer;
            timer = decisionInterval;
            Step(elapsed);
        }

        private void Step(float elapsed)
        {
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null || sim == null) return;

            // Adopt before reconciling (spec §7): reconciling first would see "at war, no party" after
            // a load and raise a second party beside the restored one.
            AdoptRestoredParties();
            Reconcile(ledger);

            book.Tick(elapsed);
            sim.CollectPlayerPositions(playerPositions);

            foreach (War war in book.Snapshot())
                StepWar(war);
        }

        // ── Saved escalation (FactionGoodwillSaveable) ───────────────────────────

        public int WarTierFor(FactionDefinition tribe, string profileId) => book.TierFor(tribe, profileId);

        public void RestoreWarTier(FactionDefinition tribe, string profileId, int tier) =>
            book.RestoreTier(tribe, profileId, tier);

        // ── Wars opening and closing ─────────────────────────────────────────────

        private void OnBandChanged(FactionDefinition tribe, string profileId, GoodwillBand previous, GoodwillBand next)
        {
            // A null profile is a client's mirror updating: a picture of a decision, not one.
            if (!Network.Decides || string.IsNullOrEmpty(profileId)) return;

            if (next == GoodwillBand.AtWar) book.Open(tribe, profileId);
            else if (previous == GoodwillBand.AtWar) EndWar(book.Find(tribe, profileId));
        }

        /// <summary>
        /// RestoreRow raises no event, so after a load the ledger can say "at war" without the director
        /// ever hearing it. Only a bound quarry's war is ended here: an unbound player's rows have not
        /// been restored yet, and their Wary default is not an answer.
        /// </summary>
        private void Reconcile(FactionGoodwillLedger ledger)
        {
            foreach ((FactionDefinition tribe, string profileId, float _, GoodwillBand band) in ledger.All())
                if (band == GoodwillBand.AtWar) book.Open(tribe, profileId);

            foreach (War war in book.Snapshot())
            {
                if (!TryGetQuarry(war.ProfileId, out _)) continue;
                if (ledger.BandFor(war.Tribe, war.ProfileId) != GoodwillBand.AtWar) EndWar(war);
            }
        }

        private void EndWar(War war)
        {
            if (war == null) return;

            if (war.HasParty) sim.ReleaseGroup(war.PartyGroupId);
            Unwatch(war);
            present.Remove(war);
            book.Close(war);

            Notify(war, WarNotice.GaveUp);
        }

        private void AdoptRestoredParties()
        {
            foreach (NpcGroup group in new List<NpcGroup>(sim.Groups))
            {
                if (!group.IsWarParty || book.FindByGroup(group.Id) != null) continue;

                FactionDefinition tribe = sim.TribeOf(group);
                if (tribe == null) continue;

                War existing = book.Find(tribe, group.QuarryProfileId);
                if (existing != null && existing.HasParty && sim.FindGroup(existing.PartyGroupId) != null)
                {
                    Debug.LogWarning($"[WarParty] '{group.Id}' is a second party for the war that " +
                                     $"'{existing.PartyGroupId}' is already fighting; disbanding it.", this);
                    sim.DisbandGroup(group.Id);
                    continue;
                }

                book.Adopt(tribe, group.QuarryProfileId, group.Id, group.Tier);
            }
        }

        // ── One war ──────────────────────────────────────────────────────────────

        private void StepWar(War war)
        {
            if (book.Find(war.Tribe, war.ProfileId) != war) return;   // closed earlier this step

            if (!TryGetQuarry(war.ProfileId, out GameObject quarry))
            {
                // Here earlier and gone now: they left. The war waits in their save (spec §6); their
                // party does not wait in the world, and nothing is resolved.
                if (present.Remove(war) && war.HasParty)
                {
                    sim.ReleaseGroup(war.PartyGroupId);
                    book.ClearParty(war, settings.partyCooldown);
                }

                Unwatch(war);
                return;
            }

            present.Add(war);
            Watch(war, quarry);

            Vector3 quarryPosition = quarry.transform.position;

            if (!war.HasParty)
            {
                if (book.ReadyToRaise(war)) Raise(war, quarryPosition);
                return;
            }

            NpcGroup party = sim.FindGroup(war.PartyGroupId);
            if (party == null)
            {
                book.ClearParty(war, settings.partyCooldown);
                return;
            }

            if (WarPartyRules.IsDefeated(party.FightersSpawned, party.FightersDead, party.WipedOut))
            {
                Resolve(war, Reckoning.Defeated);
                return;
            }

            if (WarPartyRules.ShouldAbandon(party.Position, quarryPosition, settings.maxPursuitDistance))
            {
                Resolve(war, Reckoning.Abandoned);
                return;
            }

            if (!party.Spawned) Track(party, quarryPosition);
        }

        private void Raise(War war, Vector3 quarryPosition)
        {
            NpcGroupTemplate template = sim.WarPartyTemplateFor(war.Tribe);
            if (template == null)
            {
                if (reportedMissingTemplate.Add(war.Tribe))
                    Debug.LogError($"[WarParty] {war.Tribe.factionName} is at war but the NpcWorldSim has no " +
                                   "war-party template for it (runtimeOnly, bountyHunters, tribe set). Run " +
                                   "Tools/SpaceGame/Agents/Wire War Party Templates.", this);

                book.ClearParty(war, settings.partyCooldown);
                return;
            }

            string id = book.AssignParty(war, taken => sim.FindGroup(taken) != null);
            NpcGroup party = sim.CreateGroup(template, id, ChooseOrigin(quarryPosition));
            if (party == null)
            {
                book.ClearParty(war, settings.partyCooldown);
                return;
            }

            party.QuarryProfileId = war.ProfileId;
            party.Tier = war.Tier;
            party.Lead = WarPartyRules.TrailFix(quarryPosition, Random.insideUnitCircle, settings.trailFuzz);
            party.HasLead = true;
            party.LeadAge = 0f;

            Notify(war, WarNotice.Raised);
        }

        /// <summary>The nearest camp if nobody can see it, else a point beyond staging nobody can see.</summary>
        private Vector3 ChooseOrigin(Vector3 quarryPosition)
        {
            float staging = Staging;

            if (WorldSiteRegistry.TryFindNearest(SiteKind.Camp, quarryPosition, settings.campSearchRadius, out WorldSite camp)
                && WarPartyRules.IsUnobserved(camp.Position, playerPositions, staging))
                return camp.Position;

            Vector3 origin = quarryPosition;
            for (int attempt = 0; attempt < Mathf.Max(1, fallbackAttempts); attempt++)
            {
                origin = WarPartyRules.FallbackOrigin(quarryPosition, Random.insideUnitCircle, staging + settings.fallbackExtra);
                if (WarPartyRules.IsUnobserved(origin, playerPositions, staging)) break;
            }

            return origin;
        }

        /// <summary>Refresh a folded party's fuzzy fix on its quarry and catch it up unseen (spec §5.2).</summary>
        private void Track(NpcGroup party, Vector3 quarryPosition)
        {
            if (!party.HasLead || party.LeadAge >= settings.trailInterval)
            {
                party.Lead = WarPartyRules.TrailFix(quarryPosition, Random.insideUnitCircle, settings.trailFuzz);
                party.HasLead = true;
                party.LeadAge = 0f;
            }

            float staging = Staging;
            if (WarPartyRules.TryCatchUp(party.Position, party.Lead, staging + settings.trailFuzz,
                                         playerPositions, staging, out Vector3 moved))
                party.Position = moved;
        }

        private void Resolve(War war, Reckoning outcome)
        {
            sim.ReleaseGroup(war.PartyGroupId);

            int maxTier = war.Tribe.roster != null ? war.Tribe.roster.MaxTier : 0;
            book.Resolve(war, outcome, maxTier, settings.partyCooldown);

            // After the book: a credit that ends the war closes it through BandChanged, which must find
            // the party already settled.
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger != null) ledger.Credit(war.Tribe, war.ProfileId, WarPartyRules.CreditFor(outcome, settings));

            if (outcome == Reckoning.Defeated && book.Find(war.Tribe, war.ProfileId) == war)
                Notify(war, WarNotice.Weakening);
        }

        // ── Caught ───────────────────────────────────────────────────────────────

        private void Watch(War war, GameObject quarry)
        {
            HealthComponent health = quarry.GetComponentInChildren<HealthComponent>();
            if (watches.TryGetValue(war, out QuarryWatch current) && current.Health == health) return;

            Unwatch(war);
            if (health == null) return;

            var watch = new QuarryWatch { Health = health };
            watch.Handler = () => OnQuarryDied(war, watch.Health);
            health.OnDeath += watch.Handler;
            watches[war] = watch;
        }

        private void Unwatch(War war)
        {
            if (!watches.TryGetValue(war, out QuarryWatch watch)) return;

            if (watch.Health != null) watch.Health.OnDeath -= watch.Handler;
            watches.Remove(war);
        }

        /// <summary>Caught only if THIS party dealt the blow; a Clanker, a fall or another tribe is not a reckoning.</summary>
        private void OnQuarryDied(War war, HealthComponent health)
        {
            if (!Network.Decides || health == null || health.IsRestoring) return;
            if (book.Find(war.Tribe, war.ProfileId) != war || !war.HasParty) return;

            if (WarPartyRules.IsCaughtBy(GroupMembership.GroupIdOf(health.LastDamageSource), war.PartyGroupId))
                Resolve(war, Reckoning.Caught);
        }

        // ── Telling people ───────────────────────────────────────────────────────

        private void OnQuarrySighted(NpcGroup group, GameObject fighter)
        {
            FactionDefinition tribe = sim.TribeOf(group);
            DialogPool pool = tribe != null && tribe.roster != null ? tribe.roster.hostileLines : null;
            if (pool == null || pool.lines == null || pool.lines.Length == 0) return;

            if (fighter != null && fighter.TryGetComponent(out ChatterModule chatter))
                chatter.WarCry(Random.Range(0, pool.lines.Length));
        }

        private static void Notify(War war, WarNotice notice)
        {
            if (TryGetQuarry(war.ProfileId, out GameObject quarry)
                && quarry.TryGetComponent(out FactionGoodwillNetwork network))
                network.Notify(war.Tribe, notice);
        }

        private static bool TryGetQuarry(string profileId, out GameObject quarry)
        {
            quarry = null;
            PlayerSaveService players = SaveManager.Instance != null ? SaveManager.Instance.Players : null;
            return players != null && players.TryGetBoundPlayer(profileId, out quarry);
        }

        private void OnValidate()
        {
            decisionInterval = Mathf.Clamp(decisionInterval, 0.1f, 5f);
            fallbackAttempts = Mathf.Max(1, fallbackAttempts);
            settings.partyCooldown = Mathf.Max(0f, settings.partyCooldown);
            settings.maxPursuitDistance = Mathf.Max(settings.campSearchRadius, settings.maxPursuitDistance);
            settings.trailInterval = Mathf.Max(1f, settings.trailInterval);
            settings.trailFuzz = Mathf.Max(0f, settings.trailFuzz);
        }
    }
}
```

Before writing, confirm three assumptions with grep and adjust if wrong: the player's `HealthComponent` is on the networked player root or a child (`GetComponentInChildren` covers both); `FactionGoodwillNetwork` is on the player root (it is on `PlayerCharacterNetworked.prefab`); `SaveManager.Players` is a public property.

- [ ] **Step 4: Run `WarPartyDirectorTests`** → 7 passed. Re-run `FactionGoodwillLedgerTests`, `WarBookTests`, `RuntimeGroupTests` → green.

- [ ] **Step 5: Add the director to the scene through the wiring menu** — at the end of the `WithWorldSim` lambda in `RosterAuthoring.WireWorldSim`, before `so.ApplyModifiedPropertiesWithoutUndo();` is fine, or after it:

```csharp
                if (sim.GetComponent<WarPartyDirector>() == null)
                    sim.gameObject.AddComponent<WarPartyDirector>();
```

Run the menu again, then:

```bash
grep -c "SpaceGame.Agents.WarPartyDirector" Assets/Game/Scenes/world/persistentScene.unity
```

Expected: `1`. It must sit on the same GameObject as `NpcWorldSim` and `FactionGoodwillLedger`.

- [ ] **Step 6: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/agents/World/WarPartyDirector.cs* Assets/Game/Editor/Agents/RosterAuthoring.cs Assets/Game/Editor/Tests/WarPartyDirectorTests.cs*
git add -p Assets/Game/Scenes/world/persistentScene.unity
git commit -m "feat(factions): war-party director raises, tracks and settles war parties"
```

---

### Task 10: Persistence — escalation saved with the player, restored parties adopted

**Files:**
- Modify: `Assets/Game/Scripts/Core/Persistence/Adapters/FactionGoodwillSaveable.cs`
- Test: `Assets/Game/Editor/Tests/WarPartyPersistenceTests.cs`

**Interfaces:**
- Consumes: Task 9 `WarPartyDirector.Instance`, `WarTierFor`, `RestoreWarTier`, private `AdoptRestoredParties`/`Reconcile`.
- Produces: `FactionGoodwillSaveable.Standing.warTier : int` (appended).

- [ ] **Step 1: Write the failing tests**

```csharp
// Rosters spec §7: a party that was out when the game saved comes back as the same party and is never
// doubled; the tier a war had escalated to survives a quit during the cooldown.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class WarPartyPersistenceTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Player = "profile-a";

        private readonly List<Object> junk = new();
        private FactionGoodwillLedger ledger;
        private NpcWorldSim sim;
        private WarPartyDirector director;
        private FactionDefinition sand;

        [SetUp]
        public void SetUp()
        {
            sand = ScriptableObject.CreateInstance<FactionDefinition>();
            sand.ID = sand.factionName = "Sand";
            junk.Add(sand);

            var ledgerGo = new GameObject("Ledger");
            junk.Add(ledgerGo);
            ledger = ledgerGo.AddComponent<FactionGoodwillLedger>();
            var so = new SerializedObject(ledger);
            so.FindProperty("tribes").arraySize = 1;
            so.FindProperty("tribes").GetArrayElementAtIndex(0).objectReferenceValue = sand;
            so.ApplyModifiedPropertiesWithoutUndo();
            Invoke(ledger, "Awake");

            var simGo = new GameObject("Sim");
            junk.Add(simGo);
            sim = simGo.AddComponent<NpcWorldSim>();
            var template = new NpcGroupTemplate { id = "sand-war-party", tribe = sand, runtimeOnly = true, bountyHunters = true };
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Invoke(sim, "Awake");

            director = simGo.AddComponent<WarPartyDirector>();
            Invoke(director, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private static object Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        private NpcGroup RestoreParty(string id, int tier)
        {
            var saved = new NpcGroup { Id = id, TemplateId = "sand-war-party", QuarryProfileId = Player, Tier = tier, RosterSeed = 7 };
            sim.RestoreRecords(new[] { saved.ToRecord() });
            return sim.FindGroup(id);
        }

        [Test]
        public void ARestoredParty_IsAdopted_NotDoubled()
        {
            RestoreParty("warparty:Sand:profile-a:3", 2);

            Invoke(director, "AdoptRestoredParties");
            Invoke(director, "AdoptRestoredParties");

            War war = director.Book.Find(sand, Player);
            Assert.AreEqual(1, director.Book.Wars.Count);
            Assert.AreEqual("warparty:Sand:profile-a:3", war.PartyGroupId);
            Assert.AreEqual(2, war.Tier);
            Assert.IsFalse(director.Book.ReadyToRaise(war), "the restored party IS this war's party");
            Assert.AreEqual(7, sim.FindGroup(war.PartyGroupId).RosterSeed, "same people, same guns");
        }

        [Test]
        public void ASecondPartyForTheSameWar_IsDisbanded()
        {
            RestoreParty("warparty:Sand:profile-a:1", 0);
            Invoke(director, "AdoptRestoredParties");

            NpcGroup duplicate = sim.CreateGroup(sim.WarPartyTemplateFor(sand), "warparty:Sand:profile-a:2", Vector3.zero);
            duplicate.QuarryProfileId = Player;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("second party"));

            Invoke(director, "AdoptRestoredParties");

            Assert.IsNull(sim.FindGroup("warparty:Sand:profile-a:2"));
            Assert.IsNotNull(sim.FindGroup("warparty:Sand:profile-a:1"));
        }

        [Test]
        public void Reconcile_DoesNotEndTheWar_OfAPlayerWhoHasNotBoundYet()
        {
            RestoreParty("warparty:Sand:profile-a:1", 1);
            Invoke(director, "AdoptRestoredParties");

            // The world restores before the player does: the ledger has no row for them yet.
            Invoke(director, "Reconcile", ledger);

            Assert.IsNotNull(director.Book.Find(sand, Player));
            Assert.IsNotNull(sim.FindGroup("warparty:Sand:profile-a:1"));
        }

        [Test]
        public void Reconcile_OpensAWar_ForARestoredAtWarRow()
        {
            ledger.RestoreRow(sand, Player, -90f, GoodwillBand.AtWar);

            Invoke(director, "Reconcile", ledger);

            Assert.IsNotNull(director.Book.Find(sand, Player));
        }

        [Test]
        public void WarTier_RoundTripsOnTheStanding()
        {
            var standing = new FactionGoodwillSaveable.Standing
            {
                factionId = "Sand", value = -85f, band = GoodwillBand.AtWar, warTier = 2,
            };

            JObject json = JObject.FromObject(standing, SaveSerializer.Serializer);
            var back = json.ToObject<FactionGoodwillSaveable.Standing>(SaveSerializer.Serializer);

            Assert.AreEqual(2, back.warTier);
        }

        [Test]
        public void WarTier_FromAnOlderSave_IsZero()
        {
            var back = JObject.Parse("{\"factionId\":\"Sand\",\"value\":-85.0,\"band\":0}")
                .ToObject<FactionGoodwillSaveable.Standing>(SaveSerializer.Serializer);

            Assert.AreEqual(0, back.warTier);
        }

        [Test]
        public void ARestoredTier_IsWhatTheNextWarStartsAt()
        {
            director.RestoreWarTier(sand, Player, 2);
            ledger.RestoreRow(sand, Player, -90f, GoodwillBand.AtWar);

            Invoke(director, "Reconcile", ledger);

            Assert.AreEqual(2, director.Book.Find(sand, Player).Tier);
        }
    }
}
```

Check `GoodwillBand`'s numeric value for `AtWar` before trusting `"band":0` in the old-save test; use the right integer.

- [ ] **Step 2: Run to verify it fails** — type-check → `Standing` has no `warTier`.

- [ ] **Step 3: Extend `FactionGoodwillSaveable`**

Append to `struct Standing`, after `band`:

```csharp
            /// <summary>
            /// How far this tribe's war with this player had escalated (rosters spec §7). Saved here,
            /// beside the band it belongs to, because between war parties it lives only in the
            /// director's memory — and a reload that reset it would send scouts after a player who had
            /// already beaten two parties. Appended 2026-09-16; older saves read 0.
            /// </summary>
            public int warTier;
```

In `CaptureState`, before the loop: `WarPartyDirector director = WarPartyDirector.Instance;`. Inside the loop, replace the skip and the `Add`:

```csharp
                int warTier = director != null ? director.WarTierFor(faction, profileId) : 0;

                // A row sitting at neutral with no war says exactly what a missing row says. Skipping it
                // keeps a save from growing an entry per tribe per player who has never met either.
                if (Mathf.Approximately(value, GoodwillMath.Neutral) && band == GoodwillBand.Wary && warTier == 0)
                    continue;

                standings.Add(new Standing { factionId = faction.ID, value = value, band = band, warTier = warTier });
```

In `RestoreState`, before the loop: `WarPartyDirector director = WarPartyDirector.Instance;`. After `ledger.RestoreRow(...)`:

```csharp
                if (director != null) director.RestoreWarTier(faction, profileId, standing.warTier);
```

Update the file header's last paragraph: the restore also hands the war tier to the director, and the director reconciles the restored band into a war on its next decision (no event is raised by `RestoreRow`).

- [ ] **Step 4: Run `WarPartyPersistenceTests`** → 7 passed. Re-run `FactionGoodwillLedgerTests`, `GroupRecordTests`, `RuntimeGroupTests`, `WarPartyDirectorTests` → green. Run `PrefabPersistenceTests` if it exists, to catch saver wiring regressions.

- [ ] **Step 5: Commit** (only if authorised)

```bash
git add Assets/Game/Scripts/Core/Persistence/Adapters/FactionGoodwillSaveable.cs Assets/Game/Editor/Tests/WarPartyPersistenceTests.cs*
git commit -m "feat(persistence): war tier saved with the player; restored parties adopted"
```

---

### Task 11: Documentation, full verification and the client check

**Files:**
- Modify: `docs/AI/systems/AgentSystem.md` (world sim + factions sections, Gotchas, `symptoms:`, `updated:`)
- Modify: `docs/AI/systems/Persistence.md` (the `npcworld` record fields; `factionGoodwill` `warTier`)
- Modify: `.claude/skills/spacegame-persistence/SKILL.md` (the goodwill row: `warTier`)
- Modify: `.claude/skills/spacegame-agent/SKILL.md` (a short "Rosters and war parties" note: roles, `runtimeOnly` templates, never hand-edit `candidates`, re-run the two menus)
- Modify: `docs/superpowers/plans/2026-09-07-faction-system.md` (status table: Phase 4 sub-project 1)
- Regenerate: `docs/AI/INDEX.md`, `docs/AI/ROUTING.md`

- [ ] **Step 1: Read the governing docs first** — `grep -n 'agents/World\|agents/faction' docs/AI/ROUTING.md`, then read each named doc's Model, Multiplayer, Persistence and Gotchas sections in full.

- [ ] **Step 2: Update `AgentSystem.md`** — add, in the doc's own shape:
  - **Model:** a tribe's `FactionRoster`; roster-drawn members; runtime groups; `WarPartyDirector` (decides when) vs `NpcWorldSim` (moves); `GroupMembership`.
  - **Flows:** at war → war opens → party raised at a camp or fallback → folded catch-up to `staging + trailFuzz` → spawn → Caught / Defeated / Abandoned → credit, tier, cooldown → next party or peace.
  - **Multiplayer:** director server-only; membership stamped before spawn so the loadout roll is seeded; war cry = `AgentAction.WarCry` with a line index; notices host-local or targeted Rpc (not `Send`'s skip-the-host rule); faction is not replicated, hence the roster validation rule.
  - **Gotchas:**
    - `NpcSpawn.Create`'s `beforeSpawn` is the only place a value read in `OnNetworkSpawn` can be set.
    - A war party's mount stays Fauna; the tribe goes on the rider.
    - `RestoreRow` raises no event, so wars after a load come from `Reconcile`, which must not end an unbound player's war.
    - Never hand-edit a nomad's `NpcRandomLoadout.candidates`: `RosterAssetTests` fails; re-run the builder.
    - A runtime group's template must stay in the sim's `templates` list, or its saved record is dropped on load.
  - Delete any sentence that says caravans roll weapons "afresh" or that bounty hunters are the only hunters.
  - Also fix the one code comment the spec made untrue: `GoodwillBand.AtWar`'s summary in `GoodwillMath.cs` says "their caravans route toward your last known position" — replace with "a war party is sent after you (WarPartyDirector); ordinary caravans shoot on sight but keep travelling". Grep `Assets/Game/Scripts` for `route toward` to catch any other copy.
  - `symptoms:` entries: `"a caravan comes back with different guns after walking out of range"`, `"two war parties hunt one player after loading"`, `"no war party ever comes although a tribe is at war"`, `"killing the war party hunting me still costs goodwill"`.
  - Bump `updated:` to the day of the change.

- [ ] **Step 3: Update `Persistence.md` and the two skills** as listed above.

- [ ] **Step 4: Update the faction plan's status table** — Phase 4 row: sub-project 1 done (date), with evidence (`FactionRoster`, `SandTribe.asset`, `WarPartyDirector`, test fixture names), carrying its client-check debt until Step 7 is done.

- [ ] **Step 5: Regenerate and validate docs**

Run: `python tools/docs_check.py --index`
Expected: `0 errors, 0 warnings`.

- [ ] **Step 6: Full verification**

1. `python tools/typecheck.py --editor` → `No errors.`
2. Full EditMode run (`Tools ▸ Tests ▸ Run EditMode Tests (headless)`) → compare failures against the pre-existing list (Ostrich, AgentCarry, ArmAim, Hogtie, PackSize, BackpackNetworking). Any **new** failure blocks the task.
3. `git status --short` — confirm no `SkyCity*` / `sky_city*` file was touched by this work.

- [ ] **Step 7: Play checklist, host AND a client** (MPPM clone or the batch autotest). CLAUDE.md: a feature seen only on the host is not finished. Record each result.

1. Push a tribe to `AtWar` (shoot Sand nomads) → the warning notice appears on the hunted player's own screen (test once as host, once as client) and a party rides out from a camp.
2. Flee beyond 1500 m → the party gives up; after 180 s a new party rises nearer you.
3. Wipe a party out → "resolve weakening"; the next party is tier 1 (3 Warriors + 1 Scout).
4. Get killed by the party → the war ends; "given up" appears; the party leaves only once out of sight.
5. Save and reload mid-hunt → the same party continues; no second party appears.
6. Two players at war with Sand → two parties.
7. A crewmate helping fight the party loses no goodwill (compare the ledger value before and after).
8. Quit and reload during a cooldown → the next party is still the escalated tier.
9. Walk away from a Sand caravan until it folds, walk back → the same members carry the same weapons, on the client too.
10. The first-sight war cry pops on the client as well as the host.

If Unity MCP is not connected, stop here and ask the user to reconnect it or run the checklist; do not mark the task done on the host alone.

- [ ] **Step 8: Commit** (only if authorised)

```bash
git add docs/AI/systems/AgentSystem.md docs/AI/systems/Persistence.md docs/AI/INDEX.md docs/AI/ROUTING.md .claude/skills/spacegame-persistence/SKILL.md .claude/skills/spacegame-agent/SKILL.md docs/superpowers/plans/2026-09-07-faction-system.md
git commit -m "docs: rosters and war parties"
```
