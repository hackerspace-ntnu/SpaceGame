using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How much fight is left in one net.
    ///
    /// <para>
    /// One pool shared by every captive under the net rather than a timer each. That is a design
    /// decision and not an implementation shortcut: with a timer each, a wide net that sweeps up
    /// three creatures holds all three for the full thirty seconds and is strictly better than a
    /// careful single shot, which is a dominant option rather than a choice. Sharing the pool makes
    /// the wide net trade duration for coverage.
    /// </para>
    /// <para>
    /// Expressed as seconds-of-hold remaining rather than as an abstract hit-point count, so the
    /// authored number means what it says: a net rated thirty seconds holds one ordinary captive
    /// for thirty seconds. Keeping that promise is why <see cref="Drain"/> takes the GREATER of its
    /// two rates rather than their sum — see there.
    /// </para>
    /// </summary>
    public class SnareIntegrity
    {
        /// <summary>
        /// The struggle load one ordinary captive puts on the net, in kilogrammes.
        ///
        /// The divisor that turns summed captive mass into a multiple of "one animal's worth", so a
        /// 200 kg creature drains at the rated speed and a 900 kg one drains four and a half times
        /// faster and tears out early.
        /// </summary>
        public const float ReferenceLoad = 200f;

        /// <summary>
        /// How fast an EMPTY net rots, as a fraction of the rated drain.
        ///
        /// Not zero, or a net that catches nothing lies on the sand for the rest of the session.
        /// Not one either — see <see cref="Drain"/> for what a full-rate baseline does to the
        /// meaning of the authored number.
        /// </summary>
        public const float IdleRotShare = 0.25f;

        private float remaining;
        private float capacity;

        /// <summary>Seconds of hold left, if nothing were struggling.</summary>
        public float Remaining => remaining;

        /// <summary>0 when the net is about to tear, 1 when it is fresh. For the HUD.</summary>
        public float Fraction => capacity <= 0f ? 0f : Mathf.Clamp01(remaining / capacity);

        /// <summary>The net has given out and everything under it is free.</summary>
        public bool IsSpent => remaining <= 0f;

        public void Reset(float holdSeconds)
        {
            capacity = Mathf.Max(holdSeconds, 0.01f);
            remaining = capacity;
        }

        /// <summary>
        /// Advance one frame.
        ///
        /// <para>
        /// <paramref name="strugglingMass"/> is the summed estimated mass of everything currently
        /// fighting. The net gives out at whichever is faster: its own slow rot, or the load in it.
        /// </para>
        /// <para>
        /// The GREATER of the two, and not their sum, and that is what keeps the authored number
        /// honest. Adding a baseline second-per-second on top of the load makes one ordinary
        /// captive drain at twice the rated speed, so a net authored at thirty seconds holds one
        /// creature for fifteen — the tooltip and the class summary both saying otherwise. A
        /// designer tuning "how long does a net hold something" would be tuning a number that is
        /// not that. Taking the maximum leaves one reference captive draining at exactly
        /// 1/HoldSeconds while an empty net still rots away on its own.
        /// </para>
        /// </summary>
        public void Drain(float strugglingMass, float deltaTime)
        {
            if (remaining <= 0f) return;

            float load = Mathf.Max(strugglingMass, 0f) / ReferenceLoad;

            remaining -= deltaTime * Mathf.Max(IdleRotShare, load);
            if (remaining < 0f) remaining = 0f;
        }
    }
}
