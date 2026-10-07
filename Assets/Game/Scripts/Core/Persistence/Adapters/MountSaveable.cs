using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists who was riding this mount, and puts them back in the seat.
    ///
    /// <b>Why this is deferred rather than restored in place.</b> A mount is restored while its scene
    /// hydrates, and at that moment the rider does not exist: Netcode spawns players at a time the
    /// save system does not control, and a player who is joining may be several seconds away. So the
    /// reference is read during <see cref="RestoreState"/>, held, and acted on in
    /// <see cref="OnLoadComplete"/> — which <see cref="SaveManager"/> runs once the world is hydrated
    /// and players are bound.
    ///
    /// <b>Only the rider is stored.</b> Nothing else about being mounted is a choice the player made:
    /// the seat comes from the mount's own <c>seatPoint</c> and the camera from its
    /// <c>defaultPerspective</c>, neither of which changes during play. Saving either would be storing
    /// a value that can only ever equal the prefab's.
    ///
    /// <b>The rider's pose is not stored either.</b> Mounting parents the rider to the seat, so the
    /// seat's transform decides where they sit. Restoring a world position as well would be two
    /// answers to one question, and the winner would be whichever ran second.
    /// </summary>
    [RequireComponent(typeof(MountModule))]
    public class MountSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "mount";

        private MountModule mount;

        private MountModule Mount => mount != null ? mount : mount = GetComponent<MountModule>();

        public string SaveKey => Key;

        /// <summary>
        /// Early: seating the rider re-establishes a relationship other savers read. The ornithopter's
        /// saver in particular needs to know whether anybody is aboard before it decides whether to
        /// put a craft back into the air, and it previously had to subscribe to
        /// <c>MountModule.Mounted</c> to work around not being able to say so.
        /// </summary>
        public int LoadOrder => IDeferredSaveable.Early;

        /// <summary>The reference read from the save, waiting for the rider to exist.</summary>
        private SaveRef pendingRider;

        public struct State
        {
            /// <summary>Who was in the seat. Unset means nobody was, which is worth recording.</summary>
            public SaveRef rider;
        }

        public object CaptureState()
        {
            if (Mount == null) return null;

            // MountedPlayerTransform is the rider's own transform; SaveRef.From walks up from it to
            // whatever owns an identity, which for a player is the object bound to their profile.
            return new State
            {
                rider = Mount.IsMounted ? SaveRef.From(Mount.MountedPlayerTransform) : SaveRef.None,
            };
        }

        public void RestoreState(JObject state)
        {
            pendingRider = state == null
                ? SaveRef.None
                : state.ToObject<State>(SaveSerializer.Serializer).rider;
        }

        public void OnLoadComplete()
        {
            if (Mount == null || !pendingRider.IsSet) return;

            // Somebody else took the seat while this reference was waiting. The saved rider has lost
            // their claim, so the reference is dropped rather than retried forever.
            if (Mount.IsMounted)
            {
                pendingRider = SaveRef.None;
                return;
            }

            // Kept on failure, NOT consumed. This pass runs again every time a player binds, and
            // in multiplayer players arrive one at a time — a reference dropped the first time it failed
            // would permanently strand the second player's mount. Absent is an ordinary answer here: the
            // rider may simply not have rejoined yet, which is why this is silent rather than a warning.
            if (!pendingRider.TryResolve(out GameObject riderObject)) return;

            pendingRider = SaveRef.None;

            // A corpse must not be seated. Mounting subscribes to the rider's death and freezes their
            // movement; doing that to something already dead locks a body into a seat that its respawn
            // cannot get it out of.
            var health = riderObject.GetComponent<HealthComponent>();
            if (health != null && !health.Alive) return;

            var interactor = riderObject.GetComponentInChildren<Interactor>(true);
            if (interactor == null) return;

            // Through the network sync where there is one, exactly as an interaction would. Calling
            // MountModule.TryMount directly would seat the rider on the server alone: no peer is told
            // and ownership never moves, so the rider could not steer and every other client would see
            // them standing in the sand.
            if (TryGetComponent(out MountNetworkSync sync)) sync.ServerMount(interactor);
            else Mount.TryMount(interactor, transform);
        }
    }
}
