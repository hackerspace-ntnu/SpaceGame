// The expedition data holds together: every shipped profile validates clean, and the validator catches
// what would otherwise break a band silently at runtime (too few warriors, an unarmed kit, a profile
// whose index cannot be replicated because the catalog does not list it).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ExpeditionAssetTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void DestroyMade()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        [Test]
        public void ShippedProfiles_HaveNoProblems()
        {
            var catalog = Resources.Load<ExpeditionCatalog>(ExpeditionCatalog.ResourcePath);
            Assert.IsNotNull(catalog, $"no catalog at Resources/{ExpeditionCatalog.ResourcePath}");
            Assert.IsNotEmpty(catalog.profiles, "the catalog lists no profile, so no settlement runs bands");

            foreach (ExpeditionProfile profile in catalog.profiles)
            {
                Assert.IsNotNull(profile, "the catalog holds an empty entry");
                List<string> problems = ExpeditionValidation.Problems(profile, catalog);
                Assert.IsEmpty(problems, $"{profile.name}:\n{string.Join("\n", problems)}");
            }
        }

        [Test]
        public void Validation_FlagsGoalWithOneWarrior()
        {
            RoleSlot[] twoWarriors = { Slot(ExpeditionRole.Warrior, 2, 2), Slot(ExpeditionRole.Scout, 1, 2) };
            Assert.IsEmpty(ExpeditionValidation.SlotProblems("scout", twoWarriors, _ => true));

            RoleSlot[] oneWarrior = { Slot(ExpeditionRole.Warrior, 1, 1), Slot(ExpeditionRole.Scout, 2, 2) };
            List<string> problems = ExpeditionValidation.SlotProblems("scout", oneWarrior, _ => true);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("Warrior", problems[0]);
        }

        [Test]
        public void Validation_FlagsAnyAdultStandingInOnAWarriorSlot()
        {
            RoleSlot scoutStandIns = Slot(ExpeditionRole.Scout, 1, 2);
            scoutStandIns.fillFromAnyAdult = true;
            Assert.IsEmpty(ExpeditionValidation.SlotProblems("scout", new[] { Slot(ExpeditionRole.Warrior, 2, 2), scoutStandIns }, _ => true),
                           "any adult may stand in for a scout");

            RoleSlot warriorStandIns = Slot(ExpeditionRole.Warrior, 2, 2);
            warriorStandIns.fillFromAnyAdult = true;
            List<string> problems = ExpeditionValidation.SlotProblems("scout", new[] { warriorStandIns, Slot(ExpeditionRole.Scout, 1, 2) }, _ => true);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("Warrior slots take only Warriors", problems[0]);
        }

        [Test]
        public void Validation_FlagsRoleQuotaNoArchetypeCanMeet()
        {
            RoleQuota Quota(ExpeditionRole role) => new RoleQuota { role = role, small = new Vector2Int(1, 2), large = 3 };
            bool OnlyScouts(ExpeditionRole role) => role == ExpeditionRole.Scout;

            Assert.IsEmpty(ExpeditionValidation.QuotaProblems(new[] { Quota(ExpeditionRole.Scout) }, OnlyScouts));
            StringAssert.Contains("no archetype", ExpeditionValidation.QuotaProblems(new[] { Quota(ExpeditionRole.Healer) }, OnlyScouts).Single());
            StringAssert.Contains("warrior quota", ExpeditionValidation.QuotaProblems(new[] { Quota(ExpeditionRole.Warrior) }, _ => true).Single());
            StringAssert.Contains("exactly one role",
                ExpeditionValidation.QuotaProblems(new[] { Quota(ExpeditionRole.Scout | ExpeditionRole.Hunter) }, _ => true).Single());
            StringAssert.Contains("repeats",
                ExpeditionValidation.QuotaProblems(new[] { Quota(ExpeditionRole.Scout), Quota(ExpeditionRole.Scout) }, OnlyScouts).Single());
        }

        [Test]
        public void Validation_FlagsKitWithoutWeapon()
        {
            ExpeditionProfile profile = ValidProfile(out ExpeditionCatalog catalog);
            Assert.IsEmpty(ExpeditionValidation.Problems(profile, catalog), "the fixture itself must be clean");

            ExpeditionKit unarmed = profile.goals[0].kits[1].kit;
            unarmed.weapon = null;

            List<string> problems = ExpeditionValidation.Problems(profile, catalog);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains(unarmed.name, problems[0]);
            StringAssert.Contains("weapon", problems[0]);
        }

        [Test]
        public void Validation_FlagsProfileMissingFromCatalog()
        {
            ExpeditionProfile profile = ValidProfile(out ExpeditionCatalog catalog);
            Assert.IsEmpty(ExpeditionValidation.Problems(profile, catalog), "the fixture itself must be clean");

            catalog.profiles = new ExpeditionProfile[0];

            List<string> problems = ExpeditionValidation.Problems(profile, catalog);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("ExpeditionCatalog", problems[0]);
        }

        [Test]
        public void Validation_FlagsMusterUseThatIsNoAssembly()
        {
            ExpeditionProfile profile = ValidProfile(out ExpeditionCatalog catalog);
            profile.musterUse.role = SpotRole.Leisure;   // the day planner would send strollers there

            List<string> problems = ExpeditionValidation.Problems(profile, catalog);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("Assembly", problems[0]);
        }

        [Test]
        public void Validation_FlagsProfileWithoutItsCulture()
        {
            ExpeditionProfile profile = ValidProfile(out ExpeditionCatalog catalog);
            SettlementCulture culture = profile.culture;

            profile.culture = null;   // a stand-in could not look up its archetype
            List<string> problems = ExpeditionValidation.Problems(profile, catalog);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains("culture", problems[0]);

            profile.culture = culture;
            culture.expeditions = null;   // the link must go both ways
            problems = ExpeditionValidation.Problems(profile, catalog);
            Assert.AreEqual(1, problems.Count, string.Join("\n", problems));
            StringAssert.Contains(culture.name, problems[0]);
        }

        private static RoleSlot Slot(ExpeditionRole role, int min, int max) => new RoleSlot { role = role, min = min, max = max };

        // A profile with one Scout goal that breaks no rule, listed in its own catalog.
        private ExpeditionProfile ValidProfile(out ExpeditionCatalog catalog)
        {
            var goal = Make<ExpeditionGoal>("Goal_Test");
            goal.slots = new[] { Slot(ExpeditionRole.Warrior, 2, 2), Slot(ExpeditionRole.Scout, 1, 2) };
            goal.stages = new[] { new StageSpec { kind = StageKind.Travel }, new StageSpec { kind = StageKind.ReturnHome } };
            goal.kits = new[]
            {
                new RoleKit { role = ExpeditionRole.Warrior, kit = Kit("Kit_TestWarrior") },
                new RoleKit { role = ExpeditionRole.Scout, kit = Kit("Kit_TestScout") },
            };

            var profile = Make<ExpeditionProfile>("TestExpeditions");
            profile.goals = new[] { goal };
            profile.musterUse = Make<SpotUse>("TestMuster");
            profile.musterUse.role = SpotRole.Assembly;
            profile.culture = Make<SettlementCulture>("TestCulture");
            profile.culture.expeditions = profile;

            catalog = Make<ExpeditionCatalog>("TestCatalog");
            catalog.profiles = new[] { profile };
            return profile;
        }

        private ExpeditionKit Kit(string name)
        {
            var kit = Make<ExpeditionKit>(name);
            kit.weapon = Make<InventoryItem>(name + "_Weapon");
            return kit;
        }

        private T Make<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            made.Add(asset);
            return asset;
        }
    }
}
