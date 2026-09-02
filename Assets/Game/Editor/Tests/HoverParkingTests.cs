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
    }
}
