// The Striders' three crewed dune barges are prefab variants of the dune-barge art prefabs: these read
// the built variants back and check each is an NPC hull the agent brain drives through its
// TrackedHullMotor, crewed from the open roof, while the dune barges themselves stay untouched.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Motors;

namespace SpaceGame.EditorTools
{
    public class StriderBargeTests
    {
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        // A post this far above the roof it was measured on still stands on it.
        private const float DeckTolerance = 0.3f;

        private static IEnumerable<string> Variants() => StriderBargeBuilder.Barges.Select(b => b.Variant);

        private static GameObject Barge(string variant)
        {
            var barge = AssetDatabase.LoadAssetAtPath<GameObject>(StriderBargeBuilder.PrefabPath(variant));
            Assert.IsNotNull(barge, "run Tools/SpaceGame/Vehicles/Build Strider Barges");
            return barge;
        }

        private static string BasePath(string variant) =>
            DuneBargeBuilder.PrefabPath(StriderBargeBuilder.Barges.Single(b => b.Variant == variant).Base);

        [TestCaseSource(nameof(Variants))]
        public void IsAVariantOfItsDuneBarge_WhichStaysUncrewed(string variant)
        {
            GameObject barge = Barge(variant);
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(barge));
            Assert.AreEqual(BasePath(variant), AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(barge)));

