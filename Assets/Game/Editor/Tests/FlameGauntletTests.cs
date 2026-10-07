// The built Flame Gauntlet, read off disk. Each assertion is one way the builder can write a
// prefab that looks complete and is a different item in play: a lance that is not committed
// stops when the finger lifts; a tank that need not be full hands back a half-burst; a jet with
// no particle rig burns invisibly; a missing network entry fails on clients only.
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class FlameGauntletTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlameGauntlet.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/FlameGauntlet.asset";
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        /// <summary>The burst the item was asked for. The builder sizes the tank to it.</summary>
        private const float BurstSeconds = 3f;

        private GameObject prefab;
        private InventoryItem item;

        [SetUp]
        public void SetUp()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (prefab == null || item == null)
                Assert.Ignore("The Flame Gauntlet has not been built (Tools > Build Flame Gauntlet Artifact).");
        }

        [Test]
        public void TheItemIsAGauntletThatPointsAtItsPrefabAndBack()
        {
            Assert.AreEqual(EquipKind.Gauntlet, item.equipKind, "worn on the forearm, fired on that arm's key");
            Assert.AreEqual(prefab, item.itemPrefab);
            Assert.IsFalse(string.IsNullOrEmpty(item.ID), "a null id breaks the item registry in builds");

            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            Assert.IsNotNull(pickup, "cannot be picked back up");
            Assert.AreEqual(item, new SerializedObject(pickup).FindProperty("item").objectReferenceValue);
        }

        [Test]
        public void OnePressIsOneThreeSecondBurst()
        {
            var artifact = prefab.GetComponent<FlamethrowerArtifact>();
            Assert.IsNotNull(artifact);
            var so = new SerializedObject(artifact);
            Assert.IsTrue(so.FindProperty("commitToBurst").boolValue, "the trigger must not end the burst early");

            var tank = so.FindProperty("tank").objectReferenceValue as SupplyReservoir;
            Assert.IsNotNull(tank, "no tank, no burst length");
            var tankSo = new SerializedObject(tank);
            float drain = tankSo.FindProperty("drainPerSecond").floatValue;
            Assert.Greater(drain, 0f);
            Assert.AreEqual(BurstSeconds, 1f / drain, 0.01f, "a full tank is one burst");
            Assert.AreEqual(1f, tankSo.FindProperty("restartFraction").floatValue, 1e-4f,
                            "must be full to light, or a press hands back whatever is left");
            Assert.Greater(tankSo.FindProperty("refillPerSecond").floatValue, 0f, "it has to come back");
        }

        [Test]
        public void TheFireIsAttached()
        {
            var artifact = prefab.GetComponent<FlamethrowerArtifact>();
            var so = new SerializedObject(artifact);

            var jet = so.FindProperty("jet").objectReferenceValue as FlameJet;
            Assert.IsNotNull(jet, "no FlameJet wired");
            var jetSo = new SerializedObject(jet);
            foreach (string field in new[] { "jetRoot", "flame", "embers", "smoke", "pilot", "flameLight" })
                Assert.IsNotNull(jetSo.FindProperty(field).objectReferenceValue, $"FlameJet.{field} is empty");

            var muzzle = so.FindProperty("muzzle").objectReferenceValue as Transform;
            Assert.IsNotNull(muzzle, "no muzzle: the flame leaves from the wrist");
            // The model puts the mouth 7 cm forward of the wrist joint on the bore axis, 29 cm
            // above the deck: forward of the hand and up off the forearm, not in the fist.
            Assert.Greater(muzzle.localPosition.z, 0.05f, "the muzzle sits forward of the wrist, on the prefab's forward");
            Assert.Greater(muzzle.localPosition.y, 0.2f, "the muzzle sits on the bore axis above the deck");
            Assert.IsNotNull(so.FindProperty("groundFirePrefab").objectReferenceValue, "the fire leaves no ground fire");
        }

        [Test]
        public void ItIsWornNetworkedAndSaved()
        {
            Assert.IsNotNull(prefab.GetComponent<ItemGrip>());
            Assert.IsNotNull(prefab.GetComponent<GauntletFit>(), "seated on the forearm like every gauntlet");
            Assert.IsNotNull(prefab.GetComponent<NetworkObject>());
            Assert.IsNotNull(prefab.GetComponent<SpaceGame.Core.Persistence.SaveableEntity>());

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsTrue(list.Contains(prefab), "an unregistered item fails to drop on clients");
        }
    }
}
