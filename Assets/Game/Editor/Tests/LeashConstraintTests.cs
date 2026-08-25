// What a rope has to do, checked without a physics scene.
//
// The leash's constraint was wrong in a way nobody could see from the outside: it applied the whole
// positional error every physics step, shared nothing between its two ends, and had no ceiling — so
// it teleported kinematic bodies, and a large error produced an enormous force. Those are all
// properties of one small piece of arithmetic, and the arithmetic is pure, so it is pinned here
// rather than left to be judged by feel in play mode.
//
// The property that matters most is CONVERGENCE, and it is the one a combined velocity-and-position
// term cannot have: correcting a position error by adding velocity leaves that velocity behind on
// the next step, so the ends accelerate toward each other, sail through the right distance and
// collide. Hence two separate terms, and hence the first test.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class LeashConstraintTests
    {
        private const float Dt = 0.02f;             // a 50 Hz physics step
        private const float Correction = 0.35f;
        private const float MaxSpeed = 25f;
        private const float MaxStep = 0.5f;

        /// <summary>
        /// Step a one-dimensional rope, exactly as Leash resolves it: velocity only ever removed,
        /// the position error given back as a position. Reports the settled gap and how much it
        /// still moves once settled.
        /// </summary>
        private static (float gap, float swing) Settle(
            float massA, float massB, float length, float startGap,
            float driveA = 0f, int steps = 600)
        {
            float posA = 0f, posB = startGap, velA = 0f, velB = 0f;
            float low = float.MaxValue, high = float.MinValue;

            for (int i = 0; i < steps; i++)
            {
                // A walking player insists on their own velocity every step, which is what makes
                // them different from a crate and is the case the old rope could not hold.
                if (driveA != 0f) velA = -driveA;

                float stretch = posB - posA - length;
                if (stretch > 0f)
                {
                    float separation = velB - velA;      // rate the gap is opening
                    float shareA = Leash.ShareOf(massA, massB);
                    float shareB = Leash.ShareOf(massB, massA);

                    velA += Leash.ArrestSpeed(separation, shareA, MaxSpeed);
                    posA += Leash.CorrectionDistance(stretch, shareA, Correction, MaxStep);

                    velB -= Leash.ArrestSpeed(separation, shareB, MaxSpeed);
                    posB -= Leash.CorrectionDistance(stretch, shareB, Correction, MaxStep);
                }

                posA += velA * Dt;
                posB += velB * Dt;

                if (i >= steps - 100)
                {
                    low = Mathf.Min(low, posB - posA);
                    high = Mathf.Max(high, posB - posA);
                }
            }

            return (posB - posA, high - low);
        }

        [Test]
        public void AnOverstretchedRopeClosesToItsLengthAndStaysThere()
        {
            // Four metres too far apart with nothing else acting on either end. A convergent
            // constraint arrives at the length and stops; one that corrects position with velocity
            // arrives carrying the speed it built up and slams the two ends together.
            (float gap, float swing) = Settle(massA: 80f, massB: 80f, length: 8f, startGap: 12f);

            Assert.That(gap, Is.EqualTo(8f).Within(0.02f));
            Assert.That(swing, Is.LessThan(0.01f), "The rope must settle, not ring.");
        }

        [Test]
        public void ARopeHoldsAPlayerWhoKeepsWalkingIntoIt()
        {
            // The case the leash never once managed: a player on the ground, walking, restrained by
            // a rope to something immovable. They should be stopped at its length and stay there,
            // however long they lean on it.
            (float gap, float swing) = Settle(80f, Mathf.Infinity, length: 8f, startGap: 8f, driveA: 6f);

            Assert.That(gap, Is.EqualTo(8f).Within(0.05f));

            // The residue is one step of walking, arrested — not oscillation.
            Assert.That(swing, Is.LessThan(6f * Dt + 0.01f));
        }

        [Test]
        public void AWalkingPlayerTowsALighterObjectRatherThanBeingStopped()
        {
            (float gap, _) = Settle(80f, 20f, length: 8f, startGap: 8f, driveA: 6f, steps: 300);

            // The rope stays taut, which it can only do if the crate came along.
            Assert.That(gap, Is.EqualTo(8f).Within(0.05f));

            // And the lighter end is the one doing the moving.
            Assert.That(Leash.ShareOf(20f, 80f), Is.GreaterThan(Leash.ShareOf(80f, 20f)));
        }

        [Test]
        public void TheTwoSharesAddToExactlyOne()
        {
            // Anything else and the rope resolves more or less error than it actually has — over
            // one, and the two ends fight; under one, and the rope never closes.
            Assert.That(Leash.ShareOf(80f, 20f) + Leash.ShareOf(20f, 80f),
                        Is.EqualTo(1f).Within(1e-4f));

            // A rope to a wall: the wall does nothing and the player does everything, or a player
            // roped to the world would only ever be half-restrained.
            Assert.That(Leash.ShareOf(80f, Mathf.Infinity), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Leash.ShareOf(Mathf.Infinity, 80f), Is.Zero);
        }

        [Test]
        public void ASuddenlyDistantEndIsNotDraggedAcrossTheMap()
        {
            // An endpoint hundreds of metres away has been teleported, streamed in, or carried off
            // by a vehicle. Chasing that error at full rate is how the old rope slingshotted things.
            Assert.That(Leash.CorrectionDistance(stretch: 500f, share: 1f, Correction, MaxStep),
                        Is.EqualTo(MaxStep).Within(1e-4f));

            Assert.That(Leash.ArrestSpeed(separation: 400f, share: 1f, MaxSpeed),
                        Is.EqualTo(MaxSpeed).Within(1e-4f));
        }

        [Test]
        public void ASlackRopeDoesNothingAtAll()
        {
            // Below its length a rope must be completely absent, not weakly springy — otherwise a
            // leashed creature drifts toward its owner while the rope visibly hangs loose.
            Assert.That(Leash.CorrectionDistance(-2f, 1f, Correction, MaxStep), Is.Zero);
            Assert.That(Leash.ArrestSpeed(-5f, 1f, MaxSpeed), Is.Zero,
                        "Closing motion is never resisted: a rope pulls, it does not push.");
        }

        [Test]
        public void ALeashCanNeverMakeAPlayerFaster()
        {
            // The leash is not a grappling hook and must not become one by accident. The dangerous
            // case is not the player running into the rope — it is the rope's far end LEAVING, which
            // opens the gap just as fast and turns the arrest term into a tow.
            Vector3 standingStill = Vector3.zero;
            Vector3 toward = Vector3.forward;

            // A vehicle tears away at 40 m/s. Without the clamp this hands the player 25 m/s of free
            // speed along the rope, which is a launch.
            Vector3 launched = LeashEnd.Restrain(standingStill, toward, arrestSpeed: 25f);
            Assert.That(launched.magnitude, Is.EqualTo(0f).Within(1e-4f),
                "A rope may drag a standing player, but it must not give them speed.");

            // Mid-swing: moving fast sideways, rope pulling at right angles. Adding along the rope
            // would lengthen the vector — that is exactly how a pendulum is pumped.
            Vector3 across = new Vector3(12f, 0f, 0f);
            Vector3 swung = LeashEnd.Restrain(across, toward, arrestSpeed: 9f);
            Assert.That(swung.magnitude, Is.LessThanOrEqualTo(across.magnitude + 1e-3f));
        }

        [Test]
        public void ALeashStillTakesSpeedAwayFromAPlayerRunningIntoIt()
        {
            // The clamp must not have cost the leash its actual job. Running at the end of the rope
            // has to stop, or nothing restrains anyone.
            Vector3 runningOut = new Vector3(0f, 0f, -6f);       // away from the anchor
            Vector3 toward = Vector3.forward;                    // rope pulls back toward it

            Vector3 held = LeashEnd.Restrain(runningOut, toward, arrestSpeed: 6f);

            Assert.That(held.z, Is.EqualTo(0f).Within(1e-3f), "The outward run should be arrested.");
            Assert.That(held.magnitude, Is.LessThan(runningOut.magnitude));
        }

        [Test]
        public void NothingInTheLeashReachesForTheGrapplingHooksSwingSteering()
        {
            // PlayerMovement.SetTethered hands the body to the rope: the player can pump an arc,
            // keeps the speed they build across it, and takes no fall damage for the whole swing.
            // It is right for the grappling hook and it is the single call that would quietly turn
            // the leash back into one, so it is pinned at the source rather than in behaviour —
            // there is no runtime state to assert against, only the temptation to add it back.
            const string dir = "Assets/Game/Scripts/Items/Artifacts/Leash";
            Assert.That(Directory.Exists(dir), "Leash source folder moved — update this test.");

            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string source = File.ReadAllText(file);

                // The comment explaining why it is absent is allowed to name it; a call is not.
                Assert.That(source, Does.Not.Contain("SetTethered("),
                    Path.GetFileName(file) + " calls SetTethered. A leash restrains; it does not " +
                    "carry anyone. See the note at the top of LeashedBody.cs.");
            }
        }

        [Test]
        public void AimingAtARopeMeasuresTheGapToIt()
        {
            // A rope has no collider, so being able to click one is done against the drawn line.
            // Straight down the middle of a segment two metres in front of the eye.
            Vector3 origin = Vector3.zero;
            Vector3 forward = Vector3.forward;

            float gap = LeashRope.RayToSegment(origin, forward,
                                               new Vector3(-1f, 0f, 2f), new Vector3(1f, 0f, 2f),
                                               maxDistance: 50f,
                                               out float along, out Vector3 on);

            Assert.That(gap, Is.EqualTo(0f).Within(1e-3f), "The ray passes through this segment.");
            Assert.That(along, Is.EqualTo(2f).Within(1e-3f), "It crosses it two metres out.");
            Assert.That(on.z, Is.EqualTo(2f).Within(1e-3f));

            // Offset sideways by half a metre: that is exactly the gap.
            float offset = LeashRope.RayToSegment(origin, forward,
                                                  new Vector3(0.5f, 0f, 2f), new Vector3(3f, 0f, 2f),
                                                  50f, out _, out _);
            Assert.That(offset, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void ARopeBehindYouIsNotAimedAt()
        {
            // Solved as two infinite lines, a rope behind the player is as pickable as one in front
            // — you would untie ropes by looking away from them. The forward clamp is what prevents
            // that, and it is the kind of thing that only shows up when someone reports it.
            float gap = LeashRope.RayToSegment(Vector3.zero, Vector3.forward,
                                               new Vector3(-1f, 0f, -5f), new Vector3(1f, 0f, -5f),
                                               maxDistance: 50f,
                                               out float along, out _);

            Assert.That(along, Is.Zero, "Nothing behind the eye is in front of it.");
            Assert.That(gap, Is.EqualTo(5f).Within(1e-3f), "It is measured as five metres away.");
        }

        [Test]
        public void AimingIsClampedToTheSegmentAndToRange()
        {
            // Past the end of a segment the nearest point is its endpoint, not somewhere off in
            // space along the line it happens to lie on.
            float gap = LeashRope.RayToSegment(Vector3.zero, Vector3.forward,
                                               new Vector3(3f, 0f, 2f), new Vector3(5f, 0f, 2f),
                                               50f, out _, out Vector3 on);

            Assert.That(on, Is.EqualTo(new Vector3(3f, 0f, 2f)).Using(Vec), "Clamped to the near end.");
            Assert.That(gap, Is.EqualTo(3f).Within(1e-3f));

            // And a rope beyond the player's reach is measured from the end of that reach.
            LeashRope.RayToSegment(Vector3.zero, Vector3.forward,
                                   new Vector3(-1f, 0f, 80f), new Vector3(1f, 0f, 80f),
                                   maxDistance: 30f, out float along, out _);

            Assert.That(along, Is.EqualTo(30f).Within(1e-3f));
        }

        /// <summary>Vector3 comparison that tolerates float noise, for Is.EqualTo on positions.</summary>
        private static readonly System.Collections.IComparer Vec =
            System.Collections.Generic.Comparer<Vector3>.Create((x, y) => Vector3.Distance(x, y) < 1e-3f ? 0 : 1);

        [Test]
        public void SagIsZeroWhenTautAndHalfTheRopeWhenTheEndsMeet()
        {
            // The two ends the old ratio-based sag got wrong. A rope pulled straight must draw
            // straight, or nothing on screen distinguishes a taut rope from a slack one.
            Assert.That(LeashRope.SagDepth(span: 8f, length: 8f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(LeashRope.SagDepth(span: 0f, length: 8f), Is.EqualTo(4f).Within(1e-3f));

            // And in between it is monotone: more span, less droop.
            Assert.That(LeashRope.SagDepth(2f, 8f), Is.GreaterThan(LeashRope.SagDepth(6f, 8f)));
        }

        // ── What has to AGREE between two machines ─────────────────────────────
        //
        // A rope's shape and its break verdict both follow from two numbers fixed at the moment of
        // the tie: where the knot sits, and how much rope was paid out. Both used to be MEASURED on
        // each machine, at its own Present moment a relay apart — so a rope tied across anything
        // moving settled on a different knot and a different length everywhere, permanently. These
        // pin that both are now taken from the message instead.

        [Test]
        public void AKnotIsAnOffsetSoItRidesAMovingAnchor()
        {
            var target = new GameObject("runner");
            target.transform.position = Vector3.zero;

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, target, new Vector3(0f, 1f, 0.5f));

            Vector3 before = rope.A.Position;
            target.transform.position = new Vector3(10f, 0f, 0f);

            Assert.That(rope.A.Position, Is.EqualTo(before + new Vector3(10f, 0f, 0f)).Using(Vec),
                        "the knot did not ride the anchor, so two machines hold different parts of it");

            rope.Dispose();
            Object.DestroyImmediate(target);
        }

        [Test]
        public void APaidOutLengthIsTakenNotMeasured()
        {
            var target = new GameObject("post");

            var player = new GameObject("holder") { tag = "Player" };

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.PinEndTo(true, Vector3.zero);

            // The hand end has to exist before it can be moved onto anything — TieHandEndOnto acts
            // on whichever end IS in a hand and does nothing at all when neither is.
            rope.TieEndToHand(false, player, null);

            // Deliberately unrelated to the gap between the ends: the point is that the number
            // arrives rather than being derived from whatever this machine can currently see.
            rope.TieHandEndOnto(target, Vector3.zero, 12.5f);

            Assert.That(rope.Length, Is.EqualTo(12.5f).Within(1e-3f),
                        "the length the clicking machine settled on is not the length this one used");

            rope.Dispose();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void ATieNeverShortensARopeThatIsAlreadyLonger()
        {
            var target = new GameObject("post");

            var player = new GameObject("holder") { tag = "Player" };

            var rope = Leash.Create(new Leash.Settings { length = 16f, rope = new LeashRope() });
            rope.PinEndTo(true, Vector3.zero);
            rope.TieEndToHand(false, player, null);
            rope.TieHandEndOnto(target, Vector3.zero, 4f);

            Assert.That(rope.Length, Is.EqualTo(16f).Within(1e-3f),
                        "a short tie shrank a rope somebody had already paid out");

            rope.Dispose();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void ARopeIsFoundByAPointInItsAnchorsSpace()
        {
            // How an untie and a snap name a rope on a machine that did not click it. Addressed
            // against the anchor so the point rides whatever the rope is tied to: a bare world
            // point has left the tolerance by the time a peer looks at an animal running at 8 m/s.
            //
            // Leash.Nearest searches the LIVE registry, which OnEnable fills — and Unity does not
            // raise OnEnable for a runtime AddComponent outside play mode, so a rope built here is
            // never in it. What can be pinned without play mode is the ADDRESSING itself: that a
            // point resolved through an anchor rides that anchor, which is the whole property the
            // fix turns on.
            var runner = new GameObject("runner");
            runner.transform.position = Vector3.zero;

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, runner, new Vector3(0f, 1f, 0f));

            Vector3 clickedAt = rope.A.Position;
            Vector3 inAnchorSpace = runner.transform.InverseTransformPoint(clickedAt);

            // The target runs eight metres while the click is in flight — further than the one-metre
            // untie tolerance, which is exactly the case that used to lose the rope.
            runner.transform.position = new Vector3(8f, 0f, 0f);

            Assert.That(runner.transform.TransformPoint(inAnchorSpace),
                        Is.EqualTo(rope.A.Position).Using(Vec),
                        "the addressed point did not ride the anchor, so a peer would resolve it " +
                        "eight metres behind the rope and find nothing");

            Assert.That(Vector3.Distance(clickedAt, rope.A.Position), Is.GreaterThan(1f),
                        "test setup: the anchor must move further than the untie tolerance, or " +
                        "this passes for the wrong reason");

            rope.Dispose();
            Object.DestroyImmediate(runner);
        }

    }
}
