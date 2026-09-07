// Where an item goes when the view is the player's OWN camera, moved off the eye.
//
// The mounted case has its own tests: there the view is a second camera, metres behind the craft,
// and AimProvider is told about it. This is the case that looks like the first-person one and is
// not — the jetpack steps the player's own lens back over their shoulder, so the eye and the view
// are one object at two places, and "the eye IS the view, there is no parallax" quietly stops
// being true. Left alone the ray leaves a point behind the player pointing straight ahead: it runs
// through their own back, and every shot lands where the crosshair is not.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class ShoulderViewAimTests
    {
        private GameObject player;
        private GameObject eyeRest;
        private GameObject target;
        private AimProvider aim;
        private Camera lens;

        [SetUp]
        public void SetUp()
        {
            // A player standing at the origin facing +Z, with the lens where a first-person eye is.
            player = new GameObject("player", typeof(AimProvider));

            var lensObject = new GameObject("lens", typeof(Camera));
            lensObject.transform.SetParent(player.transform, false);
            lensObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            lens = lensObject.GetComponent<Camera>();

            aim = player.GetComponent<AimProvider>();
            typeof(AimProvider)
                .GetField("playerCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(aim, lens);

            // The anchor JetpackThirdPerson hands over: a child of the lens, standing where the
            // lens rested before it was stepped away.
            eyeRest = new GameObject("eye rest");
            eyeRest.transform.SetParent(lensObject.transform, false);
            eyeRest.transform.position = lensObject.transform.position;

            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { target, player })
                if (go != null) Object.DestroyImmediate(go);
            target = player = null;
        }

        /// <summary>Step the lens back, up and off the right shoulder, the way the jetpack does.</summary>
        private void StepTheLensOverTheShoulder()
        {
            lens.transform.localPosition += new Vector3(1.35f, 0.9f, -4.2f);
            eyeRest.transform.position = player.transform.position + new Vector3(0f, 1.6f, 0f);
            Physics.SyncTransforms();
        }

        /// <summary>A 1 m cube 30 m ahead, under the stepped-back lens's centre line.</summary>
        private void PlaceTargetUnderTheCrosshair()
        {
            target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "target";
            target.transform.position = lens.transform.position + lens.transform.forward * 30f;
            Physics.SyncTransforms();
        }

        [Test]
        public void TheAimLeavesTheEyeRatherThanTheSteppedBackLens()
        {
            StepTheLensOverTheShoulder();
            aim.SetEyeAnchor(eyeRest.transform);

            Ray ray = aim.GetAimRay();

            Assert.AreEqual(eyeRest.transform.position.z, ray.origin.z, 0.001f,
                "Firing from the shoulder camera spawns every dart, net and beam four metres " +
                "behind the player, where it promptly hits their own back.");
        }

        [Test]
        public void TheAimLandsOnWhatTheCrosshairCovers()
        {
            StepTheLensOverTheShoulder();
            PlaceTargetUnderTheCrosshair();
            aim.SetEyeAnchor(eyeRest.transform);

            Assert.IsTrue(aim.TryGetAimHit(60f, out RaycastHit hit),
                "The crosshair is over a cube 30 m away and the aim reported nothing at all.");
            Assert.AreSame(target, hit.collider.gameObject,
                "Without the anchor the ray runs parallel to the view from a metre and a third to " +
                "its left, so it misses everything the crosshair is actually on.");
        }

        [Test]
        public void ClearingTheAnchorGivesTheAimBackToTheLens()
        {
            aim.SetEyeAnchor(eyeRest.transform);
            aim.ClearEyeAnchor();

            Ray ray = aim.GetAimRay();

            Assert.AreEqual(lens.transform.position, ray.origin,
                "First person is the identity case: the eye IS the view, nothing is cast, and a " +
                "landing must leave the aim exactly as it found it.");
            Assert.AreEqual(lens.transform.forward, ray.direction);
        }
    }
}
