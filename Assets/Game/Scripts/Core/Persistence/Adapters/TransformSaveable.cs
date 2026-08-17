using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists where an object is.
    ///
    /// Only authored objects need this. A runtime object's position already travels on its
    /// <c>EntityRecord</c>, because the store must know where to put it before the object — and
    /// therefore this component — exists. An authored object is different: the chunk scene puts it
    /// back at its authored position on every load, so the only way a player-moved crate stays
    /// moved is a saver that overwrites that.
    ///
    /// Scale is included because a handful of world props are scaled at runtime, and a scale left
    /// behind is far more visible than a metre of drift.
    /// </summary>
    public class TransformSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "transform";

        public string SaveKey => Key;

        public struct State
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        public object CaptureState() => new State
        {
            position = transform.position,
            rotation = transform.rotation,
            scale = transform.localScale,
        };

        public void RestoreState(JObject state)
        {
            if (state == null) return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            // Leave the body's velocity alone when a RigidbodySaveable is present: it owns momentum,
            // and the two savers run in whatever order the components sit in.
            bool zeroVelocity = GetComponent<RigidbodySaveable>() == null;

            SaveTeleport.Move(gameObject, restored.position, restored.rotation, zeroVelocity);

            // A zero scale is what a truncated payload reads as, and applying it makes the object
            // invisible rather than merely misplaced.
            if (restored.scale != Vector3.zero) transform.localScale = restored.scale;
        }
    }
}
