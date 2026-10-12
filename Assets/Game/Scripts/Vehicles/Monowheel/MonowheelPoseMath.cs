// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPoseMath.cs
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// How far a monowheel's chassis tips about its hub so the ski rests on the sand. Pure, in a
    /// side view along the heading: x is forward, y is up, a positive pitch tips the nose down and a
    /// positive slope means the ground rises ahead.
    ///
    /// <para>The pivot is the hub because the wheel is round: tipped about its own centre, the wheel
    /// still touches the ground at its bottom, whatever the pitch. Only the ski has to be brought
    /// down to the sand.</para>
    /// </summary>
    public static class MonowheelPoseMath
    {
        /// <summary>
        /// The nose-down pitch (degrees) that puts the ski point on a ground line
        /// <paramref name="hubClearance"/> metres from the hub, square to it, with the given slope.
        /// Ground out of the ski's reach points it straight at the ground; the caller clamps.
        /// </summary>
        public static float SkiPitch(Vector2 hubToSki, float hubClearance, float groundSlopeDeg)
        {
            float reach = hubToSki.magnitude;
            if (reach < 1e-4f) return 0f;
            float belowForward = Mathf.Atan2(-hubToSki.y, hubToSki.x) * Mathf.Rad2Deg;
            float needed = Mathf.Asin(Mathf.Clamp(hubClearance / reach, -1f, 1f)) * Mathf.Rad2Deg;
            return needed - groundSlopeDeg - belowForward;
        }

        /// <summary>
        /// The ground as a straight line through two height samples along the heading, and how far
        /// the hub stands from it, square to the line.
        /// </summary>
        public static void GroundUnderHub(float contactForward, float groundAtContact, float skiForward, float groundAtSki,
                                          float hubForward, float hubHeight, out float slopeDeg, out float clearance)
        {
            float slope = Mathf.Atan2(groundAtSki - groundAtContact, skiForward - contactForward);
            float groundAtHub = groundAtContact + Mathf.Tan(slope) * (hubForward - contactForward);
            slopeDeg = slope * Mathf.Rad2Deg;
            clearance = (hubHeight - groundAtHub) * Mathf.Cos(slope);
        }
    }
}
