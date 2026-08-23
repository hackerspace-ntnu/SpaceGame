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
            //
            // That claim is only true because RigidbodySaveable now answers on BOTH paths — a
            // payload restores the saved velocity, no payload zeroes it. It used to return early
            // when its key was absent, which is the common case (a kinematic capture stores
            // nothing), so between the two of them nobody set a velocity at all and a teleported
            // object kept the momentum it had somewhere else.
            bool zeroVelocity = GetComponent<RigidbodySaveable>() == null;

            SaveTeleport.Move(gameObject, restored.position, restored.rotation, zeroVelocity);

            // No zero-scale sentinel here any more. It was written to catch a truncated payload, but
            // a payload with no scale field deserializes to default(Vector3) only because the struct
            // has no initializer — and the case it DID catch was an object deliberately scaled to
            // zero, which is how several props in this project hide without being disabled. Those
            // came back at full size. A missing payload is now expressed by the record's HasScale
            // flag, and a payload that genuinely predates the field is detected by asking the JSON
            // rather than by inspecting the value — which is the distinction the sentinel could not
            // make.
            if (state["scale"] != null) transform.localScale = restored.scale;
        }
    }
}
