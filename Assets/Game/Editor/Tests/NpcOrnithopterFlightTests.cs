// Assets/Game/Editor/Tests/NpcOrnithopterFlightTests.cs
// The riskiest slice end to end, on the REAL prefab (no Play Mode): a pilot seated in NpcOrnithopter
// takes off from the ground — or launches off Sky City height — flies to a goal, sets down within the
// spec's ~15 m, steps off unhurt, and the craft is retired. The craft lives in a preview scene with its
// own physics; the ground lives in the default scene, which is where PhysicsGroundProbe looks.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class NpcOrnithopterFlightTests
    {
        private static readonly Vector3 Ground0 = new Vector3(200000f, 0f, 200000f);
        private const float Dt = 0.02f;
        private const float LandingTolerance = 15f;
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private InstantiatingWorld world;
        private SimulationMode originalMode;
        private UnityEngine.SceneManagement.Scene scene;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.hideFlags = HideFlags.HideAndDontSave;
            junk.Add(ground);
            ground.transform.position = Ground0 + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(6000f, 1f, 6000f);
            Physics.SyncTransforms();
            scene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Physics.simulationMode = originalMode;
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        [TestCase(3f, 600f)]
        [TestCase(280f, 2000f)]
        public void TheRealCraft_FliesToItsGoal_AndSetsThePilotDownNearIt(float startHeight, float goalDistance)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "build the NPC craft first");
            var craft = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            junk.Add(craft);
            craft.transform.position = Ground0 + Vector3.up * startHeight;
            var motor = craft.GetComponent<FlyingRigidbodyMotor>();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(motor, null);
            var aviator = craft.GetComponent<NpcAviator>();
            GameObject pilot = NpcAviatorTests.Pilot(junk);
            pilot.transform.position = craft.transform.position;
            Vector3 goal = Ground0 + Vector3.right * goalDistance;
            Assert.IsTrue(aviator.Fly(pilot, goal, 60f, 6f));

            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < 25000 && !world.Despawned.Contains(craft); i++)
            {
                MoveIntent? intent = aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, Dt);
                MoveIntent applied = intent ?? MoveIntent.Idle();
                motor.Tick(in applied, Dt);
                motor.StepPhysics(Dt);
                physics.Simulate(Dt);
            }

            Assert.Contains(craft, world.Despawned, "the craft never landed");
            Assert.IsNull(pilot.transform.parent, "the pilot was never set down");
            var flat = new Vector2(pilot.transform.position.x - goal.x, pilot.transform.position.z - goal.z);
            Assert.Less(flat.magnitude, LandingTolerance, $"set down {flat.magnitude:F1} m from the goal");
            var health = pilot.GetComponent<HealthComponent>();
            Assert.AreEqual(health.GetMaxHealth, health.GetHealth, "the landing hurt the pilot");
        }

        [Test]
        public void TheRealCraft_ClimbsVisiblyAfterTakeOff()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "build the NPC craft first");
            var craft = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            junk.Add(craft);
            Vector3 start = Ground0 + Vector3.up * 3f;
            craft.transform.position = start;
            var motor = craft.GetComponent<FlyingRigidbodyMotor>();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(motor, null);
            var aviator = craft.GetComponent<NpcAviator>();
            GameObject pilot = NpcAviatorTests.Pilot(junk);
            pilot.transform.position = start;
            Assert.IsTrue(aviator.Fly(pilot, Ground0 + Vector3.right * 2000f, 60f, 6f));

            PhysicsScene physics = scene.GetPhysicsScene();
            const float Out = 100f;
            for (int i = 0; i < 5000 && Vector3.ProjectOnPlane(craft.transform.position - start, Vector3.up).magnitude < Out; i++)
            {
                MoveIntent applied = aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, Dt) ?? MoveIntent.Idle();
                motor.Tick(in applied, Dt);
                motor.StepPhysics(Dt);
                physics.Simulate(Dt);
            }

            float climb = Mathf.Atan2(craft.transform.position.y - start.y, Out) * Mathf.Rad2Deg;
            Assert.Greater(climb, 15f, $"climbed only {climb:F1} degrees over its first {Out} m: a low skim nobody reads as taking off");
        }

        /// <summary>
        /// Formation flight is only worth building if the real craft can hold it: a wingman on the real prefab keeps
        /// its chevron station on a leader cruising at the patrol's 0.7 of top speed, through a 90° turn.
        /// </summary>
        [Test]
        public void TheRealCraft_HoldsAWingStation_OnALeaderThroughATurn()
        {
            const float SettleSeconds = 15f, TurnStart = 20f, TurnSeconds = 15f, TotalSeconds = 50f, StationTolerance = 12f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "build the NPC craft first");
            var craft = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            junk.Add(craft);
            var motor = craft.GetComponent<FlyingRigidbodyMotor>();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(motor, null);
            var aviator = craft.GetComponent<NpcAviator>();
            var escort = (EscortSettings)typeof(NpcAviator).GetField("escort", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(aviator);
            escort.driftAmplitude = 0f;

            // The leader: a plain transform flown by the test at 0.7 of the craft's top speed, 60 m up.
            var leader = new GameObject("Leader");
            junk.Add(leader);
            leader.transform.position = Ground0 + Vector3.up * 60f;
            float leaderSpeed = 0.7f * motor.TopSpeed;
            var patrol = new NpcGroupAirPatrol();
            Vector3 offset = NpcAirPatrol.WingOffset(0, patrol);

            craft.transform.position = leader.transform.TransformPoint(offset);
            GameObject pilot = NpcAviatorTests.Pilot(junk);
            pilot.transform.position = craft.transform.position;
            Assert.IsTrue(aviator.Board(pilot, 60f, 6f, Ground0));
            aviator.Escort(FlightStation.Fixed(leader.transform, offset, 0));

            PhysicsScene physics = scene.GetPhysicsScene();
            float worst = 0f;
            for (float t = 0f; t < TotalSeconds; t += Dt)
            {
                if (t >= TurnStart && t < TurnStart + TurnSeconds) leader.transform.Rotate(0f, 90f / TurnSeconds * Dt, 0f);
                leader.transform.position += leader.transform.forward * leaderSpeed * Dt;

                MoveIntent applied = aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, Dt) ?? MoveIntent.Idle();
                motor.Tick(in applied, Dt);
                motor.StepPhysics(Dt);
                physics.Simulate(Dt);

                if (t >= SettleSeconds)
                    worst = Mathf.Max(worst, Vector3.Distance(craft.transform.position, leader.transform.TransformPoint(offset)));
            }

            Assert.Less(worst, StationTolerance, $"the wingman strayed {worst:F1} m from its station: no formation would hold");
            Assert.IsTrue(pilot.transform.IsChildOf(craft.transform), "the wingman landed");
        }

        // pitch: craft nose up (+) / down (-), degrees; roll: bank, degrees. The craft banks to 35 and pitches to 40.
        [TestCase(1f, true, 0f, 0f)]
        [TestCase(-1f, false, 0f, 0f)]
        [TestCase(1f, true, 0f, 35f)]
        [TestCase(-1f, false, 0f, 35f)]
        [TestCase(1f, true, -35f, 0f)]
        [TestCase(-1f, false, -35f, 0f)]
        [TestCase(1f, true, 35f, 0f)]
        [TestCase(-1f, false, 35f, 0f)]
        public void APilotLyingInTheRealCradle_SeesAlongTheCraftsNose(float side, bool seen, float pitch, float roll)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "build the NPC craft first");
            var craft = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            junk.Add(craft);
            craft.transform.position = Ground0 + Vector3.up * 50f;
            GameObject pilot = NpcAviatorTests.Pilot(junk);
            var eye = pilot.AddComponent<PerceptionModule>();
            var so = new SerializedObject(eye);
            so.FindProperty("occlusionLayers").intValue = ~0;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(craft.GetComponent<VesselSeats>().Seat(0, pilot));
            Assert.Greater(Mathf.Abs(pilot.transform.forward.y), 0.9f, "the cradle no longer lays its pilot prone; this test lost its point");
            craft.transform.rotation = Quaternion.Euler(-pitch, 0f, roll);
            Vector3 nose = Vector3.ProjectOnPlane(craft.transform.forward, Vector3.up).normalized;

            var target = new GameObject("Target");
            junk.Add(target);
            target.transform.position = pilot.transform.position + nose * (30f * side);
            target.AddComponent<CapsuleCollider>().height = 3f;
            Physics.SyncTransforms();

            Assert.AreEqual(seen, eye.IsVisible(target.transform),
                            seen ? "a prone pilot is blind to what lies ahead of its craft" : "a prone pilot sees behind its craft");
        }
    }
}
