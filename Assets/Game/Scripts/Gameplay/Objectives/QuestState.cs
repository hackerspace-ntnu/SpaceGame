using System;
using Unity.Collections;
using Unity.Netcode;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// One begun quest in the only form a <c>NetworkList</c> will carry: unmanaged, fixed size, comparable. The
    /// quest by <see cref="ObjectiveChain.Id"/>, when it was taken on, and the crew's place in it.
    /// </summary>
    public struct QuestState : INetworkSerializable, IEquatable<QuestState>
    {
        public FixedString64Bytes Quest;

        /// <summary><see cref="Objectives.Quest.Order"/>.</summary>
        public int Order;

        public ObjectiveProgress Progress;

        public static QuestState Of(Quest quest) => new()
        {
            Quest = new FixedString64Bytes(quest.Id),
            Order = quest.Order,
            Progress = quest.Progress,
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Quest);
            serializer.SerializeValue(ref Order);
            Progress.NetworkSerialize(serializer);
        }

        /// <summary>
        /// Required by <c>NetworkList</c>, which uses it to decide whether a write is a change worth telling anyone
        /// about.
        /// </summary>
        public bool Equals(QuestState other) =>
            Quest.Equals(other.Quest) && Order == other.Order && Progress.Equals(other.Progress);

        public override bool Equals(object obj) => obj is QuestState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Quest.GetHashCode(), Order, Progress);
    }
}
