using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists where a drifting hull is on its route: which waypoint it is sailing for (or moored
    /// at), whether it is under way, and how long is left at the mooring.
    ///
    /// The pose itself is the entity record's, re-applied through <c>SaveTeleport</c>; this is the
    /// part the pose cannot say. Without it a load restores the Sky City in mid-voyage with a fresh
    /// module that believes it is moored at waypoint 0 — so it waits out a full mooring in mid-air,
    /// then turns round and sails back to the start of the loop.
    /// </summary>
    [RequireComponent(typeof(DriftRouteModule))]
    public class DriftRouteSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "driftRoute";     // written into save files — NEVER rename

        private DriftRouteModule route;

        // Lazy, not cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private DriftRouteModule Route => route != null ? route : route = GetComponent<DriftRouteModule>();

        public string SaveKey => Key;

        public struct State
        {
            public int leg;
            public bool underWay;
            public float mooredRemaining;
        }

        public object CaptureState() => Route == null
            ? null
            : new State { leg = Route.Leg, underWay = Route.UnderWay, mooredRemaining = Route.MooredRemaining };

        public void RestoreState(JObject state)
        {
            // No record: the module keeps the fresh world's answer, moored at its first waypoint.
            if (Route == null || state == null)
                return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);
            Route.RestoreDrift(restored.leg, restored.underWay, restored.mooredRemaining);
        }
    }
}
