using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What the lander's long-range transmitter heard, once it worked: whether the signal has been
    /// received at all, and the settlement it leads to. The whole of <see cref="ShipSignal"/>'s state,
    /// small enough to replicate as one value and save as one record. <c>default</c> is a world whose
    /// transmitter has never worked.
    ///
    /// <para>
    /// The place is kept, not just its id: a destination chosen in a world must stay that world's
    /// destination however the baked catalog it came from changes later, so a save holds where it is.
    /// </para>
    /// </summary>
    public struct SignalDestination : INetworkSerializable, IEquatable<SignalDestination>
    {
        private bool received;
        private FixedString64Bytes id;
        private FixedString64Bytes origin;
        private Vector3 position;
        private float radius;

        /// <summary>The transmitter has listened: the destination below is decided, or there is none.</summary>
        public bool Received => received;

        /// <summary>Received, and it leads somewhere.</summary>
        public bool HasDestination => received && id.Length > 0;

        /// <summary>The settlement's catalog id (<c>WorldSiteCatalog.TownEntry.id</c>). Empty for none.</summary>
        public string Id => id.ToString();

        /// <summary>Whose settlement it is, as the faction names itself — what the COMMS page calls the source.</summary>
        public string Origin => origin.ToString();

        public Vector3 Position => position;

        /// <summary>How far the settlement's buildings reach from <see cref="Position"/>, metres.</summary>
        public float Radius => radius;

        /// <summary>A signal leading to a settlement.</summary>
        public static SignalDestination To(string id, string origin, Vector3 position, float radius) => new()
        {
            received = true,
            id = Fixed(id),
            origin = Fixed(origin),
            position = position,
            radius = Mathf.Max(0f, radius),
        };

        /// <summary>The transmitter listened and nothing it could lead the crew to answered.</summary>
        public static SignalDestination Nowhere => new() { received = true };

        /// <summary>
        /// Truncated, never thrown: an id is a 32-character GUID and a faction name a word or two, far
        /// inside the 61 bytes a <see cref="FixedString64Bytes"/> holds.
        /// </summary>
        private static FixedString64Bytes Fixed(string value)
        {
            var fixedString = new FixedString64Bytes();
            if (!string.IsNullOrEmpty(value)) fixedString.CopyFromTruncated(value);
            return fixedString;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref received);
            serializer.SerializeValue(ref id);
            serializer.SerializeValue(ref origin);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref radius);
        }

        /// <summary>Netcode compares old and new before marking the variable dirty.</summary>
        public bool Equals(SignalDestination other) =>
            received == other.received && id.Equals(other.id) && origin.Equals(other.origin) &&
            position == other.position && Mathf.Approximately(radius, other.radius);

        public override bool Equals(object obj) => obj is SignalDestination other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(received, id, origin, position, radius);
    }
}
