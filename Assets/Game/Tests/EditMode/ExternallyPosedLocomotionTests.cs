// A legged machine that is NOT the one deciding where it goes.
//
// LeggedLocomotion is invariant I4: the single owner of the body's transform. That is right on the
// machine simulating it and wrong on every other machine in a multiplayer session, where the pose
// arrives over the wire — the local locomotion runs in LateUpdate, so it overwrote the replicated
// pose every single frame and the remote copy never moved at all. Two players riding out together
// each saw the other's ostrich standing back where it spawned, with the rider drifting away from it
// across the desert.
//
// These tests pin the following mode down from the outside: the transform is written by the test
// (standing in for a NetworkTransform), and the machine has to leave it alone while still stepping
// its feet against the ground it is being carried over.
using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class ExternallyPosedLocomotionTests
    {
        private GameObject ground;
        private GameObject machine;
        private Mesh cube;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(600f, 1f, 600f);

            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (machine != null) Object.DestroyImmediate(machine);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        [Test]
        public void AnExternallyPosedMachineNeverWritesItsOwnBody()
        {
            TestMachine m = BuildQuadruped();
            m.Initialise();
            m.SnapToGround();

            m.ExternallyPosed = true;
            Vector3 parked = m.transform.position;
            Quaternion facing = m.transform.rotation;

            // A full-speed order, deliberately: the point is that a machine which is being posed by
            // something else ignores its own command channel entirely rather than merely being told
            // to stand still. A driver left running on a remote copy is exactly the case.
            for (int i = 0; i < 240; i++)
            {
                m.SetTwist(m.MaxSpeed, 45f);
                m.Step(1f / 60f);
                Physics.SyncTransforms();
            }

            Assert.AreEqual(parked, m.transform.position,
                "an externally posed machine moved itself; on a client this is what overwrites the " +
                "replicated pose every frame and strands the remote copy where it spawned");
            Assert.AreEqual(facing, m.transform.rotation,
                "an externally posed machine yawed itself");
        }

        [Test]
        public void AnExternallyPosedMachineStepsAgainstMotionItDidNotChoose()
        {
            TestMachine m = BuildQuadruped();
            m.Initialise();
            m.SnapToGround();

            m.ExternallyPosed = true;

            // What a NetworkTransform does: write the pose, then let everything else read it. No
            // SetTwist anywhere — the motor is disabled on a machine that does not own the entity,
            // so measured motion is the only thing the gait has to go on.
            float speed = m.MaxSpeed * 0.6f;
            float dt = 1f / 60f;
            int swingFrames = 0;

            for (int i = 0; i < 240; i++)
            {
                m.transform.position += m.transform.forward * (speed * dt);
                Physics.SyncTransforms();

                m.Step(dt);

                if (m.LastFrame.SwingingLegs > 0) swingFrames++;
            }

            Assert.Greater(swingFrames, 0,
                "the feet never left the ground while the body was carried 8+ metres — a remote " +
                "machine sliding along on still feet is the other half of this bug");
        }

        [Test]
        public void FollowingDoesNotFightThePoseItIsGiven()
        {
            TestMachine m = BuildQuadruped();
            m.Initialise();
            m.SnapToGround();

            m.ExternallyPosed = true;

            // One large jump, of the kind a spawn, a save restore or a chunk migration produces.
            Vector3 placed = m.transform.position + new Vector3(120f, 0f, -80f);
            m.transform.position = placed;
            Physics.SyncTransforms();

            m.Step(1f / 60f);

            Assert.AreEqual(placed, m.transform.position,
                "the follower moved a body it was handed");

            // The feet are of course left 140 m behind on the frame of the jump — that is not the
            // question. The question is whether they CATCH UP, which they do by stepping, under the
            // gait's own step-early rule. The clamp in FollowBody is what makes that possible: an
            // unclamped delta would wind the clock through whole cycles in one frame and throw
            // every foot at once, and the machine would arrive doing the splits.
            //
            // Standing still afterwards is the harder case deliberately. With no travel the clock
            // does not turn at all, so recovery rests entirely on the step-early rule rather than
            // on the machine happening to walk its feet back underneath itself.
            for (int i = 0; i < 300; i++)
            {
                m.transform.position = placed;
                Physics.SyncTransforms();
                m.Step(1f / 60f);
            }

            Assert.AreEqual(0, m.LastFrame.UnreachableLegs,
                "the legs never recovered from a teleport; a replicated machine would arrive at " +
                "every chunk migration stuck in the splits");
        }

        [Test]
        public void TakingTheBodyBackResumesFromWhereItActuallyIs()
        {
            TestMachine m = BuildQuadruped();
            m.Initialise();
            m.SnapToGround();

            // Ridden by somebody else for a while, then handed over — which is exactly what
            // NetAuthority does when a mount's ownership moves to the player climbing on.
            m.ExternallyPosed = true;
            Vector3 carriedTo = m.transform.position + new Vector3(30f, 0f, 12f);
            m.transform.position = carriedTo;
            Physics.SyncTransforms();
            m.Step(1f / 60f);

            m.ExternallyPosed = false;
            m.SetTwist(0f, 0f);
            m.Step(1f / 60f);
            Physics.SyncTransforms();

            // Height is the body's own business again the moment it takes over, so only the ground
            // plane is asserted — it is the channel a stale path would have teleported.
            Vector3 now = m.transform.position;
            Assert.AreEqual(carriedTo.x, now.x, 0.05f,
                "taking the transform back resumed a path integrated before the wire took over");
            Assert.AreEqual(carriedTo.z, now.z, 0.05f,
                "taking the transform back resumed a path integrated before the wire took over");
        }

        // ─────────── rig ───────────

        /// Four splayed legs, the same shape SyntheticMachineTests builds. Deliberately not the
        /// ostrich: what is under test is the base class every legged machine shares, so a rig with
        /// no art and no policy of its own is the honest subject.
        private TestMachine BuildQuadruped()
        {
            machine = new GameObject("Follower");
            machine.transform.position = new Vector3(0f, 2f, 0f);

            (Vector3 attach, float upper, float lower)[] legs =
            {
                (new Vector3(-1f, 0f, 1f), 1f, 1.4f),
                (new Vector3(1f, 0f, 1f), 1f, 1.4f),
                (new Vector3(-1f, 0f, -1f), 1f, 1.4f),
                (new Vector3(1f, 0f, -1f), 1f, 1.4f),
            };

            for (int i = 0; i < legs.Length; i++)
            {
                (Vector3 attach, float upper, float lower) = legs[i];
                Vector3 outward = new Vector3(attach.x, 0f, attach.z).normalized;
                Vector3 pitch = Vector3.Cross(Vector3.up, outward).normalized;

                Transform coxa = Joint("Coxa_" + i, machine.transform, attach, Vector3.up);
                Transform hip = Joint("Hip_" + i, coxa, Vector3.zero, pitch);
                Transform knee = Joint("Knee_" + i, hip,
                                       outward * upper * 0.7f - Vector3.up * upper * 0.3f, pitch);
                Transform ankle = Joint("Ankle_" + i, knee,
                                        outward * lower * 0.2f - Vector3.up * lower, pitch);
                Sole(ankle, -Vector3.up * 0.15f);
            }

            Physics.SyncTransforms();
            return machine.AddComponent<TestMachine>();
        }

        private Transform Joint(string name, Transform parent, Vector3 offset, Vector3 hinge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;

            var pin = new GameObject(name.Split('_')[0] + "Pin");
            pin.transform.SetParent(go.transform, false);
            pin.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, hinge.normalized);
            pin.transform.localScale = new Vector3(0.03f, 0.03f, 0.3f);
            pin.AddComponent<MeshFilter>().sharedMesh = cube;
            return go.transform;
        }

        private void Sole(Transform parent, Vector3 offset)
        {
            var go = new GameObject("SoleMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localScale = new Vector3(0.25f, 0.05f, 0.3f);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>();
        }
    }
}
