using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The harpoon in flight: a copy of the one loaded in the gun, and the arithmetic of where its
    /// tip and its tail are.
    ///
    /// <para>
    /// A copy rather than the loaded part itself, so the gun's own hierarchy is never torn apart
    /// mid-shot — the loaded harpoon is only hidden, and a gun unequipped with its dart out comes
    /// back whole on the next equip. A plain class rather than a component: it is drawing, driven
    /// by <see cref="ShockHarpoonArtifact"/> on every machine, with nothing of its own to tick.
    /// </para>
    /// <para>
    /// The ends are stored in the harpoon mesh's own space, because the FBX node carries whatever
    /// rotation and scale the .blend gave it (see Artifacts.md Gotchas); the builder measures them
    /// there, and every pose below goes through that frame rather than assuming +Z is forward.
    /// </para>
    /// </summary>
    public sealed class HarpoonDart
    {
        private readonly Transform dart;
        private readonly Vector3 tipLocal;
        private readonly Vector3 tailLocal;
        private readonly Quaternion alongToLocal;

        /// <summary>Spawn a free copy of <paramref name="loaded"/> exactly where it sits in the gun.</summary>
        public HarpoonDart(Transform loaded, Vector3 tipLocal, Vector3 tailLocal)
        {
            this.tipLocal = tipLocal;
            this.tailLocal = tailLocal;

            Vector3 along = tipLocal - tailLocal;
            alongToLocal = Quaternion.Inverse(Quaternion.LookRotation(
                along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.forward));

            dart = Object.Instantiate(loaded.gameObject, loaded.position, loaded.rotation).transform;
            dart.name = loaded.name + " (fired)";
            // Instantiate copies the LOCAL scale; the loaded part is under a hand-sized item.
            dart.localScale = loaded.lossyScale;
            dart.gameObject.SetActive(true);
        }

        public Vector3 Tip => dart.TransformPoint(tipLocal);
        public Vector3 Tail => dart.TransformPoint(tailLocal);

        /// <summary>Tip to tail, in world metres.</summary>
        public float Length => Vector3.Distance(Tip, Tail);

        /// <summary>Put the tail at <paramref name="tail"/> with the tip pointing along <paramref name="heading"/>.</summary>
        public void PlaceTail(Vector3 tail, Vector3 heading)
        {
            if (heading.sqrMagnitude < 1e-8f) return;

            dart.rotation = Quaternion.LookRotation(heading.normalized) * alongToLocal;
            dart.position += tail - Tail;
        }

        /// <summary>
        /// Ride along with whatever it went into. Kept in world space so it does not jump, and
        /// parented to the nearest bone so it falls with a body that goes limp.
        /// </summary>
        public void StickTo(Transform host)
        {
            if (host == null) return;
            dart.SetParent(host, true);
            // In the body, not of it: the ragdoll, the X-ray and the skeleton must all skip it.
            BodyAttachment.Mark(dart.gameObject);
        }

        public void Free()
        {
            dart.SetParent(null, true);
            if (dart.TryGetComponent(out BodyAttachment mark)) Object.Destroy(mark);
        }

        public void Destroy()
        {
            if (dart != null) Object.Destroy(dart.gameObject);
        }
    }
}
