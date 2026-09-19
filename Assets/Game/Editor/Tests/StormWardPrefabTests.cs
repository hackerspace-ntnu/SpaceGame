// The built storm ward, read off disk: the pair conserves (placing it and picking it up gives back
// the same item), and the placed half actually has a shockwave to show and a ward to run.
using NUnit.Framework;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class StormWardPrefabTests
    {
        private GameObject held;
        private GameObject placed;
        private InventoryItem asset;

        [SetUp]
        public void SetUp()
        {
            held = AssetDatabase.LoadAssetAtPath<GameObject>(StormWardBuilder.HeldPath);
            placed = AssetDatabase.LoadAssetAtPath<GameObject>(StormWardBuilder.PlacedPath);
            asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(StormWardBuilder.AssetPath);
            if (held == null || placed == null || asset == null)
                Assert.Ignore("The storm ward has not been built (Tools > Items > Build Storm Ward).");
        }

        [Test]
        public void PlacingAndPickingUpGivesBackTheSameItem()
        {
            Assert.AreSame(held, asset.itemPrefab);

            var rule = new SerializedObject(held.GetComponent<GroundPlacement>());
            Assert.AreSame(placed, rule.FindProperty("placedPrefab").objectReferenceValue);

            Assert.AreSame(asset, placed.GetComponent<PlacedObject>().ReturnItem);
        }

        [Test]
        public void ThePlacedWardHasItsShockwaveAndFlash()
        {
            var ward = new SerializedObject(placed.GetComponent<StormWard>());
            var wave = (ParticleSystem)ward.FindProperty("shockwave").objectReferenceValue;
            var flash = (ParticleSystem)ward.FindProperty("flash").objectReferenceValue;

            Assert.IsNotNull(wave, "shockwave");
            Assert.IsNotNull(flash, "flash");
            Assert.IsFalse(wave.main.playOnAwake, "the ward plays it on its pulse, not on spawn");
            Assert.AreEqual(ParticleSystemShapeType.Circle, wave.shape.shapeType);
        }

        [Test]
        public void TheRingStrikesAndCarriesTheFlash()
        {
            var ward = new SerializedObject(placed.GetComponent<StormWard>());
            var head = (Transform)ward.FindProperty("head").objectReferenceValue;
            var flash = (ParticleSystem)ward.FindProperty("flash").objectReferenceValue;

            Assert.IsNotNull(head, "head");
            Assert.IsTrue(flash.transform.IsChildOf(head), "the flash must ride the ring's stroke");
        }
    }
}
