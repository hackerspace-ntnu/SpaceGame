using System;
using Unity.Netcode;

namespace SpaceGame.Core
{
    /// <summary>
    /// What the server states about the sky: at clock reading <see cref="Clock"/> the world reads
    /// <see cref="Phase"/>. Replicated by <see cref="SkyNetwork"/>.
    /// <para>
    /// One struct rather than two NetworkVariables, because the two numbers are only meaningful
    /// together. Netcode reads a behaviour's variables one at a time and raises each callback as
    /// it goes, so a separate pair would be observed half-updated — a new phase against a stale
    /// clock reading is an arbitrary hour, and the sun would be pointed at it before the second
    /// value landed.
    /// </para>
    /// </summary>
    public struct SkyAnchor : INetworkSerializable, IEquatable<SkyAnchor>
    {
        /// <summary>
        /// False until the server has actually said something. Without it the default value —
        /// phase 0 at clock 0 — is indistinguishable from a genuine statement that the world is
        /// at midnight, and every client joining before the host's sun had woken up would be
        /// told so.
        /// </summary>
        public bool Stated;

        /// <summary>Time of day, 0 midnight to 1 midnight.</summary>
        public float Phase;

        /// <summary>The reading of the shared clock that <see cref="Phase"/> was measured at.</summary>
        public double Clock;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Stated);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Clock);
        }

        /// <summary>
        /// Netcode compares the old and new value before it marks the variable dirty, so an
        /// exact re-statement of the same anchor costs nothing. This is what makes it safe for
        /// <c>SkyNetwork.Publish</c> to run on every announcement without rate-limiting itself.
        /// </summary>
        public bool Equals(SkyAnchor other) =>
            Stated == other.Stated &&
            Phase.Equals(other.Phase) &&
            Clock.Equals(other.Clock);
    }
}
