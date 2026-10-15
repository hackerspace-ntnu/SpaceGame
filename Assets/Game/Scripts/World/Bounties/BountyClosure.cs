using System;
using Unity.Netcode;

namespace SpaceGame.World.Bounties
{
    /// <summary>How a hunt ended. A hunt ends once and is never offered again. Saved by name: never rename a value.</summary>
    public enum BountyClosure : byte
    {
        /// <summary>Handed in and paid.</summary>
        Claimed,

        /// <summary>Out of time, or the crew left its bounds: gone for good, its people taken away.</summary>
        Expired,
    }

    /// <summary>One ended hunt as the wire carries it: its <see cref="BountyCatalog"/> place, and how it ended.</summary>
    public struct ClosedHunt : INetworkSerializable, IEquatable<ClosedHunt>
    {
        public int place;
        public BountyClosure closure;

        public ClosedHunt(int place, BountyClosure closure)
        {
            this.place = place;
            this.closure = closure;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref place);
            serializer.SerializeValue(ref closure);
        }

        public bool Equals(ClosedHunt other) => place == other.place && closure == other.closure;

        public override bool Equals(object obj) => obj is ClosedHunt other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(place, closure);
    }
}
