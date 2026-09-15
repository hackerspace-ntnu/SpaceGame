// The built Wrist Blade, read off disk. Each assertion is one way the builder can write a prefab
// that looks complete and does nothing in play: an item that is not a Gauntlet lands in the hand
// instead of on the forearm; a blade the artifact cannot find never moves; damage that drifted
// from fifty is not the item that was asked for; a missing network entry fails on clients only.
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class WristBladeTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/WristBlade.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/WristBlade.asset";
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";

        private GameObject prefab;
        private InventoryItem item;

        [SetUp]
        public void SetUp()
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (prefab == null || item == null)
                Assert.Ignore("The Wrist Blade has not been built (Tools > Build Wrist Blade Artifact).");
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
        public void TheBladeIsWiredAndDoesFiftyDamage()
        {
            var artifact = prefab.GetComponent<WristBladeArtifact>();
            Assert.IsNotNull(artifact);
            var so = new SerializedObject(artifact);

            Assert.AreEqual(50, so.FindProperty("damage").intValue);

            SerializedProperty parts = so.FindProperty("bladeParts");
            Assert.AreEqual(1, parts.arraySize, "one blade, sliding by one offset");
            var blade = parts.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
            Assert.IsNotNull(blade, "the blade object was not found in the FBX");
            Assert.AreEqual("Mesh_RetractBlade_Straight", blade.name);

            Vector3 axis = so.FindProperty("bladeAxis").vector3Value;
            Assert.Greater(axis.magnitude, 0.5f, "a slide axis of zero never moves the blade");
            Assert.Greater(so.FindProperty("bladeThrow").floatValue, 0.5f, "the throw is the blade's length: over half a metre of steel, not a letter opener");
            Assert.Greater(so.FindProperty("bladeDelay").floatValue, 0.3f, "the steel waits for the arm to lock");
            Assert.Greater(so.FindProperty("bladeOutTime").floatValue, 0.1f, "the slide is meant to be seen");

            Assert.IsNotNull(so.FindProperty("mouthSparks").objectReferenceValue, "sparks at the mouth");
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
