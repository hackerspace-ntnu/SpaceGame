// The Sky Tribe's NPC transports, read off disk, and the seats that carry a war party.
//
//   the pieces a flown, shootable, networked vessel cannot do without — a server-authoritative
//   NetworkTransform, a kinematic body, seats matching its capacity, the ramp and drop markers,
//   the Sky faction and health;
//   both prefabs registered, or clients never see a vessel the host flies;
//   a seat/unseat round trip that hands an NPC back exactly as it was taken.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class SkyTransportPrefabTests
    {
        private const string NetworkManagerPrefabPath = "Assets/Game/Prefabs/Systems/NetworkManager.prefab";

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private static IEnumerable<SkyVesselBuilder.Transport> Transports => SkyVesselBuilder.Transports;

        private static GameObject LoadBuilt(SkyVesselBuilder.Transport transport)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(transport.PrefabPath);
            if (prefab == null)
                Assert.Ignore($"{transport.PrefabPath} has not been built (Tools > SpaceGame > Vehicles > Build Sky Transports).");
            return prefab;
        }

        [TestCaseSource(nameof(Transports))]
        public void TheVesselIsAServerFlownNetworkEntity(SkyVesselBuilder.Transport transport)
        {
            GameObject prefab = LoadBuilt(transport);

            Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), "root NetworkObject");
            var netTransform = prefab.GetComponent<NetworkTransform>();
            Assert.IsNotNull(netTransform, "NetworkTransform");
            Assert.AreEqual(NetworkTransform.AuthorityModes.Server, netTransform.AuthorityMode,
                "the pilot runs on the server; an owner-authoritative transform would ignore it");
            Assert.IsTrue(netTransform.Interpolate, "clients only interpolate");

            var body = prefab.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);
            Assert.IsTrue(body.isKinematic, "the pilot places the hull; physics must not");

            Assert.IsNotNull(prefab.GetComponent<VesselPilot>());
            Assert.IsTrue(prefab.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger),
                "the hull is solid to fly into and shoot");
        }

        [TestCaseSource(nameof(Transports))]
        public void TheVesselSeatsItsCapacityAndHasItsMarkers(SkyVesselBuilder.Transport transport)
        {
            GameObject prefab = LoadBuilt(transport);

            var seats = prefab.GetComponent<VesselSeats>();
            Assert.IsNotNull(seats);
            Assert.AreEqual(transport.Seats.Length, seats.Capacity);
            Assert.AreEqual(transport.Seats.Length, prefab.transform.Find("Seats").childCount);
            Assert.IsNotNull(prefab.transform.Find("Ramp"), "where a landed party walks off");
            Assert.IsNotNull(prefab.transform.Find("Drop"), "where a hovering vessel drops its party");
        }

        [Test]
        public void TheSkiffCarriesFourAndTheFreighterEight()
        {
            Assert.AreEqual(4, Transports.Single(t => t.Name == "SkySkiffTransport").Seats.Length);
            Assert.AreEqual(8, Transports.Single(t => t.Name == "SkyFreighterTransport").Seats.Length);
        }

        [TestCaseSource(nameof(Transports))]
        public void TheVesselIsASkyTargetThatCanBeShot(SkyVesselBuilder.Transport transport)
        {
            GameObject prefab = LoadBuilt(transport);

            var faction = prefab.GetComponent<EntityFaction>();
            Assert.IsNotNull(faction);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath), faction.Faction);
            Assert.IsNotNull(faction.RelationshipTable);

            var health = prefab.GetComponent<HealthComponent>();
            Assert.IsNotNull(health);
            Assert.AreEqual(transport.MaxHealth, health.GetMaxHealth);
            Assert.IsNotNull(prefab.GetComponent<NetworkedHealthComponent>(), "or its health never reaches clients");
            Assert.IsNotNull(prefab.GetComponent<SpaceGame.Core.NetRelay>(), "or a client's shots never reach it");
        }

        [TestCaseSource(nameof(Transports))]
        public void TheVesselIsARegisteredNetworkPrefab(SkyVesselBuilder.Transport transport)
        {
            GameObject prefab = LoadBuilt(transport);

            var manager = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath).GetComponent<NetworkManager>();
            bool registered = manager.NetworkConfig.Prefabs.NetworkPrefabsLists
                .Where(list => list != null)
                .SelectMany(list => list.PrefabList)
                .Any(entry => entry?.Prefab == prefab);

            Assert.IsTrue(registered, "unregistered: the host would fly it and every client see nothing");
        }

        // ── Seats on bare GameObjects ─────────────────────────────────────────────

        [Test]
        public void ASeatedNpcIsCarriedAndStepsOffAsItWas()
        {
            VesselSeats seats = NewVessel(seatCount: 2);
            GameObject npc = NewObject("npc");
            var brain = npc.AddComponent<AgentController>();

            Assert.AreEqual(0, seats.Seat(npc));
            Assert.AreSame(seats.transform, npc.transform.parent.parent, "seated under the vessel's seat marker");
            Assert.IsTrue(brain.RidesAsPassenger, "cargo on the deck may not walk");
            Assert.IsTrue(brain.enabled, "but keeps its brain, so it shoots from the deck");
            Assert.AreEqual(1, seats.Occupied);

            var dropOff = new Vector3(40f, 0f, 12f);
            Assert.AreSame(npc, seats.Unseat(0, dropOff));

            Assert.IsNull(npc.transform.parent, "back in the world, not under the hull");
            Assert.IsFalse(brain.RidesAsPassenger);
            Assert.AreEqual(0, seats.Occupied);
            Assert.That(Vector3.Distance(npc.transform.position, dropOff), Is.LessThan(1e-3f),
                "no NavMesh in an EditMode scene, so the point itself");
        }

        [Test]
        public void SeatsFillInOrderAndRefuseWhenFull()
        {
            VesselSeats seats = NewVessel(seatCount: 2);

            Assert.AreEqual(0, seats.Seat(NewObject("a")));
            Assert.AreEqual(1, seats.Seat(NewObject("b")));
            GameObject left = NewObject("c");
            Assert.AreEqual(-1, seats.Seat(left));
            Assert.IsNull(left.transform.parent);
        }

        [Test]
        public void StepsOffGiveBackOnlyWhatSeatingTook()
        {
            VesselSeats seats = NewVessel(seatCount: 1);
            GameObject npc = NewObject("npc");
            var agent = npc.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.enabled = false;
            var body = npc.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            seats.Seat(npc);
            Assert.IsTrue(body.isKinematic, "carried bodies are held still");
            Assert.IsFalse(body.useGravity);
            Assert.AreEqual(RigidbodyInterpolation.None, body.interpolation, "or it shakes loose of a moving deck");
            seats.Unseat(0, Vector3.zero);

            Assert.IsFalse(agent.enabled, "its pathing was off before it boarded");
            Assert.IsFalse(body.isKinematic, "and its body was dynamic");
            Assert.IsTrue(body.useGravity, "with its weight");
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);
        }

        [Test]
        public void ATornDownHullLeavesNobodyAsCargo()
        {
            VesselSeats seats = NewVessel(seatCount: 2);
            GameObject npc = NewObject("npc");
            var brain = npc.AddComponent<AgentController>();
            var agent = npc.AddComponent<UnityEngine.AI.NavMeshAgent>();
            npc.AddComponent<Rigidbody>();

            seats.Seat(npc);
            seats.AbandonAll();

            Assert.AreEqual(0, seats.Occupied);
            Assert.IsFalse(brain.RidesAsPassenger,
                "netcode lifts a seated passenger to the scene root when the hull despawns; it must " +
                "not stand there as cargo for the rest of the session");
            Assert.IsTrue(agent.enabled, "with its feet back");
            Assert.IsFalse(CarriedBody.IsHeld(npc), "and no body claim left that nothing will ever release");
        }

        [Test]
        public void ASeatWhoseOccupantWasDespawnedGivesUpItsClaim()
        {
            VesselSeats seats = NewVessel(seatCount: 1);
            GameObject gone = NewObject("gone");
            gone.AddComponent<Rigidbody>();
            gone.AddComponent<HealthComponent>();
            seats.Seat(gone);
            Object.DestroyImmediate(gone);

            int claimsBefore = HeldBodyCount();
            GameObject next = NewObject("next");
            Assert.AreEqual(0, seats.Seat(next), "the destroyed occupant's seat is free");
            Assert.AreEqual(claimsBefore - 1, HeldBodyCount(), "the destroyed occupant's body claim is dropped");
        }

        private static int HeldBodyCount()
        {
            var held = (System.Collections.IDictionary)typeof(CarriedBody)
                .GetField("s_held", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            return held.Count;
        }

        [Test]
        public void AKilledPassengerLeavesItsSeat()
        {
            VesselSeats seats = NewVessel(seatCount: 1);
            GameObject npc = NewObject("npc");
            var health = npc.AddComponent<HealthComponent>();
            var brain = npc.AddComponent<AgentController>();

            seats.Seat(npc);
            health.Damage(health.GetMaxHealth);

            Assert.AreEqual(0, seats.Occupied, "the dead are not passengers to unload");
            Assert.IsNull(npc.transform.parent);
            Assert.IsTrue(brain.RidesAsPassenger, "and a corpse is not handed its feet back");
        }

        [Test]
        public void AWatchingMachineSitsDownWhoeverNetcodeSeated()
        {
            VesselSeats seats = NewVessel(seatCount: 1);
            GameObject npc = NewObject("npc");
            npc.AddComponent<AgentController>();
            var npcCollider = npc.AddComponent<CapsuleCollider>();
            var hull = seats.gameObject.AddComponent<BoxCollider>();

            // All a client is handed: the parenting.
            npc.transform.SetParent(seats.transform, false);
            seats.RefreshPresented();

            Assert.AreEqual(0, seats.Occupied, "only the authority seats anybody");
            Assert.IsTrue(Physics.GetIgnoreCollision(npcCollider, hull),
                "a watching machine still has to stop the passenger shoving the hull");
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        // AddComponent runs no Awake in edit mode, so the serialized seat list is planted by hand.
        private VesselSeats NewVessel(int seatCount)
        {
            GameObject vessel = NewObject("vessel");
            vessel.transform.SetPositionAndRotation(new Vector3(10f, 30f, -5f), Quaternion.Euler(0f, 40f, 0f));

            var markers = new Transform[seatCount];
            for (int i = 0; i < seatCount; i++)
            {
                markers[i] = new GameObject($"Seat_{i}").transform;
                markers[i].SetParent(vessel.transform, false);
                markers[i].localPosition = new Vector3(i, -5f, 0f);
            }

            var seats = vessel.AddComponent<VesselSeats>();
            typeof(VesselSeats).GetField("seats", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(seats, markers);
            return seats;
        }
    }
}
