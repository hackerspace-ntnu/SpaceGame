using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps a player inside the cave they were standing in when the world was written.
    ///
    /// <para>
    /// <c>InteriorManager</c> holds, per player, which interior they are in and where in the exterior
    /// they walked in from — and held it in a plain dictionary that no save ever read. A world saved
    /// from inside a cave therefore came back with the player's recorded position being a set of
    /// coordinates that only mean anything in a scene the load never opens: they appeared inside the
    /// terrain of whatever chunk shares those coordinates, with no record of the door they came
    /// through and no way to get back out through it.
    /// </para>
    /// <para>
    /// Player-scoped rather than world-scoped, because being in a cave is a fact about a person and
    /// not about the map. Two players can be in two different interiors, and one can quit while the
    /// other stays inside.
    /// </para>
    /// <para>
    /// Deferred, and it has to be. Restoring means loading a scene, moving a body into it and
    /// teleporting it — none of which can happen while the player is still being assembled, and the
    /// scene load in particular finishes several frames later.
    /// </para>
    /// </summary>
    public class InteriorVisitSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "interior";       // written into save files — NEVER rename

        public string SaveKey => Key;

        private bool hasPending;
        private State pending;

        public struct State
        {
            public string interiorScene;
            public Vector3 insidePosition;
            public Quaternion insideRotation;
            public Vector3 returnPosition;
            public Quaternion returnRotation;
        }

        public object CaptureState()
        {
            InteriorManager interiors = InteriorManager.Instance;

            // Null drops the key, which is exactly right: "this player is not in a cave" is the
            // overwhelmingly common case and does not deserve five fields in every save file.
            if (interiors == null || !interiors.TryGetVisit(gameObject, out InteriorManager.InteriorVisit visit))
                return null;

            return new State
            {
                interiorScene = visit.InteriorScene,
                insidePosition = visit.InsidePosition,
                insideRotation = visit.InsideRotation,
                returnPosition = visit.ReturnPosition,
                returnRotation = visit.ReturnRotation,
            };
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pending = default;

            // A null payload means the record says nothing about interiors, which means this player
            // was outside. Nothing to undo: a player who is somehow already in an interior at restore
            // time got there this session, and walking them out from here would be inventing a move
            // the save never described.
            if (state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            if (string.IsNullOrEmpty(restored.interiorScene)) return;

            pending = restored;
            hasPending = true;
        }

        /// <summary>
        /// Runs many times — once world-wide, again on every player binding, again per late chunk
        /// hydrate — so it consumes only on success and leaves the pending visit alone otherwise.
        /// The manager lives in persistentScene and is normally already there; a world entered
        /// without one simply keeps the player outside.
        /// </summary>
        public void OnLoadComplete()
        {
            if (!hasPending) return;

            InteriorManager interiors = InteriorManager.Instance;
            if (interiors == null) return;

            hasPending = false;

            interiors.RestoreVisit(gameObject, new InteriorManager.InteriorVisit
            {
                InteriorScene = pending.interiorScene,
                InsidePosition = pending.insidePosition,
                InsideRotation = pending.insideRotation,
                ReturnPosition = pending.returnPosition,
                ReturnRotation = pending.returnRotation,
            });

            pending = default;
        }
    }
}
