// Every fault this machine has seen this session, newest last, bounded.
//
// Static and not a component, for the same reason ChatLog is: the ledger has to outlive scene loads,
// and the world streams chunk scenes in and out constantly. A buffer living on a scene object would
// be emptied by events the player did not cause, which is exactly when they most want to report one.
//
// A List used as a queue rather than a real ring buffer, matching ChatLog: at this size the shuffle
// is a few dozen pointer copies per fault, and in exchange a reader can index it in arrival order.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Diagnostics
{
    public static class FaultLedger
    {
        /// <summary>Records kept. Beyond this the oldest is dropped; <see cref="TotalFaults"/> still counts it.</summary>
        public const int Capacity = 64;

        private static readonly List<FaultRecord> records = new(Capacity);

        /// <summary>Oldest first. Never null.</summary>
        public static IReadOnlyList<FaultRecord> Recent => records;

        /// <summary>
        /// Every fault since the session began, including the ones the buffer has dropped.
        /// Reported separately because "64 faults" and "9000 faults" are very different sessions and
        /// the buffer length cannot tell them apart.
        /// </summary>
        public static int TotalFaults { get; private set; }

        /// <summary>
        /// Statics survive play-mode exit here — enter-play-mode options are on — so without this the
        /// second play session in an Editor starts holding the first one's faults.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            records.Clear();
            TotalFaults = 0;
        }

        public static void Add(in FaultRecord record)
        {
            records.Add(record);
            if (records.Count > Capacity) records.RemoveAt(0);
            TotalFaults++;
        }
    }
}
