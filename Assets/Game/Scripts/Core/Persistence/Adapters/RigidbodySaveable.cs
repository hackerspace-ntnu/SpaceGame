using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a Rigidbody's motion.
    ///
    /// Position and rotation are NOT stored here — the world store already carries them on the
    /// entity record, because it has to place an object before any of its components exist. What is
    /// left is the state that would otherwise be lost: a crate slid to a stop stays stopped, a
    /// vehicle left rolling keeps its momentum, and anything the game had put to sleep does not
    /// wake up and fall over on load.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RigidbodySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "rigidbody";

        private Rigidbody body;

        private Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

        public string SaveKey => Key;

        public struct State
        {
            public Vector3 velocity;
            public Vector3 angularVelocity;
            public bool isKinematic;
        }

        public object CaptureState() => Body == null
            ? null
            : new State
            {
                velocity = Body.isKinematic ? Vector3.zero : Body.linearVelocity,
                angularVelocity = Body.isKinematic ? Vector3.zero : Body.angularVelocity,
                isKinematic = Body.isKinematic,
            };

        public void RestoreState(JObject state)
        {
            if (Body == null || state == null) return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Body.isKinematic = restored.isKinematic;

            // Unity throws when velocity is assigned on a kinematic body, and a kinematic body has
            // no velocity to restore in the first place.
            if (restored.isKinematic) return;

            Body.linearVelocity = restored.velocity;
            Body.angularVelocity = restored.angularVelocity;
        }
    }
}
