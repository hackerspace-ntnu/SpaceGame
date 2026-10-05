using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps the satellite dish pointing where a player left it.
    ///
    /// <para>
    /// The angles live in <see cref="DishRig"/>'s replicated state, so a restore goes through
    /// <see cref="DishRig.RestoreAngles"/> — which lands the dish there at once and publishes it to every
    /// client — and never through the pivot transforms.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(DishRig))]
    public class DishRigSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "dishRig";   // written into save files — NEVER rename

        /// <summary>Degrees within which a dish counts as never touched, and nothing is stored for it.</summary>
        private const float RestTolerance = 0.01f;

        private DishRig rig;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private DishRig Rig => rig != null ? rig : rig = GetComponent<DishRig>();

        public string SaveKey => Key;

        public struct State
        {
            public float azimuth;
            public float elevation;
        }

        public object CaptureState()
        {
            if (Rig == null || Rig.IsAtRest(RestTolerance)) return null;

            return new State { azimuth = Rig.Azimuth, elevation = Rig.Elevation };
        }

        public void RestoreState(JObject state)
        {
            if (Rig == null) return;

            // No entry means "at rest" — and it has to be SAID, because the same dish may have been
            // turned by a previous restore in this session.
            if (state == null)
            {
                Rig.RestoreAngles(Rig.RestAzimuth, Rig.RestElevation);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Rig.RestoreAngles(restored.azimuth, restored.elevation);
        }
    }
}
