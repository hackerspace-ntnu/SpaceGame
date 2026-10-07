using System;
using Unity.Netcode;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Where the crew are in the objective chain. The whole of the chain's shared state: small
    /// enough to replicate as one value and save as one record. <c>default</c> is a world nobody
    /// has played yet.
    /// </summary>
    public struct ObjectiveProgress : INetworkSerializable, IEquatable<ObjectiveProgress>
    {
        /// <summary>Index into the chain. Equal to the step count once the chain is finished.</summary>
        public int Step;

        /// <summary>
        /// The current step's one-time setup has run on the server — the module is lying in the
        /// sand. Saved, so a load never runs it twice and never skips it.
        /// </summary>
        public bool Begun;

        /// <summary>
        /// Which of the current step's remarks (<see cref="ObjectiveStep.IsRemarkDue"/>) have been said,
        /// one bit per remark index. Cleared with every new step; saved, so none is said twice.
        /// </summary>
        public int Remarks;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Step);
            serializer.SerializeValue(ref Begun);
            serializer.SerializeValue(ref Remarks);
        }

        /// <summary>
        /// Netcode compares old and new before marking the variable dirty, so re-stating the same
        /// progress costs nothing on the wire.
        /// </summary>
        public bool Equals(ObjectiveProgress other) =>
            Step == other.Step && Begun == other.Begun && Remarks == other.Remarks;

        public override bool Equals(object obj) => obj is ObjectiveProgress other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Step, Begun, Remarks);
    }
}
