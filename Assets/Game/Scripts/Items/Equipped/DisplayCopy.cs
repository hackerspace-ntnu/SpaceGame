using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Turns an item prefab into an inert display copy: something that can be looked at and
    /// nothing else. The pack's mat, the ship's gear wall and the body screen's ghosts all show
    /// items this way.
    ///
    /// <para>
    /// A display copy is not an item: it holds no state and must never run gameplay code. So
    /// everything that could tick, collide, animate, make noise or own a network identity is taken
    /// off it before it gets a chance to run — and it has to be taken off BEFORE the copy is ever
    /// active, because <c>Instantiate</c> runs <c>Awake</c> synchronously. A copy born under a
    /// deactivated stage is never <c>activeInHierarchy</c>, so no <c>Awake</c> runs at all and
    /// <c>DestroyImmediate</c> takes the components off clean.
    /// </para>
    /// </summary>
    public static class DisplayCopy
    {
        /// <summary>
        /// A stripped copy of <paramref name="prefab"/> under <paramref name="parent"/>, at the
        /// identity local pose and unit scale. The caller seats and scales it.
        /// </summary>
        public static GameObject Make(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;

            var stage = new GameObject("DisplayCopyStage");
            stage.SetActive(false);

            GameObject copy = Object.Instantiate(prefab, stage.transform);
            Strip(copy);

            Transform t = copy.transform;
            t.SetParent(parent, false);

            // Normalise: the prefab's own root pose is about to be replaced by whoever seats the
            // copy, and a zero on one scale axis would make the inverse transform inside
            // ItemBounds.Measure non-finite.
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            Object.DestroyImmediate(stage);
            return copy;
        }

        /// <summary>
        /// Take everything that could tick, collide, animate, make noise or own a network identity
        /// off a copy, leaving pure scenery.
        ///
        /// <para>
        /// Public because <see cref="HolderBuilder"/> needs exactly this and a second stripper is
        /// the wrong answer: this one is hard-won, and the ways it can be got wrong are all silent.
        /// Order matters. MonoBehaviours go first because a <c>[RequireComponent]</c> on a script
        /// blocks removal of the Rigidbody or Collider it names. ParticleSystemRenderer goes with
        /// its ParticleSystem for the same reason — the renderer requires the system, and a
        /// particle renderer with nothing feeding it draws nothing anyway.
        /// </para>
        /// <para>
        /// Only ever call it on a copy under a <b>deactivated</b> parent. Instantiate runs Awake
        /// synchronously, so a copy born active has already registered itself before the first
        /// component comes off.
        /// </para>
        /// </summary>
        public static void Strip(GameObject copy)
        {
            if (copy == null) return;

            // NetworkBehaviours before the plain pass, because NetworkObject is itself a
            // MonoBehaviour and every NetworkBehaviour on the item requires it. The retry loop
            // below does get there eventually, but only after Unity has logged a refusal for each
            // one — ten warnings per pack refresh, which buries anything real. A stowed copy is
            // scenery; it has no business owning a network identity either way.
            DestroyAll<Unity.Netcode.NetworkBehaviour>(copy);

            DestroyAll<MonoBehaviour>(copy);
            DestroyAll<ParticleSystemRenderer>(copy);
            DestroyAll<ParticleSystem>(copy);

            // Line and trail renderers usually run in WORLD space, which means they ignore their
            // own transform: the copy gets scaled and seated and the rope stays exactly where the
            // original prefab drew it. On the grappling hook and the lasso that measured as a
            // 1 x 1 x 2 m item stuck at the pack's origin. They are also meaningless on a stowed
            // copy — a coil of rope in a pack is not mid-throw.
            DestroyAll<LineRenderer>(copy);
            DestroyAll<TrailRenderer>(copy);

            DestroyAll<Rigidbody>(copy);
            DestroyAll<Collider>(copy);
            DestroyAll<Animator>(copy);
            DestroyAll<AudioSource>(copy);
        }

        // Unity refuses to remove a component while another one on the same object declares it as
        // a requirement, and only logs rather than throwing — so a single pass silently leaves
        // whichever half of a [RequireComponent] pair it happened to reach first. Repeating until
        // the count stops falling clears the dependents and then what they were holding.
        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            int previous = int.MaxValue;

            for (int pass = 0; pass < 8; pass++)
            {
                T[] found = root.GetComponentsInChildren<T>(true);

                int alive = 0;
                foreach (T component in found)
                    if (component != null) alive++;   // missing scripts come back as null entries

                if (alive == 0 || alive >= previous) return;
                previous = alive;

                foreach (T component in found)
                    if (component != null) Object.DestroyImmediate(component);
            }
        }
    }
}
