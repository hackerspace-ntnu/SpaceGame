using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a flyer's roost and the leg it was flying.
    ///
    /// <b>The roost is the point.</b> <c>AirWanderModule.OnEnable</c> used to set its wander volume's
    /// centre to <c>transform.position</c> every single time the module came back — on a load, and
    /// on every chunk that streamed the creature out and in again. With the pose restored first,
    /// that means the volume follows the creature instead of containing it: a flyer that drifts to
    /// the far edge of its cylinder and is saved there wakes up with the whole cylinder moved out to
    /// meet it, and does it again next time. Over a few sessions the colony leaves the map.
    ///
    /// The module now latches once; this saver is what carries that latch across a reload.
    /// </summary>
    [RequireComponent(typeof(AirWanderModule))]
    public class AirWanderSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "airWander";     // written into save files — NEVER rename

        private AirWanderModule wander;

        private AirWanderModule Wander =>
            wander != null ? wander : wander = GetComponent<AirWanderModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool hasAnchor;

            /// <summary>Centre of the wander cylinder. Unused when the module has an explicit anchor transform.</summary>
            public Vector3 anchorPosition;

            public bool hasDestination;

            /// <summary>Meaningless unless <see cref="hasDestination"/>.</summary>
            public Vector3 destination;

            public float waitTimer;
        }

        public object CaptureState()
        {
            if (Wander == null) return null;

            return new State
            {
                hasAnchor = Wander.HasAnchorPosition,
                anchorPosition = Wander.AnchorPosition,
                hasDestination = Wander.HasDestination,
                destination = Wander.CurrentDestination,
                waitTimer = Wander.WaitTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Wander == null) return;

            if (state == null)
            {
                // The anchor is not cleared: "no record" must not mean "re-roost where you are".
                Wander.RestoreAirWander(false, Vector3.zero, false, Vector3.zero, 0f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Wander.RestoreAirWander(restored.hasAnchor, restored.anchorPosition,
                                    restored.hasDestination, restored.destination, restored.waitTimer);
        }
    }
}
