using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Teleporting;
using SpaceGame.Vehicles.Motors;

namespace SpaceGame.EditorTools
{
    public class TrackedHullMotorTests
    {
        private const float Dt = 0.02f;
        private const float RideHeight = 1.2f;

        /// <summary>
        /// Below any Terrain or scenery that might be loaded in the editor (the ground rays reach 300 m),
        /// and near enough the origin that float spacing stays well under the asserts' tolerances.
        /// </summary>
        private static readonly Vector3 Site = new Vector3(3000f, -1000f, 3000f);

        private static Vector3 At(float x, float y, float z) => Site + new Vector3(x, y, z);

        private readonly List<GameObject> made = new List<GameObject>();
        private GameObject go;
        private TrackedHullMotor motor;

        [SetUp] public void SetUp()
        {
            go = Make("Hull");
            go.transform.position = Site;
            motor = go.AddComponent<TrackedHullMotor>();
            var so = new SerializedObject(motor);
            SerializedProperty ride = so.FindProperty("rideHeight");
            Assert.IsNotNull(ride, "TrackedHullMotor.rideHeight was renamed; the barge builder writes it by name");
            ride.floatValue = RideHeight;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown] public void TearDown()
        {
            foreach (GameObject g in made)
                if (g != null) Object.DestroyImmediate(g);
            made.Clear();
        }

        private GameObject Make(string name)
        {
            var g = new GameObject(name);
            made.Add(g);
            return g;
        }

        private GameObject Box(string name, Vector3 centre, Vector3 size, Transform parent = null)
        {
            GameObject g = parent == null ? Make(name) : new GameObject(name);
            if (parent != null) g.transform.SetParent(parent, false);
            g.transform.position = centre;
            g.AddComponent<BoxCollider>().size = size;
            return g;
        }

        // --- Orders (the IMovementMotor contract) --------------------------------------------------