            var original = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath(variant));
            Assert.IsNull(original.GetComponent<AgentController>(), "the dune barge itself is nobody's");
            Assert.IsNull(original.GetComponent<CrewShift>());
        }

        [TestCaseSource(nameof(Variants))]
        public void TheAgentBrain_DrivesIt_ThroughItsTrackedHullMotor(string variant)
        {
            GameObject barge = Barge(variant);
            var motor = barge.GetComponent<TrackedHullMotor>();
            Assert.IsNotNull(motor);
            Assert.AreEqual(1, barge.GetComponents<IMovementMotor>().Length, "one mover: the motor");
            Assert.AreSame(motor, new SerializedObject(barge.GetComponent<AgentController>()).FindProperty("MotorComponent").objectReferenceValue);
            Assert.IsNotNull(barge.GetComponent<NetAuthority>(), "without it every client runs its own motor");
            Assert.IsTrue(barge.GetComponent<Rigidbody>().isKinematic, "the motor places the hull; physics must not");

            Assert.IsNull(barge.GetComponent<MountModule>(), "nobody takes the helm of a Strider barge");
            Assert.IsNull(barge.GetComponent<HealthComponent>(), "indestructible, like the houses: never a fighter");

            var formation = new SerializedObject(barge.GetComponent<FormationModule>());
            Assert.AreEqual(ModulePriority.Social, formation.FindProperty("priority").intValue);
            Assert.IsEmpty(formation.FindProperty("formationId").stringValue);
            Assert.IsTrue(formation.FindProperty("holdSlotAtRest").boolValue, "a barge keeps its place in the column at a stop");
            Assert.Greater(formation.FindProperty("regroupDistance").floatValue, RosterAuthoring.CityFarthestSlot,
                           "a slot it could never hold (rebuild after Wire Strider City changes the column)");
        }

        [TestCaseSource(nameof(Variants))]
        public void RidesOnItsTracks_WhoseContactsAreNamed(string variant)
        {
            GameObject barge = Barge(variant);
            Transform contacts = barge.transform.Find(StriderBargeBuilder.TrackContactsName);
            Assert.IsNotNull(contacts, "the dust emitters attach to these");
            Assert.GreaterOrEqual(contacts.childCount, 4, "front and rear of each main track");

            var motor = new SerializedObject(barge.GetComponent<TrackedHullMotor>());
            float rideHeight = motor.FindProperty("rideHeight").floatValue;
            Vector2 centre = motor.FindProperty("footprintCenter").vector2Value;
            Vector2 size = motor.FindProperty("footprintSize").vector2Value;
            Assert.Greater(rideHeight, 0f, "the pivot rides above the track bottoms");

            foreach (Transform contact in contacts)
            {
                Vector3 p = contact.localPosition;
                Assert.AreEqual(-rideHeight, p.y, 0.01f, $"{contact.name} is where the track meets the ground");
                Assert.LessOrEqual(Mathf.Abs(p.x - centre.x), size.x * 0.5f + 0.01f, $"{contact.name} is outside the footprint");
                Assert.LessOrEqual(Mathf.Abs(p.z - centre.y), size.y * 0.5f + 0.01f, $"{contact.name} is outside the footprint");
            }
        }

        [TestCaseSource(nameof(Variants))]
        public void EveryCrewPost_StandsOnTheOpenRoof_OutsideEveryRoom(string variant)
        {
            GameObject prefab = Barge(variant);
            Assert.AreEqual(StriderBargeBuilder.CrewPosts, prefab.GetComponent<VesselSeats>().Capacity);

            var reveal = new SerializedObject(prefab.GetComponent<InteriorReveal>());
            Assert.Greater(reveal.FindProperty("interiorRenderers").arraySize, 0, "the interior is still drawn only on entry");
            SerializedProperty volumes = reveal.FindProperty("localVolumes");

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var barge = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Physics.SyncTransforms();
                PhysicsScene physics = scene.GetPhysicsScene();
                SerializedProperty seats = new SerializedObject(barge.GetComponent<VesselSeats>()).FindProperty("seats");
                for (int i = 0; i < seats.arraySize; i++)
                {
                    var post = (Transform)seats.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.IsTrue(physics.Raycast(post.position + Vector3.up, Vector3.down, out RaycastHit hit, 1f + DeckTolerance,
                                                  ~0, QueryTriggerInteraction.Ignore), $"{post.name}: no deck under it");
                    Assert.IsTrue(hit.collider.transform.IsChildOf(barge.transform), $"{post.name} stands on {hit.collider.name}");
                    Assert.IsFalse(physics.Raycast(post.position + Vector3.up * 0.1f, Vector3.up, 50f, ~0, QueryTriggerInteraction.Ignore),
                                   $"{post.name} is under a roof, not on the open deck");

                    Vector3 local = barge.transform.InverseTransformPoint(post.position);
                    for (int v = 0; v < volumes.arraySize; v++)
                        Assert.IsFalse(volumes.GetArrayElementAtIndex(v).boundsValue.Contains(local), $"{post.name} is inside room {v}");
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCaseSource(nameof(Variants))]
        public void ItsGangway_IsOnTheGround_BesideTheHull(string variant)
        {
            GameObject barge = Barge(variant);
            var shift = barge.GetComponent<CrewShift>();
            Assert.IsNotNull(shift);
            var gangway = new SerializedObject(shift).FindProperty("gangway").objectReferenceValue as Transform;
            Assert.IsNotNull(gangway);

            var motor = new SerializedObject(barge.GetComponent<TrackedHullMotor>());
            Assert.AreEqual(-motor.FindProperty("rideHeight").floatValue, gangway.localPosition.y, 0.01f, "at the tracks' feet, not on deck");
            float halfWidth = motor.FindProperty("footprintSize").vector2Value.x * 0.5f;
            Assert.Greater(Mathf.Abs(gangway.localPosition.x), halfWidth, "beside the tracks, not under them");
        }

        [TestCaseSource(nameof(Variants))]
        public void IsARegisteredStrider_WithItsOwnNetworkIdentity(string variant)
        {
            GameObject barge = Barge(variant);
            var faction = new SerializedObject(barge.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
            Assert.AreEqual(1, barge.GetComponents<EntityFaction>().Length);

            var net = barge.GetComponent<NetworkObject>();
            Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.AreNotEqual(AssetDatabase.LoadAssetAtPath<GameObject>(BasePath(variant)).GetComponent<NetworkObject>().PrefabIdHash,
                               net.PrefabIdHash, "a variant needs its own network identity");
            Assert.IsTrue(net.SceneMigrationSynchronization, "a spawned barge must tell clients when it drives into another chunk scene");
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath).Contains(barge),
                          "the world sim spawns it on the server; clients never see it unregistered");
        }
    }
}
