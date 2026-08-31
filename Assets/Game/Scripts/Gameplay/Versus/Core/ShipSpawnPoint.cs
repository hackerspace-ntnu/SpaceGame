using System;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where one team's ship starts: a spot on the ground plane and a heading.
    ///
    /// <para>
    /// The position is a <see cref="Vector2"/> in X/Z, not a <see cref="Vector3"/>, and that is the
    /// whole point of the type. Height is never authored — it comes from the ground under the
    /// point, resolved at spawn time — so a Y field here would be a number somebody sets carefully
    /// and the grounding pass then throws away. A shape that cannot express the wrong thing is
    /// cheaper than a comment asking people not to.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ShipSpawnPoint
    {
        [Tooltip("Which team starts here. One point per team, numbered from zero.")]
        [SerializeField] private int team;

        [Tooltip("Where on the ground plane, in world X/Z. The height is measured, not authored.")]
        [SerializeField] private Vector2 groundXZ;

        [Tooltip("Which way the ship faces, in degrees. Zero looks down +Z, matching Unity's yaw.")]
        [SerializeField] private float yaw;

        public ShipSpawnPoint(int team, Vector2 groundXZ, float yaw)
        {
            this.team = team;
            this.groundXZ = groundXZ;
            this.yaw = yaw;
        }

        public int Team => team;

        public Vector2 GroundXZ => groundXZ;

        public float Yaw => yaw;

        /// <summary>
        /// The point as a world position at an arbitrary height, for the callers that need somewhere
        /// to aim a probe or a chunk preload before any ground is known.
        /// </summary>
        public Vector3 At(float height) => new(groundXZ.x, height, groundXZ.y);
    }
}
