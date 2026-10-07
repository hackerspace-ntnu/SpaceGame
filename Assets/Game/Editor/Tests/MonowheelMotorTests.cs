using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Teleporting;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class MonowheelMotorTests
    {
        private GameObject go;
        private MonowheelMotor motor;

        [SetUp] public void SetUp()
        {
            go = new GameObject("Monowheel");
            go.AddComponent<Rigidbody>();
            motor = go.AddComponent<MonowheelMotor>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(go);

        [Test] public void RiderFrame_SkipsTheMoveIntent()
        {
            motor.ApplyRiderInput(new RiderInput(new Vector2(0f, 1f), 0f, false), 0.02f);
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f)), 0.02f);
            Assert.IsNull(motor.CurrentDestination, "the rider owns this frame; the AI's destination is ignored");
        }

        [Test] public void AnAiFrame_TakesTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            Assert.AreEqual(new Vector3(100f, 0f, 0f), motor.CurrentDestination);
            Assert.IsFalse(motor.HasReachedDestination);
        }

        [Test] public void IdleIntent_ClearsTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            motor.Tick(MoveIntent.Idle(), 0.02f);
            Assert.IsNull(motor.CurrentDestination);
            Assert.IsTrue(motor.HasReachedDestination);
        }

        [Test] public void AnAiFrame_AfterARiderFrame_TakesTheDestination()
        {
            motor.ApplyRiderInput(new RiderInput(new Vector2(0f, 1f), 0f, false), 0.02f);

            // Time.frameCount does not advance in EditMode, so the rider's stamp is moved back a
            // frame instead: the rider spoke last frame and has let go since.
            FieldInfo stamp = typeof(MonowheelMotor).GetField("riderDriveFrame", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(stamp, "MonowheelMotor.riderDriveFrame was renamed; update this test");
            stamp.SetValue(motor, Time.frameCount - 1);

            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            Assert.AreEqual(new Vector3(100f, 0f, 0f), motor.CurrentDestination,
                            "the rider let go; the AI drives again");
        }

        [Test] public void ForceStop_ClearsTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            motor.ForceStop();
            Assert.IsNull(motor.CurrentDestination);
        }

        [Test] public void StopAndFace_ClearsTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            motor.Tick(MoveIntent.StopAndFace(new Vector3(0f, 0f, 50f)), 0.02f);
            Assert.IsNull(motor.CurrentDestination);
            Assert.IsTrue(motor.HasReachedDestination);
        }

        [Test] public void OnTeleported_RebasesTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            Vector3 offset = new Vector3(0f, 0f, 40f);
            go.transform.position = offset;
            motor.OnTeleported(new TeleportMove(Vector3.zero, Quaternion.identity, offset, Quaternion.identity));
            Assert.AreEqual(new Vector3(100f, 0f, 40f), motor.CurrentDestination);
        }

        [Test] public void NoStopDistance_FallsBackToTheDefault()
        {
            // defaultStopDistance is 6 m: 4 m off counts as arrived, 8 m off does not.
            // Built by hand: MoveIntent.MoveTo floors the stop distance above zero.
            motor.Tick(new MoveIntent { Type = AgentIntentType.MoveToPosition, TargetPosition = new Vector3(4f, 0f, 0f) }, 0.02f);
            Assert.IsTrue(motor.HasReachedDestination);

            // A suggestion drops the last order's 10 m stop distance for the default.
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 10f), 0.02f);
            motor.SuggestDestination(new Vector3(8f, 0f, 0f));
            Assert.IsFalse(motor.HasReachedDestination);
        }
    }
}
