using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// Guards the one property the dragon bazooka's whole multiplayer story rests on: the rocket
    /// wanders unpredictably, and every machine's copy wanders IDENTICALLY.
    ///
    /// <para>
    /// These live under <c>Editor/</c> rather than beside the asmdef'd EditMode tests because
    /// they touch Assembly-CSharp types, and an asmdef cannot reference Assembly-CSharp.
    /// </para>
    /// </summary>
    public class DragonRocketFlightTests
    {
        private const float Speed = 15f;
        private const float Amplitude = 5.4f;
        private const float Settle = 0.3f;
        private const float Frequency = 1.55f;
        private const float Drift = 2.4f;
        private const float MaxTime = 4.2f;

        private static readonly Quaternion Aim = Quaternion.LookRotation(Vector3.forward);
        private static readonly Vector3 Origin = new Vector3(10f, 2f, -4f);

        private static Vector3 Position(int seed, float t) =>
            DragonRocketFlight.PositionAt(Origin, Aim, seed, t, Speed, Amplitude, Settle,
                                          Frequency, Drift);

        private static Vector2 Stray(int seed, float t) =>
            DragonRocketFlight.Offset(seed, t, Amplitude, Settle, Frequency, Drift);

        // ── Determinism: the reason any of this works over the network ─────────

        [Test]
        public void PositionIsAFunctionOfTimeAloneNotOfHowItWasReached()
        {
            // The real point of the test. One machine walks the flight in 120 Hz steps and
            // another asks for the same instant directly; if the path had any per-frame memory
            // — the obvious way to write "moves like a bug" — these would diverge, and the
            // server would bill an explosion the clients never saw.
            const int seed = 8123;
            const float target = 1.7f;

            Vector3 walked = Vector3.zero;
            for (int i = 1; i <= 204; i++)
                walked = Position(seed, i / 120f);

            Assert.That(Vector3.Distance(walked, Position(seed, 204 / 120f)), Is.LessThan(1e-4f),
                        "Stepping to a time and asking for it directly must agree.");
            Assert.That(Position(seed, target), Is.EqualTo(Position(seed, target)),
                        "Repeated queries must be identical.");
        }

        [Test]
        public void DifferentFrameRatesReachTheSamePlace()
        {
            const int seed = -55012;

            // 30 Hz and 144 Hz, sampled to a common instant.
            Vector3 slow = Position(seed, 60f / 30f);
            Vector3 fast = Position(seed, 288f / 144f);

            Assert.That(Vector3.Distance(slow, fast), Is.LessThan(1e-4f));
        }

        [Test]
        public void DifferentSeedsFlyDifferentPaths()
        {
            // Guards against the failure that would make everything else pass trivially: a
            // wander that ignores its seed is perfectly deterministic and completely useless.
            Vector3 a = Position(1, 1.4f);
            Vector3 b = Position(2, 1.4f);

            Assert.That(Vector3.Distance(a, b), Is.GreaterThan(0.25f),
                        "Two seeds should not fly the same rocket.");
        }

        // ── The shape of the flight ────────────────────────────────────────────

        [Test]
        public void LeavesTheMuzzleOnTheAimBeforeItMisbehaves()
        {
            // settleSeconds exists so the player sees their aim honoured before the rocket starts
            // swerving (GDC-L1-DESIGN-0006). At t = 0 the stray must be exactly zero.
            for (int seed = -3; seed <= 3; seed++)
            {
                Vector2 stray = DragonRocketFlight.Offset(seed, 0f, Amplitude, Settle, Frequency, Drift);
                Assert.That(stray.magnitude, Is.LessThan(1e-5f),
                            $"Seed {seed} left the muzzle already off-aim.");
            }
        }

        [Test]
        public void ActuallyWanders()
        {
            // The feature, asserted: by mid-flight the rocket is a long way off the line it was
            // fired along, for essentially any seed.
            int strayed = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                Vector2 stray = DragonRocketFlight.Offset(seed * 7919, 1.6f, Amplitude, Settle,
                                                          Frequency, Drift);
                if (stray.magnitude > 0.4f) strayed++;
            }

            Assert.That(strayed, Is.GreaterThan(50),
                        "A rocket that flies straight is not this weapon.");
        }

        [Test]
        public void StaysWithinItsAuthoredAmplitude()
        {
            // Harmonic weights are normalised so `amplitude` sets the scale of the stray. Without
            // that the number in the Inspector would mean nothing and tuning it would be guesswork.
            float worst = 0f;
            for (int seed = 0; seed < 40; seed++)
                for (float t = 0f; t <= MaxTime; t += 0.01f)
                    worst = Mathf.Max(worst,
                                      DragonRocketFlight.Offset(seed * 104729, t, Amplitude,
                                                                Settle, Frequency, Drift).magnitude);

            // Two terms, and the bound has to admit both. The swerve's hard ceiling is
            // amplitude * 1.3 * sqrt(2) ~= 1.84x: per-axis weights sum to 1 and each carries up
            // to +30% jitter, giving 1.3x PER AXIS, and the two axes are independent so the 2-D
            // magnitude is that again times root two. (An earlier version asserted 1.35x, having
            // forgotten the second axis, and failed at 1.45x against perfectly correct maths.)
            // On top of that the per-shot lean reaches driftRate * t.
            float ceiling = Amplitude * 1.3f * Mathf.Sqrt(2f) + Drift * MaxTime;
            Assert.That(worst, Is.LessThanOrEqualTo(ceiling));
            Assert.That(worst, Is.GreaterThan(Amplitude * 0.4f),
                        "The authored amplitude should be broadly reachable, not theoretical.");
        }

        [Test]
        public void MeanPathIsTheAimRay()
        {
            // The design contract that keeps the weapon aimable at all (GDC-L1-DESIGN-0006).
            // Note what it does and does not promise, now that each rocket also carries a
            // constant lean: an INDIVIDUAL shot is not expected to converge on the crosshair any
            // more — see ASingleShotGenuinelyLeansOffTarget — but the weapon still shoots where
            // it is pointed, because both the swerve and the lean average to zero across seeds.
            Vector2 sum = Vector2.zero;
            const int seeds = 400;
            for (int i = 0; i < seeds; i++)
                sum += DragonRocketFlight.Offset(unchecked(i * 1103515245), 1.5f, Amplitude,
                                                 Settle, Frequency, Drift);

            Assert.That((sum / seeds).magnitude, Is.LessThan(Amplitude * 0.12f),
                        "The average shot must land on the crosshair.");
        }

        // ── The per-shot lean ──────────────────────────────────────────────────

        [Test]
        public void ASingleShotGenuinelyLeansOffTarget()
        {
            // The point of the drift term, and the thing the swerve alone cannot do. Sinusoids
            // average to zero over their own period, so without this a rocket keeps returning to
            // the aim ray no matter how violently it swings — it is noise about a line, and at
            // long range it still converges on the crosshair. Averaging each shot over its own
            // flight isolates that: the mean of ONE seed's stray must be substantially off zero.
            int leaned = 0;
            const int seeds = 60;

            for (int i = 0; i < seeds; i++)
            {
                int seed = unchecked(i * 1664525);
                Vector2 sum = Vector2.zero;
                int samples = 0;
                for (float t = 0.5f; t <= MaxTime; t += 0.02f, samples++)
                    sum += Stray(seed, t);

                if ((sum / samples).magnitude > Amplitude * 0.25f) leaned++;
            }

            Assert.That(leaned, Is.GreaterThan(50),
                        "Most shots should end up somewhere other than where they were aimed.");
        }

        [Test]
        public void TheLeanIsDeterministicAndNeverVanishes()
        {
            for (int i = 0; i < 40; i++)
            {
                int seed = unchecked(i * 40503);
                Vector2 drift = DragonRocketFlight.Drift(seed, Drift);

                Assert.That(drift, Is.EqualTo(DragonRocketFlight.Drift(seed, Drift)),
                            "The lean must be reproducible from the seed alone.");

                // Floored at a third of the authored rate: a lean that could roll near zero would
                // give the occasional suspiciously well-behaved shot, and one rocket in ten
                // flying straight reads as the effect being broken rather than as variety.
                Assert.That(drift.magnitude, Is.InRange(Drift * 0.34f, Drift * 1.01f));
            }
        }

        [Test]
        public void TheLeanAveragesToZeroAcrossShots()
        {
            // What keeps the launcher aimable even though no single rocket is. If the seeded
            // directions were biased the weapon would pull consistently to one side, which is a
            // very different — and much worse — feel than "unpredictable".
            Vector2 sum = Vector2.zero;
            const int seeds = 800;
            for (int i = 0; i < seeds; i++)
                sum += DragonRocketFlight.Drift(unchecked(i * 1103515245), Drift);

            Assert.That((sum / seeds).magnitude, Is.LessThan(Drift * 0.12f));
        }

        [Test]
        public void ZeroDriftRateRestoresTheOldConvergingFlight()
        {
            // The lean is a tunable, not a law: at 0 the rocket goes back to swerving around the
            // aim ray and converging on it, which is what the whelps want.
            Assert.That(DragonRocketFlight.Drift(12345, 0f), Is.EqualTo(Vector2.zero));

            Vector2 sum = Vector2.zero;
            int samples = 0;
            for (float t = 0.5f; t <= MaxTime; t += 0.02f, samples++)
                sum += DragonRocketFlight.Offset(999, t, Amplitude, Settle, Frequency, 0f);

            Assert.That((sum / samples).magnitude, Is.LessThan(Amplitude * 0.25f),
                        "With no lean a single shot should still average onto the aim.");
        }

        [Test]
        public void AlwaysMakesForwardProgress()
        {
            // A rocket that stalls or reverses would sit in the air until its fuse ran out. The
            // forward component is un-wandered by construction; this pins that down.
            for (int seed = -5; seed <= 5; seed++)
            {
                Vector3 previous = Position(seed, 0f);
                for (float t = 1f / 120f; t <= MaxTime; t += 1f / 120f)
                {
                    Vector3 now = Position(seed, t);
                    Assert.That(now.z - previous.z, Is.GreaterThan(0f),
                                $"Seed {seed} stalled at t={t}.");
                    previous = now;
                }
            }
        }

        // ── The analytic derivative ────────────────────────────────────────────

        [Test]
        public void VelocityMatchesTheSlopeOfThePosition()
        {
            // The model is pointed along VelocityAt and the trail streams off it, so a derivative
            // that disagreed with the path would show as a nose pointing the wrong way — most
            // visibly during the settle ramp, where the envelope is changing and the product rule
            // is easy to get wrong. Sampled across it deliberately.
            const int seed = 4242;

            // Step size is a compromise the test has to make deliberately, and 1e-4 was the wrong
            // side of it: a central difference divides by 2h, so float32's ~1e-7 relative error on
            // a position of magnitude ~40 lands as 0.02 of noise at 1e-4 and 0.007 at 3e-4, while
            // the truncation error it trades against is still only ~0.002 here. At 1e-4 the check
            // failed at 0.0503 against a 0.05 bound — measuring the yardstick, not the code.
            const float h = 3e-4f;

            for (float t = 0.02f; t <= 2.5f; t += 0.05f)
            {
                Vector3 numeric = (Position(seed, t + h) - Position(seed, t - h)) / (2f * h);
                Vector3 analytic = DragonRocketFlight.VelocityAt(Aim, seed, t, Speed, Amplitude,
                                                                 Settle, Frequency, Drift);

                Assert.That(Vector3.Distance(numeric, analytic), Is.LessThan(0.05f),
                            $"Derivative disagrees with the path at t={t}.");
            }
        }

        // ── The burst ──────────────────────────────────────────────────────────

        [Test]
        public void ChildSeedsAreStableAndDistinct()
        {
            // Every machine bursts on its own with nothing sent about it, so the child seeds have
            // to be reproducible from the parent's — and distinct, or the brood flies as one.
            const int parent = 991;
            var seen = new HashSet<int>();

            for (int i = 0; i < 8; i++)
            {
                int child = DragonRocketFlight.ChildSeed(parent, i);
                Assert.That(child, Is.EqualTo(DragonRocketFlight.ChildSeed(parent, i)),
                            "Child seeds must be reproducible.");
                Assert.That(seen.Add(child), $"Child {i} repeated an earlier seed.");
            }
        }

        [Test]
        public void BurstDirectionsFanOutWithinTheCone()
        {
            const float spread = 52f;
            Vector3 axis = Vector3.up;

            Vector3[] dirs = DragonRocketFlight.BurstDirections(31337, axis, 4, spread);

            Assert.That(dirs.Length, Is.EqualTo(4));
            foreach (Vector3 dir in dirs)
            {
                Assert.That(dir.magnitude, Is.EqualTo(1f).Within(1e-3f), "Directions must be unit.");
                Assert.That(Vector3.Angle(axis, dir), Is.LessThanOrEqualTo(spread + 0.5f),
                            "A whelp left the cone.");
            }

            // Stratified yaw: no two whelps may set off in nearly the same direction, which four
            // independent draws would do about a third of the time and which reads as a bug.
            for (int i = 0; i < dirs.Length; i++)
                for (int j = i + 1; j < dirs.Length; j++)
                    Assert.That(Vector3.Angle(dirs[i], dirs[j]), Is.GreaterThan(12f),
                                $"Whelps {i} and {j} fly together.");
        }

        [Test]
        public void BurstDirectionsAreDeterministicPerSeed()
        {
            Vector3[] a = DragonRocketFlight.BurstDirections(77, Vector3.forward, 4, 52f);
            Vector3[] b = DragonRocketFlight.BurstDirections(77, Vector3.forward, 4, 52f);

            for (int i = 0; i < a.Length; i++)
                Assert.That(Vector3.Distance(a[i], b[i]), Is.LessThan(1e-5f));
        }

        [Test]
        public void BurstSurvivesADegenerateAxis()
        {
            // A rocket that runs out of fuse pointing straight up, or bursts against a collider
            // whose normal came back zero, must still scatter rather than throw.
            Vector3[] dirs = DragonRocketFlight.BurstDirections(5, Vector3.zero, 3, 40f);

            Assert.That(dirs.Length, Is.EqualTo(3));
            foreach (Vector3 dir in dirs)
                Assert.That(dir.magnitude, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void ZeroWhelpsIsAnEmptyBurstNotACrash()
        {
            // The whelp prefab itself is built with whelpCount 0, so this is the shipped path for
            // every second-generation rocket, not a defensive edge case.
            Assert.That(DragonRocketFlight.BurstDirections(1, Vector3.up, 0, 40f), Is.Empty);
        }
    }
}
