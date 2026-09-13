// Suspending collision between a rider and the mount carrying them, and putting it back exactly.
//
// A seated rider's collider sits inside the mount's, and the physics engine resolves that overlap
// the only way it knows how: by shoving one of them. That is the mount spinning in place under its
// own rider. Both riding paths need the same suspension and neither owns it — MountModule seats a
// player, NpcPassenger seats an NPC, and the two deliberately share no other code — so it lives
// here rather than being written twice with two chances to leak a pair.
//
// Deliberately NOT a way to hide the rider from the world. Physics.IgnoreCollision has no effect
// whatsoever on a raycast, an overlap query or an interaction probe, so a rider suspended this way
// is still shot at, still lassoed and still talked to. That is the whole difference between this
// and switching the rider's colliders off, and switching them off is exactly why the caravan's
// mounted nomads could not be hurt, roped or reached by anything at all.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// One rider's worth of suspended collider pairs. Apply on seating, Restore on dismount;
    /// both are safe to call twice and safe to call with either side already gone.
    /// </summary>
    public sealed class RiderCollisionIgnore
    {
        private (Collider a, Collider b)[] pairs;

        /// <summary>Whether anything is currently suspended.</summary>
        public bool IsApplied => pairs != null;

        /// <summary>How many pairs are suspended. For tests and diagnostics.</summary>
        public int PairCount => pairs?.Length ?? 0;

        /// <summary>
        /// Stop every collider under <paramref name="rider"/> colliding with every collider under
        /// <paramref name="mount"/>. Replaces any previous suspension.
        /// </summary>
        public void Apply(Transform rider, Transform mount)
        {
            Restore();

            if (rider == null || mount == null)
                return;

            Collider[] riderColliders = rider.GetComponentsInChildren<Collider>(true);
            Collider[] mountColliders = mount.GetComponentsInChildren<Collider>(true);
            if (riderColliders.Length == 0 || mountColliders.Length == 0)
                return;

            var found = new List<(Collider, Collider)>(riderColliders.Length * mountColliders.Length);

            foreach (Collider r in riderColliders)
            {
                if (!r) continue;

                foreach (Collider m in mountColliders)
                {
                    // A seated rider is parented to the mount, so the mount's own sweep finds the
                    // rider's colliders as well. Pairing those against each other is not a
                    // rider/mount contact at all, and restoring it would switch on collisions
                    // INSIDE the rider that may never have been on in the first place.
                    if (!m || r == m || m.transform.IsChildOf(rider)) continue;

                    Physics.IgnoreCollision(r, m, true);
                    found.Add((r, m));
                }
            }

            pairs = found.ToArray();
        }

        /// <summary>Hand every suspended pair back to physics. A pair whose collider is gone is dropped.</summary>
        public void Restore()
        {
            if (pairs == null)
                return;

            foreach ((Collider a, Collider b) in pairs)
            {
                // Unity refuses IgnoreCollision on a collider that is not active and enabled, and
                // says so with an error per pair. A pair with a switched-off side has no collision
                // to hand back anyway — a disabled collider does not collide with anything.
                if (IsLive(a) && IsLive(b))
                    Physics.IgnoreCollision(a, b, false);
            }

            pairs = null;
        }

        /// <summary>
        /// Drop the pairs without touching physics, for a teardown where restoring them is not
        /// merely pointless but illegal: the hierarchy is deactivating, so every collider in it is
        /// on its way to disabled and <see cref="Physics.IgnoreCollision"/> would log an error for
        /// each one on the way past.
        /// </summary>
        public void Forget() => pairs = null;

        private static bool IsLive(Collider collider) =>
            collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }
}
