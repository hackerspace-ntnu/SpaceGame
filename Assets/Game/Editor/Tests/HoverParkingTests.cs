// A parked hover hull must be immovable by contact. Agents and mounts walk on KINEMATIC bodies,
// which depenetrate a dynamic hull with infinite authority regardless of mass — measured as a
// strolling NPC shoving the 60-tonne arrival wreck across the sand. The defence is the parked
// constraint set, and these pin the one fact it exists for.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class HoverParkingTests
    {
        [Test]
        public void ParkingPinsTheHorizontalAxesAndOnlyThose()
        {
            RigidbodyConstraints parked = HoverRigidbodyMotor.ParkedConstraints(RigidbodyConstraints.None);

            Assert.That(parked & RigidbodyConstraints.FreezePositionX, Is.Not.EqualTo(RigidbodyConstraints.None),
                        "A parked hull left free in X can be shoved by any kinematic walker leaning on it.");
            Assert.That(parked & RigidbodyConstraints.FreezePositionZ, Is.Not.EqualTo(RigidbodyConstraints.None),
                        "A parked hull left free in Z can be shoved by any kinematic walker leaning on it.");

            // Y stays free: gravity is what seats the parked hull on the ground under it. Freezing
            // it would leave a craft parked mid-hover hanging where it stopped.
            Assert.That(parked & RigidbodyConstraints.FreezePositionY, Is.EqualTo(RigidbodyConstraints.None),
                        "Parking must not freeze Y — gravity settling the hull is the whole point of restWhenParked.");
        }

        [Test]
        public void ParkingKeepsWhatWasAuthored()
        {
            RigidbodyConstraints authored = RigidbodyConstraints.FreezeRotation;
            RigidbodyConstraints parked = HoverRigidbodyMotor.ParkedConstraints(authored);

            Assert.That(parked & RigidbodyConstraints.FreezeRotation, Is.EqualTo(RigidbodyConstraints.FreezeRotation),
                        "Parking must add to the authored constraints, never replace them — the hull's " +
                        "rotation freeze is what keeps a wreck level on uneven ground.");
        }

        // FreezeRotation stops physics TILTING the hull. It equally stops physics correcting a tilt
        // the hull did not get from physics — one written in by an interrupted descent, a restored
        // save or a teleport — and the driven path's every-step attitude write is the only thing
        // that ever took one back out. Parking used to return before that write, so the parked state
        // was the one place a tilt could stand for the rest of the session, with the crew sliding
        // off their own deck and nothing in the console.

        [Test]
        public void LevellingEndsFlatAndKeepsTheHeading()
        {
            Quaternion tilted = Quaternion.Euler(23f, 140f, -11f);

            Quaternion levelled = HoverRigidbodyMotor.LevelStep(tilted, 360f);

            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, levelled.eulerAngles.x), 1e-3f,
                        "a levelled hull still has pitch in it, so its deck is still a slope");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, levelled.eulerAngles.z), 1e-3f,
                        "a levelled hull still has roll in it, so its deck is still a slope");
            Assert.AreEqual(0f, Mathf.DeltaAngle(140f, levelled.eulerAngles.y), 1e-2f,
                        "levelling turned the hull — a parked ship must face the way it was parked");
        }

        [Test]
        public void LevellingIsRateLimited()
        {
            Quaternion tilted = Quaternion.Euler(40f, 0f, 0f);

            Quaternion stepped = HoverRigidbodyMotor.LevelStep(tilted, 1f);

            // A wreck that flicks upright in one frame reads as a glitch, and anybody standing on
            // the deck is carried by that rotation.
            Assert.AreEqual(1f, Quaternion.Angle(tilted, stepped), 1e-2f,
                        "levelling took more than the step it was allowed");
        }

        [Test]
        public void LevellingCanBeTurnedOff()
        {
            Quaternion tilted = Quaternion.Euler(40f, 0f, 0f);

            Assert.AreEqual(0f, Quaternion.Angle(tilted, HoverRigidbodyMotor.LevelStep(tilted, 0f)), 1e-3f,
                        "a zero rate must leave the attitude alone — it is the opt-out for a hull " +
                        "that is meant to sit at whatever angle the ground gives it");
        }
    }
}
