using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Surface
{
    /// <summary>
    /// One coat in a form a save file can hold, and the form a coat waits in while the chunk under
    /// it is unloaded.
    ///
    /// <para>
    /// Position, radius and kind — the same shape as any other placed thing — plus the physics
    /// layer its geometry stood on, which is the one thing that cannot be worked out again on the
    /// way back in. No id and no clock: ids are session handles for "break that patch" and nothing
    /// that outlives a session refers to one, and the only kind that is saved is the kind that has
    /// no clock.
    /// </para>
    /// <para>
    /// Three floats rather than a <see cref="Vector3"/>, following <c>SandstormSaveable</c>'s storm
    /// record for the same reason: this project's JSON layer walks a Unity vector's derived
    /// properties — <c>normalized</c>, and from there round and round — into a
    /// <c>StackOverflowException</c> unless it is handed a converter, and three floats cannot do
    /// that whatever it is handed.
    /// </para>
    /// </summary>
    [Serializable]
    public struct SurfaceCoatRecord
    {
        /// <summary>The <see cref="SurfaceCoatKind"/>. An int because that is what a file holds.</summary>
        public int kind;

        public float x;
        public float y;
        public float z;

        /// <summary>Footprint in metres.</summary>
        public float radius;

        /// <summary>
        /// The physics layer the coat's geometry was built on, for the one kind that has any.
        ///
        /// Saved rather than re-derived, because re-deriving it means probing the surface again and
        /// a chunk that has finished LOADING has not necessarily finished building its colliders —
        /// so the probe would come back empty and the ice would come back on the wrong layer, found
        /// by nothing that walks. A file written before this field existed reads back 0, which is
        /// the Default layer and is what every ground probe in the game looks at anyway.
        /// </summary>
        public int layer;

        /// <summary>What kind of coat this record describes.</summary>
        public SurfaceCoatKind Kind => (SurfaceCoatKind)kind;

        /// <summary>Where it was sprayed, in world space.</summary>
        public Vector3 Center => new Vector3(x, y, z);

        /// <summary>The record for a live patch.</summary>
        public static SurfaceCoatRecord Of(SurfaceCoatPatch patch) => new SurfaceCoatRecord
        {
            kind = (int)patch.Kind,
            x = patch.Center.x,
            y = patch.Center.y,
            z = patch.Center.z,
            radius = patch.Radius,
            layer = patch.ColliderLayer,
        };
    }
}
