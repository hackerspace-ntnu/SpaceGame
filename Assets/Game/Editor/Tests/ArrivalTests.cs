// What the crash landing arrival has to keep being true about itself.
//
// The failures worth catching here are the silent ones. A trajectory that overshoots its lateral
// budget still flies and still lands — it just drags the world streamer through chunks nobody asked
// for, which surfaces as a stutter on somebody else's machine and never as an error. A terminal
// pose a degree off level still looks like a landing in the editor and then leaves the wreck resting
// on one wing forever, because the hull is persisted exactly where the trajectory left it. An
// unstable seat sort satisfies "sorted by order" while filling seats in a sequence that changes
// between runs. And a shake that is merely near zero when a player has turned shake off still makes
// that player ill.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class ArrivalTrajectoryTests
    {
        /// <summary>Metres of float drift treated as noise rather than as a miss.</summary>
        private const float PositionTolerance = 0.001f;

        /// <summary>Degrees of float drift treated as level.</summary>
        private const float AngleTolerance = 0.01f;

        /// <summary>
        /// The shipped defaults, moved off the origin. Deliberately the real values rather than
        /// hand-made ones, so a retune that breaks an invariant is caught here rather than in play.
        /// </summary>
        private static ArrivalPath Path()
        {
            ArrivalPath path = ArrivalPath.Default;
            path.ImpactPosition = new Vector3(120f, 30f, -75f);
            return path;
        }

        [Test]
        public void StartsAtTheConfiguredAltitude()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(0f, path, out Vector3 position, out Quaternion _);

            Assert.AreEqual(path.ImpactPosition.y + path.StartAltitude, position.y, PositionTolerance);
        }

        [Test]
        public void EndsExactlyOnTheImpactPosition()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 position, out Quaternion _);

            Assert.Less(Vector3.Distance(path.ImpactPosition, position), PositionTolerance,
                        "The wreck is persisted where the trajectory leaves it, so a terminal " +
                        "position that is merely close is a hull permanently buried in, or hovering " +
                        "above, the ground.");
        }

        [Test]
        public void LandsLevel()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 _, out Quaternion rotation);

            Vector3 euler = rotation.eulerAngles;
            float roll = Mathf.DeltaAngle(0f, euler.z);
            float pitch = Mathf.DeltaAngle(0f, euler.x);

            Assert.AreEqual(0f, roll, AngleTolerance, "Bank must unwind to zero or the wreck rests on a wing.");
            Assert.AreEqual(0f, pitch, AngleTolerance, "Pitch must flare to zero or the wreck stands on its nose.");
        }

        [Test]
        public void DescendsMonotonically()
        {
            ArrivalPath path = Path();
            float previous = float.MaxValue;

            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f;
                ArrivalTrajectory.Evaluate(t, path, out Vector3 position, out Quaternion _);

                Assert.Less(position.y, previous + PositionTolerance,
                            $"Altitude rose between samples at t={t}. The descent must never climb.");
                previous = position.y;
            }
        }

        [Test]
        public void NeverExceedsTheLateralBudget()
        {
            ArrivalPath path = Path();

            for (int i = 0; i <= 200; i++)
            {
                ArrivalTrajectory.Evaluate(i / 200f, path, out Vector3 position, out Quaternion _);

                Vector2 offset = new(position.x - path.ImpactPosition.x,
                                     position.z - path.ImpactPosition.z);

                Assert.LessOrEqual(offset.magnitude, path.LateralBudget + PositionTolerance,
                                   "The lateral budget is a world-streaming limit, not a suggestion.");
            }
        }

        [Test]
        public void CurvesRatherThanDivingStraight()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(0f, path, out Vector3 start, out Quaternion _);
            ArrivalTrajectory.Evaluate(0.5f, path, out Vector3 middle, out Quaternion _);

            Vector2 a = new(start.x, start.z);
            Vector2 b = new(path.ImpactPosition.x, path.ImpactPosition.z);
            Vector2 m = new(middle.x, middle.z);

            // Distance from the mid-descent point to the straight line joining start and impact. A
            // dead straight dive puts this at zero; the brief explicitly asked for a curving path.
            Vector2 line = b - a;
            float deviation = Mathf.Abs(line.x * (a.y - m.y) - (a.x - m.x) * line.y) / line.magnitude;

            Assert.Greater(deviation, 1f, "The path must be an arc, not a straight line.");
        }

        [Test]
        public void NoseIsDownThroughTheDescent()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(0.5f, path, out Vector3 _, out Quaternion rotation);

            float pitch = Mathf.DeltaAngle(0f, rotation.eulerAngles.x);

            Assert.Greater(pitch, 10f,
                           "Mid-descent the hull must be visibly pointing at the ground it is " +
                           "falling towards, not flying level into it.");
        }

        [Test]
        public void NoseIsLevelAtTheTop()
        {
            // The descent begins with no vertical rate at all, so a hull already pitched down there
            // is pointing somewhere it is not going. This is what a fixed authored pitch got wrong.
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(0f, path, out Vector3 _, out Quaternion rotation);

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, rotation.eulerAngles.x), 0.5f);
        }

        [Test]
        public void PitchNeverExceedsItsCap()
        {
            ArrivalPath path = Path();

            for (int i = 0; i <= 200; i++)
            {
                ArrivalTrajectory.Evaluate(i / 200f, path, out Vector3 _, out Quaternion rotation);

                float pitch = Mathf.DeltaAngle(0f, rotation.eulerAngles.x);

                Assert.LessOrEqual(pitch, path.MaxPitchDegrees + 0.01f,
                                   $"Dive angle exceeded its cap at t={i / 200f}.");
            }
        }

        [Test]
        public void PitchIsContinuousThroughTheFlare()
        {
            // A step from a steep dive to level on the last frame reads as the hull snapping rather
            // than landing, so the flare has to be smooth right up to touchdown.
            ArrivalPath path = Path();
            float previous = 0f;

            for (int i = 0; i <= 400; i++)
            {
                ArrivalTrajectory.Evaluate(i / 400f, path, out Vector3 _, out Quaternion rotation);

                float pitch = Mathf.DeltaAngle(0f, rotation.eulerAngles.x);

                if (i > 0)
                    Assert.Less(Mathf.Abs(pitch - previous), 3f,
                                $"Pitch jumped {Mathf.Abs(pitch - previous):F2} degrees between " +
                                $"adjacent samples at t={i / 400f}.");

                previous = pitch;
            }
        }

        [Test]
        public void ZeroFlareStillLandsLevel()
        {
            // A flare of zero is a step discontinuity, so it is clamped rather than honoured. The
            // wreck is saved in its terminal attitude, and one stood on its nose is permanent.
            ArrivalPath path = Path();
            path.FlareFraction = 0f;

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 _, out Quaternion rotation);

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, rotation.eulerAngles.x), AngleTolerance);
        }

        [Test]
        public void ZeroSweepIsStillValid()
        {
            ArrivalPath path = Path();
            path.SweepDegrees = 0f;

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 position, out Quaternion rotation);

            Assert.Less(Vector3.Distance(path.ImpactPosition, position), PositionTolerance);
            Assert.IsFalse(float.IsNaN(rotation.x), "A straight-in approach must not produce a NaN heading.");
        }

        [Test]
        public void ClampsOutOfRangeTime()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(2f, path, out Vector3 over, out Quaternion _);
            ArrivalTrajectory.Evaluate(-1f, path, out Vector3 under, out Quaternion _);

            // A caller whose own timer overshoots gets the terminal pose, not an extrapolated one
            // somewhere under the terrain.
            Assert.Less(Vector3.Distance(path.ImpactPosition, over), PositionTolerance);
            Assert.AreEqual(path.ImpactPosition.y + path.StartAltitude, under.y, PositionTolerance);
        }
    }

    public class SeatOrderingTests
    {
        [Test]
        public void SortsByOrder()
        {
            int[] result = SeatOrdering.OrderedIndices(new[] { 30, 10, 20 });

            Assert.AreEqual(new[] { 1, 2, 0 }, result);
        }

        [Test]
        public void TiesKeepHierarchyOrder()
        {
            // Every seat left at the default zero is the common case: somebody added ShipSeat
            // components and never touched the order field. Those must fill top to bottom, which is
            // what the person who never touched the field would expect. An unstable sort satisfies
            // "sorted by order" while filling them arbitrarily, and differently between runs.
            int[] result = SeatOrdering.OrderedIndices(new[] { 0, 0, 0, 0 });

            Assert.AreEqual(new[] { 0, 1, 2, 3 }, result);
        }

        [Test]
        public void PartialTiesKeepHierarchyOrderWithinEachGroup()
        {
            int[] result = SeatOrdering.OrderedIndices(new[] { 5, 1, 5, 1 });

            Assert.AreEqual(new[] { 1, 3, 0, 2 }, result);
        }

        [Test]
        public void MorePlayersThanSeatsWraps()
        {
            // A twelve-strong crew in a seven-seat hull is a fair thing to have. Two bodies briefly
            // sharing a pose push apart on the next physics step; a player handed no seat at all is
            // left standing in the sky and does not recover on its own.
            Assert.AreEqual(0, SeatOrdering.SeatFor(claim: 0, seatCount: 3));
            Assert.AreEqual(2, SeatOrdering.SeatFor(claim: 2, seatCount: 3));
            Assert.AreEqual(0, SeatOrdering.SeatFor(claim: 3, seatCount: 3));
            Assert.AreEqual(1, SeatOrdering.SeatFor(claim: 7, seatCount: 3));
        }

        [Test]
        public void NoSeatsIsRefusedRatherThanDividedByZero()
        {
            Assert.AreEqual(-1, SeatOrdering.SeatFor(claim: 0, seatCount: 0));
        }

        [Test]
        public void EmptyInputIsEmptyOutput()
        {
            Assert.IsEmpty(SeatOrdering.OrderedIndices(new int[0]));
        }

        [Test]
        public void NullInputIsEmptyRatherThanAThrow()
        {
            Assert.IsEmpty(SeatOrdering.OrderedIndices(null));
        }
    }

    public class ShakeMathTests
    {
        private const float MaxTranslation = 0.15f;

        [Test]
        public void ZeroSettingsScaleIsExactlyStill()
        {
            // The accessibility guarantee, and the reason ShakeMath early-returns rather than
            // multiplying by zero: Perlin noise is 0.5 at its sample origin, so the arithmetic would
            // otherwise leave a small CONSTANT offset — a camera sitting permanently off-centre for
            // the player who turned shake off to avoid exactly that.
            Vector3 offset = ShakeMath.Displacement(intensity: 1f, settingsScale: 0f,
                                                    maxTranslation: MaxTranslation,
                                                    time: 3.7f, frequency: 18f);

            Assert.AreEqual(Vector3.zero, offset);
        }

        [Test]
        public void ZeroIntensityIsExactlyStill()
        {
            Vector3 offset = ShakeMath.Displacement(intensity: 0f, settingsScale: 1f,
                                                    maxTranslation: MaxTranslation,
                                                    time: 3.7f, frequency: 18f);

            Assert.AreEqual(Vector3.zero, offset);
        }

        [Test]
        public void NeverExceedsTheCap()
        {
            for (int i = 0; i <= 500; i++)
            {
                float time = i * 0.037f;

                Vector3 offset = ShakeMath.Displacement(intensity: 1f, settingsScale: 1f,
                                                        maxTranslation: MaxTranslation,
                                                        time: time, frequency: 23f);

                Assert.LessOrEqual(offset.magnitude, MaxTranslation + 0.0001f,
                                   $"Shake exceeded its cap at t={time}. An uncapped shake is an " +
                                   "unreadable frame the moment two sources overlap.");
            }
        }

        [Test]
        public void OutOfRangeIntensityIsClampedRatherThanAmplified()
        {
            Vector3 offset = ShakeMath.Displacement(intensity: 50f, settingsScale: 1f,
                                                    maxTranslation: MaxTranslation,
                                                    time: 1.3f, frequency: 18f);

            Assert.LessOrEqual(offset.magnitude, MaxTranslation + 0.0001f);
        }

        [Test]
        public void PartialSettingsScaleReducesTheCapProportionally()
        {
            // The player's preference is a real dial, not just an on/off. Half intensity must
            // actually halve the ceiling, or "turn it down a bit" does nothing.
            for (int i = 0; i <= 200; i++)
            {
                Vector3 offset = ShakeMath.Displacement(intensity: 1f, settingsScale: 0.5f,
                                                        maxTranslation: MaxTranslation,
                                                        time: i * 0.05f, frequency: 23f);

                Assert.LessOrEqual(offset.magnitude, MaxTranslation * 0.5f + 0.0001f);
            }
        }

        [Test]
        public void IsContinuousInTime()
        {
            // Perlin noise is smooth; a shake built on Random would not be, and would read as the
            // camera glitching rather than a hull shaking.
            Vector3 a = ShakeMath.Displacement(1f, 1f, MaxTranslation, 2.000f, 18f);
            Vector3 b = ShakeMath.Displacement(1f, 1f, MaxTranslation, 2.001f, 18f);

            Assert.Less(Vector3.Distance(a, b), MaxTranslation * 0.25f,
                        "Adjacent samples jumped. That is a glitch, not a shake.");
        }
    }
}
