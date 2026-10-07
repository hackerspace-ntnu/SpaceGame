using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists settlement expeditions: every settlement's rotation (its roster snapshot, the bands raised so far,
    /// who is resting, the last goal) and every band's record, members and stage included.
    ///
    /// <para>
    /// Beside <see cref="NpcWorldSaveable"/> on the NpcWorldSim object in the persistent scene, whose
    /// <c>SaveableEntity</c> finds it. The band's moving group is saved there, under <c>npcworld</c>, as a record
    /// owned "expedition"; its people are saved here, so a band comes back with its losses and its stand-ins are
    /// never saved into a chunk. The director re-adopts the group after the load.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ExpeditionDirector))]
    public class ExpeditionSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "expeditions";

        private ExpeditionDirector director;

        // Lazy, not cached in Awake: EditMode tests never run Awake.
        private ExpeditionDirector Director => director != null ? director : director = GetComponent<ExpeditionDirector>();

        public string SaveKey => Key;

        public struct State
        {
            public SettlementState[] settlements;
            public ExpeditionRecord[] bands;
        }

        public object CaptureState()
        {
            if (Director == null) return null;

            return new State { settlements = Director.CaptureSettlements(), bands = Director.CaptureBands() };
        }

        public void RestoreState(JObject state)
        {
            if (state == null || Director == null) return;

            // Through the shared serializer: it alone reads a Vector3 back without recursing into a stack overflow.
            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            Director.Restore(restored.settlements, restored.bands);
        }
    }
}
