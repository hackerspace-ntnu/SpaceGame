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

        // Unity refuses to remove a component while another one on the same object declares it
        // as a requirement, and only logs rather than throwing. So each pass removes only the
        // components nothing else still requires — a NetworkObject goes after the
        // NetworkBehaviours that name it, a SupplyReservoir after its DockableSupply — and the
        // next pass takes what they were holding. Destroying in discovery order instead would
        // still clear the object eventually, but only after Unity had logged a refusal for every
        // required component on the way, which buries anything real.
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

                int removed = 0;
                foreach (T component in found)
                {
                    if (component == null || IsRequiredBySibling(component)) continue;
                    Object.DestroyImmediate(component);
                    removed++;
                }

                // A requirement cycle would leave every survivor blocked and spin the loop out
                // without removing anything. Take the refusal logs over leaving live scripts on
                // a display copy.
                if (removed == 0)
                {
                    foreach (T component in found)
                        if (component != null) Object.DestroyImmediate(component);
                }
            }
        }

        // Whether any surviving component on the same GameObject names this one in a
        // [RequireComponent]. Requirements are declared by type, so a base type counts: a script
        // requiring Collider is held up by the BoxCollider sitting next to it.
        private static bool IsRequiredBySibling(Component component)
        {
            Component[] siblings = component.gameObject.GetComponents<Component>();

            foreach (Component sibling in siblings)
            {
                if (sibling == null || sibling == component) continue;

                object[] requirements = sibling.GetType()
                    .GetCustomAttributes(typeof(RequireComponent), inherit: true);

                foreach (object requirement in requirements)
                {
                    var required = (RequireComponent)requirement;
                    if (Names(required.m_Type0, component) ||
                        Names(required.m_Type1, component) ||
                        Names(required.m_Type2, component))
                        return true;
                }
            }

            return false;
        }

        private static bool Names(System.Type required, Component component) =>
            required != null && required.IsInstanceOfType(component);
    }
}
