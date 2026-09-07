// What a freeze looks like, in one place.
//
// Separated from the sprayer because it describes the ICE rather than the gun: the material a
// statue is made of, the plinth that grows under it, and how fast a body that stopped being
// sprayed sheds its rime again. The sprayer carries one of these and hands it to every body it
// chills, so a body needs no authoring of its own to be freezable — see FrozenBody.Ensure.
using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The three things a frozen body needs that cannot be worked out from the body itself.
    ///
    /// <para>
    /// A serialized class rather than three loose fields on the artifact, because they are one
    /// idea and they travel together: <see cref="FrozenBody"/> is added at runtime and Unity
    /// serializes nothing onto a component created that way, so whatever it is going to look like
    /// has to arrive from the thing that froze it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class FrostLook
    {
        [Tooltip("The ice. Mat_FrozenStatue, whose _Freeze property runs 0..1 as the rime creeps " +
                 "over the body — 0 clips the whole shell away and 1 covers it. Opaque and in the " +
                 "Geometry queue on purpose, so the ink post pass finds it in the depth texture " +
                 "and outlines the silhouette; a transparent stand-in would lose that with no " +
                 "error anywhere.")]
        [SerializeField] private Material statueMaterial;

        [Tooltip("The plinth and shards that grow under a body the moment it freezes solid. " +
                 "Optional — the ice reads on its own without it. It must carry NO collider: a " +
                 "statue is still the body's own colliders, and a second one under its feet would " +
                 "be something the thawed creature then has to walk out of.")]
        [SerializeField] private GameObject plinthPrefab;

        [Tooltip("How fast a body that is no longer being sprayed loses its build-up, in freeze " +
                 "fractions per second. Slower than the build-up gains (which is 1 / freeze time) " +
                 "so that a crosshair slipping off a moving animal for a moment costs a little " +
                 "rather than everything — but fast enough that breaking line of sight or backing " +
                 "out of range is a real escape, which is the whole of what a victim can do about " +
                 "being frozen (GDC-L1-MP-0002).")]
        [SerializeField, Min(0f)] private float thawPerSecond = 0.5f;

        /// <summary>The ice material. Instanced per body — see <see cref="FrostShell"/>.</summary>
        public Material StatueMaterial => statueMaterial;

        /// <summary>The plinth under a finished statue, or null for none.</summary>
        public GameObject PlinthPrefab => plinthPrefab;

        /// <summary>Freeze fractions shed per second while nothing is spraying this body.</summary>
        public float ThawPerSecond => thawPerSecond;
    }
}
