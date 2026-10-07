// Worn gear rides the bone it was seated on, and keeps riding it.
//
// Two failures live here, and they are opposites. A gauntlet is seated from the wearer's own
// anatomy, so its pose is PINNED: nothing outside may move it. A torso item's position comes off
// the pack's lash rail, which comes and goes with every deploy — so its pose is RE-DERIVED, because
// reading the rail once at wear time made the pose a snapshot of a relationship that then changed
// underneath it, with nothing re-seating and nothing logged.
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class WornAnchorTests
    {
        private GameObject bone;
        private GameObject instance;
        private GameObject mount;

        private static readonly Vector3 Seated = new(0.01f, 0.42f, -0.03f);
        private static readonly Vector3 Mirrored = new(-1f, 1f, 1f);

        /// <summary>The fit's fallback: where back gear sits with no pack on the back.</summary>
        private static readonly Vector3 Fallback = new(0f, 0.06f, -0.2f);

        [SetUp]
        public void SetUp()
        {
            bone = new GameObject("Spine");
            bone.transform.SetPositionAndRotation(new Vector3(3790f, 102f, 1598f),
                                                  Quaternion.Euler(55f, 101f, 354f));

            instance = new GameObject("Gear");
            instance.transform.SetParent(bone.transform, false);

            // The lash rail, out behind the spine where the real one sits.
            mount = new GameObject("Mesh_Rig_LashRail");
            mount.transform.SetParent(bone.transform, false);
            mount.transform.localPosition = new Vector3(0.003f, 0.630f, -0.522f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(mount);
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(bone);
        }

        private WornAnchor Anchor => instance.GetComponent<WornAnchor>();

        private WornFit Fit(Vector3 localPosition, bool anchorToBone = false)
        {
            WornFit fit = instance.GetComponent<WornFit>();
            if (fit == null) fit = instance.AddComponent<WornFit>();

            var so = new SerializedObject(fit);
            so.FindProperty("localPosition").vector3Value = localPosition;
            so.FindProperty("anchorToBone").boolValue = anchorToBone;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fit;
        }

        // ── Gauntlets: pinned ────────────────────────────────────────────────────

        private void Pin()
        {
            instance.transform.localPosition = Seated;
            instance.transform.localRotation = Quaternion.Euler(20f, 30f, 40f);
            instance.transform.localScale = Mirrored;
            WornAnchor.Pin(instance, bone.transform);
        }

        [Test]
        public void APinnedItemDoesNotKeepAForeignPositionWrite()
        {
            Pin();

            // What "the gauntlet moved metres away from the body" looks like from here.
            instance.transform.position += new Vector3(4f, -2f, 7f);
            Anchor.Reassert();

            Assert.AreEqual(Seated, instance.transform.localPosition);
        }

        [Test]
        public void APinnedItemDoesNotKeepAForeignRotationWrite()
        {
            Pin();
            Quaternion seated = instance.transform.localRotation;

            instance.transform.rotation = Quaternion.Euler(11f, 222f, 333f);
            Anchor.Reassert();

            Assert.Less(Quaternion.Angle(seated, instance.transform.localRotation), 0.01f);
        }

        [Test]
        public void APinnedItemKeepsItsMirroredScale()
        {
            Pin();

            // The left arm wears a mirrored gauntlet. Losing the sign turns the cuff inside out.
            instance.transform.localScale = Vector3.one;
            Anchor.Reassert();

            Assert.AreEqual(Mirrored, instance.transform.localScale);
        }

        [Test]
        public void APinnedItemStillFollowsTheBone()
        {
            Pin();

            bone.transform.position += new Vector3(10f, 0f, 10f);
            bone.transform.rotation *= Quaternion.Euler(0f, 90f, 0f);

            Vector3 expected = bone.transform.TransformPoint(Seated);
            Anchor.Reassert();

            Assert.Less(Vector3.Distance(expected, instance.transform.position), 1e-4f);
        }

        // ── Torso gear: re-derived ───────────────────────────────────────────────

        // The bug, in the direction players hit most: put the jetpack on while the pack is off your
        // back, then shoulder the pack. The rail only exists from that moment, and the seat had
        // already run — so the gear used to stay at the fallback, 0.65 m off the rail, for good.
        [Test]
        public void AMountArrivingAfterTheSeatIsPickedUp()
        {
            WornFit fit = Fit(Fallback);
            Transform live = null;

            WornSeat.Pose(instance.transform, fit, null);
            WornAnchor.Follow(instance, bone.transform, fit, () => live);

            Assert.AreEqual(Fallback, instance.transform.localPosition, "seated without a mount");

            live = mount.transform;
            Anchor.Reassert();

            Assert.Less(Vector3.Distance(mount.transform.position, instance.transform.position), 1e-4f,
                        "The pack came home and the gear did not move onto its rail.");
        }

        // The other direction: worn with the pack on, then the pack is deployed. The rail goes away,
        // and the gear used to stay hanging where the rail had been.
        [Test]
        public void AMountLeavingFallsBackToTheFit()
        {
            WornFit fit = Fit(Fallback);
            Transform live = mount.transform;

            WornSeat.Pose(instance.transform, fit, live);
            WornAnchor.Follow(instance, bone.transform, fit, () => live);

            live = null;
            Anchor.Reassert();

            Assert.AreEqual(Fallback, instance.transform.localPosition,
                            "The pack was deployed and the gear stayed where its rail had been.");
        }

        [Test]
        public void AnchorToBoneIgnoresTheMountEntirely()
        {
            WornFit fit = Fit(Fallback, anchorToBone: true);
            WornAnchor.Follow(instance, bone.transform, fit, () => mount.transform);

            Anchor.Reassert();

            Assert.AreEqual(Fallback, instance.transform.localPosition);
        }

        [Test]
        public void AFollowedItemDoesNotKeepAForeignWrite()
        {
            WornFit fit = Fit(Fallback);
            WornAnchor.Follow(instance, bone.transform, fit, () => null);

            instance.transform.position += new Vector3(3f, 1f, -2f);
            Anchor.Reassert();

            Assert.AreEqual(Fallback, instance.transform.localPosition);
        }

        // ── Reporting ────────────────────────────────────────────────────────────

        [Test]
        public void GearThatEndedUpNowhereNearTheBodyIsReported()
        {
            // A mount that is not on the wearer at all — the shape of every "my jetpack vanished"
            // report, and previously invisible.
            var stray = new GameObject("StrayRail");
            stray.transform.position = bone.transform.position + new Vector3(0f, 0f, 40f);
            try
            {
                WornFit fit = Fit(Fallback);
                WornAnchor.Follow(instance, bone.transform, fit, () => stray.transform);

                LogAssert.Expect(LogType.Warning, new Regex("WornAnchor.*further than worn gear can sit"));
                Anchor.Reassert();

                // Said once, not once a frame.
                Anchor.Reassert();
            }
            finally
            {
                Object.DestroyImmediate(stray);
            }
        }

        [Test]
        public void DetachedGearIsReportedOnceAndNotSilentlyReparented()
        {
            Pin();
            instance.transform.SetParent(null, true);

            LogAssert.Expect(LogType.Warning, new Regex("WornAnchor.*no longer parented"));
            Anchor.Reassert();

            // Said once: a per-frame retry would fight NetworkObject, which reverts a re-parent on
            // an unspawned instance and logs an exception each time it does.
            Anchor.Reassert();

            Assert.IsNull(instance.transform.parent);
        }
    }
}
