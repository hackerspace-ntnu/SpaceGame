using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Air you can breathe. A trigger volume that stops <see cref="SuitOxygen"/> draining while
    /// something is inside it.
    ///
    /// <para>
    /// Put one on the ship's interior, on a sealed habitat, on anywhere the fiction says has an
    /// atmosphere. Everywhere else on the planet drains, so the absence of one of these is what
    /// makes the open world cost you something to be in.
    /// </para>
    /// <para>
    /// A volume rather than a flag on the ship, so shelter is a property of SPACE and not of a
    /// particular object: an interior scene, a tent and a cave all become breathable by having one
    /// of these in them, with no code knowing what any of them are.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BreathableVolume : MonoBehaviour
    {
        private void Reset()
        {
            // A volume that is not a trigger is a wall the player walks into, which is a confusing
            // way to find out you forgot a checkbox.
            var box = GetComponent<Collider>();
            if (box != null) box.isTrigger = true;
        }

        private void Awake()
        {
            var box = GetComponent<Collider>();
            if (box != null && !box.isTrigger)
            {
                Debug.LogWarning($"[BreathableVolume] {name}'s collider is not a trigger, so nothing " +
                                 "will ever enter it and the air here will never count as breathable.", this);
            }
        }

        private void OnTriggerEnter(Collider other) => Find(other)?.EnterBreathable(this);

        private void OnTriggerExit(Collider other) => Find(other)?.ExitBreathable(this);

        /// <summary>
        /// <c>GetComponentInParent</c> rather than <c>GetComponent</c>: the collider that trips
        /// this is a capsule somewhere under the player, not the root the suit lives on.
        /// </summary>
        private static SuitOxygen Find(Collider other) =>
            other != null ? other.GetComponentInParent<SuitOxygen>() : null;

        private void OnDisable()
        {
            // Unity does not raise OnTriggerExit when the volume itself is switched off or
            // streamed out, so a player standing in a chunk that unloads would keep the shelter
            // for ever and never breathe their own supply again.
            SuitOxygen.ForgetVolume(this);
        }
    }
}
