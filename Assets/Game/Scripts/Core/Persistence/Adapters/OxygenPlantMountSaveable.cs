using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists whether the lander's oxygen plant is out of its mount, and the line it was thrown along.
    ///
    /// <para>
    /// The loose plant itself is a runtime entity with its own record (its pose: wherever it was
    /// last dragged to). This holds only the ship's half. A world that has never had the plant
    /// thrown out writes nothing, and a save from before this saver existed reads null: the plant
    /// is in its mount, which is the rule for every world already past its crash.
    /// </para>
    /// <para>
    /// A child saver on the fixture, collected by the hull's entity like
    /// <c>OxygenGeneratorSaveable</c> beside it, and baked onto the fixture by hand for the same
    /// reason: <c>SaveablePolicy.Ensure</c> runs on an entity's root alone.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(OxygenPlantMount))]
    public class OxygenPlantMountSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "oxygenMount";           // written into save files — never rename

        private OxygenPlantMount mount;

        // Lazy-resolved, NOT cached in Awake: EditMode tests never run Awake.
        private OxygenPlantMount Mount => mount != null ? mount : mount = GetComponent<OxygenPlantMount>();

        public string SaveKey => Key;

        public struct State
        {
            public bool detached;

            // The crash's end points. Named for the furrow they used to draw; the names are in save
            // files, so they stay. A non-zero furrowTo means "already thrown out once".
            public Vector3 furrowFrom;
            public Vector3 furrowTo;
        }

        public object CaptureState()
        {
            if (Mount == null || (!Mount.Detached && !Mount.HasBeenEjected)) return null;

            return new State { detached = Mount.Detached, furrowFrom = Mount.EjectedFrom, furrowTo = Mount.EjectedTo };
        }

        public void RestoreState(JObject state)
        {
            if (Mount == null) return;

            if (state == null)
            {
                Mount.Restore(false, Vector3.zero, Vector3.zero);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);
            Mount.Restore(restored.detached, restored.furrowFrom, restored.furrowTo);
        }
    }
}
