// One place a rope bends around the world.
//
// A readonly struct rather than a class: a path holds a handful of these, rebuilds them constantly,
// and nothing ever needs to alias one. In its own file rather than nested in LeashPath so that
// LeashRail, which produces a wrap of its own, does not have to reach inside another type.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>One waypoint on a rope's path: where it bends, and what it is bending around.</summary>
    public readonly struct LeashWrap
    {
        /// <summary>The bend point, already pushed clear of the surface by the path's clearance.</summary>
        public readonly Vector3 Position;

        /// <summary>The surface normal at the contact, before that clearance was applied.</summary>
        public readonly Vector3 Normal;

        /// <summary>
        /// What the rope is bending around.
        ///
        /// <para>
        /// Held so the path can drop a wrap whose collider has been destroyed — a rope bent around a
        /// crate that has since been carried off is bent around nothing, and would otherwise hold its
        /// corner in mid-air until something else disturbed it.
        /// </para>
        /// </summary>
        public readonly Collider Surface;

        /// <summary>
        /// The slot this bend is riding, or null for an ordinary bend on a surface.
        ///
        /// <para>
        /// A rail-bound wrap is not frozen where it was made: its position is re-solved every step,
        /// which is what makes a rail a rail rather than a pin.
        /// </para>
        /// </summary>
        public readonly LeashRail Rail;

        public LeashWrap(Vector3 position, Vector3 normal, Collider surface)
            : this(position, normal, surface, null)
        {
        }

        public LeashWrap(Vector3 position, Vector3 normal, Collider surface, LeashRail rail)
        {
            Position = position;
            Normal = normal;
            Surface = surface;
            Rail = rail;
        }
    }
}
