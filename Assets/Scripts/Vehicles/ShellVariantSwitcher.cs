// Swaps between two authored versions of a hull shell: a seamless "closed" mesh and an "open"
// mesh with the access openings cut out of it.
//
// ShipRV's model ships both. The closed shell has solid sides, so opening a wall panel while it
// is showing would swing the panel away from a wall that is still there. Watching the panels and
// switching to the cut-out shell the moment anything starts moving keeps the two consistent,
// while still using the seamless mesh whenever the ship is buttoned up.
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public class ShellVariantSwitcher : MonoBehaviour
    {
        [Header("Shell Meshes")]
        [Tooltip("Seamless shell, shown while every watched part is fully closed.")]
        [SerializeField] private Renderer closedShell;

        [Tooltip("Shell with the openings cut out, shown as soon as any watched part leaves the closed pose.")]
        [SerializeField] private Renderer openShell;

        [Header("Watched Parts")]
        [Tooltip("Panels that sit in the openings. Any of them off the closed pose swaps the shell.")]
        [SerializeField] private ArticulatedPart[] parts;

        private bool applied;
        private bool lastAnyOpen;

        private void Awake() => Apply(AnyOpen(), force: true);

        // Polled rather than event-driven: ArticulatedPart raises Settled when it stops, but the swap
        // has to happen the instant a panel *starts* moving, and there is no "started" event to hang
        // it on. Four float comparisons a frame is not worth an interface for.
        private void Update()
        {
            bool anyOpen = AnyOpen();
            if (anyOpen != lastAnyOpen || !applied)
                Apply(anyOpen, force: false);
        }

        private bool AnyOpen()
        {
            if (parts == null)
                return false;

            foreach (ArticulatedPart part in parts)
            {
                // IsOpen flips as soon as opening is commanded; Openness stays above zero until a
                // closing panel has fully seated. Together they cover the whole travel both ways.
                if (part && (part.IsOpen || part.Openness > 0f))
                    return true;
            }

            return false;
        }

        private void Apply(bool anyOpen, bool force)
        {
            if (!force && applied && anyOpen == lastAnyOpen)
                return;

            if (closedShell)
                closedShell.enabled = !anyOpen;
            if (openShell)
                openShell.enabled = anyOpen;

            lastAnyOpen = anyOpen;
            applied = true;
        }
    }
}
