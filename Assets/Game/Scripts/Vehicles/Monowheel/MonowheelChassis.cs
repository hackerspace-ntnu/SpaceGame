// The solid chassis of a monowheel, fore and aft of the seats: a kinematic body of its own, posed
// with the art.
//
// The physics root never pitches (its rotation is frozen to yaw), but the art does: MonowheelLean
// tips the Body about the hub until the ski rests on the sand. Chassis boxes on the root therefore
// stayed level while the art tipped, and on rising ground (about 13 degrees and up) their noses met
// the slope before the wheel did and held the whole vehicle up. The pose then brought the ski down
// to the sand and left the wheel hanging in the air.
//
// Hung under Body instead, on a kinematic Rigidbody, the chassis follows the posed art, so what
// gets shot and bumped into is where the frame is drawn. Being kinematic, it never stands on
// anything: a kinematic body makes no contacts with static ground, so only the wheel carries the
// vehicle. It still shoves moving bodies (players, creatures) and blocks raycasts. Its contacts
// with the wheel's own colliders on the root are suspended here, or it would push its own vehicle
// about every frame.
using SpaceGame.Agents;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class MonowheelChassis : MonoBehaviour
    {
        private readonly RiderCollisionIgnore ownWheel = new RiderCollisionIgnore();

        private void Awake() => IgnoreOwnWheel();

        /// <summary>
        /// Stop the chassis colliding with every other collider of the vehicle it belongs to.
        /// Public so an EditMode test, which gets no Awake, can switch it on.
        /// </summary>
        public void IgnoreOwnWheel()
        {
            Rigidbody vehicle = transform.parent != null ? transform.parent.GetComponentInParent<Rigidbody>() : null;
            if (vehicle == null)
            {
                Debug.LogError($"[MonowheelChassis] '{name}' is not under a vehicle Rigidbody; it would push nothing " +
                               "and be pushed by nothing it belongs to.", this);
                return;
            }

            ownWheel.Apply(transform, vehicle.transform);
        }
    }
}
