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
        /// <summary>The left trigger's aperture — deep gold.</summary>
        public const int Primary = 0;

        /// <summary>The right trigger's aperture — pale lemon.</summary>
        ///
        /// Both are yellow. They are told apart by value, not hue: two saturated
        /// complementary colours across one screen read as clip art, and a
        /// player only ever needs to know "this one, or the other one".
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
        /// </summary>
        public Portal Open(int index, Portal prefab, Vector3 position, Quaternion rotation,
                           Collider host, Vector2 size, Color colour)
        {
            if (prefab == null || index < 0 || index >= portals.Length) return null;

            Portal portal = portals[index];
            if (portal == null)
            {
                portal = Instantiate(prefab);
                portal.name = $"Portal {(index == Primary ? "Primary" : "Secondary")} ({name})";
                portal.SetColour(colour);
                portals[index] = portal;
            }

            portal.SetSize(size);
            portal.Place(position, rotation, host, index);

            Portal other = portals[1 - index];
            Portal.Link(portal, other);

            return portal;
        }

        /// <summary>Shut both apertures — on death, on unequip-and-holster, on world unload.</summary>
        public void CloseAll()
        {
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i] == null) continue;

                // Existing, not Instance: this also runs from OnDestroy during
                // a scene unload, where creating a renderer to tell it about a
                // portal that is going away is both pointless and an error.
                if (PortalRenderer.Existing != null)
                    PortalRenderer.Existing.Forget(portals[i]);

                Destroy(portals[i].gameObject);
                portals[i] = null;
            }
        }

        private void OnDestroy() => CloseAll();
    }
}
