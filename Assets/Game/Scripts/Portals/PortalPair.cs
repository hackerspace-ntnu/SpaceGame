// The two apertures belonging to one shooter.
//
// It lives on the PLAYER, not on the gun, and that placement is the whole point
// of the class. Portals outlive the thing that made them: switching hotbar slot
// destroys the gun prefab, and a pair owned by the gun would take the player's
// portals down with it — which, since EquipmentController rebuilds the held
// item on every slot change, means portals vanishing whenever you glance at
// your inventory. Owning them per player also gives multiplayer the behaviour
// everyone expects for free: two players each have their own orange and blue,
// and neither can close the other's.
using UnityEngine;

namespace SpaceGame.Portals
{
    [DisallowMultipleComponent]
    public sealed class PortalPair : MonoBehaviour
    {
        /// <summary>The left trigger's aperture — orange.</summary>
        public const int Primary = 0;

        /// <summary>
        /// The right trigger's aperture — blue.
        ///
        /// Told apart by hue rather than by value, and deliberately: the two ends of a portal are
        /// the one pair of objects in this game a player has to identify instantly and from across
        /// a room, often through the other one. Two shades of the same colour lose that at the
        /// exact distance it matters.
        /// </summary>
        public const int Secondary = 1;

        private readonly Portal[] portals = new Portal[2];

        /// <summary>The pair belonging to <paramref name="owner"/>, created on first use.</summary>
        public static PortalPair Of(GameObject owner)
        {
            if (owner == null) return null;

            return owner.TryGetComponent(out PortalPair pair)
                ? pair
                : owner.AddComponent<PortalPair>();
        }

        public Portal Get(int index) =>
            index >= 0 && index < portals.Length ? portals[index] : null;

        /// <summary>
        /// Open, or move, one of the two apertures.
        ///
        /// Moving the existing GameObject rather than destroying and respawning
        /// it keeps the render target alive across a re-fire. Reallocating a
        /// screen-sized RenderTexture every time somebody taps the trigger is a
        /// stutter you can hear the GC in.
        ///
        /// <paramref name="lifetime"/> is seconds until the aperture irises shut, or 0 for one
        /// that never does. It is set on every shot rather than authored on the prefab, because
        /// "how long a portal lasts" is the GUN's rule — a pair placed in a scene by hand is
        /// scenery and stays put.
        /// </summary>
        public Portal Open(int index, Portal prefab, Vector3 position, Quaternion rotation,
                           Collider host, Vector2 size, Color colour, float lifetime = 0f)
        {
            if (prefab == null || index < 0 || index >= portals.Length) return null;

            Portal portal = portals[index];
            if (portal == null)
            {
                portal = Instantiate(prefab);
                portal.name = $"Portal {(index == Primary ? "Primary" : "Secondary")} ({name})";
                portal.SetColour(colour);

                // An aperture now shuts on its own when its time is up, so the pair has to hear
                // about it from the portal rather than being the only thing that can end one.
                // Without this the slot holds a destroyed reference, and the next shot from that
                // barrel takes the "already have one, move it" branch on an object that is gone.
                portal.Closed += Forget;

                portals[index] = portal;
            }

            portal.SetSize(size);
            portal.Place(position, rotation, host, index);
            portal.SetLifetime(lifetime);

            Portal other = portals[1 - index];
            Portal.Link(portal, other);

            return portal;
        }

        /// <summary>Shut one aperture, leaving the other exactly as it is.</summary>
        public void Close(int index)
        {
            if (index < 0 || index >= portals.Length) return;

            Portal portal = portals[index];
            portals[index] = null;

            // Close() raises Closed, which lands in Forget below — harmless, because the slot is
            // already cleared. Nulling first rather than after is what makes the two entry points
            // (this one, and the portal expiring by itself) converge instead of racing.
            if (portal != null) portal.Close();
        }

        /// <summary>Shut both apertures — on death, on unequip-and-holster, on world unload.</summary>
        public void CloseAll()
        {
            for (int i = 0; i < portals.Length; i++) Close(i);
        }

        /// <summary>
        /// An aperture told us it is shutting. Let go of the slot.
        ///
        /// Matched by reference rather than by index, because the portal reporting in may be one
        /// this pair has already replaced — a re-fire moves the existing aperture, but a restore
        /// or a world reload can put a different one in the slot while the old one is still
        /// finishing its own teardown.
        /// </summary>
        private void Forget(Portal portal)
        {
            for (int i = 0; i < portals.Length; i++)
                if (portals[i] == portal) portals[i] = null;
        }

        private void OnDestroy() => CloseAll();
    }
}
