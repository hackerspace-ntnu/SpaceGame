using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a door standing the way the player left it.
    ///
    /// <para>
    /// The visible half of that is one leaf pose, and it is the less important half.
    /// <c>SandstormShelter</c> asks <see cref="DoorInteraction.IsOpen"/> whether a hull counts as
    /// shelter, so a hatch the player sealed against a storm and then loaded into came back OPEN —
    /// and the first thing the world did was start sanding them inside the ship they had closed.
    /// </para>
    /// <para>
    /// Not <see cref="ArticulatedPartsSaveable"/>, which covers hatches and ramps on vehicles: that
    /// saver is added to objects that already qualify as world entities, and a door qualifies as
    /// nothing at all until <c>DoorInteraction</c> declares itself an <c>IPersistentEntity</c>.
    /// The state also lives somewhere else — in the door's <c>NetLatch</c>, which is what the rest
    /// of the session reads.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(DoorInteraction))]
    public class DoorSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "door";       // written into save files — NEVER rename

        private DoorInteraction door;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private DoorInteraction Door => door != null ? door : door = GetComponent<DoorInteraction>();

        public string SaveKey => Key;

        public struct State
        {
            public bool open;
        }

        public object CaptureState()
        {
            if (Door == null) return null;

            // A shut door is the authored state of every door in the project, so storing nothing for
            // it keeps the record to the doors somebody actually opened.
            return Door.IsOpen ? new State { open = true } : (object)null;
        }

        public void RestoreState(JObject state)
        {
            if (Door == null) return;

            // No entry means "this door was shut" — and it has to be SAID, because the same object
            // may have been left open by a previous restore in this session.
            if (state == null) { Door.RestoreOpen(false); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Door.RestoreOpen(restored.open);
        }
    }
}
