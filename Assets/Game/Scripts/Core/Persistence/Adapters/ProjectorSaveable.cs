using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a map projector running the way the player left it.
    ///
    /// <para>
    /// Same shape as <see cref="DoorSaveable"/>, for the same reason: the state lives in the
    /// fixture's <c>NetLatch</c> — which is what the session reads — so the restore goes through
    /// <see cref="HoloProjectorInteraction.RestorePowered"/> rather than flipping the hologram
    /// directly, and an absent entry has to be applied as "off" because a previous restore in
    /// this session may have left it on.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(HoloProjectorInteraction))]
    public class ProjectorSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "projector";       // written into save files — NEVER rename

        private HoloProjectorInteraction projector;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private HoloProjectorInteraction Projector =>
            projector != null ? projector : projector = GetComponent<HoloProjectorInteraction>();

        public string SaveKey => Key;

        public struct State
        {
            public bool powered;
        }

        public object CaptureState()
        {
            if (Projector == null) return null;

            // Off is the authored state of every projector, so storing nothing for it keeps the
            // record to the ones somebody actually switched on.
            return Projector.IsPowered ? new State { powered = true } : (object)null;
        }

        public void RestoreState(JObject state)
        {
            if (Projector == null) return;

            if (state == null) { Projector.RestorePowered(false); return; }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Projector.RestorePowered(restored.powered);
        }
    }
}
