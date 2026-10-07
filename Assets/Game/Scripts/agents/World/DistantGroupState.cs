// One group seen from afar, as it crosses the wire (DistantGroups): which group, which template (a client
// deals the column itself from the template and the seed -- ColumnDeal is deterministic), where it is and
// which way it faces, and whether it is live (then the real members are drawn instead). Unmanaged and
// fixed size, as a NetworkList needs. Whether it is moving is not sent: the drawn transform's own motion
// says so (Multiplayer.md: derivable from replicated state, send nothing).
using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents
{
    public struct DistantGroupState : INetworkSerializable, IEquatable<DistantGroupState>
    {
        /// <summary><see cref="NpcGroupTemplate.HashOf"/> of the group's id: the silhouette's key.</summary>
        public int GroupHash;
        /// <summary><see cref="NpcGroupTemplate.IdHash"/> of its template (NpcWorldSim.FindTemplateByHash).</summary>
        public int TemplateHash;
        /// <summary>The group's saved roster seed: the same seed deals the same column on every machine.</summary>
        public int RosterSeed;
        /// <summary>The group's position: its leader's slot.</summary>
        public Vector3 Position;
        /// <summary>Degrees about +Y of the heading the live spawn would face (NpcGroup.Heading).</summary>
        public float Yaw;
        /// <summary>Live: its members are real and the silhouette steps aside.</summary>
        public bool Spawned;

        public static DistantGroupState Of(NpcGroup group) => new DistantGroupState
        {
            GroupHash = NpcGroupTemplate.HashOf(group.Id),
            TemplateHash = NpcGroupTemplate.HashOf(group.TemplateId),
            RosterSeed = group.RosterSeed,
            Position = group.Position,
            Yaw = YawOf(group.Heading),
            Spawned = group.Spawned,
        };

        public static float YawOf(Vector3 heading) => Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;

        public Vector3 Heading => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref GroupHash);
            serializer.SerializeValue(ref TemplateHash);
            serializer.SerializeValue(ref RosterSeed);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Spawned);
        }

        /// <summary>Required by NetworkList, which sends only a write that differs.</summary>
        public bool Equals(DistantGroupState other) =>
            GroupHash == other.GroupHash
            && TemplateHash == other.TemplateHash
            && RosterSeed == other.RosterSeed
            && Position.Equals(other.Position)
            && Yaw.Equals(other.Yaw)
            && Spawned == other.Spawned;

        public override bool Equals(object obj) => obj is DistantGroupState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(GroupHash, TemplateHash, RosterSeed, Position, Yaw, Spawned);
    }
}