        [Test] public void MoveTo_TakesTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(At(100f, 0f, 0f), 5f), Dt);
            Assert.AreEqual(At(100f, 0f, 0f), motor.CurrentDestination);
            Assert.IsFalse(motor.HasReachedDestination);
        }

        [Test] public void MoveTo_WithinTheStopDistance_HasReached_MeasuredFlat()
        {
            go.transform.position = At(0f, 30f, 0f);
            motor.Tick(MoveIntent.MoveTo(At(4f, 0f, 0f), 5f), Dt);
            Assert.IsTrue(motor.HasReachedDestination, "a hull rides above its NavMesh destination; height is not distance");
        }

        [Test] public void Idle_ClearsTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(At(100f, 0f, 0f), 5f), Dt);
            motor.Tick(MoveIntent.Idle(), Dt);
            Assert.IsNull(motor.CurrentDestination);
            Assert.IsTrue(motor.HasReachedDestination);
        }

        [Test] public void ForceStop_ClearsTheDestination_AndTheSpeed()
        {
            motor.Tick(MoveIntent.MoveTo(At(0f, 0f, 500f), 5f), Dt);
            for (int i = 0; i < 50; i++) motor.Step(Dt);
            Assert.Greater(motor.Speed, 0f, "precondition: it got moving");

            motor.ForceStop();
            Assert.IsNull(motor.CurrentDestination);
            Assert.AreEqual(0f, motor.Speed);
            Assert.AreEqual(Vector3.zero, motor.Velocity);
        }

        [Test] public void StopAndFace_ClearsTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(At(100f, 0f, 0f), 5f), Dt);
            motor.Tick(MoveIntent.StopAndFace(At(0f, 0f, 50f)), Dt);
            Assert.IsNull(motor.CurrentDestination);
            Assert.IsTrue(motor.HasReachedDestination);
        }

        [Test] public void StopAndFace_TurnsOnTheSpot_WithoutMoving()
        {
            motor.Tick(MoveIntent.StopAndFace(At(50f, 0f, 0f)), Dt);
            for (int i = 0; i < 10; i++) motor.Step(Dt);

            Assert.Greater(go.transform.eulerAngles.y, 0f, "turned toward +x");
            Assert.AreEqual(0f, MotorOrders.FlatDistance(go.transform.position, Site), 1e-3f);
        }

        [Test] public void NoStopDistance_FallsBackToTheDefault()
        {
            // defaultStopDistance is 8 m: 6 m off counts as arrived, 10 m off does not.
            // Built by hand: MoveIntent.MoveTo floors the stop distance above zero.
            motor.Tick(new MoveIntent { Type = AgentIntentType.MoveToPosition, TargetPosition = At(6f, 0f, 0f) }, Dt);
            Assert.IsTrue(motor.HasReachedDestination);

            // A suggestion drops the last order's 20 m stop distance for the default.
            motor.Tick(MoveIntent.MoveTo(At(100f, 0f, 0f), 20f), Dt);
            motor.SuggestDestination(At(10f, 0f, 0f));
            Assert.IsFalse(motor.HasReachedDestination);
        }

        [Test] public void NudgeDestination_BiasesIt()
        {
            motor.Tick(MoveIntent.MoveTo(At(100f, 0f, 0f), 5f), Dt);
            motor.NudgeDestination(new Vector3(0f, 0f, 3f));
            Assert.AreEqual(At(100f, 0f, 3f), motor.CurrentDestination);
        }

        [Test] public void OnTeleported_RebasesTheDestination_AndRereadsTheHeading()
        {
            motor.Tick(MoveIntent.MoveTo(At(100f, 0f, 0f), 5f), Dt);
            Vector3 offset = new Vector3(0f, 0f, 40f);
            Quaternion turned = Quaternion.Euler(0f, 90f, 0f);
            go.transform.SetPositionAndRotation(Site + offset, turned);
            motor.OnTeleported(new TeleportMove(Site, Quaternion.identity, Site + offset, Quaternion.identity));
            Assert.AreEqual(At(100f, 0f, 40f), motor.CurrentDestination);

            motor.ForceStop();
            motor.Step(Dt);
            Assert.AreEqual(90f, go.transform.eulerAngles.y, 1e-3f, "the heading came from the new pose, not the old one");
        }

        // --- Driving --------------------------------------------------------------------------------

        [Test] public void ADestinationBehind_IsTurnedToward_BeforeItIsDrivenAt()
        {
            motor.Tick(MoveIntent.MoveTo(At(0f, 0f, -500f), 5f), Dt);
            motor.Step(Dt);
            Assert.AreEqual(0f, motor.Speed, "180 degrees off the nose: turn on the spot first");
            Assert.AreNotEqual(0f, Mathf.DeltaAngle(0f, go.transform.eulerAngles.y));
        }

        [Test] public void ADestinationAhead_IsDrivenAt_NoFasterThanCruise()
        {
            motor.Tick(MoveIntent.MoveTo(At(0f, 0f, 5000f), 5f), Dt);
            for (int i = 0; i < 2000; i++) motor.Step(Dt);

            Assert.Greater(go.transform.position.z, Site.z);
            Assert.LessOrEqual(motor.Speed, motor.TopSpeed + 1e-4f);
            Assert.AreEqual(motor.TopSpeed, motor.Speed, 1e-3f, "40 s is long enough to reach cruise");
            Assert.AreEqual(motor.Speed, motor.Velocity.magnitude, 1e-4f);
        }

        // --- Ground ---------------------------------------------------------------------------------

        [Test] public void Ground_IsReadUnderTheFootprint_ThroughItsOwnColliders_AndItsPassengers()
        {
            Box("Ground", At(0f, 4.5f, 0f), new Vector3(400f, 1f, 400f));   // top at y = 5
            go.transform.position = At(0f, 20f, 0f);
            Box("Hull_Deck", At(0f, 19f, 0f), new Vector3(12f, 1f, 30f), go.transform);

            // A player on the deck (dynamic) and a crewman standing beside it (kinematic): neither is ground.
            GameObject player = Box("Player", At(0f, 8f, 0f), new Vector3(40f, 1f, 60f));
            player.AddComponent<Rigidbody>().useGravity = false;
            GameObject crew = Box("Crew", At(0f, 10f, 0f), new Vector3(40f, 1f, 60f));
            crew.AddComponent<Rigidbody>().isKinematic = true;

            Physics.SyncTransforms();
            motor.Step(Dt);

            Assert.AreEqual(Site.y + 5f + RideHeight, go.transform.position.y, 1e-3f);
        }

        [Test] public void NoGround_HoldsItsHeight()
        {
            go.transform.position = At(0f, 37f, 0f);
            Physics.SyncTransforms();
            motor.Step(Dt);
            Assert.AreEqual(Site.y + 37f, go.transform.position.y, 1e-3f, "a miss is 'not loaded yet', not a height");
        }

        [Test] public void Ground_ThatGoesAway_LeavesTheHullAtTheLastHeightItHad()
        {
            GameObject ground = Box("Ground", At(0f, 4.5f, 0f), new Vector3(400f, 1f, 400f));
            go.transform.position = At(0f, 20f, 0f);
            Physics.SyncTransforms();
            motor.Step(Dt);

            Object.DestroyImmediate(ground);
            Physics.SyncTransforms();
            motor.Step(Dt);

            Assert.AreEqual(Site.y + 5f + RideHeight, go.transform.position.y, 1e-3f);
        }

        // --- Netcode ------------------------------------------------------------------------------

        [Test] public void IsASimulationDriver_SoNetAuthoritySwitchesItOffOnRemoteCopies()
        {
            CollectionAssert.Contains(SimulationDrivers.Discover(go), motor);
        }
    }
}
