// Who Generate makes of the characters it placed: special characters first, beds filled in order, a shared
// dwelling is family, nobody without a bed is lost (they camp where they stand), and the same settlement
// always makes the same people.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents.Tests
{
    public class ResidentAssignmentTests
    {
        private readonly List<Object> made = new();
        private Settlement settlement;
        private SettlementCulture culture;
        private ResidentArchetype villager, elder;
        private Dwelling pairHouse, singleHouse;

        [SetUp]
        public void SetUp()
        {
            villager = Make(ScriptableObject.CreateInstance<ResidentArchetype>());
            villager.name = "Villager";
            elder = Make(ScriptableObject.CreateInstance<ResidentArchetype>());
            elder.name = "Elder";
            culture = Make(ScriptableObject.CreateInstance<SettlementCulture>());
            culture.names = new[] { "Asha", "Bekir", "Dasra", "Emru" };
            culture.archetypes = new[] { villager };

            var config = Make(ScriptableObject.CreateInstance<SettlementConfig>());
            config.culture = culture;
            var root = Make(new GameObject("settlement"));
            root.transform.position = new Vector3(120f, 0f, -40f);
            settlement = root.AddComponent<Settlement>();
            var so = new SerializedObject(settlement);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();

            var generated = new GameObject("Generated").transform;
            generated.SetParent(root.transform, false);
            pairHouse = House(generated, "PairHouse", 2);
            singleHouse = House(generated, "SingleHouse", 1);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        [Test]
        public void SpecialsFirst_BedsInOrder_SharedDwellingIsFamily_TheRestCamp()
        {
            Resident special = Body("Quest giver", "Old Mara");
            Resident second = Body("A"), third = Body("B"), homeless = Body("C");
            homeless.transform.position = new Vector3(3f, 0f, 4f);

            Assign(special, elder, second, third, homeless);

            Assert.AreEqual(0, special.index);
            Assert.AreEqual("Old Mara", special.displayName, "a special character keeps its prefab's name");
            Assert.AreEqual(elder, special.archetype, "and the archetype the config gives it");
            Assert.AreEqual(villager, second.archetype);
            Assert.AreEqual(pairHouse, special.home);
            Assert.AreEqual(pairHouse, second.home);
            Assert.AreEqual(singleHouse, third.home);
            Assert.IsNull(homeless.home, "more people than beds: the rest sleep in the open");
            Assert.AreEqual(homeless.transform.position, homeless.campPosition);
            Assert.IsTrue(special.IsBondedTo(second) && second.IsBondedTo(special), "one dwelling is one family");
            Assert.AreEqual(settlement, third.settlement);
            CollectionAssert.AllItemsAreUnique(new[] { second.displayName, third.displayName, homeless.displayName });
        }

        [Test]
        public void TheSameSettlement_MakesTheSamePeople()
        {
            Resident a = Body("A"), b = Body("B");
            Assign(null, null, a, b);
            (string nameA, int seedA) = (a.displayName, a.seed);

            Resident again = Body("A again"), other = Body("B again");
            Assign(null, null, again, other);

            Assert.AreEqual(nameA, again.displayName);
            Assert.AreEqual(seedA, again.seed);
        }

        [Test]
        public void ACharacterWithAProfile_IsDrawnFromIt_WhenOneOfItsArchetypesIsUsable()
        {
            ResidentArchetype hauler = Make(ScriptableObject.CreateInstance<ResidentArchetype>());
            hauler.name = "Hauler";
            ResidentArchetype smith = Make(ScriptableObject.CreateInstance<ResidentArchetype>());
            smith.name = "Smith";
            smith.post = Make(ScriptableObject.CreateInstance<SpotUse>());   // no spot of it here: never usable
            culture.archetypes = new[] { villager, hauler, smith };
            GameObject strongman = Make(new GameObject("Strongman prefab")), wanderer = Make(new GameObject("Wanderer prefab"));
            culture.profiles = new[]
            {
                new CharacterProfile { prefab = strongman, suits = new[] { hauler } },
                new CharacterProfile { prefab = wanderer, suits = new[] { smith } },
            };

            Resident a = Body("A"), b = Body("B"), c = Body("C");
            ResidentAssignment.Assign(settlement, culture, new List<ResidentAssignment.Newcomer>
            {
                new(a.gameObject, null, false, strongman),
                new(b.gameObject, null, false, strongman),
                new(c.gameObject, null, false, wanderer),
            }, new[] { pairHouse, singleHouse });

            Assert.AreEqual(hauler, a.archetype, "the profile picks among its usable archetypes");
            Assert.AreEqual(hauler, b.archetype, "even when that archetype is already taken");
            Assert.AreNotEqual(smith, c.archetype, "an unusable profile falls back to the settlement's archetypes");
            Assert.IsNotNull(c.archetype);
        }

        [Test]
        public void ACharacterMadeForARole_KeepsIt_WhateverTheSharesOrProfilesSay()
        {
            ResidentArchetype miner = Make(ScriptableObject.CreateInstance<ResidentArchetype>());
            miner.name = "Miner";
            GameObject minerPrefab = Make(new GameObject("Miner prefab"));
            culture.profiles = new[] { new CharacterProfile { prefab = minerPrefab, suits = new[] { villager } } };

            Resident a = Body("A"), b = Body("B");
            a.archetype = miner;   // the instance of a prefab that names its role
            ResidentAssignment.Assign(settlement, culture, new List<ResidentAssignment.Newcomer>
            {
                new(a.gameObject, null, false, minerPrefab),
                new(b.gameObject, null, false, minerPrefab),
            }, new[] { pairHouse, singleHouse });

            Assert.AreEqual(miner, a.archetype, "a role the body came with is not re-dealt, even outside the culture's list");
            Assert.AreEqual(villager, b.archetype, "a body without one is still handed a role");
        }

        [Test]
        public void ARoleIsHostedOnlyWhereItsPlaceExists()
        {
            SpotUse mineFace = Make(ScriptableObject.CreateInstance<SpotUse>());
            mineFace.role = SpotRole.Work;
            ResidentArchetype miner = Make(ScriptableObject.CreateInstance<ResidentArchetype>());
            miner.post = mineFace;

            Assert.IsFalse(ResidentAssignment.Places.Of(settlement).CanHost(miner), "no mine face: no miner");
            Assert.IsTrue(ResidentAssignment.Places.Of(settlement).CanHost(villager), "no post: always");

            var spot = new GameObject("Mine face").AddComponent<SettlementSpot>();
            spot.transform.SetParent(settlement.GeneratedRoot, false);
            var so = new SerializedObject(spot);
            so.FindProperty("use").objectReferenceValue = mineFace;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(ResidentAssignment.Places.Of(settlement).CanHost(miner), "a mine face: a miner");
        }

        [Test]
        public void Assign_StampsSourcePrefab_ForEveryResident()
        {
            GameObject elderPrefab = Make(new GameObject("Elder prefab")), commonPrefab = Make(new GameObject("Common prefab"));
            Resident special = Body("Quest giver", "Old Mara");
            Resident a = Body("A"), b = Body("B"), homeless = Body("C");

            ResidentAssignment.Assign(settlement, culture, new List<ResidentAssignment.Newcomer>
            {
                new(special.gameObject, elder, true, elderPrefab),
                new(a.gameObject, null, false, commonPrefab),
                new(b.gameObject, null, false, commonPrefab),
                new(homeless.gameObject, null, false, elderPrefab),
            }, new[] { pairHouse, singleHouse });

            Assert.AreEqual(elderPrefab, special.sourcePrefab, "a special character keeps the prefab it was placed from");
            Assert.AreEqual(commonPrefab, a.sourcePrefab);
            Assert.AreEqual(commonPrefab, b.sourcePrefab, "two copies of one prefab both name it");
            Assert.AreEqual(elderPrefab, homeless.sourcePrefab, "and so does a resident without a bed");
        }

        private void Assign(Resident special, ResidentArchetype specialArchetype, params Resident[] everyone)
        {
            var newcomers = new List<ResidentAssignment.Newcomer>();
            if (special != null) newcomers.Add(new ResidentAssignment.Newcomer(special.gameObject, specialArchetype, true));
            foreach (Resident r in everyone)
                if (r != null) newcomers.Add(new ResidentAssignment.Newcomer(r.gameObject, null, false));
            ResidentAssignment.Assign(settlement, culture, newcomers, new[] { pairHouse, singleHouse });
        }

        private Resident Body(string name, string presetName = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(settlement.transform, false);
            Resident resident = go.AddComponent<Resident>();
            resident.displayName = presetName;
            return resident;
        }

        private Dwelling House(Transform parent, string name, int beds)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var dwelling = go.AddComponent<Dwelling>();
            var so = new SerializedObject(dwelling);
            so.FindProperty("beds").intValue = beds;
            so.ApplyModifiedPropertiesWithoutUndo();
            return dwelling;
        }

        private T Make<T>(T o) where T : Object
        {
            made.Add(o);
            return o;
        }
    }
}
