using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    public class BackpackDeployArcTests
    {
        // The over-the-shoulder flight as the controller actually configures it — since the deploy
        // became a toss from in front of the chest, this chord is the STOW's, flown ground-to-back.
        // Player standing at the origin facing +Z: the back socket at spine height and a little
        // behind the spine at one end, a drop point in front at the other, with the shipped
        // arcHeight and arcOutward. Evaluate is direction-agnostic, so the sweep is tested in the
        // deploy's old orientation unchanged.
        private static readonly Pose Shouldered =
            new Pose(new Vector3(0f, 1.15f, -0.18f), Quaternion.identity);

        private static readonly Pose Grounded =
            new Pose(new Vector3(0f, 0.01f, 1.6f), Quaternion.Euler(-90f, 35f, 0f));

        private const float ArcHeight = 2.6f;
        private const float Outward = 0.55f;

        // ─────────── the player the pack is thrown over ───────────
        //
        // A standing astronaut, in the frame above: the spine is the vertical line through the
        // origin and these are the heights and the girth measured off it.
        private const float HeadTop = 1.80f;
        private const float ShoulderHeight = 1.45f;
        private const float BodyRadius = 0.30f;

        /// <summary>Half the shoulder span. What "a shoulder's offset" has to beat to mean anything.</summary>
        private const float ShoulderHalfWidth = 0.22f;

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

        // ─────────── the toss ───────────
        //
        // The deploy no longer flies over the shoulder: it appears at chest height in front of the
        // player and drops at their feet. The claim worth pinning is the reason it was changed —
        // no sample of the flight is ever behind the player, or inside them.

        [Test]
        public void TheToss_StaysInFrontOfThePlayerForTheWholeFlight()
        {
            // The shipped toss numbers: tossStartForward 0.45, tossStartHeight 1.25, landing
            // deployDistance 2.4 m out, tossArcHeight 0.5, no outward bow.
            var tossStart = new Pose(new Vector3(0f, 1.25f, 0.45f), Grounded.rotation);
            var tossEnd = new Pose(new Vector3(0f, 0.01f, 2.4f), Grounded.rotation);

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 p = BackpackDeployArc.Evaluate(tossStart, tossEnd, T(i), 0.5f, 0f).position;

                Assert.Greater(p.z, BodyRadius,
                    $"the toss went inside or behind the player at t = {T(i)}");
            }

            AssertNeverDips(tossStart, tossEnd, 0.5f, 0f, "the toss");
        }

        // ─────────── over the shoulder ───────────
        //
        // The claim the deploy makes is that the pack goes up and OVER one shoulder, clears the
        // head, and lands in front. That is three checkable statements about a pure function, and
        // the numbers on BackpackController were picked to satisfy them rather than by eye.

        [Test]
        public void TheApex_ClearsAStandingPlayersHead()
        {
            // Height along the arc is y(t) = h0(1-t) + 2A t(1-t) -- the run contributes nothing,
            // because the control point sits exactly at the midpoint along it. That peaks at
            // t = (2A - h0) / 4A with apex (h0 + A) / 2 + h0^2 / 8A, which for a 1.15 m socket and
            // A = 2.6 is 1.94 m at t = 0.39.
            Sweep(out float apex, out float apexAt, out _);

            Assert.Greater(apex, HeadTop,
                "the pack has to pass over the player's head, not through their chest");

            // A throw peaks early and falls further than it rose. Peaking in the second half would
            // read as the pack being lifted onto the sand rather than lobbed at it.
            Assert.Less(apexAt, 0.5f, "the apex belongs in the first half of the flight");
            Assert.Greater(apexAt, 0.2f, "and not so early that the pack leaves the back sideways");
        }

        [Test]
        public void AnArcHeightNearHalfTheSocketHeight_DoesNotRiseInAnyVisibleSense()
        {
            // The bug this replaced, kept as a test because it was invisible on screen. The peak of
            // y(t) = h0(1-t) + 2A t(1-t) is at t = (2A - h0) / 4A, which is at or below zero for any
            // A up to h0/2 -- so with the shipped arcHeight of 0.6 out of a 1.15 m socket the pack's
            // whole "arc" was half a millimetre of climb and then a slow fall through the chest.
            // Every frame of it looked arced, because the sideways bow was doing all the work.
            const float Old = 0.6f;

            float start = Shouldered.position.y;
            float peak = float.MinValue;

            for (int i = 0; i <= Samples; i++)
                peak = Mathf.Max(peak, BackpackDeployArc.Evaluate(
                    Shouldered, Grounded, T(i), Old, Outward).position.y);

            Assert.Less(peak - start, 0.01f,
                $"arcHeight {Old} climbed {peak - start} m, which is not what it was believed to do");

            Assert.Less(peak, ShoulderHeight, "and it never reached the shoulder, let alone the head");
        }

        [Test]
        public void ThePath_LeavesTheBodyUpward_AndNeverSweepsBackThroughTheTorso()
        {
            // "Over the shoulder" is a statement about the player's own body, so this is the one
            // test that measures against it. Two things have to hold while the pack is still within
            // a body's radius of the spine: it must never be lower than the socket it left -- that
            // is the sweep through the chest -- and by the time it clears the body it must be above
            // shoulder height, because that is what going over a shoulder means.
            float socket = Shouldered.position.y;
            float lastInside = -1f;
            float heightLeaving = 0f;

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 p = BackpackDeployArc.Evaluate(Shouldered, Grounded, T(i), ArcHeight, Outward).position;

                if (Horizontal(p) >= BodyRadius) continue;

                Assert.GreaterOrEqual(p.y, socket - 1e-4f,
                    $"the pack sank to {p.y} while still over the player, at t = {T(i)}");

                lastInside = T(i);
                heightLeaving = p.y;
            }

            Assert.Greater(lastInside, 0f, "the fixture starts inside the body; a socket is on a back");

            Assert.Greater(heightLeaving, ShoulderHeight,
                $"the pack left the player's own girth at {heightLeaving} m, below the shoulder");

            // Early, too. Reaching shoulder height only once the pack is already a metre out in
            // front is not going over a shoulder, it is being pushed off a table.
            Assert.Less(lastInside, 0.4f, "the pack should be clear of the player early in the flight");
        }

        [Test]
        public void TheBow_CarriesThePackAShouldersWidthOffTheSpine()
        {
            // The sideways term is what picks a shoulder rather than the top of the head. It has to
            // be worth a shoulder: a quadratic Bezier reaches half its control-point offset, so the
            // 0.55 the controller ships is 0.275 m of actual clearance.
            Sweep(out _, out _, out float bow);

            Assert.Greater(bow, ShoulderHalfWidth,
                "the pack is meant to pass beside the head, not skim the top of it");

            Assert.AreEqual(Outward * 0.5f, bow, 1e-3f, "and that is exactly half the control offset");
        }

        [Test]
        public void ThePack_StillLandsInFrontOfThePlayer()
        {
            // The throw is not allowed to have moved the drop point. Everything above is about the
            // journey; this is the thing the player asked for.
            Vector3 end = BackpackDeployArc.Evaluate(Shouldered, Grounded, 1f, ArcHeight, Outward).position;

            Assert.AreEqual(Grounded.position, end);
            Assert.Greater(end.z, 1f, "in front, along the deploy direction");
            Assert.Less(Mathf.Abs(end.x), 1e-4f, "and square on, whatever the arc did on the way");
        }

        /// <summary>Horizontal distance from the spine, which the fixture puts on the world Y axis.</summary>
        private static float Horizontal(Vector3 p) => new Vector2(p.x, p.z).magnitude;

        /// <summary>One pass over the shipped arc, reporting the peak and the widest bow.</summary>
        private static void Sweep(out float apex, out float apexAt, out float bow)
        {
            apex = float.MinValue;
            apexAt = 0f;
            bow = 0f;

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 p = BackpackDeployArc.Evaluate(Shouldered, Grounded, T(i), ArcHeight, Outward).position;

                if (p.y > apex) { apex = p.y; apexAt = T(i); }

                // The run is along +Z in this fixture, so the bow square to it is the world X.
                bow = Mathf.Max(bow, Mathf.Abs(p.x));
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
