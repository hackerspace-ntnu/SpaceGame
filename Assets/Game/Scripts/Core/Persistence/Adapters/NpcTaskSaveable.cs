using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a live NPC's errand — which job it is on, how far through it is, and where home is.
    ///
    /// <b>The gap this fills.</b> <see cref="NpcWorldSaveable"/> already stores every NPC group as a
    /// record, and that covers the ordinary case, because most NPCs are records rather than
    /// GameObjects. It does not cover an NPC that exists as a GameObject in its own right —
    /// authored into a chunk scene, or spawned and then detached from its group. For those the
    /// errand lives only in this component, and <c>OnEnable</c> hard-resets it to
    /// <c>Phase.Choosing</c>: a scavenger two thirds of the way through a forty-second dig reloads
    /// having decided to do something else somewhere else.
    ///
    /// <b>The home is the part with teeth.</b> <c>EnsureHome</c> resolves <c>HomePosition</c> to
    /// whichever site of the right kind is nearest — nearest to <em>the current position</em>, which
    /// after a load is wherever the NPC was saved. So an NPC saved at the far end of a two-kilometre
    /// errand permanently adopts a new home, and every task with <c>searchFromHome</c> then measures
    /// its radius from there. It never comes back. Restoring the home alongside the
    /// <c>homeResolved</c> flag is what closes that door: <c>EnsureHome</c> returns immediately when
    /// the flag is already set.
    /// </summary>
    [RequireComponent(typeof(NpcTaskModule))]
    public class NpcTaskSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "npcTask";     // written into save files — NEVER rename

        private NpcTaskModule module;

        private NpcTaskModule Module => module != null ? module : module = GetComponent<NpcTaskModule>();

        public string SaveKey => Key;

        public struct State
        {
            public NpcTaskModule.Phase phase;

            /// <summary>Index into the module's own task list. -1 for "has not picked one yet".</summary>
            public int taskIndex;

            /// <summary>What the NPC would name as its destination. Chatter and dialog read this.</summary>
            public string destinationName;

            /// <summary>The site it last headed for; the planner avoids picking the same one twice running.</summary>
            public string lastSiteId;

            /// <summary>Retry delay while Choosing, or the remaining dwell while Dwelling.</summary>
            public float phaseTimer;

            /// <summary>How long the current journey has run, against the module's travel timeout.</summary>
            public float travelElapsed;

            /// <summary>A queued ForceTask that had not been consumed yet. -1 normally.</summary>
            public int forcedTaskIndex;

            public bool homeResolved;
            public Vector3 homePosition;
        }

        public object CaptureState()
        {
            if (Module == null) return null;

            return new State
            {
                phase = Module.CurrentPhase,
                taskIndex = Module.CurrentTaskIndex,
                destinationName = Module.CurrentDestinationName,
                lastSiteId = Module.LastSiteId,
                phaseTimer = Module.PhaseTimer,
                travelElapsed = Module.TravelElapsed,
                forcedTaskIndex = Module.ForcedTaskIndex,
                homeResolved = Module.HomeResolved,
                homePosition = Module.HomePosition,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Module == null) return;

            if (state == null)
            {
                // Defaults: no job, choose one now. The home is left alone — an NPC with no record
                // must not be told to adopt whichever camp it happens to have been reloaded beside.
                Module.RestoreTaskState(NpcTaskModule.Phase.Choosing, -1, string.Empty, string.Empty,
                                        0f, 0f, -1, false, Vector3.zero);
                return;
            }

            // Through the shared serializer, always — it carries a Vector3, and reading one without
            // the registered converters recurses through its own properties into a stack overflow.
            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Module.RestoreTaskState(restored.phase, restored.taskIndex, restored.destinationName,
                                    restored.lastSiteId, restored.phaseTimer, restored.travelElapsed,
                                    restored.forcedTaskIndex, restored.homeResolved,
                                    restored.homePosition);
        }
    }
}
