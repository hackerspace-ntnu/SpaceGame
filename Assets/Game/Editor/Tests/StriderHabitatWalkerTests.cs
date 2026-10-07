// The Striders' walking house is a variant of the player's RigWalker: these read the built variant
// back and check it is a crewed NPC vehicle while the RigWalker stays a player vehicle.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class StriderHabitatWalkerTests
    {
        private const string RigWalkerPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";
        private static GameObject Habitat() => AssetDatabase.LoadAssetAtPath<GameObject>(StriderCityBuilder.HabitatPath);

        [Test]
        public void IsAVariantOfTheRigWalker_WithTheHelmRemoved()
        {
            GameObject habitat = Habitat();
            Assert.IsNotNull(habitat, "run Tools/SpaceGame/Vehicles/Build Strider Habitat Walker");
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(habitat));
            Assert.IsNull(habitat.GetComponent<MountModule>(), "nobody takes the helm of a Strider house");
            Assert.IsNull(habitat.GetComponent<SteerModule>());
            Assert.IsNull(habitat.GetComponentInChildren<MountStation>(true));
            Assert.IsNotNull(habitat.GetComponent<WalkerPlatformCarrier>(), "players can still ride the deck");
            Assert.IsNull(habitat.GetComponent<WanderModule>(), "a parked house must not wander into its neighbour (spike)");
            Assert.IsTrue(new SerializedObject(habitat.GetComponent<FormationModule>()).FindProperty("holdSlotAtRest").boolValue,
                "a house keeps its place in the column at a stop -- nothing parks (user 2026-09-24)");
        }

        [Test]
        public void TheRigWalkerItself_IsStillAPlayerVehicle()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath);
            Assert.IsNotNull(rig.GetComponent<MountModule>());
            Assert.IsNull(rig.GetComponent<CrewShift>());
        }

        [Test]
        public void HasSixCrewPosts_AnElderPost_AGangwayOnTheGround_AndACrewShift()
        {
            GameObject habitat = Habitat();
            var seats = habitat.GetComponent<VesselSeats>();
            Assert.AreEqual(StriderCityBuilder.CrewPosts + StriderCityBuilder.StandingPosts, seats.Capacity);
            var shift = habitat.GetComponent<CrewShift>();
            Assert.IsNotNull(shift);
            Assert.AreEqual(StriderCityBuilder.StandingPosts, new SerializedObject(shift).FindProperty("standingPosts").intValue,
                "the last seat is the elder's standing post");
            var gangway = new SerializedObject(shift).FindProperty("gangway").objectReferenceValue as Transform;
            Assert.IsNotNull(gangway);
            Assert.AreEqual(0f, gangway.localPosition.y, 0.01f, "the gangway is at the walker's feet, not on the deck");
        }

        [Test]
        public void EveryPost_StandsOnTheDeck_OutsideTheHouse()
        {
            GameObject habitat = Habitat();
            Bounds deck = RootBounds(habitat, "COL_Deck");
            Bounds hull = RootBounds(habitat, "COL_Hull");
            var seats = new SerializedObject(habitat.GetComponent<VesselSeats>()).FindProperty("seats");
            float elderRise = StriderElderBuilder.StandingHeight() / habitat.transform.lossyScale.y;
            for (int i = 0; i < seats.arraySize; i++)
            {
                var post = (Transform)seats.GetArrayElementAtIndex(i).objectReferenceValue;
                Vector3 p = habitat.transform.InverseTransformPoint(post.position);
                bool standing = i >= StriderCityBuilder.CrewPosts;
                Assert.AreEqual(deck.max.y + (standing ? elderRise : 0f), p.y, 0.01f,
                    standing ? $"{post.name} holds the elder's hips, so its feet are on the deck"
                             : $"{post.name} stands on the deck's surface");
                Assert.IsTrue(p.x > deck.min.x && p.x < deck.max.x && p.z > deck.min.z && p.z < deck.max.z,
                    $"{post.name} at {p} is off the deck {deck}");
                Assert.IsFalse(p.x > hull.min.x && p.x < hull.max.x && p.z > hull.min.z && p.z < hull.max.z,
                    $"{post.name} at {p} is inside the house's walls {hull}");
            }
        }

        /// A box collider's bounds in the prefab root's space, read off the asset without a scene.
        private static Bounds RootBounds(GameObject root, string name)
        {
            BoxCollider box = null;
            foreach (BoxCollider candidate in root.GetComponentsInChildren<BoxCollider>(true))
                if (candidate.name == name) box = candidate;
            Assert.IsNotNull(box, $"the RigWalker has no {name}");

            var bounds = new Bounds(root.transform.InverseTransformPoint(box.transform.TransformPoint(box.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                bounds.Encapsulate(root.transform.InverseTransformPoint(box.transform.TransformPoint(box.center + corner)));
            }
            return bounds;
        }

        [Test]
        public void CanLeadOrFollowTheCity_AsAStrider()
        {
            GameObject habitat = Habitat();
            Assert.IsNotNull(habitat.GetComponent<NpcTaskModule>());
            Assert.IsNotNull(habitat.GetComponent<GoalTravelModule>());
            var formation = new SerializedObject(habitat.GetComponent<FormationModule>());
            Assert.AreEqual(ModulePriority.Social, formation.FindProperty("priority").intValue);
            Assert.IsEmpty(formation.FindProperty("formationId").stringValue);
            var faction = new SerializedObject(habitat.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
            var net = habitat.GetComponent<Unity.Netcode.NetworkObject>();
            Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.AreNotEqual(AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath).GetComponent<Unity.Netcode.NetworkObject>().PrefabIdHash,
                net.PrefabIdHash, "a variant needs its own network identity");
            Assert.IsTrue(net.SceneMigrationSynchronization,
                "a spawned house must tell clients when it walks into another chunk scene");
        }

        [Test]
        public void CarriesAScoutRota_WhoseTimeoutOutlastsOneLoop()
        {
            var rota = Habitat().GetComponent<ScoutRota>();
            Assert.IsNotNull(rota, "every house carries the rota; the one the column follows runs it");
            var so = new SerializedObject(rota);
            float loop = ScoutRotaLogic.LoopLength(so.FindProperty("sweepRadius").floatValue, so.FindProperty("sweepPoints").intValue);
            Assert.Greater(so.FindProperty("sweepTimeout").floatValue, loop / StriderMonowheelBuilder.CruiseSpeed,
                "a timeout shorter than the loop sends every scout home before it has ridden round");
        }

        [Test]
        public void TheLeader_OutlastsTheWalkToItsFurthestStop()
        {
            float timeout = new SerializedObject(Habitat().GetComponent<NpcTaskModule>()).FindProperty("travelTimeout").floatValue;
            Assert.GreaterOrEqual(timeout, StriderCityBuilder.CitySearchRadius / StriderCityBuilder.CityLeaderSpeed,
                "a timeout shorter than the walk makes the city give up and re-choose before it reaches a stop");
        }
    }
}
