using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Remembers that this world has already been arrived in.
    ///
    /// <para>
    /// One flag on one saver, following <c>NpcWorldSaveable</c>'s shape: a subsystem's whole
    /// persisted state under a single key, rather than an entity per fact. The wreck itself needs
    /// nothing here — <c>PlayerShip</c> already carries a <c>SaveableEntity</c> with a
    /// <c>TransformSaveable</c>, so the hull's final pose persists on its own.
    /// </para>
    ///
    /// <para>
    /// Put this next to <see cref="ArrivalDirector"/> in the persistent scene, on an object that
    /// also carries a <c>SaveableEntity</c> — that component is what finds savers at all.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ArrivalDirector))]
    public class ArrivalSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "arrival";

        private ArrivalDirector director;

        private ArrivalDirector Director =>
            director != null ? director : director = GetComponent<ArrivalDirector>();

        public string SaveKey => Key;

        public struct State
        {
            public bool arrived;
        }

        public object CaptureState()
        {
            // A save taken WHILE the descent is running still records "arrived". Replaying a crash
            // landing on somebody already standing in the wreck is a far worse outcome than cutting
            // one short for somebody who quit halfway down — and the hull is persisted wherever it
            // had got to, so a resumed descent would start in the sky with a ship already recorded
            // on the ground.
            return new State { arrived = Director.HasArrived || Director.IsRunning };
        }

        public void RestoreState(JObject state)
        {
            // Null is a value, not an absence: it means the save was taken before this saver existed,
            // or with the component at its defaults. Either way the honest reading is "this world has
            // not been arrived in", which is what a brand new world wants.
            bool arrived = state?[nameof(State.arrived)]?.Value<bool>() ?? false;

            Director.RestoreArrived(arrived);
        }
    }
}
