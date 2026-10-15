using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.World.Bounties
{
    /// <summary>How far the crew are with the bounty they took. Saved by name: never rename a value.</summary>
    public enum BountyPhase : byte
    {
        /// <summary>Taken: ask around the settlement where it was posted.</summary>
        Asking,

        /// <summary>Somebody gave a tip: the visor shows where to search.</summary>
        Located,

        /// <summary>The target surrendered or is dead: bring them back.</summary>
        Caught,
    }

    /// <summary>Why the open hunt last started over. Only on the wire, so every machine can tell its player why.</summary>
    public enum BountyFailure : byte
    {
        None,

        /// <summary>The cast stood longer than the hunt's time limit.</summary>
        OutOfTime,

        /// <summary>Every player stayed outside the hunt's bounds too long.</summary>
        OutOfBounds,

        /// <summary>The target was wanted alive and died.</summary>
        TargetKilled,

        /// <summary>Every player went down at once.</summary>
        CrewDown,
    }

    /// <summary>
    /// The crew's open hunt as every machine reads it: which one, where it is handed in, where its site is, the target,
    /// how long the crew have been out of its bounds, and why it last started over. Small enough to replicate as one
    /// value. <c>default</c> is a world where no hunt is open. Claimed hunts are a list of their own.
    /// </summary>
    public struct BountyProgress : INetworkSerializable, IEquatable<BountyProgress>
    {
        /// <summary>The open bounty's place in <see cref="BountyCatalog"/> plus one; 0 when none is open.</summary>
        public int Open;

        public BountyPhase Phase;

        /// <summary>The settlement the bounty was taken in: where tips come from and where it is handed in.</summary>
        public Vector3 Town;

        public float TownRadius;

        /// <summary>Where the hunt's site is: the visor's search area is drawn round it.</summary>
        public Vector3 Site;

        /// <summary>The target's NetworkObjectId once it exists; 0 before, and offline.</summary>
        public ulong Target;

        public bool TargetDead;

        /// <summary>Seconds every player has been outside the hunt's bounds; 0 while anyone is inside.</summary>
        public float OutsideFor;

        /// <summary>Counts the times the open hunt started over, wrapping: a change is a failure just now.</summary>
        public byte Resets;

        /// <summary>Why it last started over.</summary>
        public BountyFailure Failure;

        public bool HasOpen => Open > 0;

        public int OpenIndex => Open - 1;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Open);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref Town);
            serializer.SerializeValue(ref TownRadius);
            serializer.SerializeValue(ref Site);
            serializer.SerializeValue(ref Target);
            serializer.SerializeValue(ref TargetDead);
            serializer.SerializeValue(ref OutsideFor);
            serializer.SerializeValue(ref Resets);
            serializer.SerializeValue(ref Failure);
        }

        public bool Equals(BountyProgress other) =>
            Open == other.Open && Phase == other.Phase && Town == other.Town && TownRadius.Equals(other.TownRadius) && Site == other.Site &&
            Target == other.Target && TargetDead == other.TargetDead && OutsideFor.Equals(other.OutsideFor) &&
            Resets == other.Resets && Failure == other.Failure;

        public override bool Equals(object obj) => obj is BountyProgress other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(HashCode.Combine(Open, Phase, Town, TownRadius, Site, Target, TargetDead, OutsideFor), Resets, Failure);
    }
}
