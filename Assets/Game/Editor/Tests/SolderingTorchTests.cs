// The soldering torch is a complete artifact: a registered, saveable, pickable prefab whose item lives where the registry
// scans, a harmless tool that NPCs do not read as a weapon, a blue flame, and one on the ship's gear wall.
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class SolderingTorchTests
    {
        private static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(SolderingTorchBuilder.PrefabPath);
        private static InventoryItem Item => AssetDatabase.LoadAssetAtPath<InventoryItem>(SolderingTorchBuilder.ItemPath);

        [Test]
        public void TheTorchIsAWholeArtifact()
        {
            GameObject prefab = Prefab;
            Assert.IsNotNull(prefab, "no torch prefab — run Tools/SpaceGame/Items/Build Soldering Torch.");
            Assert.IsNotNull(prefab.GetComponent<SolderingTorchArtifact>(), "the prefab has no torch on it.");
            Assert.AreNotEqual(0u, prefab.GetComponent<NetworkObject>().PrefabIdHash, "a hash of 0 can never spawn on a client.");
            Assert.IsNotNull(prefab.GetComponent<SaveableEntity>(), "a dropped torch would vanish on reload.");
            Assert.IsNotNull(prefab.GetComponent<ItemGrip>(), "the torch has no grip, so it floats in the hand.");
            Assert.IsNotNull(prefab.GetComponentInChildren<CryoSprayerNozzle>(true), "the torch has no flame rig.");
            Assert.IsNotNull(prefab.GetComponentInChildren<FlameFlicker>(true), "the flame lights nothing.");

            InventoryItem item = Item;
            Assert.IsNotNull(item, "the item is not under Resources/Items, so it never registers.");
            Assert.AreEqual(prefab, item.itemPrefab, "the item does not point at the torch.");
            Assert.IsFalse(item.menacing, "a jeweller's torch reads as a weapon to every NPC.");
            Assert.IsNotNull(item.icon, "the torch has no inventory icon.");
        }

        [Test]
        public void ClientsCanSpawnIt()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset");
            Assert.IsTrue(list.Contains(Prefab), "a dropped torch would exist for the host alone.");
        }

        [Test]
        public void TheFlameIsBlueWhite()
        {
            var flame = AssetDatabase.LoadAssetAtPath<Material>(SolderingTorchBuilder.TorchFlamePath);
            Assert.IsNotNull(flame, "no torch flame material.");
            Color edge = flame.GetColor("_EdgeColor");
            Assert.Greater(edge.b, edge.r, "the torch burns orange like a campfire, not blue like gas.");
        }

        [Test]
        public void OneTorchHangsOnTheShipsGearWall()
        {
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(SolderingTorchBuilder.GearWallPath).GetComponent<WallInventory>();
            SerializedProperty stocked = new SerializedObject(wall).FindProperty("startingMainItems");
            int torches = Enumerable.Range(0, stocked.arraySize).Count(i => stocked.GetArrayElementAtIndex(i).objectReferenceValue == Item);
            Assert.AreEqual(1, torches, "the crew cannot solder the oxygen plant without a torch, and need only one.");
        }
    }
}
