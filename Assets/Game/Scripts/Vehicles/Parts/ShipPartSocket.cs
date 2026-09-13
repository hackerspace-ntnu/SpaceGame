using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Vehicles
{
    /// <summary>How a socket is being drawn to someone carrying a module.</summary>
    public enum ShipPartGhost
    {
        /// <summary>Nothing to say: either the part is fitted, or nobody is carrying one.</summary>
        Off,

        /// <summary>Something belongs here and it is not here. Red.</summary>
        Missing,

        /// <summary>The module in your hands belongs here, and you are pointing at it. Green.</summary>
        Target,
    }

    /// <summary>
    /// One mount point on a hull: the place a <see cref="ShipPartKind"/> bolts into.
    ///
    /// <para>
    /// The part's mesh always lives here, in the ship prefab, at the pose the modeller put it in.
    /// Fitting one does not spawn anything and removing one does not despawn anything — the socket
    /// simply shows or hides geometry it already owns. That is what lets an unfitted socket draw a
    /// perfectly accurate ghost of the missing part: the ghost <em>is</em> the part, painted.
    /// </para>
    /// <para>
    /// Whether a socket is filled is not this component's business — <see cref="ShipPartRack"/>
    /// owns that, because it is replicated and saved state and there must be exactly one copy of
    /// it. A socket only knows how to look filled, empty, or wanted.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipPartSocket : MonoBehaviour
    {
        [Tooltip("Which module fits here. Mirrored sockets share a kind, so one salvaged motor " +
                 "fits either mount.")]
        [SerializeField] private ShipPartKind kind;

        [Tooltip("The part's mesh. Hidden while the socket is empty, painted while it is ghosted.")]
        [SerializeField] private Renderer partRenderer;

        [Tooltip("The part's own collision. Off while the socket is empty, so a missing engine is " +
                 "not still solid.")]
        [SerializeField] private Collider partCollider;

        /// <summary>
        /// Shared across every socket in the session and never destroyed, deliberately. A material
        /// per socket would be eleven identical materials on one ship and a fresh leak on every
        /// hull that streams in; these two are created once and outlive everything that uses them.
        /// </summary>
        private static Material missingMaterial;
        private static Material targetMaterial;

        /// <summary>
        /// The part's real materials, captured before the first repaint. Read from the renderer
        /// rather than serialized: an FBX re-export changes submesh count, and a serialized copy
        /// would restore the wrong number of slots with no error.
        /// </summary>
        private Material[] fitted;

        private ShipPartGhost ghost = ShipPartGhost.Off;
        private bool installed;

        public ShipPartKind Kind => kind;
        public bool Installed => installed;

        /// <summary>Where a HUD marker or a ghost label would sit: the middle of the part itself.</summary>
        public Vector3 Centre => AimBounds.center;

        /// <summary>
        /// The volume someone points at to fit this module — the part's own world bounds, which
        /// stay correct whether or not the renderer is currently drawing.
        ///
        /// <para>
        /// Falls back to a small box at the pivot when the renderer is missing, so a
        /// half-authored socket is aimable-at rather than silently unhittable at the origin.
        /// </para>
        /// </summary>
        public Bounds AimBounds =>
            partRenderer != null
                ? partRenderer.bounds
                : new Bounds(transform.position, Vector3.one);

        private void Awake() => Apply();

        /// <summary>
        /// Show or hide the fitted part. Called by <see cref="ShipPartRack"/> only, on every
        /// machine, from the replicated mask.
        /// </summary>
        public void SetInstalled(bool value)
        {
            if (installed == value) return;

            installed = value;
            Apply();
        }

        /// <summary>
        /// Paint an empty socket for whoever is carrying a module. Purely local and purely
        /// cosmetic — it is never sent, never saved, and a fitted socket ignores it outright.
        /// </summary>
        public void SetGhost(ShipPartGhost value)
        {
            if (ghost == value) return;

            ghost = value;
            Apply();
        }

        private void Apply()
        {
            if (partRenderer == null) return;

            if (fitted == null) fitted = partRenderer.sharedMaterials;

            if (installed)
            {
                partRenderer.enabled = true;
                partRenderer.sharedMaterials = fitted;
                if (partCollider != null) partCollider.enabled = true;
                return;
            }

            // An absent part is not solid — you can walk through the hole where an engine was.
            if (partCollider != null) partCollider.enabled = false;

            if (ghost == ShipPartGhost.Off)
            {
                partRenderer.enabled = false;
                return;
            }

            partRenderer.enabled = true;
            partRenderer.sharedMaterials = Painted(ghost == ShipPartGhost.Target
                ? Target
                : Missing);
        }

        /// <summary>One tint per submesh — a renderer keeps drawing the slots it has, not the one
        /// material you meant.</summary>
        private Material[] Painted(Material tint)
        {
            var slots = new Material[Mathf.Max(1, fitted.Length)];
            for (int i = 0; i < slots.Length; i++) slots[i] = tint;
            return slots;
        }

        private static Material Missing =>
            missingMaterial != null
                ? missingMaterial
                : missingMaterial = PlacementTint.BuildMaterial("ShipPartMissing", PlacementTint.Refused);

        private static Material Target =>
            targetMaterial != null
                ? targetMaterial
                : targetMaterial = PlacementTint.BuildMaterial("ShipPartTarget", PlacementTint.Legal);
    }
}
