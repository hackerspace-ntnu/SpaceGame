// The drifting Sky fleet's moving parts, driven by hand:
//
//   FlyingRigidbodyMotor in kinematic-hull mode actually moves a kinematic body, keeps its own
//   velocity, and honours the intent's facing channel;
//   DriftRouteModule sails, moors only once it has stopped, and loops;
//   FleetEscortModule aims at its station in the flagship's frame and holds the flagship's heading;
//   SettlementDeck parks the people on the deck when the hull sails and hands them back when it moors.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class SkyFleetDriftTests
    {
        private const float Step = 0.02f;

        private readonly List<GameObject> made = new();
        private SimulationMode originalSimulationMode;

        [SetUp]
        public void SetUp()
        {
            originalSimulationMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = originalSimulationMode;
            foreach (GameObject go in made)
                if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        private GameObject Make(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go;
        }

        // ── Motor ──────────────────────────────────────────────────────────────

        private FlyingRigidbodyMotor KinematicHull(out Rigidbody body)
        {
            GameObject hull = Make("Hull");
            body = hull.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            hull.AddComponent<BoxCollider>();
            var motor = hull.AddComponent<FlyingRigidbodyMotor>();
            Rigidbody b = body;
            SerializedFields.Edit(motor, so =>
            {
                SerializedFields.Set(so, "body", b);
                SerializedFields.SetBool(so, "kinematicHull", true);
                SerializedFields.SetBool(so, "altitudeHold", false);
                SerializedFields.SetFloat(so, "maxSpeed", 2f);
                SerializedFields.SetFloat(so, "acceleration", 0.5f);
                SerializedFields.SetFloat(so, "deceleration", 0.5f);
                SerializedFields.SetFloat(so, "faceRotateSpeed", 1f);
            });
            return motor;
        }

        private static void Fly(FlyingRigidbodyMotor motor, MoveIntent intent, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                motor.Tick(in intent, Step);
                motor.StepPhysics(Step);
                Physics.Simulate(Step);
            }
        }

        [Test]
        public void KinematicHull_IsMovedTowardItsDestination_AtNoMoreThanItsTopSpeed()
        {
            FlyingRigidbodyMotor motor = KinematicHull(out Rigidbody body);

            Fly(motor, MoveIntent.MoveTo(new Vector3(0f, 0f, 500f), 1f), 500);   // 10 s

            Assert.IsTrue(body.isKinematic, "the motor must not make the hull dynamic");
            Assert.Greater(body.position.z, 5f, "a kinematic hull ignores linearVelocity; it has to be moved");
            Assert.LessOrEqual(motor.Velocity.magnitude, 2f + 1e-3f);
            Assert.AreEqual(body.position.z, 10f * 2f, 15f, "ramps to 2 m/s at 0.5 m/s²: ~16 m in 10 s");
        }

        [Test]
        public void KinematicHull_TurnsToFaceItsTravel()
        {
            FlyingRigidbodyMotor motor = KinematicHull(out Rigidbody body);

            Fly(motor, MoveIntent.MoveTo(new Vector3(500f, 0f, 0f), 1f), 500);

            Assert.Less(Vector3.Angle(body.rotation * Vector3.forward, Vector3.right), 5f);
        }

        [Test]
        public void KinematicHull_HoldsTheIntentsFacingInsteadOfItsTravel()
        {
            FlyingRigidbodyMotor motor = KinematicHull(out Rigidbody body);

            // Travel +X while told to look +Z: an escort holding its flagship's heading.
            for (int i = 0; i < 500; i++)
            {
                MoveIntent intent = MoveIntent.MoveTo(new Vector3(500f, 0f, 0f), 1f)
                                              .WithFacing(body.position + Vector3.forward);
                motor.Tick(in intent, Step);
                motor.StepPhysics(Step);
                Physics.Simulate(Step);
            }

            Assert.Less(Vector3.Angle(body.rotation * Vector3.forward, Vector3.forward), 1f);
            Assert.Greater(body.position.x, 5f);
        }

        [Test]
        public void KinematicHull_StopsWhenForced()
        {
            FlyingRigidbodyMotor motor = KinematicHull(out Rigidbody body);
            Fly(motor, MoveIntent.MoveTo(new Vector3(0f, 0f, 500f), 1f), 200);

            motor.ForceStop();
            float z = body.position.z;
            motor.StepPhysics(Step);
            Physics.Simulate(Step);

            Assert.AreEqual(Vector3.zero, motor.Velocity);
            Assert.AreEqual(z, body.position.z, 1e-4f);
        }

        // ── Route ──────────────────────────────────────────────────────────────

        private DriftRouteModule Route(params Vector3[] waypoints)
        {
            var route = Make("Flagship").AddComponent<DriftRouteModule>();
            SerializedFields.Edit(route, so =>
            {
                SerializedProperty list = so.FindProperty("route");
                list.arraySize = waypoints.Length;
                for (int i = 0; i < waypoints.Length; i++)
                    list.GetArrayElementAtIndex(i).vector3Value = waypoints[i];
                SerializedFields.SetFloat(so, "arriveRadius", 40f);
                SerializedFields.SetFloat(so, "mooredBelowSpeed", 0.2f);
                SerializedFields.SetFloat(so, "mooredSeconds", 120f);
            });
            return route;
        }

        private static MoveIntent? Tick(DriftRouteModule route, Vector3 position, Vector3 velocity, float dt) =>
            route.Tick(new AgentContext { Position = position, Velocity = velocity }, dt);

        [Test]
        public void Route_WaitsOutItsMooring_ThenSailsForTheNextWaypoint()
        {
            DriftRouteModule route = Route(Vector3.zero, new Vector3(800f, 0f, 0f));
            route.RestoreDrift(0, false, 10f);

            Assert.IsNull(Tick(route, Vector3.zero, Vector3.zero, 9f), "still moored");
            Assert.IsFalse(route.UnderWay);

            MoveIntent? sail = Tick(route, Vector3.zero, Vector3.zero, 2f);

            Assert.IsTrue(route.UnderWay);
            Assert.AreEqual(1, route.Leg);
            Assert.IsTrue(sail.HasValue);
            Assert.AreEqual(AgentIntentType.MoveToPosition, sail.Value.Type);
            Assert.AreEqual(new Vector3(800f, 0f, 0f), sail.Value.TargetPosition);
        }

        [Test]
        public void Route_DoesNotMoorWhileTheHullIsStillSliding()
        {
            DriftRouteModule route = Route(Vector3.zero, new Vector3(800f, 0f, 0f));
            route.RestoreDrift(1, true, 0f);

            Vector3 inside = new(780f, 0f, 0f);
            Assert.IsTrue(Tick(route, inside, new Vector3(1f, 0f, 0f), Step).HasValue, "braking, not moored");
            Assert.IsTrue(route.UnderWay);

            Assert.IsNull(Tick(route, inside, Vector3.zero, Step));
            Assert.IsFalse(route.UnderWay);
            Assert.AreEqual(120f, route.MooredRemaining, 1e-4f);
        }

        [Test]
        public void Route_LoopsBackToItsFirstWaypoint()
        {
            DriftRouteModule route = Route(Vector3.zero, new Vector3(800f, 0f, 0f), new Vector3(800f, 0f, 800f));
            route.RestoreDrift(2, false, 0f);

            MoveIntent? sail = Tick(route, new Vector3(800f, 0f, 800f), Vector3.zero, Step);

            Assert.AreEqual(0, route.Leg);
            Assert.AreEqual(Vector3.zero, sail.Value.TargetPosition);
        }

        [Test]
        public void Route_WithFewerThanTwoWaypoints_HoldsStation() =>
            Assert.IsNull(Tick(Route(Vector3.zero), Vector3.zero, Vector3.zero, 500f));

        // ── Escort ─────────────────────────────────────────────────────────────

        [Test]
        public void Wander_StaysInsideItsAmplitude_AndPhaseSeparatesEscorts()
        {
            var amplitude = new Vector3(14f, 5f, 14f);
            var period = new Vector3(61f, 43f, 73f);
            for (float t = 0f; t < 600f; t += 0.7f)
            {
                Vector3 w = FleetEscortModule.WanderOffset(amplitude, period, Vector3.zero, t);
                Assert.LessOrEqual(Mathf.Abs(w.x), amplitude.x + 1e-4f);
                Assert.LessOrEqual(Mathf.Abs(w.y), amplitude.y + 1e-4f);
                Assert.LessOrEqual(Mathf.Abs(w.z), amplitude.z + 1e-4f);
            }

            Vector3 a = FleetEscortModule.WanderOffset(amplitude, period, Vector3.zero, 10f);
            Vector3 b = FleetEscortModule.WanderOffset(amplitude, period, new Vector3(1.3f, 2.1f, 0.4f), 10f);
            Assert.Greater(Vector3.Distance(a, b), 1f);
        }

        [Test]
        public void Escort_AimsAtItsStationInTheFlagshipsFrame_AndHoldsTheFlagshipsHeading()
        {
            GameObject flagship = Make("Flagship");
            flagship.transform.SetPositionAndRotation(new Vector3(1000f, 280f, 500f), Quaternion.Euler(0f, 90f, 0f));

            GameObject escortGo = Make("Escort");
            var escort = escortGo.AddComponent<FleetEscortModule>();
            SerializedFields.Edit(escort, so =>
            {
                SerializedFields.Set(so, "flagship", flagship.transform);
                SerializedFields.SetVector3(so, "station", new Vector3(0f, 0f, 150f));
                SerializedFields.SetVector3(so, "wanderAmplitude", Vector3.zero);
            });

            MoveIntent? intent = escort.Tick(new AgentContext { Position = escortGo.transform.position }, Step);

            Assert.IsTrue(intent.HasValue);
            // 150 m ahead of a flagship yawed 90° is 150 m along world +X.
            Assert.Less(Vector3.Distance(new Vector3(1150f, 280f, 500f), intent.Value.TargetPosition), 0.01f);
            Assert.IsTrue(intent.Value.OverrideFacing);
            Vector3 heading = intent.Value.FacePosition - escortGo.transform.position;
            Assert.Less(Vector3.Angle(heading, Vector3.right), 0.01f);
        }

        // ── Deck ───────────────────────────────────────────────────────────────

        private const string SkyFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/SkyTribeFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        private SettlementDeck Deck(out DriftRouteModule route, out StaticNavMeshData navMesh,
                                    out SettlementPopulation population, out EntityFaction resident)
        {
            var sky = AssetDatabase.LoadAssetAtPath<FactionDefinition>(SkyFactionPath);
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);

            route = Route(Vector3.zero, new Vector3(800f, 0f, 0f));
            GameObject hull = route.gameObject;
            navMesh = hull.AddComponent<StaticNavMeshData>();
            population = hull.AddComponent<SettlementPopulation>();
            population.Configure(sky, table, new SettlementPopulation.Inhabitant[0], 16, 45f, 0f, 90f, 100f);

            var volumeGo = new GameObject("DeckVolume");
            volumeGo.transform.SetParent(hull.transform, false);
            var volume = volumeGo.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(40f, 20f, 80f);

            var deck = hull.AddComponent<SettlementDeck>();
            deck.Configure(route, navMesh, population, null, volume);

            GameObject npc = Make("Resident");
            npc.transform.position = new Vector3(5f, 1f, 10f);
            npc.AddComponent<AgentController>();
            resident = npc.AddComponent<EntityFaction>();
            resident.SetFaction(sky, table);
            EntityTargetRegistry.Register(resident);
            return deck;
        }

        [Test]
        public void Deck_ParksItsPeopleAndWithdrawsItsNavMesh_WhileUnderWay_AndHandsThemBackMoored()
        {
            SettlementDeck deck = Deck(out DriftRouteModule route, out StaticNavMeshData navMesh,
                                       out SettlementPopulation population, out EntityFaction resident);
            try
            {
                route.RestoreDrift(1, true, 0f);
                deck.Step(0f);

                Assert.AreEqual(1, deck.ParkedCount);
                Assert.AreEqual(route.transform, resident.transform.parent, "carried with the hull");
                Assert.IsTrue(resident.GetComponent<AgentController>().RidesAsPassenger, "feet taken, brain kept");
                Assert.IsFalse(navMesh.enabled, "no NavMesh left hanging where the deck was");
                Assert.IsTrue(population.SpawningSuspended);

                route.RestoreDrift(1, false, 120f);
                deck.Step(1f);

                Assert.AreEqual(0, deck.ParkedCount);
                Assert.IsNull(resident.transform.parent);
                Assert.IsFalse(resident.GetComponent<AgentController>().RidesAsPassenger);
                Assert.IsTrue(navMesh.enabled);
                Assert.IsFalse(population.SpawningSuspended);
            }
            finally
            {
                EntityTargetRegistry.Unregister(resident);
            }
        }

        [Test]
        public void Deck_LeavesAloneSomeoneOffTheDeck()
        {
            SettlementDeck deck = Deck(out DriftRouteModule route, out _, out _, out EntityFaction resident);
            try
            {
                resident.transform.position = new Vector3(60f, 1f, 0f);   // beside the deck, in the city's radius
                route.RestoreDrift(1, true, 0f);
                deck.Step(0f);

                Assert.AreEqual(0, deck.ParkedCount);
                Assert.IsNull(resident.transform.parent);
            }
            finally
            {
                EntityTargetRegistry.Unregister(resident);
            }
        }
    }
}
