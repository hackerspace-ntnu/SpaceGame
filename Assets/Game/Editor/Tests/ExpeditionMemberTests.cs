// A stand-in is replicated as a key, a name and four indices, and every machine resolves them the same way: the kit
// from catalog -> profile -> goal -> row, the archetype from the profile's culture, the bag from the kit with the
// weapon first. An index that names nothing resolves to nothing, never to a neighbour.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ExpeditionMemberTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void DestroyMade()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        [Test]
        public void Nobody_IsNoStandIn()
        {
            Assert.IsFalse(StandInIdentity.Nobody.IsStandIn, "a resident at home has no key");
            Assert.AreEqual(StandInIdentity.None, StandInIdentity.Nobody.kit);
        }

        [Test]
        public void AnIdentityWithAKey_IsAStandIn_AndHasNoNullText()
        {
            var identity = new StandInIdentity("r:12", null, 0, 0, 3, 1);

            Assert.IsTrue(identity.IsStandIn);
            Assert.AreEqual(string.Empty, identity.displayName, "a missing name is empty, never null: it is sent as a FixedString");
            Assert.IsFalse(new StandInIdentity(null, "Rasha", 0, 0, 0, 0).IsStandIn, "no key, no stand-in");
        }

        [Test]
        public void KitAt_ResolvesCatalogProfileGoalAndRow()
        {
            ExpeditionProfile[] profiles = Profiles(out ExpeditionKit warrior, out ExpeditionKit scout);

            Assert.AreSame(warrior, ExpeditionMember.KitAt(profiles, 1, 0, 0));
            Assert.AreSame(scout, ExpeditionMember.KitAt(profiles, 1, 0, 1));
        }

        [Test]
        public void KitAt_IsNullWhenAnyIndexNamesNothing()
        {
            ExpeditionProfile[] profiles = Profiles(out _, out _);

            Assert.IsNull(ExpeditionMember.KitAt(profiles, StandInIdentity.None, 0, 0), "no profile");
            Assert.IsNull(ExpeditionMember.KitAt(profiles, 2, 0, 0), "past the catalog");
            Assert.IsNull(ExpeditionMember.KitAt(profiles, 1, 1, 0), "past the goals");
            Assert.IsNull(ExpeditionMember.KitAt(profiles, 1, 0, StandInIdentity.None), "a member whose slot has no kit row");
            Assert.IsNull(ExpeditionMember.KitAt(profiles, 1, 0, 2), "past the kit rows");
            Assert.IsNull(ExpeditionMember.KitAt(profiles, 0, 0, 0), "a profile with no goals");
        }

        [Test]
        public void GoalIndexOf_FindsTheGoalByItsSavedId()
        {
            ExpeditionProfile profile = Profiles(out _, out _)[1];

            Assert.AreEqual(0, ExpeditionMember.GoalIndexOf(profile, "scout"));
            Assert.AreEqual(StandInIdentity.None, ExpeditionMember.GoalIndexOf(profile, "hunt"));
            Assert.AreEqual(StandInIdentity.None, ExpeditionMember.GoalIndexOf(null, "scout"));
        }

        [Test]
        public void ArchetypeAt_ReadsTheProfilesCulture()
        {
            ExpeditionProfile profile = Profiles(out _, out _)[1];
            var guard = Make<ResidentArchetype>("Guard");
            var scout = Make<ResidentArchetype>("Scout");
            profile.culture = Make<SettlementCulture>("Culture");
            profile.culture.archetypes = new[] { guard, scout };

            Assert.AreSame(scout, ExpeditionMember.ArchetypeAt(profile, 1));
            Assert.IsNull(ExpeditionMember.ArchetypeAt(profile, 2), "past the archetypes");
            Assert.IsNull(ExpeditionMember.ArchetypeAt(profile, StandInIdentity.None), "a resident whose archetype the culture does not list");

            profile.culture = null;
            Assert.IsNull(ExpeditionMember.ArchetypeAt(profile, 0), "a profile without its culture");
        }

        [Test]
        public void Contents_PutsTheWeaponFirst_ThenTheTool_ThenTheBelt()
        {
            ExpeditionKit kit = Kit("Kit");
            kit.tool = Make<InventoryItem>("Spyglass");
            var horn = Make<InventoryItem>("Horn");
            kit.beltItems = new[] { null, horn };

            CollectionAssert.AreEqual(new[] { kit.weapon, kit.tool, horn }, ExpeditionMember.Contents(kit),
                                      "the weapon is drawn from slot 0 and takes its belt anchor first; empty belt rows are skipped");
            CollectionAssert.IsEmpty(ExpeditionMember.Contents(null));
        }

        // Two profiles in the catalog's order: one with no goals, then one with a scout goal of two kit rows.
        private ExpeditionProfile[] Profiles(out ExpeditionKit warrior, out ExpeditionKit scout)
        {
            warrior = Kit("Kit_Warrior");
            scout = Kit("Kit_Scout");

            var goal = Make<ExpeditionGoal>("Goal_Scout");
            goal.id = "scout";
            goal.kits = new[]
            {
                new RoleKit { role = ExpeditionRole.Warrior, kit = warrior },
                new RoleKit { role = ExpeditionRole.Scout, kit = scout },
            };

            var empty = Make<ExpeditionProfile>("Empty");
            var scouting = Make<ExpeditionProfile>("Scouting");
            scouting.goals = new[] { goal };
            return new[] { empty, scouting };
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
