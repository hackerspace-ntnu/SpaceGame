using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// <summary>
    /// One foot coming down: the moment a swing ends and the foot takes the ground. Read off
    /// <see cref="LeggedLocomotion.Footfalls"/> by anything that answers a step -- dust, sound --
    /// on every machine, since the legs simulate everywhere (Locomotion.md, Multiplayer).
    /// </summary>
    public readonly struct Footfall
    {
        /// <summary>The leg's index in the machine's fixed leg order.</summary>
        public readonly int Leg;
        /// <summary>Where the sole's contact point landed, in world space.</summary>
        public readonly Vector3 Point;
        /// <summary>The ground's normal under it.</summary>
        public readonly Vector3 Normal;
        /// <summary>How far the sole spreads round its contact point, measured off the foot's meshes.</summary>
        public readonly float FootprintRadius;

        public Footfall(int leg, Vector3 point, Vector3 normal, float footprintRadius)
        {
            Leg = leg;
            Point = point;
            Normal = normal;
            FootprintRadius = footprintRadius;
        }
    }
}
