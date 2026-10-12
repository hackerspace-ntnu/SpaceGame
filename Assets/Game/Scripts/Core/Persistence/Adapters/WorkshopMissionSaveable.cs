using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the workshop's story: how far it has got, whether the thieves were spawned, how much of their trail was
    /// found and whether the vault's reactor core was laid. Without it a reload would put the Shellback back in front of
    /// a gate the players opened, and a hunt already under way would spawn a second band of thieves beside the first
    /// (or a second core in the vault), which are saved entities of their own.
    ///
    /// <para>
    /// On the entity's root, beside its <c>SaveableEntity</c>; the mission is somewhere below it. Server-authoritative
    /// on the way back in: the mission lands the state and announces it, so clients follow by message. The Shellback
    /// himself is not saved: the mission rebuilds him where this state has him.
    /// </para>
    /// </summary>
    public class WorkshopMissionSaveable : MonoBehaviour, ISaveable
    {
        /// <summary>Written into save files. NEVER rename.</summary>
        public const string Key = "workshopMission";

        private WorkshopMission mission;

        // Lazy, not cached in Awake: EditMode tests never run Awake.
        private WorkshopMission Mission => mission != null ? mission : mission = GetComponentInChildren<WorkshopMission>(true);

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>A <see cref="WorkshopMissionPhase"/> by name.</summary>
            public string phase;

            /// <summary>The thieves and the gear were spawned at their camp: they are records of their own now.</summary>
            public bool bandSpawned;

            /// <summary>How many clues of the thieves' trail have been examined.</summary>
            public int clues;

            /// <summary>The reactor core was laid in the vault: it is a record of its own now.</summary>
            public bool coreSpawned;

            /// <summary>The hoist fitted the core to a ship before the Shellbacks offered it.</summary>
            public bool coreFitted;
        }

        public object CaptureState()
        {
            if (Mission == null) return null;

            // The authored start. The core lies in the vault from the first moment, so it alone is worth a record.
            if (Mission.Phase == WorkshopMissionPhase.Locked && !Mission.BandSpawned && !Mission.CoreSpawned) return null;

            return new State
            {
                phase = Mission.Phase.ToString(),
                bandSpawned = Mission.BandSpawned,
                clues = Mission.CluesFound,
                coreSpawned = Mission.CoreSpawned,
                coreFitted = Mission.CoreFitted,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Mission == null) return;

            State restored = state != null ? state.ToObject<State>(SaveSerializer.Serializer) : default;
            WorkshopMissionPhase phase = WorkshopMissionPhase.Locked;
            if (!string.IsNullOrEmpty(restored.phase) && !Enum.TryParse(restored.phase, out phase))
            {
                // A phase this build no longer has: start the story again rather than guess where it was.
                Debug.LogError($"[WorkshopMission] the save names phase '{restored.phase}', which this build does not " +
                               "have; the story starts again.", this);
                phase = WorkshopMissionPhase.Locked;
            }

            Mission.Restore(phase, restored.bandSpawned, restored.clues, restored.coreSpawned, restored.coreFitted);
        }
    }
}
