using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The arithmetic behind flipping the pack's front leaf up by hand.
    ///
    /// <para>
    /// Only the pure half is exercised here: the swing about the hinge and the screen-space
    /// progress. The click-versus-drag commit needs a pack, a camera and a mouse, none of which an
    /// EditMode test can honestly build.
    /// </para>
    /// </summary>
    public class PackLeafDragTests
    {
        // The rig's leaf hinge as expedition_rig authors it: a line along world X at the near edge
        // of the back panel, with the mat lying forward of it along +Z and PIVOT_Leaf turning -90
        // about its own X to stand the board up.
        private static readonly Vector3 HingeOrigin = new Vector3(0f, 0.05f, 0f);
        private static readonly Vector3 HingeAxis = Vector3.right;
        private const float RackDegrees = -90f;

        /// <summary>The middle of the board's free edge, 0.5 m out along the mat.</summary>
        private static readonly Vector3 Hem = new Vector3(0f, 0.05f, 0.5f);

        [Test]
        public void Swinging_TheHemThroughTheRack_StandsItUp()
        {
            Vector3 up = PackLeafDrag.Swing(Hem, HingeOrigin, HingeAxis, RackDegrees);

            // Unity turns left-handed, so -90 about +X takes +Z to +Y: a hem lying 0.5 m out along
            // the mat rises 0.5 m straight above the hinge. That is the rack -- the board vertical,
            // the free edge on top, which is also why the gesture reverses by dragging that same
            // edge back DOWN rather than by finding some other part of the rig to pull.
            Assert.AreEqual(0f, up.x, 1e-4f);
            Assert.AreEqual(HingeOrigin.y + 0.5f, up.y, 1e-4f, "the hem ends up above the hinge");
            Assert.AreEqual(HingeOrigin.z, up.z, 1e-4f, "and over it, not out in front of it");
        }

        [Test]
        public void TheSwing_PreservesTheDistanceFromTheHinge_AtEveryPointOfTheArc()
        {
            // The whole gesture rests on the grabbed point travelling a circle about the hinge: the
            // two ends of that circle are computed once, at the grab, and everything after is a
            // question about the segment between them. A swing that changed the radius would put
            // the leaf somewhere the drag never asked for.
            float radius = PackLeafDrag.RadiusFromHinge(Hem, HingeOrigin, HingeAxis);

            Assert.AreEqual(0.5f, radius, 1e-4f, "the fixture's hem is half a metre out");

            for (int i = 0; i <= 90; i++)
            {
                Vector3 p = PackLeafDrag.Swing(Hem, HingeOrigin, HingeAxis, -i);

                Assert.AreEqual(radius, PackLeafDrag.RadiusFromHinge(p, HingeOrigin, HingeAxis), 1e-3f,
                    $"the radius drifted at {i} degrees");
            }
        }

        [Test]
        public void APointOnTheHingeLine_DoesNotMove_HoweverFarTheLeafTurns()
        {
            // The hinge end of the board is the fixed end. It is also what stops a grab near the
            // fold from having any travel to speak of, which is why the grab band is at the hem.
            Vector3 onAxis = HingeOrigin + HingeAxis * 0.3f;
            Vector3 turned = PackLeafDrag.Swing(onAxis, HingeOrigin, HingeAxis, RackDegrees);

            Assert.Less(Vector3.Distance(onAxis, turned), 1e-4f);
        }

        [Test]
        public void ADegenerateAxis_HoldsThePointRatherThanReturningANaN()
        {
            // A rig with an unwired hinge axis. Unity carries a NaN position silently until it
            // reaches a Transform, at which point the leaf simply disappears.
            Vector3 held = PackLeafDrag.Swing(Hem, HingeOrigin, Vector3.zero, RackDegrees);

            Assert.AreEqual(Hem, held);
        }

        // ─────────── the drag itself ───────────
        //
        // Progress is measured on screen: the cursor's travel since the grab, projected onto the
        // segment between where the grabbed point sits with the leaf flat and where it sits with
        // the leaf racked. The fixture below is a leaf whose hem climbs 200 px up the screen as it
        // stands -- screen Y grows upward in Unity.

        private static readonly Vector2 FlatOnScreen = new Vector2(600f, 300f);
        private static readonly Vector2 RackedOnScreen = new Vector2(600f, 500f);

        private static float Progress(Vector2 cursor, float from = 0f) =>
            PackLeafDrag.Progress(FlatOnScreen, RackedOnScreen, FlatOnScreen, cursor, from);

        [Test]
        public void NotMovingTheCursor_LeavesTheLeafExactlyWhereItWasGrabbed()
        {
            Assert.AreEqual(0f, Progress(FlatOnScreen), 1e-5f);
            Assert.AreEqual(1f, Progress(RackedOnScreen, from: 1f), 1e-5f);
            Assert.AreEqual(0.37f, Progress(FlatOnScreen, from: 0.37f), 1e-5f);
        }

        [Test]
        public void DraggingUp_RaisesTheLeaf_AndDraggingBackDownLowersItAgain()
        {
            // The half that makes it a gesture rather than a button: it is reversible mid-drag, and
            // the leaf is wherever the cursor currently is rather than wherever it has been.
            Assert.AreEqual(0.5f, Progress(FlatOnScreen + new Vector2(0f, 100f)), 1e-4f);
            Assert.AreEqual(1f, Progress(FlatOnScreen + new Vector2(0f, 200f)), 1e-4f);

            // Past the top, and back below the bottom. Neither is allowed to fold the leaf through
            // itself; the arc is the whole of the travel there is.
            Assert.AreEqual(1f, Progress(FlatOnScreen + new Vector2(0f, 900f)), 1e-4f);
            Assert.AreEqual(0f, Progress(FlatOnScreen - new Vector2(0f, 900f)), 1e-4f);

            // Coming back down from a raised leaf lands on the same number the way up gave.
            Assert.AreEqual(0.25f,
                PackLeafDrag.Progress(FlatOnScreen, RackedOnScreen, RackedOnScreen,
                                      RackedOnScreen - new Vector2(0f, 150f), 1f), 1e-4f);
        }

        [Test]
        public void DraggingSquareToTheArc_DoesNothing()
        {
            // Sideways is not part of the gesture. Without the projection a player pulling the
            // cursor across the screen would flip the leaf, which is the difference between holding
            // an edge and having merely touched one.
            Assert.AreEqual(0f, Progress(FlatOnScreen + new Vector2(400f, 0f)), 1e-4f);
            Assert.AreEqual(0.5f, Progress(FlatOnScreen + new Vector2(-400f, 100f)), 1e-4f);
        }

        [Test]
        public void AnArcThatIsEdgeOnToTheCamera_HoldsTheLeafRatherThanDividingByZero()
        {
            // Both ends of the swing landing on one pixel. It carries no direction at all, so the
            // only honest answer is the one the leaf already had -- and the alternative is a
            // one-pixel jitter being read as the entire travel.
            var here = new Vector2(400f, 400f);

            Assert.AreEqual(0.6f,
                PackLeafDrag.Progress(here, here + new Vector2(1f, 1f), here,
                                      here + new Vector2(90f, 90f), 0.6f), 1e-5f);
        }

        [Test]
        public void TheCommitPoint_IsTheMiddleOfTheArc()
        {
            // Stated as a test because the release reads it and the hover hint reads it, and the
            // two saying different things would show as a leaf that flipped the way the label said
            // it would not.
            Assert.AreEqual(0.5f, PackLeafDrag.CommitAt, 1e-6f);
        }

        [Test]
        public void EveryFaceOfTheFrontFlap_CanBeGrabbed()
        {
            // The wings and the lash line are children of PIVOT_Leaf -- the whole front is one
            // flap -- so a bare point on any of them IS the board and flips it. Only the two
            // back-panel faces stay off limits: they do not move with the leaf at all.
            Assert.IsTrue(PackLeafDrag.IsLeafFace(PackSurfaceId.Leaf));
            Assert.IsTrue(PackLeafDrag.IsLeafFace(PackSurfaceId.Rack));
            Assert.IsTrue(PackLeafDrag.IsLeafFace(PackSurfaceId.LongGoods));
            Assert.IsTrue(PackLeafDrag.IsLeafFace(PackSurfaceId.WingLeft));
            Assert.IsTrue(PackLeafDrag.IsLeafFace(PackSurfaceId.WingRight));

            Assert.IsFalse(PackLeafDrag.IsLeafFace(PackSurfaceId.BackPanelLeft));
            Assert.IsFalse(PackLeafDrag.IsLeafFace(PackSurfaceId.BackPanelRight));
        }
    }
}
