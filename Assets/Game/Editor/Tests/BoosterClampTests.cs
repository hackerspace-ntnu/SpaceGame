using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// What a strap-on booster will stick to, and what it will move.
    ///
    /// <para>
    /// Those are two different questions and used to be one, which is the bug these tests exist to
    /// keep fixed: while the clamp itself asked "would this move?", the item refused every surface
    /// in the game that does not move — the terrain, the settlement walls, the parked hulls — and a
    /// press on any of them did nothing at all. The clamp now takes anything; only the crosshair
    /// hint and the push still ask whether the thing goes anywhere.
    /// </para>
    /// </summary>
    public class BoosterClampTests
    {
        private GameObject scratch;

        [SetUp]
        public void SetUp() => scratch = new GameObject("BoosterClampTests");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(scratch);

        private GameObject New(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(scratch.transform);
            return go;
        }

        // ── What it sticks to ──────────────────────────────────────────────────

        [Test]
        public void PlainScenery_HasNoBody_WhichIsAWorldClamp()
        {
            GameObject rock = New("Rock");
            rock.AddComponent<BoxCollider>();

            Assert.IsNull(BoosterClamp.BodyFor(rock),
                          "Scenery carries neither a NetworkObject nor a Rigidbody, so there is no " +
                          "body to strap to. Null is the world clamp, not a refusal.");
        }

        [Test]
        public void ChildCollider_ResolvesToTheBodyAboveIt()
        {
            GameObject crate = New("Crate");
            crate.AddComponent<Rigidbody>();

            var lid = new GameObject("Lid");
            lid.transform.SetParent(crate.transform, false);
            lid.AddComponent<BoxCollider>();

            Assert.AreSame(crate.transform, BoosterClamp.BodyFor(lid),
                           "A ray lands on a lid, a hatch or a wheel; the booster rides the body.");
        }

        // ── What it moves ──────────────────────────────────────────────────────

        [Test]
        public void DynamicBody_Moves()
        {
            GameObject crate = New("Crate");
            crate.AddComponent<Rigidbody>();

            Assert.IsTrue(BoosterClamp.CanPush(crate.transform));
        }

        [Test]
        public void KinematicBody_DoesNot()
        {
            GameObject parked = New("ParkedHull");
            parked.AddComponent<Rigidbody>().isKinematic = true;

            Assert.IsFalse(BoosterClamp.CanPush(parked.transform),
                           "Force written to a kinematic body is discarded in silence. It still " +
                           "takes the clamp — it just does not go anywhere.");
        }

        [Test]
        public void TheWorld_DoesNot()
        {
            Assert.IsFalse(BoosterClamp.CanPush(null));
        }

        // ── How it sits ────────────────────────────────────────────────────────

        [Test]
        public void Seat_PointsTheBellOutOfTheSurface()
        {
            Vector3 exhaust = Vector3.forward;
            Vector3 normal = Vector3.up;

            Vector3 seated = BoosterClamp.Seat(exhaust, normal) * exhaust;

            Assert.AreEqual(0f, Vector3.Distance(seated, normal), 1e-4f,
                            "The exhaust leaves along the surface normal, so the thrust goes the " +
                            "other way — into whatever it is stuck to.");
        }

        [Test]
        public void Seat_OnAnUpwardFace_IsNotDegenerate()
        {
            // FromToRotation and not LookRotation: the commonest clamp in the game is onto the top
            // of something, where LookRotation's default up vector is the same axis and answers
            // with an error and an identity rotation.
            Quaternion seat = BoosterClamp.Seat(Vector3.forward, Vector3.up);

            Assert.AreEqual(0f, Vector3.Distance(seat * Vector3.forward, Vector3.up), 1e-4f);
        }
    }
}
