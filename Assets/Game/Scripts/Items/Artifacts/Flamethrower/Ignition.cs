using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// What the flame is allowed to set alight, and the one call that sets it alight.
    ///
    /// <para>
    /// Shared by the cone and by the patches of <see cref="GroundFire"/> the jet leaves behind,
    /// because the two must agree: a crate the jet ignites and a crate that only wandered into a
    /// patch have to end up in the same state, and a rule written twice is a rule that drifts.
    /// </para>
    /// <para>
    /// <b>Everything burns — but "everything" is not literally everything.</b> The flame reaches
    /// with a mask of <c>~0</c> by default, and a receiver is created on whatever it finds, so
    /// without a rule the first sweep across a dune puts a <see cref="StatusReceiver"/> on the
    /// terrain chunk and sets a square kilometre of ground on fire as a single body. That rule is
    /// <see cref="StatusReceiver.EnsureOnBody"/> and it is shared with every other continuous
    /// artifact, because a flame and a plume of vapour that disagreed about what counts as a body
    /// would be two rules to keep in step. Scenery is left to the patches, which is the system that
    /// already draws fire standing on it.
    /// </para>
    /// </summary>
    public static class Ignition
    {
        /// <summary>
        /// Set <paramref name="body"/> alight, if it is the sort of thing that can catch. Returns
        /// the receiver that now carries the fire, or null if it is not.
        ///
        /// <para>
        /// Safe to call on every machine and deliberately called that way. <see cref="StatusReceiver.Apply"/>
        /// returns early on a machine that does not simulate the body, so the fire is billed exactly
        /// once however many machines announce it — and running it everywhere is what puts the
        /// receiver on the same bodies everywhere. A receiver only the server invented is a body
        /// that burns for the server alone, because the status arrives on that body's own relay and
        /// a relay with nothing subscribed drops it without a word.
        /// </para>
        /// </summary>
        /// <param name="source">
        /// Who is responsible. <c>ProvocationModule</c> reads it to decide who a creature is now
        /// afraid of, so it is the player holding the lance rather than the flame that touched them.
        /// </param>
        public static StatusReceiver Light(GameObject body, Transform source)
        {
            StatusReceiver receiver = Receiver(body);
            if (receiver == null) return null;

            // No duration and no magnitude: five seconds is what the fire says it is worth, and a
            // flamethrower does not get to decide how long it burns.
            receiver.Apply(StatusKind.Burning, source: source);
            return receiver;
        }

        /// <summary>
        /// The receiver <paramref name="body"/> should burn through, creating one where the body is
        /// the sort of thing that catches fire. Null for world geometry.
        /// </summary>
        public static StatusReceiver Receiver(GameObject body)
        {
            if (body == null) return null;

            // Authored, or already alight from something else, is always honoured; otherwise the
            // shared "is this a body at all" rule decides, which is what keeps the flame and the
            // cryo sprayer's plume agreeing about what a dune is.
            StatusReceiver receiver = StatusReceiver.EnsureOnBody(body);
            if (receiver == null) return null;

            // The flames go on beside the receiver, on the same object and on every machine, so a
            // crate nobody authored anything onto still visibly burns. BurningVisual takes it from
            // there by listening to the receiver — nothing here tells it to draw.
            if (receiver.GetComponent<BurningVisual>() == null)
                receiver.gameObject.AddComponent<BurningVisual>();

            return receiver;
        }
    }
}
