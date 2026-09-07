using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// Frozen liquid: the one coat that is geometry as well as grip.
    ///
    /// <para>
    /// The other two kinds change what a surface DOES. This one changes what is there — a frozen
    /// pool is ground you can walk across, which is the whole point of pointing a cryo sprayer at
    /// water, and it is why this is the only kind that carries a collider.
    /// </para>
    /// <para>
    /// <b>It has to be laid on something that can freeze.</b> Ice sprayed onto dry sand is a
    /// bridge over nothing, and a rule the player cannot see either way (GDC-L1-SYS-0006) — so the
    /// test is a surface they can already read: standing liquid, or ground a storm has visibly
    /// rained on. Nothing else takes it.
    /// </para>
    /// <para>
    /// It is also the only kind worth saving. Slick and Wet are gone inside half a minute; a frozen
    /// pool that somebody has made a bridge of is a change to the world, and it comes back on the
    /// next load as position, radius and kind — the same shape as any other placed thing.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class IceCoat : SurfaceCoatBehaviour
    {
        /// <summary>Permanent. Nothing but a break ends a sheet of ice.</summary>
        private const float NoExpiry = 0f;

        /// <summary>A little wider than a slick dab — this is a stepping stone, not a film.</summary>
        private const float DefaultPatchRadius = 1.5f;

        /// <summary>
        /// Slipperier than the sprayed film, and deliberately so: ice is the coat you can also
        /// stand on, so the trade for being allowed to cross it is having almost no purchase while
        /// you do. Still not zero — see the base class on why nothing here ever is.
        /// </summary>
        private const float DefaultGrip = 0.03f;

        [Tooltip("Surfaces that count as liquid, and so can be frozen into standable ice. The " +
                 "project's built-in Water layer by default.")]
        [SerializeField] private LayerMask liquidLayers = 1 << 4;

        [Tooltip("What the surface probe is allowed to see at all. Everything, by default: a " +
                 "frozen pool in a cave and a puddle on a chunk's terrain are the same job.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        [Tooltip("How far above the sprayed point the surface probe starts, in metres. The point " +
                 "is already a hit on the surface, so a ray started exactly on it fires from " +
                 "inside the mesh and reports nothing.")]
        [SerializeField] private float probeLift = 0.3f;

        [Tooltip("How far below the sprayed point the surface probe reaches, in metres.")]
        [SerializeField] private float probeDepth = 0.6f;

        [Tooltip("Whether ground under a Wet coat freezes as well as standing liquid does. This " +
                 "is the Storm Flask into Cryo Sprayer combination — rain a patch of desert, then " +
                 "freeze it — and turning it off makes ice a water-only tool.")]
        [SerializeField] private bool wetGroundFreezes = true;

        [Tooltip("How thick the slab of ice is, in metres. Its TOP sits on the sprayed point, so " +
                 "this is how far down into the liquid it reaches and not how far it stands proud " +
                 "of it: a sheet that stood above the water it froze would be a step up onto a " +
                 "pool.")]
        [SerializeField] private float slabThickness = 0.12f;

        [Tooltip("Physics layer for the slab. -1 puts it on the layer of whatever it froze, which " +
                 "is the one layer every ground probe that already found that surface is " +
                 "guaranteed to be looking at. Set a layer explicitly only to override that.")]
        [SerializeField] private int slabLayer = -1;

        public IceCoat() : base(NoExpiry, DefaultPatchRadius, DefaultGrip) { }

        public override SurfaceCoatKind Kind => SurfaceCoatKind.Ice;

        public override bool Saved => true;

        public override bool Standable => true;

        public override bool CanCoat(Vector3 point, SurfaceCoatField field, out int colliderLayer)
        {
            colliderLayer = slabLayer >= 0 ? slabLayer : 0;

            bool onLiquid = false;

            if (Physics.Raycast(point + Vector3.up * probeLift, Vector3.down, out RaycastHit hit,
                                probeLift + probeDepth, surfaceMask, QueryTriggerInteraction.Ignore))
            {
                // collider.gameObject, never hit.transform: over anything with a Rigidbody above it
                // that resolves to the body's root, which is a different object on a different
                // layer entirely.
                int surfaceLayer = hit.collider.gameObject.layer;
                onLiquid = (liquidLayers.value & (1 << surfaceLayer)) != 0;

                if (slabLayer < 0) colliderLayer = surfaceLayer;
            }

            if (onLiquid) return true;

            // Wet ground is the other way in, and it needs no probe of its own: a Wet patch IS the
            // answer to "has this ground been rained on", already replicated and already visible.
            return wetGroundFreezes && field != null &&
                   field.HasCoat(SurfaceCoatKind.Wet, point);
        }

        public override void Build(SurfaceCoatPatch patch, int colliderLayer)
        {
            if (patch != null) patch.AddSlab(slabThickness, colliderLayer);
        }
    }
}
