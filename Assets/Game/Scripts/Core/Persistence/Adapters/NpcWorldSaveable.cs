using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists every NPC group in the world as a handful of numbers.
    ///
    /// <para>
    /// <b>Why this is one saver and not fifty entities.</b> A caravan is normally a record, not a
    /// set of GameObjects — that is the whole point of <see cref="NpcWorldSim"/>. So the thing worth
    /// saving is the record: a position, a destination, which job it is on and how far through the
    /// dwell it is. Twelve groups is twelve of those, against the dozens of individually-keyed
    /// entities that persisting live members would produce, most of which do not exist at the moment
    /// the save is written.
    /// </para>
    /// <para>
    /// <b>Spawned groups still save correctly.</b> <see cref="NpcWorldSim.CaptureRecords"/> reads
    /// the position back off the live members first, so a save taken while you are standing next to
    /// a caravan restores it where you last saw it rather than where it happened to spawn.
    /// </para>
    /// <para>
    /// Put this next to <see cref="NpcWorldSim"/> in the persistent scene, on an object that also
    /// carries a <c>SaveableEntity</c> — that component is what finds savers at all.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NpcWorldSim))]
    public class NpcWorldSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "npcworld";

        private NpcWorldSim sim;

        private NpcWorldSim Sim => sim != null ? sim : sim = GetComponent<NpcWorldSim>();

        public string SaveKey => Key;

        public struct State
        {
            public NpcGroup.Record[] groups;
        }

        public object CaptureState()
        {
            if (Sim == null) return null;

            return new State { groups = Sim.CaptureRecords() };
        }

        public void RestoreState(JObject state)
        {
            if (state == null || Sim == null) return;

            // Through the shared serializer rather than by probing tokens: it is the only thing that
            // knows how to read a Vector3 back, and one read without the registered converters
            // recurses through the struct's own properties into a stack overflow.
            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            Sim.RestoreRecords(restored.groups);
        }
    }
}
