// Taking a body out of sight and out of every query, and giving it back exactly as it was.
//
// The counterpart of CarriedBody, and deliberately shaped like it: CarriedBody answers "who is
// posing this body", this one answers "who is hiding it". Both are refcounted by holder, both
// remember the state the FIRST holder found, and both are safe to reach twice — because a
// replicated condition is applied on several machines, cleared on several machines, and torn down
// under a body that is being destroyed.
//
// WHY BOTH RENDERERS AND COLLIDERS. Hiding the renderers alone leaves a body you cannot see and
// still walk into, and one that every aim ray, ground probe and overlap in the game still stops on
// — an invisible wall where a crate used to be. Disabling the colliders is what makes "it is not
// there" true rather than merely looked-at (see the query invariant in INVARIANTS.md).
//
// WHAT IT IS NOT. It does not touch the Rigidbody: whether a hidden body is frozen, falling or
// being carried is a separate question with a separate owner, and CarriedBody already owns it.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Hide a body from sight and from physics, and put it back.
    /// </summary>
    public static class BodyVeil
    {
        private class Record
        {
            /// <summary>Only the ones this veil actually switched off — see <see cref="Hide"/>.</summary>
            public readonly List<Renderer> Renderers = new();
            public readonly List<Collider> Colliders = new();
            public readonly HashSet<object> Holders = new();
        }

        private static readonly Dictionary<GameObject, Record> Veiled = new();

        /// <summary>
        /// Dropped between play sessions, for <c>CarriedBody</c>'s reason: a record left from the
        /// last session names objects that no longer exist, and statics survive a play mode entered
        /// without a domain reload — which is how this project is configured.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Veiled.Clear();

        /// <summary>Is anything currently hiding <paramref name="body"/>?</summary>
        public static bool IsHidden(GameObject body) => body != null && Veiled.ContainsKey(body);

        /// <summary>
        /// Take <paramref name="body"/> out of sight for <paramref name="holder"/>.
        ///
        /// <para>
        /// Idempotent per holder. Only components that were ENABLED when the first holder arrived
        /// are recorded, so a renderer somebody else had already switched off — a head hidden from
        /// its owner's own camera, a spent booster's armed mesh — is not switched back on by the
        /// unveil. That is the difference between restoring a body and overwriting it.
        /// </para>
        /// </summary>
        public static void Hide(GameObject body, object holder)
        {
            if (body == null || holder == null) return;

            if (Veiled.TryGetValue(body, out Record record))
            {
                record.Holders.Add(holder);
                return;
            }

            record = new Record();
            record.Holders.Add(holder);

            // Includes inactive children, because a body may be hidden while some of its parts are
            // already off and the point is to leave those exactly as they are.
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;

                renderer.enabled = false;
                record.Renderers.Add(renderer);
            }

            foreach (Collider collider in body.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || !collider.enabled) continue;

                collider.enabled = false;
                record.Colliders.Add(collider);
            }

            Veiled[body] = record;
        }

        /// <summary>
        /// Give <paramref name="body"/> back, once every holder has let go.
        ///
        /// Safe on a body nobody is hiding, and safe on one whose components have since been
        /// destroyed — a veil outliving its body is the ordinary case when the world unloads under
        /// it.
        /// </summary>
        public static void Show(GameObject body, object holder)
        {
            if (body == null || holder == null) return;
            if (!Veiled.TryGetValue(body, out Record record)) return;

            record.Holders.Remove(holder);
            if (record.Holders.Count > 0) return;

            Veiled.Remove(body);

            for (int i = 0; i < record.Renderers.Count; i++)
                if (record.Renderers[i] != null)
                    record.Renderers[i].enabled = true;

            for (int i = 0; i < record.Colliders.Count; i++)
                if (record.Colliders[i] != null)
                    record.Colliders[i].enabled = true;
        }

        /// <summary>
        /// Put back anything <paramref name="holder"/> is still hiding.
        ///
        /// For the holder that is going away without knowing what it took — a well despawned mid
        /// hold, a status torn down with its body. Without this a body outlives its hider invisible.
        /// </summary>
        public static void Abandon(object holder)
        {
            if (holder == null) return;

            List<GameObject> ending = null;

            foreach (KeyValuePair<GameObject, Record> entry in Veiled)
            {
                if (!entry.Value.Holders.Contains(holder)) continue;

                ending ??= new List<GameObject>();
                ending.Add(entry.Key);
            }

            if (ending == null) return;

            for (int i = 0; i < ending.Count; i++) Show(ending[i], holder);
        }

        /// <summary>
        /// Switch off again anything that has been re-enabled under a live veil.
        ///
        /// <para>
        /// Not paranoia. A veiled body is still simulated, and things that own their own components
        /// keep asserting them: a ragdoll turns its bone colliders on when a hold puts the body
        /// down, an LOD group and a culling pass both write <c>Renderer.enabled</c>. A veil that is
        /// only applied once therefore leaks a limb back into the world halfway through.
        /// </para>
        /// <para>
        /// Only components the veil already recorded are touched, so this can never hide something
        /// the veil did not take in the first place.
        /// </para>
        /// </summary>
        public static void Reassert(GameObject body)
        {
            if (body == null || !Veiled.TryGetValue(body, out Record record)) return;

            for (int i = 0; i < record.Renderers.Count; i++)
                if (record.Renderers[i] != null && record.Renderers[i].enabled)
                    record.Renderers[i].enabled = false;

            for (int i = 0; i < record.Colliders.Count; i++)
                if (record.Colliders[i] != null && record.Colliders[i].enabled)
                    record.Colliders[i].enabled = false;
        }
    }
}
