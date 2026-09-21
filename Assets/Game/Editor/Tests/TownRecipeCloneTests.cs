// Moving settings between the component and a shared recipe asset must actually copy them.
//
// The failure this guards is silent in the worst way: a clone that came back empty, or one that
// still shared its group lists with the original, looks completely fine in the inspector. You would
// find out weeks later, when editing one town turned out to have been editing three.
//
// EditorJsonUtility rather than JsonUtility is the part worth pinning — only the editor one
// round-trips the prefab and material references, and getting that wrong would give you a recipe
// whose prefab slots are all empty.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.World.Towns;

namespace SpaceGame.EditorTools
{
    public class TownRecipeCloneTests
    {
        private GameObject hut;

        [SetUp]
        public void SetUp() => hut = new GameObject("Hut");

        [TearDown]
        public void TearDown()
        {
            if (hut != null) Object.DestroyImmediate(hut);
        }

        private TownRecipe Authored()
        {
            var recipe = new TownRecipe { innerRadius = 11f, midRadius = 22f, outerRadius = 33f };
            recipe.buildings.Add(new TownGroup
            {
                label = "huts",
                prefabs = new[] { hut },
                count = new Vector2Int(3, 7),
                reservesClearance = true,
            });

            return recipe;
        }

        [Test]
        public void ACloneCarriesTheValues()
        {
            TownRecipe copy = TownGeneratorEditor.Clone(Authored());

            Assert.AreEqual(11f, copy.innerRadius);
            Assert.AreEqual(33f, copy.outerRadius);
            Assert.AreEqual(1, copy.buildings.Count);
            Assert.AreEqual("huts", copy.buildings[0].label);
            Assert.AreEqual(new Vector2Int(3, 7), copy.buildings[0].count);
            Assert.IsTrue(copy.buildings[0].reservesClearance);
        }

        [Test]
        public void ACloneKeepsItsPrefabReferences()
        {
            // The whole point of EditorJsonUtility. A plain JsonUtility round trip would hand back
            // a group whose prefab slot is empty, and the recipe would quietly place nothing.
            TownRecipe copy = TownGeneratorEditor.Clone(Authored());

            Assert.AreEqual(1, copy.buildings[0].prefabs.Length);
            Assert.AreSame(hut, copy.buildings[0].prefabs[0]);
        }

        [Test]
        public void ACloneSharesNothingWithTheOriginal()
        {
            TownRecipe source = Authored();
            TownRecipe copy = TownGeneratorEditor.Clone(source);

            copy.buildings[0].count = new Vector2Int(99, 99);
            copy.buildings.Add(new TownGroup { label = "added later" });

            Assert.AreEqual(new Vector2Int(3, 7), source.buildings[0].count, "the group was shared");
            Assert.AreEqual(1, source.buildings.Count, "the list was shared");
        }

        [Test]
        public void CloningNothingGivesADefaultRecipeRatherThanThrowing()
        {
            TownRecipe copy = TownGeneratorEditor.Clone(null);

            Assert.IsNotNull(copy);
            Assert.IsNotNull(copy.buildings);
        }
    }
}
