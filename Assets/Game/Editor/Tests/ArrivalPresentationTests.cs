// What the arrival LOOKS like has to keep being true too, and its failures are as quiet as the
// hull's.
//
// A fade that starts at the impact instead of finishing there still fades, still ends black, and
// still lifts on a landed ship — it just shows the player the one frame the whole sequence exists
// to hide, and then a second of the wreck toppling over. A head clamp that lets the neck past its
// limit still turns the head; the shearing is on somebody else's screen, not the looker's. And a
// mode that hands the yaw to the neck while the body is also turning applies the same look twice,
// which reads as a character permanently glancing over their own shoulder — again, only to
// everybody else.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class ArrivalBeatsTests
    {
        /// <summary>Seconds of float drift treated as the same instant.</summary>
        private const float Tolerance = 0.0001f;

        /// <summary>The shipped numbers: a 26 s dive, a 1.6 s crash, and the authored fades.</summary>
        private static ArrivalBeats Shipped() => new(descent: 26f, impactFade: 0.6f,
                                                     settle: 1.6f, blackout: 1.4f);

        [Test]
        public void TheBlackIsCompleteAtFirstContact()
        {
            // The whole requirement, as one equality. The fade is STARTED early and ENDS on the
            // impact; a fade begun at the impact ends a second into the crash it exists to hide.
            ArrivalBeats beats = Shipped();

            Assert.AreEqual(beats.Contact, beats.FadeStart + beats.FadeDuration, Tolerance,
                            "The fade to black must finish at first contact, not begin there.");
        }

        [Test]
        public void TheFadeStartsBeforeContactByItsOwnLength()
        {
            ArrivalBeats beats = Shipped();

            Assert.AreEqual(26f - 0.6f, beats.FadeStart, Tolerance);
            Assert.AreEqual(0.6f, beats.FadeDuration, Tolerance);
        }

        [Test]
        public void AFadeLongerThanTheDescentStartsAtTheTopOfTheArc()
        {
            // Authored badly rather than impossible: the answer is a fade that takes the whole
            // descent, not a negative start time that runs the loop zero times and shows the impact
            // in full colour.
            var beats = new ArrivalBeats(descent: 4f, impactFade: 40f, settle: 1f, blackout: 1f);

            Assert.AreEqual(0f, beats.FadeStart, Tolerance);
            Assert.AreEqual(beats.Contact, beats.FadeDuration, Tolerance);
        }

        [Test]
        public void TheBlackCoversTheWholeCrashAndThenSome()
        {
            // The settle is the only thing that levels the ship and it must not be cut short, so
            // the screen has to stay black for all of it — the black is what lets the crash happen
            // behind it rather than a reason to shorten it.
            ArrivalBeats beats = Shipped();

            Assert.AreEqual(1.6f + 1.4f, beats.BlackHold, Tolerance);
            Assert.Greater(beats.BlackHold, beats.Settle,
                           "The black must outlast the topple, or the player watches the last of it.");
        }

        [Test]
        public void ALateJoinerPicksTheCurveUpWhereTheHullIs()
        {
            ArrivalBeats beats = Shipped();

            Assert.AreEqual(0f, beats.DescentProgress(0f), Tolerance);
            Assert.AreEqual(0.5f, beats.DescentProgress(13f), Tolerance);

            // Seated after the landing: the end of the curve, never an extrapolation off it.
            Assert.AreEqual(1f, beats.DescentProgress(400f), Tolerance);
            Assert.AreEqual(0f, beats.DescentProgress(-5f), Tolerance);
        }

        [Test]
        public void AZeroLengthDescentIsRefusedRatherThanDividedBy()
        {
            var beats = new ArrivalBeats(descent: 0f, impactFade: 0.6f, settle: 1f, blackout: 1f);

            Assert.Greater(beats.Descent, 0f);
            Assert.IsFalse(float.IsNaN(beats.DescentProgress(1f)),
                           "The shake curve is sampled with this; a NaN there is a still camera.");
        }
    }

    public class HeadAimTests
    {
        private const float Tolerance = 0.001f;

        /// <summary>Degrees of float drift treated as the same rotation.</summary>
        private const float AngleTolerance = 0.01f;

        /// <summary>The shipped neck: 80 either side, 60 down, 70 up.</summary>
        private static HeadAim.Limits Neck() => new(yaw: 80f, down: 60f, up: 70f);

        [Test]
        public void SeatedTheNeckCarriesTheWholeTurn()
        {
            // A body held at a seat pose cannot turn, so the neck is all there is. This is the half
            // of the split that makes a crewmate visibly look at the person beside them.
            Assert.AreEqual(45f, HeadAim.Yaw(45f, HeadAimMode.Seated, Neck()), Tolerance);
        }

        [Test]
        public void OnFootTheNeckCarriesNoneOfIt()
        {
            // PlayerLook has already spent the yaw turning the Rigidbody, so the body is facing
            // where the player looks. Asking the neck for it as well applies it twice.
            Assert.AreEqual(0f, HeadAim.Yaw(45f, HeadAimMode.Free, Neck()), Tolerance);
            Assert.AreEqual(0f, HeadAim.Yaw(-179f, HeadAimMode.Free, Neck()), Tolerance);
        }

        [Test]
        public void YawStopsAtTheNeckAndNotAtTheCamerasOldClamp()
        {
            // The camera used to be allowed 110 degrees because nothing followed it. Now the head
            // does, and 110 degrees of neck is a sheared collar.
            HeadAim.Limits neck = Neck();

            Assert.AreEqual(80f, HeadAim.Yaw(110f, HeadAimMode.Seated, neck), Tolerance);
            Assert.AreEqual(-80f, HeadAim.Yaw(-110f, HeadAimMode.Seated, neck), Tolerance);
        }

        [Test]
        public void PitchIsClampedAsymmetricallyBecauseANeckIs()
        {
            HeadAim.Limits neck = Neck();

            Assert.AreEqual(60f, HeadAim.Pitch(90f, neck), Tolerance, "Positive pitch is chin to chest.");
            Assert.AreEqual(-70f, HeadAim.Pitch(-90f, neck), Tolerance, "Negative pitch is chin up.");
            Assert.AreEqual(20f, HeadAim.Pitch(20f, neck), Tolerance);
        }

        [Test]
        public void ClampingPitchTwiceIsClampingItOnce()
        {
            // Pitch takes no mode, deliberately: only the YAW is the mode's business. A head on
            // foot still pitches, and still may not pitch further than a neck bends — that is what
            // makes another player's character bow when they look at their own feet. And because
            // the angle is re-clamped every frame from a value that was already clamped, clamping
            // has to be idempotent or the head would creep back off the limit.
            Assert.AreEqual(60f, HeadAim.Pitch(HeadAim.Pitch(90f, Neck()), Neck()), Tolerance);
        }

        [Test]
        public void NegativeLimitsAreReadAsZeroRatherThanAsAnInvertedNeck()
        {
            var mangled = new HeadAim.Limits(yaw: -80f, down: -60f, up: -70f);

            Assert.AreEqual(0f, HeadAim.Yaw(45f, HeadAimMode.Seated, mangled), Tolerance);
            Assert.AreEqual(0f, HeadAim.Pitch(45f, mangled), Tolerance);
        }

        [Test]
        public void TheBodyFrameDeltaIsTheSameRotationTheCameraUses()
        {
            // The camera is posed from Local and the bones from Delta. If those two ever disagree
            // the view slides off the head it is supposed to be riding — which is the entire point
            // of routing both through one angle pair.
            Quaternion local = HeadAim.Local(37f, -21f);
            Quaternion delta = HeadAim.Delta(37f, -21f, Vector3.up, Vector3.right);

            Assert.Less(Quaternion.Angle(local, delta), AngleTolerance);
        }

        [Test]
        public void TheSharesComposeBackToTheWholeTurn()
        {
            // The neck takes its share and the head — read back after the neck has already moved —
            // takes the remainder. If those did not compose to exactly the delta, the head would
            // end up short of, or past, where the camera is pointing.
            Quaternion delta = HeadAim.Delta(52f, 18f, Vector3.up, Vector3.right);

            Quaternion composed = HeadAim.Share(delta, 1f - 0.45f) * HeadAim.Share(delta, 0.45f);

            Assert.Less(Quaternion.Angle(delta, composed), AngleTolerance);
        }

        [Test]
        public void ALookOfNothingIsExactlyNoRotation()
        {
            Assert.Less(Quaternion.Angle(Quaternion.identity,
                                         HeadAim.Delta(0f, 0f, Vector3.up, Vector3.right)),
                        AngleTolerance);
        }
    }
}
