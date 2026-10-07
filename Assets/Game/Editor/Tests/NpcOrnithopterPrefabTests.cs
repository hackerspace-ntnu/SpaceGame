// Assets/Game/Editor/Tests/NpcOrnithopterPrefabTests.cs
// The NPC craft as written to disk by NpcOrnithopterBuilder: the player's craft with its flight motor,
// mount and every saver taken off, flown by FlyingRigidbodyMotor + NpcAviator, its wings fed by
// NpcOrnithopterWings, and a registered network prefab of its own.
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.EditorTools;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class NpcOrnithopterPrefabTests
    {
        private const string PlayerCraftPath = "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing — run Tools/SpaceGame/Vehicles/Build NPC Ornithopter");
            return prefab;
        }

        [Test]
        public void TheNpcCraft_FliesOnTheSimpleMotor_WithNoMountAndNoSavers()
        {
            GameObject craft = Load(NpcOrnithopterBuilder.PrefabPath);

            Assert.IsNull(craft.GetComponent<OrnithopterFlightMotor>(), "NPCs never fly the player's energy model");
            Assert.IsNotNull(craft.GetComponent<FlyingRigidbodyMotor>());
            Assert.IsNull(craft.GetComponent<MountModule>(), "a player could take the NPC's craft (D7)");
            Assert.IsNull(craft.GetComponent<MountNetworkSync>());
            Assert.IsNull(craft.GetComponent<SteerModule>());
            Assert.IsNull(craft.GetComponent<SaveableEntity>(), "an NPC craft must never be saved (D4)");
            Assert.IsEmpty(craft.GetComponents<ISaveable>(), "a saver survived on the NPC craft");
            Assert.IsNotNull(craft.GetComponent<NpcAviator>());
            Assert.AreEqual(1, craft.GetComponent<VesselSeats>().Capacity);
            Assert.IsFalse(SaveablePolicy.NeedsSaving(craft, out _));
        }

        [Test]
        public void TheNpcCraft_PicksNoTargetsOfItsOwn_SoItNeedsNoFaction()
        {
            GameObject craft = Load(NpcOrnithopterBuilder.PrefabPath);
            SerializedProperty acquires = new SerializedObject(craft.GetComponent<AgentController>()).FindProperty("acquiresTargets");

            Assert.IsFalse(acquires?.boolValue ?? true,
                           "the craft's controller adds an AgentTargeting, which warns that it has no EntityFaction on every launch");
            Assert.IsNull(craft.GetComponent<AgentTargeting>());
        }

        [Test]
        public void TheNpcCraft_ClimbsBriskly_UnderVerticalInput()
        {
            var motor = new SerializedObject(Load(NpcOrnithopterBuilder.PrefabPath).GetComponent<FlyingRigidbodyMotor>());
            Assert.AreEqual(NpcOrnithopterBuilder.MaxVerticalSpeed, motor.FindProperty("maxVerticalSpeed").floatValue, 0.001f);
        }

        [Test]
        public void TheNpcCraftsWings_AreFedByThePresenter()
        {
            GameObject craft = Load(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(craft.GetComponent<OrnithopterWingAnimator>());
            Assert.IsInstanceOf<NpcOrnithopterWings>(craft.GetComponent<IOrnithopterFlightState>(),
                                                    "the wing animator would bind nothing and the wings never beat");
        }

        [Test]
        public void TheNpcCraft_IsARegisteredNetworkPrefab_DistinctFromThePlayersCraft()
        {
            uint npc = Load(NpcOrnithopterBuilder.PrefabPath).GetComponent<NetworkObject>().PrefabIdHash;
            uint player = Load(PlayerCraftPath).GetComponent<NetworkObject>().PrefabIdHash;

            Assert.AreNotEqual(0u, npc, "a zero hash is dropped by NGO for every peer but the host");
            Assert.AreNotEqual(player, npc);
            Assert.IsTrue(NetworkPrefabRegistrar.IsRegistered(NpcOrnithopterBuilder.PrefabPath));
        }
    }
}
