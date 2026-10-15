// Which gears in a flat train turn, how fast, and whether they bite or jam. Pure: no scene, no clock.
//
// Every gear on a GearDoor stands on one plane, its centre a point in that plane. Two gears mesh when their centres
// stand exactly the sum of their pitch radii apart (all gears share one tooth size, so that is a tooth count times
// PerTooth); they hit when they stand closer than their tips allow without meshing. The train turns from one driver:
// a gear meshed with a turning gear turns the other way, slower by its tooth ratio. A gear reached twice at two
// different speeds is a lock-up, and everything joined to it stands still.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>What a train's tooth size and spacing are, in metres. One per door: every gear on it shares them.</summary>
    [Serializable]
    public struct GearGeometry
    {
        [Tooltip("Centre distance per tooth: two gears mesh with their centres (teeth a + teeth b) x this apart. " +
                 "Half the module, times the scale the gears are drawn at.")]
        public float perTooth;

        [Tooltip("How far a gear's tips stand out past its pitch circle.")]
        public float addendum;

        [Tooltip("How far apart two gears that do not mesh must stand, tip to tip.")]
        public float clearance;

        [Tooltip("How far off the exact spacing two gears still mesh.")]
        public float tolerance;

        public float PitchRadius(int teeth) => teeth * perTooth;

        public float OuterRadius(int teeth) => PitchRadius(teeth) + addendum;
    }

    /// <summary>One gear in a train: where its centre is on the plane, and its teeth.</summary>
    public readonly struct GearSpec
    {
        public readonly Vector2 Centre;
        public readonly int Teeth;

        public GearSpec(Vector2 centre, int teeth)
        {
            Centre = centre;
            Teeth = teeth;
        }
    }

    public enum GearContact { Free, Mesh, Hit }

    public static class GearTrain
    {
        private const float SpeedEpsilon = 1e-4f;

        /// <summary>How two gears stand to each other.</summary>
        public static GearContact Between(in GearSpec a, in GearSpec b, in GearGeometry geometry)
        {
            float distance = Vector2.Distance(a.Centre, b.Centre);
            float mesh = geometry.PitchRadius(a.Teeth) + geometry.PitchRadius(b.Teeth);
            if (Mathf.Abs(distance - mesh) <= geometry.tolerance) return GearContact.Mesh;

            float reach = geometry.OuterRadius(a.Teeth) + geometry.OuterRadius(b.Teeth) + geometry.clearance;
            return distance < reach ? GearContact.Hit : GearContact.Free;
        }

        /// <summary>
        /// Turn the train from <paramref name="driver"/> at speed 1. <paramref name="speeds"/> gets each gear's speed as
        /// a multiple of the driver's (negative the other way round, 0 standing still). Returns whether it locks up, in
        /// which case every gear joined to the driver, the driver too, stands still.
        /// </summary>
        public static bool Drive(IReadOnlyList<GearSpec> gears, int driver, in GearGeometry geometry, float[] speeds)
        {
            Array.Clear(speeds, 0, speeds.Length);
            if (driver < 0 || driver >= gears.Count) return false;

            var reached = new bool[gears.Count];
            var queue = new Queue<int>();
            reached[driver] = true;
            speeds[driver] = 1f;
            queue.Enqueue(driver);
            bool locked = false;

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();

                for (int other = 0; other < gears.Count; other++)
                {
                    if (other == at || Between(gears[at], gears[other], geometry) != GearContact.Mesh) continue;

                    float speed = -speeds[at] * gears[at].Teeth / gears[other].Teeth;
                    if (reached[other])
                    {
                        if (Mathf.Abs(speeds[other] - speed) > SpeedEpsilon) locked = true;
                        continue;
                    }

                    reached[other] = true;
                    speeds[other] = speed;
                    queue.Enqueue(other);
                }
            }

            if (locked)
                for (int i = 0; i < speeds.Length; i++)
                    if (reached[i]) speeds[i] = 0f;
            return locked;
        }

        /// <summary>
        /// The angle, in degrees about the plane's normal (anticlockwise seen from the front), that sets gear
        /// <paramref name="b"/>'s teeth into the gaps of gear <paramref name="a"/> standing at <paramref name="angleA"/>.
        /// <paramref name="toothA"/> and <paramref name="toothB"/> are where each model has a tooth tip at angle 0.
        /// </summary>
        public static float MeshedAngle(in GearSpec a, float angleA, float toothA, in GearSpec b, float toothB)
        {
            Vector2 towards = b.Centre - a.Centre;
            float contact = Mathf.Atan2(towards.y, towards.x) * Mathf.Rad2Deg;

            // How far through a tooth pitch a's tip is where the two touch; b must show the mirror of it, half a pitch on.
            float fractionA = Mathf.Repeat((contact - angleA - toothA) * a.Teeth / 360f, 1f);
            return contact + 180f - toothB - (0.5f - fractionA) * 360f / b.Teeth;
        }
    }
}
