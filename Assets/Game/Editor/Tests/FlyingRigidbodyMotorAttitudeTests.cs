// FlyingRigidbodyMotor's opt-in attitude: a craft that banks into its turns and noses along its climb
// (the NPC ornithopter), while a blimp — every Sky fleet hull — keeps the exact old upright rotation.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.EditorTools;

namespace SpaceGame.Tests
{
    public class FlyingRigidbodyMotorAttitudeTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private const float Step = 0.02f;
        private SimulationMode originalMode;
        private GameObject craft;
        private FlyingRigidbodyMotor motor;

        [SetUp]
        public void SetUp()
        {
            originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            craft = new GameObject("Craft");
            craft.transform.position = FarAway;
            craft.AddComponent<Rigidbody>().useGravity = false;
            motor = craft.AddComponent<FlyingRigidbodyMotor>();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = originalMode;
            Object.DestroyImmediate(craft);
        }

        private void Attitude(float bankPerTurnRate, float maxBank, bool pitch)
        {
            var so = new SerializedObject(motor);
            SerializedFields.SetFloat(so, "maxSpeed", 25f);
            SerializedFields.SetFloat(so, "acceleration", 8f);
            SerializedFields.SetBool(so, "altitudeHold", false);
            SerializedFields.SetFloat(so, "bankPerTurnRate", bankPerTurnRate);
            SerializedFields.SetFloat(so, "maxBank", maxBank);
            SerializedFields.SetBool(so, "pitchAlongPath", pitch);
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                        .Invoke(motor, null);
        }

        private void FlyToward(Vector3 offset, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                MoveIntent intent = MoveIntent.MoveTo(craft.transform.position + offset);
                motor.Tick(in intent, Step);
                motor.StepPhysics(Step);
                Physics.Simulate(Step);
            }
        }

        private float Roll => Mathf.DeltaAngle(0f, craft.transform.eulerAngles.z);
        private float Pitch => Mathf.DeltaAngle(0f, craft.transform.eulerAngles.x);

        [Test]
        public void WithAttitudeOff_TheMotorNeverRollsOrPitches()
        {
            Attitude(0f, 0f, pitch: false);
            FlyToward(new Vector3(300f, 200f, 50f), 100);

            Assert.AreEqual(0f, Roll, 0.01f, "a blimp rolled");
            Assert.AreEqual(0f, Pitch, 0.01f, "a blimp pitched");
        }

        [Test]
        public void ACraftTurningRight_BanksItsRightWingDown()
        {
            Attitude(0.5f, 35f, pitch: false);
            FlyToward(new Vector3(500f, 0f, 0f), 30);

            Assert.Less(Roll, -5f, $"roll {Roll:F1} deg: no bank into a right turn");
            Assert.GreaterOrEqual(Roll, -35.01f, "banked past maxBank");
        }

        [Test]
        public void ACraftClimbing_PointsItsNoseUp()
        {
            Attitude(0f, 0f, pitch: true);
            FlyToward(new Vector3(0f, 200f, 300f), 100);

            Assert.Less(Pitch, -5f, $"pitch {Pitch:F1} deg: the nose did not follow the climb");
        }

        [Test]
        public void ABankedPitchedCraftThatStopsSteering_LevelsOut_AndHoldsItsYaw()
        {
            AssertLevelsOutWhenSteeringStops(() => MoveIntent.Idle());
        }

        [Test]
        public void ABankedPitchedCraftThatReachesItsPoint_LevelsOut_AndHoldsItsYaw()
        {
            AssertLevelsOutWhenSteeringStops(() => MoveIntent.MoveTo(craft.transform.position));
        }

        private void AssertLevelsOutWhenSteeringStops(System.Func<MoveIntent> stopIntent)
        {
            Attitude(0.5f, 35f, pitch: true);
            FlyToward(new Vector3(500f, 200f, 0f), 30);
            Assert.Less(Roll, -5f, "setup: the craft never banked");
            Assert.Less(Pitch, -5f, "setup: the craft never pitched");
            float yaw = craft.transform.eulerAngles.y;

            for (int i = 0; i < 150; i++)
            {
                MoveIntent intent = stopIntent();
                motor.Tick(in intent, Step);
                motor.StepPhysics(Step);
                Physics.Simulate(Step);
            }

            Assert.AreEqual(0f, Roll, 1f, "the bank froze when steering stopped");
            Assert.AreEqual(0f, Pitch, 1f, "the pitch froze when steering stopped");
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, craft.transform.eulerAngles.y), 1f, "levelling out turned the craft");
        }
    }
}
