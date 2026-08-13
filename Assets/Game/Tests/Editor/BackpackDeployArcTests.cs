using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class BackpackDeployArcTests
    {
        // A deploy as the controller actually configures it: off the back socket at chest height on
        // a 2 m player, down to a drop point 0.9 m in front (deployDistance), with the shipped
        // arcHeight and arcOutward.
        private static readonly Pose Shouldered =
            new Pose(new Vector3(0f, 1.3f, 0f), Quaternion.identity);

        private static readonly Pose Grounded =
            new Pose(new Vector3(0f, 0.1f, 0.9f), Quaternion.Euler(-90f, 35f, 0f));

        private const float ArcHeight = 0.45f;
        private const float Outward = 0.35f;

        /// The path has to be swept, not spot-checked: the pack is kinematic, so a single frame
        /// spent under the surface is a frame of the pack buried in a dune, and a midpoint-only
        /// test misses every dip that is not exactly halfway.
        private const int Samples = 1000;

        private static float T(int i) => i / (float)Samples;

        // ─────────── endpoints ───────────

        [Test]
        public void TheEndpoints_AreReproducedExactly()
        {
            // The pack is re-parented on the frame the arc reaches its end -- unparented from the
            // back socket at t == 0, handed to the world at t == 1. Float residue at either end
            // shows as a visible pop on the frame the parenting changes hands.
            Pose atStart = BackpackDeployArc.Evaluate(Shouldered, Grounded, 0f, ArcHeight, Outward);
            Pose atEnd = BackpackDeployArc.Evaluate(Shouldered, Grounded, 1f, ArcHeight, Outward);

            Assert.AreEqual(Shouldered.position, atStart.position);
            Assert.AreEqual(Shouldered.rotation, atStart.rotation);
            Assert.AreEqual(Grounded.position, atEnd.position);
            Assert.AreEqual(Grounded.rotation, atEnd.rotation);
        }

        [Test]
        public void TIsClamped_NotExtrapolated()
        {
            // A caller driving t from elapsed/duration overshoots on the frame that finishes the
            // swing. Extrapolating there would fling the pack past the drop point.
            Assert.AreEqual(Shouldered.position,
                BackpackDeployArc.Evaluate(Shouldered, Grounded, -3f, ArcHeight, Outward).position);
            Assert.AreEqual(Grounded.position,
                BackpackDeployArc.Evaluate(Shouldered, Grounded, 4f, ArcHeight, Outward).position);
            Assert.AreEqual(Grounded.rotation,
                BackpackDeployArc.Evaluate(Shouldered, Grounded, 1.0001f, ArcHeight, Outward).rotation);
        }

        // ─────────── the ground ───────────

        [Test]
        public void NoPointOnThePath_EverDipsBelowTheLowerEndpoint()
        {
            AssertNeverDips(Shouldered, Grounded, ArcHeight, Outward, "the deploy");
            AssertNeverDips(Grounded, Shouldered, ArcHeight, Outward, "the re-shoulder, same curve backwards");

            // Downhill: the drop point is below the player's feet, so the straight line already
            // descends and the arc has room to sag under it.
            var downhill = new Pose(new Vector3(0f, 4.2f, 0f), Quaternion.identity);
            var below = new Pose(new Vector3(0f, 0.0f, 1.4f), Quaternion.Euler(-90f, 0f, 0f));
            AssertNeverDips(downhill, below, ArcHeight, Outward, "downhill deploy");
            AssertNeverDips(below, downhill, ArcHeight, Outward, "uphill re-shoulder");

            // Level, which is the case a convex-hull argument alone does not save: with a negative
            // arc the control point sits below both ends and drags the whole curve under them.
            var left = new Pose(new Vector3(-2f, 1.3f, 0f), Quaternion.identity);
            var right = new Pose(new Vector3(2f, 1.3f, 0f), Quaternion.identity);
            AssertNeverDips(left, right, -0.5f, 0f, "level path with a sagging arc");
            AssertNeverDips(Shouldered, Grounded, -1.5f, Outward, "deploy with a sagging arc");
        }

        private static void AssertNeverDips(Pose start, Pose end, float arcHeight, float outward, string because)
        {
            float floor = Mathf.Min(start.position.y, end.position.y);

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 p = BackpackDeployArc.Evaluate(start, end, T(i), arcHeight, outward).position;

                Assert.GreaterOrEqual(p.y, floor - 1e-4f, $"{because}: dipped at t = {T(i)}");
            }
        }

        // ─────────── the bow ───────────

        [Test]
        public void PositiveArcHeight_LiftsTheMidpointAboveTheStraightLine()
        {
            // Without the lift the pack travels straight from the back socket to the ground, which
            // is a line through the player's own chest.
            Vector3 straight = Vector3.Lerp(Shouldered.position, Grounded.position, 0.5f);
            Vector3 arced = BackpackDeployArc.Evaluate(Shouldered, Grounded, 0.5f, ArcHeight, 0f).position;

            Assert.Greater(arced.y, straight.y);

            // A zero arc must be exactly the straight line, so the lift above is arcHeight doing
            // the work rather than some incidental shape in the curve.
            Vector3 flat = BackpackDeployArc.Evaluate(Shouldered, Grounded, 0.5f, 0f, 0f).position;
            Assert.AreEqual(straight.x, flat.x, 1e-4f);
            Assert.AreEqual(straight.y, flat.y, 1e-4f);
            Assert.AreEqual(straight.z, flat.z, 1e-4f);
        }

        [Test]
        public void Outward_BowsThePathSquareToTheHorizontalRun()
        {
            // This is the term that swings the pack round the player's side. Bowing it along the
            // run instead would only make it arrive early, and bowing it vertically would just be
            // a second arcHeight.
            Vector3 straight = Vector3.Lerp(Shouldered.position, Grounded.position, 0.5f);
            Vector3 bowed = BackpackDeployArc.Evaluate(Shouldered, Grounded, 0.5f, 0f, Outward).position;
            Vector3 offset = bowed - straight;

            Vector3 run = Grounded.position - Shouldered.position;
            run.y = 0f;

            Assert.AreEqual(0f, offset.y, 1e-4f, "the bow is horizontal; height is arcHeight's job");
            Assert.AreEqual(0f, Vector3.Dot(offset, run.normalized), 1e-4f, "and square to the run");

            // A quadratic Bezier reaches half its control-point offset at the midpoint.
            Assert.AreEqual(Outward * 0.5f, offset.magnitude, 1e-4f);
        }

        [Test]
        public void AStraightDrop_StaysFinite_ThoughItHasNoHorizontalRunToTakeAPerpendicularFrom()
        {
            // Normalising a zero-length run hands back a NaN, and Unity carries a NaN position
            // silently until it reaches a Transform -- at which point the pack simply disappears.
            var top = new Pose(new Vector3(3f, 2f, -1f), Quaternion.identity);
            var bottom = new Pose(new Vector3(3f, 0f, -1f), Quaternion.Euler(90f, 0f, 0f));

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 p = BackpackDeployArc.Evaluate(top, bottom, T(i), ArcHeight, Outward).position;

                Assert.IsFalse(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z), $"NaN at t = {T(i)}");
                Assert.IsFalse(float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z),
                    $"infinity at t = {T(i)}");
            }
        }

        [Test]
        public void AZeroLengthPath_HoldsItsPosition_ForEveryT()
        {
            // Re-shouldering a pack you are standing on top of. There is no run to bow away from,
            // and arcing anyway would shove the pack out sideways and back again for no reason.
            var here = new Pose(new Vector3(4f, 2f, -7f), Quaternion.Euler(10f, 20f, 30f));
            var alsoHere = new Pose(here.position, Quaternion.Euler(-40f, 90f, 0f));

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 p = BackpackDeployArc.Evaluate(here, alsoHere, T(i), ArcHeight, Outward).position;

                Assert.Less(Vector3.Distance(here.position, p), 1e-5f, $"drifted at t = {T(i)}");
            }
        }

        // ─────────── the rotation ───────────

        [Test]
        public void Rotation_EasesInAndOut_RatherThanTurningAtAConstantRate()
        {
            // Linear t would have the pack snap into its turn on the frame the swing starts. The
            // slerp runs at constant angular rate, so the angle swept is exactly smoothstep(t) of
            // the total and can be read straight off Quaternion.Angle.
            float total = Quaternion.Angle(Shouldered.rotation, Grounded.rotation);
            Assert.Greater(total, 1f, "the fixture needs a real turn for this to measure anything");

            float quarter = SweptAt(0.25f);
            float half = SweptAt(0.5f);
            float threeQuarters = SweptAt(0.75f);

            Assert.AreEqual(total * 0.5f, half, 1e-2f, "smoothstep is symmetric about the midpoint");
            Assert.Less(quarter, total * 0.25f, "still peeling off the back");
            Assert.Greater(threeQuarters, total * 0.75f, "and settling onto the ground");
        }

        private static float SweptAt(float t) => Quaternion.Angle(
            Shouldered.rotation,
            BackpackDeployArc.Evaluate(Shouldered, Grounded, t, ArcHeight, Outward).rotation);

        [Test]
        public void Rotation_TurnsMonotonicallyFromStartToEnd()
        {
            float previous = -1f;

            for (int i = 0; i <= Samples; i++)
            {
                float swept = SweptAt(T(i));

                Assert.GreaterOrEqual(swept, previous - 1e-3f, $"the pack turned back on itself at t = {T(i)}");
                previous = swept;
            }
        }

        // ─────────── purity ───────────

        [Test]
        public void Evaluate_IsPure_SoTheSameTAlwaysGivesTheSamePose()
        {
            // The arc holds no state and reads no clock: the controller replays it backwards to
            // re-shoulder, and a replicated deploy would drive it from a networked t.
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                Pose first = BackpackDeployArc.Evaluate(Shouldered, Grounded, t, ArcHeight, Outward);

                // Interleave a differently-configured call, which a cached or accumulated
                // implementation would let leak into the next sample.
                BackpackDeployArc.Evaluate(Grounded, Shouldered, 1f - t, -2f, -1f);

                Pose second = BackpackDeployArc.Evaluate(Shouldered, Grounded, t, ArcHeight, Outward);

                Assert.AreEqual(first.position, second.position, $"at t = {t}");
                Assert.AreEqual(first.rotation, second.rotation, $"at t = {t}");
            }
        }
    }
}
