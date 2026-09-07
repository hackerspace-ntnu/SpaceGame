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
            float driveA = 0f, int steps = 600,
            float pullA = 0f, float pullB = 0f)
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

                    float capA = Leash.TowCap(pullB - pullA, massA);
                    float capB = Leash.TowCap(pullA - pullB, massB);

                    velA += Leash.ArrestSpeed(separation, shareA, MaxSpeed);
                    posA += Mathf.Min(Leash.CorrectionDistance(stretch, shareA, Correction, MaxStep),
                                      capA * Dt);

                    velB -= Leash.ArrestSpeed(separation, shareB, MaxSpeed);
                    posB -= Mathf.Min(Leash.CorrectionDistance(stretch, shareB, Correction, MaxStep),
                                      capB * Dt);
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

        // ── The contest ────────────────────────────────────────────────────────
        //
        // What replaced Restrain, the clamp that capped every pull at the speed the body already
        // had. It made a rope unable to move anything standing still, which is most of what a
        // leash is for. The tow it forbade is now bounded by force over mass instead.

        [Test]
        public void PullIsMassTimesTopSpeedAndAStaticAnchorHasNone()
        {
            // A heavy slow thing and a light fast thing can be evenly matched. That is the whole
            // point of multiplying rather than picking one of them.
            Assert.That(Leash.PullOf(80f, 6f), Is.EqualTo(480f).Within(1e-3f));
            Assert.That(Leash.PullOf(120f, 9f), Is.EqualTo(1080f).Within(1e-3f));

            // A wall resists infinitely but tows NOTHING. Returning zero here rather than
            // evaluating Infinity * 0 is what keeps a NaN out of the clamp downstream.
            Assert.That(Leash.PullOf(Mathf.Infinity, 6f), Is.Zero);

            // A crate has no engine and no legs.
            Assert.That(Leash.PullOf(400f, 0f), Is.Zero);
        }

        [Test]
        public void TowCapOnlyClampsTheEndThatIsLosing()
        {
            // Out-pulled by 600 with 80 kg to shift: dragged, but at a finite speed.
            Assert.That(Leash.TowCap(600f, 80f), Is.EqualTo(7.5f).Within(1e-3f));

            // Twice the mass, half the speed. This is "heavy stuff is moved slowly".
            Assert.That(Leash.TowCap(600f, 160f), Is.EqualTo(3.75f).Within(1e-3f));

            // Winning, or evenly matched: NOT clamped. Two passive crates roped together both
            // score zero, and a clamp of zero would freeze the rope instead of closing it.
            Assert.That(Leash.TowCap(-600f, 80f), Is.EqualTo(Mathf.Infinity));
            Assert.That(Leash.TowCap(0f, 80f), Is.EqualTo(Mathf.Infinity));

            // An immovable end is never towed however hard it is pulled.
            Assert.That(Leash.TowCap(600f, Mathf.Infinity), Is.Zero);
        }

        [Test]
        public void ShareStillAnswersResistanceRatherThanStrength()
        {
            // Pull and mass answer different questions and must not be conflated. A wall has NO
            // pull, so sharing by pull would hand a player roped to one a share of zero and the
            // rope would stop restraining them. Sharing by mass keeps it at 1.
            Assert.That(Leash.PullOf(Mathf.Infinity, 0f), Is.Zero);
            Assert.That(Leash.ShareOf(80f, Mathf.Infinity), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void AStrongerEndDragsAStandingPlayerAlong()
        {
            // The case Restrain forbade outright: a player standing still, roped to something that
            // walks away. They contribute no separating velocity of their own, so the arrest term
            // does nothing and the position correction is the only thing that can move them --
            // which Restrain then clamped back to the speed they already had, i.e. zero.
            (float gap, _) = Settle(massA: 80f, massB: 120f, length: 8f, startGap: 8f,
                                    driveA: 0f, steps: 400,
                                    pullA: Leash.PullOf(80f, 6f),      // player, 480
                                    pullB: Leash.PullOf(120f, 9f));    // ostrich, 1080

            // The rope stays taut, which it can only do if the player came along.
            Assert.That(gap, Is.EqualTo(8f).Within(0.6f));
        }

        [Test]
        public void AHeavierEndIsDraggedMoreSlowlyByTheSamePull()
        {
            // Same contest, twice the mass on the losing end. Force over mass, so half the speed.
            float netPull = 600f;

            Assert.That(Leash.TowCap(netPull, 80f),
                        Is.EqualTo(2f * Leash.TowCap(netPull, 160f)).Within(1e-3f));
        }

        [Test]
        public void TwoPassiveBodiesStillCloseTheirRope()
        {
            // Both score zero pull, so both caps must be uncapped rather than zero. A cap of zero
            // here would freeze two roped crates apart forever.
            (float gap, float swing) = Settle(massA: 400f, massB: 400f, length: 8f, startGap: 12f,
                                              pullA: 0f, pullB: 0f);

            Assert.That(gap, Is.EqualTo(8f).Within(0.02f));
            Assert.That(swing, Is.LessThan(0.01f), "The rope must settle, not ring.");
        }

        // ── Resist ─────────────────────────────────────────────────────────────

        [Test]
        public void EachYankBuysAFixedShareOfTheWayOutAndItFadesWhenYouStop()
        {
            const float Jerks = 8f, Decay = 0.25f;

            // One accepted yank out of the eight this rope needs.
            Assert.That(Leash.ResistStrain(0f, jerked: true, jerksNeeded: Jerks, dt: 0f, decay: Decay),
                        Is.EqualTo(0.125f).Within(1e-3f));

            // Holding a key is not fighting. A step with no yank in it earns nothing at all --
            // which is what lets somebody be towed indefinitely if they do not struggle.
            Assert.That(Leash.ResistStrain(0f, jerked: false, jerksNeeded: Jerks, dt: 1f, decay: Decay),
                        Is.Zero);

            // Four seconds of not fighting gives one yank's worth back.
            Assert.That(Leash.ResistStrain(0.25f, jerked: false, jerksNeeded: Jerks, dt: 4f, decay: Decay),
                        Is.EqualTo(0.125f).Within(1e-3f));

            // It never runs negative, so a long rest does not bank credit against the next rope.
            Assert.That(Leash.ResistStrain(0.1f, jerked: false, jerksNeeded: Jerks, dt: 60f, decay: Decay),
                        Is.Zero);

            // And it is capped, so the last yank cannot overshoot past the snap point.
            Assert.That(Leash.ResistStrain(0.95f, jerked: true, jerksNeeded: Jerks, dt: 0f, decay: Decay),
                        Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void SustainedYankingTearsFreeAndDawdlingNeverDoes()
        {
            float jerks = Resist(theirPull: 720f, myPull: 720f, theirMass: 80f, myMass: 80f);
            float strain = 0f;

            // At the meter's 2.5 Hz cap the yanks land 0.4 s apart, and the fade between them is
            // what a half-hearted struggle loses to -- so the count is what the base figure says
            // only when the fighting is sustained.
            for (int i = 0; i < (int)jerks + 1; i++)
            {
                // The gap first, then the yank, so the last thing measured is the yank that parts
                // it rather than the fade after it.
                strain = Leash.ResistStrain(strain, jerked: false, jerksNeeded: jerks,
                                            dt: 0.4f, decay: 0.25f);
                strain = Leash.ResistStrain(strain, jerked: true, jerksNeeded: jerks,
                                            dt: 0f, decay: 0.25f);
            }

            Assert.That(strain, Is.EqualTo(1f).Within(1e-3f),
                        "Nine sustained yanks -- about 3.6 s at the cap -- must part an even rope.");

            // Yanking once every four seconds instead never adds up: the fade takes back as much
            // as each yank buys.
            float dawdling = 0f;
            for (int i = 0; i < 40; i++)
            {
                dawdling = Leash.ResistStrain(dawdling, jerked: false, jerksNeeded: jerks,
                                              dt: 4f, decay: 0.25f);
                dawdling = Leash.ResistStrain(dawdling, jerked: true, jerksNeeded: jerks,
                                              dt: 0f, decay: 0.25f);
            }

            Assert.That(dawdling, Is.LessThan(0.2f));
        }

        // The scale is keyed on what HOLDS you: the greater of the far end's pull and its mass
        // relative to yours. Keyed on pull alone, every engineless anchor -- a wall, a rock, the
        // lander, a crate -- scored zero and hit the floor, so the most immovable things in the
        // game were the fastest to tear free of.

        private const float Base = 8f, MinRatio = 0.5f, MaxRatio = 1.5f;

        private static float Resist(float theirPull, float myPull, float theirMass, float myMass) =>
            Leash.ResistJerks(theirPull, myPull, theirMass, myMass, Base, MinRatio, MaxRatio);

        [Test]
        public void TearingFreeOfSomethingStrongerTakesMoreYanks()
        {
            // An even match -- another player -- is the authored base figure.
            Assert.That(Resist(theirPull: 720f, myPull: 720f, theirMass: 80f, myMass: 80f),
                        Is.EqualTo(Base).Within(1e-3f));

            // Something that out-pulls you holds you longer, up to the ceiling.
            Assert.That(Resist(theirPull: 1440f, myPull: 720f, theirMass: 80f, myMass: 80f),
                        Is.EqualTo(Base * MaxRatio).Within(1e-3f));

            // Something lighter and weaker lets go sooner, but never instantly.
            Assert.That(Resist(theirPull: 160f, myPull: 720f, theirMass: 40f, myMass: 80f),
                        Is.EqualTo(Base * MinRatio).Within(1e-3f));
        }

        [Test]
        public void AnImmovableAnchorIsTheHardestToTearFreeOf()
        {
            // A wall, a rock, the lander: no engine, so zero pull, and no body, so infinite mass.
            float wall = Resist(theirPull: 0f, myPull: 720f,
                                theirMass: Mathf.Infinity, myMass: 80f);

            Assert.That(wall, Is.EqualTo(Base * MaxRatio).Within(1e-3f),
                        "An anchor that cannot be moved must land on the ceiling, not the floor.");

            Assert.That(wall, Is.GreaterThan(Resist(720f, 720f, 80f, 80f)),
                        "Terrain must hold you longer than another player does.");

            // ...and it must still be finite, or capture has no answer at all.
            Assert.That(wall, Is.LessThan(Mathf.Infinity));
        }

        [Test]
        public void AHeavyEnginelessLoadHoldsYouByItsMassAlone()
        {
            // A crate scores no pull whatever it weighs, so mass is the only term that can
            // separate a barrel from the lander.
            float barrel = Resist(theirPull: 0f, myPull: 720f, theirMass: 20f, myMass: 80f);
            float crate = Resist(theirPull: 0f, myPull: 720f, theirMass: 100f, myMass: 80f);

            Assert.That(crate, Is.GreaterThan(barrel));
            Assert.That(barrel, Is.EqualTo(Base * MinRatio).Within(1e-3f));
        }

        // ── Several ropes on one body ──────────────────────────────────────────

        [Test]
        public void PullsFromSeveralRopesAddUp()
        {
            // Three players hauling one ship out-pull one player hauling it the other way.
            float player = Leash.PullOf(80f, 6f);

            Assert.That(Leash.CombinedPull(new[] { player, player, player }),
                        Is.EqualTo(3f * player).Within(1e-3f));

            // And an empty rope set pulls nothing rather than throwing.
            Assert.That(Leash.CombinedPull(new float[0]), Is.Zero);
        }

        [Test]
        public void OpposingRopesOnOneBodyCancel()
        {
            // Two players roping one crate in opposite directions deadlock it. The signs come from
            // the direction each rope pulls, so no rule is needed for "which side is the crate on".
            float player = Leash.PullOf(80f, 6f);

            Assert.That(Leash.CombinedPull(new[] { player, -player }), Is.Zero.Within(1e-3f));

            // A third player joining one side breaks the deadlock.
            Assert.That(Leash.CombinedPull(new[] { player, -player, player }),
                        Is.EqualTo(player).Within(1e-3f));
        }

        [Test]
        public void TerrainIsNotSomethingARopeCanBeTiedTo()
        {
            var go = new GameObject("terrain probe");
            try
            {
                // A TerrainCollider is the exact thing chunk ground uses, so the type IS the test.
                // A layer mask would have to be kept in step with the streaming config, which
                // already has a documented casing drift defect.
                Assert.That(LeashArtifact.IsTieable(go.AddComponent<TerrainCollider>()), Is.False);

                var box = new GameObject("crate");
                try
                {
                    Assert.That(LeashArtifact.IsTieable(box.AddComponent<BoxCollider>()), Is.True);
                }
                finally { Object.DestroyImmediate(box); }

                // Nothing aimed at is not tieable either, and must not throw.
                Assert.That(LeashArtifact.IsTieable(null), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
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

        // ── Who may untie ──────────────────────────────────────────────────────
        //
        // A captive who can click the rope off has no reason ever to struggle, which retires
        // LeashedBody.Struggle and the whole capture verb with it. Leash.Restrains is the one
        // question both the aim and every machine's Present ask; these pin the three answers that
        // are easy to get wrong.

        [Test]
        public void ARopeRestrainsThePlayerOnItsFarEndToo()
        {
            // The "connected area as a whole" rule. An untie is addressed to the anchor NEAREST the
            // click, so a check that followed the address alone would let a captive walk to the far
            // knot and untie themselves from there — the same escape by a longer route.
            var captive = new GameObject("captive") { tag = "Player" };
            var post = new GameObject("post");

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, post, Vector3.zero);
            rope.TieEndTo(false, captive, Vector3.zero);

            Assert.That(rope.Restrains(captive), Is.True,
                        "the rope did not own up to the player on its B end, so they could untie " +
                        "themselves by clicking near the post");

            rope.Dispose();
            Object.DestroyImmediate(captive);
            Object.DestroyImmediate(post);
        }

        [Test]
        public void ARopeKnottedToABoneRestrainsTheBodyItBelongsTo()
        {
            // A rope thrown at a downed player lands on the ragdoll bone it hit: LeashEnd.TieTo
            // anchors to the Rigidbody it finds walking up, not to the player root. An identity
            // test against the root answers no here, and the captive is free to click it off.
            var captive = new GameObject("captive") { tag = "Player" };
            var forearm = new GameObject("forearm");
            forearm.transform.SetParent(captive.transform);
            forearm.AddComponent<Rigidbody>();

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.PinEndTo(true, Vector3.zero);
            rope.TieEndTo(false, forearm, Vector3.zero);

            Assert.That(rope.B.Anchor, Is.Not.EqualTo(captive.transform),
                        "test setup: the knot must land on the bone, or this passes for the " +
                        "wrong reason");

            Assert.That(rope.Restrains(captive), Is.True,
                        "a rope on a ragdoll bone read as somebody else's rope");

            rope.Dispose();
            Object.DestroyImmediate(captive);
        }

        [Test]
        public void ARopeDoesNotRestrainABystander()
        {
            // The other half of the rule, and the one a too-broad check would break: cutting
            // somebody else loose is the point of the untie click and must keep working.
            var captive = new GameObject("captive") { tag = "Player" };
            var rescuer = new GameObject("rescuer") { tag = "Player" };

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.PinEndTo(true, Vector3.zero);
            rope.TieEndTo(false, captive, Vector3.zero);

            Assert.That(rope.Restrains(rescuer), Is.False,
                        "a bystander was refused the untie, so nobody can be cut loose");

            rope.Dispose();
            Object.DestroyImmediate(captive);
            Object.DestroyImmediate(rescuer);
        }

        // ── Path geometry ──────────────────────────────────────────────────────
        //
        // A rope is a polyline now, not a chord. The arithmetic that follows from that is pure, so
        // it is pinned here rather than judged by feel in play mode — same reasoning as everything
        // above it.

        [Test]
        public void PolylineLength_WithNoWraps_IsTheStraightDistance()
        {
            var points = new[] { new Vector3(0f, 0f, 0f), new Vector3(3f, 4f, 0f) };

            Assert.AreEqual(5f, LeashPath.PolylineLength(points), 0.0001f);
        }

        [Test]
        public void PolylineLength_SumsEverySegment()
        {
            var points = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(3f, 4f, 0f),
            };

            Assert.AreEqual(7f, LeashPath.PolylineLength(points), 0.0001f);
        }

        /// <summary>
        /// The winch. Rope spent going round a corner is rope the far end does not have, so a bend
        /// makes the SAME two endpoints measure longer — which is what draws the far end in when
        /// somebody walks away from the corner.
        /// </summary>
        [Test]
        public void ABend_MakesTheSameEndpointsMeasureLonger()
        {
            var straight = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            var bent = new[] { new Vector3(0f, 0f, 0f), new Vector3(5f, 3f, 0f), new Vector3(10f, 0f, 0f) };

            Assert.Greater(LeashPath.PolylineLength(bent), LeashPath.PolylineLength(straight));
        }

        [Test]
        public void TryMake_OffsetsTheContactAlongTheNormal()
        {
            var tuning = new LeashPath.Tuning { clearance = 0.1f, maxWraps = 8 };

            bool made = LeashPath.TryMake(
                new Vector3(5f, 0f, 0f), Vector3.up, null,
                Vector3.zero, new Vector3(10f, 0f, 0f), tuning, out LeashWrap wrap);

            Assert.IsTrue(made);
            Assert.AreEqual(0.1f, wrap.Position.y, 0.0001f);
            Assert.AreEqual(5f, wrap.Position.x, 0.0001f);
        }

        /// <summary>
        /// The degenerate case, and it is not rare: a rope lying along a flat wall contacts it
        /// everywhere, and a contact that lands on top of a point it is meant to bend between makes
        /// a zero-length segment. Without this refusal the list fills in a single step and the
        /// rope's measured length collapses.
        /// </summary>
        [Test]
        public void TryMake_RefusesAWrapSittingOnItsOwnNeighbour()
        {
            var tuning = new LeashPath.Tuning { clearance = 0.1f, maxWraps = 8 };

            bool made = LeashPath.TryMake(
                new Vector3(0.05f, 0f, 0f), Vector3.up, null,
                Vector3.zero, new Vector3(10f, 0f, 0f), tuning, out _);

            Assert.IsFalse(made);
        }

        [Test]
        public void DirectionFrom_WithNoWraps_PointsAtTheFarEnd()
        {
            var path = new LeashPath();

            Assert.That(path.DirectionFrom(true, Vector3.zero, new Vector3(0f, 0f, 10f)),
                        Is.EqualTo(Vector3.forward).Using(Vec));
        }

        /// <summary>
        /// The generalisation must be exactly that. With no bend, each end's own contribution to the
        /// rope lengthening sums to the relative-velocity term it replaces — if this drifts, every
        /// rope in the shipped game changes feel and nothing says so.
        /// </summary>
        [Test]
        public void SeparationRate_WithNoWraps_MatchesRelativeVelocity()
        {
            var path = new LeashPath();

            Vector3 endA = new(0f, 0f, 0f);
            Vector3 endB = new(0f, 0f, 10f);

            Vector3 velocityA = new(1f, 0f, -2f);
            Vector3 velocityB = new(0f, 3f, 5f);

            Vector3 towardA = path.DirectionFrom(true, endA, endB);
            Vector3 towardB = path.DirectionFrom(false, endA, endB);

            float split = Vector3.Dot(velocityA, -towardA) + Vector3.Dot(velocityB, -towardB);
            float relative = Vector3.Dot(velocityA - velocityB, -towardA);

            Assert.AreEqual(relative, split, 0.0001f);
        }
    }
}
