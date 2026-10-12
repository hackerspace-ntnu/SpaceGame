using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what the lander's transmitter heard: whether the signal has been received, and the
    /// settlement it leads to — the place itself, not just its id, so the destination a world chose stays
    /// that world's destination even if the baked town list changes under it.
    ///
    /// <para>
    /// Null until the transmitter has worked. A save from before this saver existed reads null too: such a
    /// world chooses its destination the first time the server sees a working transmitter — at once on
    /// load if one is already fitted — from where the hull lies, which is the same answer it would have
    /// got at the time.
    /// </para>
    /// <para>
    /// Placed by hand on <c>PlayerShip.prefab</c> beside <see cref="ShipSignal"/> and collected by the
    /// hull's root <c>SaveableEntity</c>.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ShipSignal))]
    public class ShipSignalSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "shipSignal";            // written into save files — never rename

        private ShipSignal signal;

        // Lazy-resolved, NOT cached in Awake: EditMode tests never run Awake.
        private ShipSignal Signal => signal != null ? signal : signal = GetComponent<ShipSignal>();

        public string SaveKey => Key;

        /// <summary>Floats, not a Vector3: Newtonsoft walks a Vector3's normalized property forever.</summary>
        public struct State
        {
            public bool received;
            public string id;
            public string origin;
            public float x, y, z;
            public float radius;
        }

        public object CaptureState()
        {
            if (Signal == null) return null;

            SignalDestination d = Signal.Destination;
            if (!d.Received) return null;

            return new State
            {
                received = true,
                id = d.Id,
                origin = d.Origin,
                x = d.Position.x,
                y = d.Position.y,
                z = d.Position.z,
                radius = d.Radius,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Signal == null) return;

            if (state == null)
            {
                Signal.Restore(default);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Signal.Restore(ToDestination(restored));
        }

        /// <summary>A saved record as the signal it describes.</summary>
        public static SignalDestination ToDestination(in State state)
        {
            if (!state.received) return default;
            if (string.IsNullOrEmpty(state.id)) return SignalDestination.Nowhere;

            return SignalDestination.To(state.id, state.origin, new Vector3(state.x, state.y, state.z), state.radius);
        }
    }
}
