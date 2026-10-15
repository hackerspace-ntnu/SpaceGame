using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Where the workshop's hoist can be, and how its one up-and-down control is shared between the hook and the
    /// lift. Pure: no scene, tested on its own.
    ///
    /// <para>
    /// <b>Three numbers place the whole hoist.</b> The bridge and the trolley are metres along their runs from where
    /// they are modelled. The third, <see cref="WorkshopCranePose.height"/>, is the hook's height against its parked
    /// one: below zero the hook is let down that far with the gantry at the bottom of its towers; above zero the hook
    /// is right up and the gantry has climbed that far. So "up" first winds the hook in and then lifts the gantry, the
    /// way a crane driver would, and the operator works one lever for both.
    /// </para>
    /// </summary>
    [Serializable]
    public struct WorkshopCraneReach
    {
        [Tooltip("Metres the bridge may run from where it is modelled: x the least, y the most. Measured by the export.")]
        public Vector2 bridge;

        [Tooltip("Metres the trolley may run along the bridge from where it is modelled: x the least, y the most. Measured by the export.")]
        public Vector2 trolley;

        [Tooltip("Metres the hook may be let down below its parked height, with the gantry at the bottom of its towers.")]
        public float hookDrop;

        [Tooltip("Metres the gantry may climb its tower beams.")]
        public float liftClimb;

        /// <summary>Lowest and highest height (see <see cref="WorkshopCranePose.height"/>).</summary>
        public float LowestHeight => -Mathf.Max(hookDrop, 0f);

        public float HighestHeight => Mathf.Max(liftClimb, 0f);

        /// <summary><paramref name="pose"/> kept inside the hoist's reach.</summary>
        public WorkshopCranePose Clamp(WorkshopCranePose pose) => new()
        {
            bridge = Mathf.Clamp(pose.bridge, Mathf.Min(bridge.x, bridge.y), Mathf.Max(bridge.x, bridge.y)),
            trolley = Mathf.Clamp(pose.trolley, Mathf.Min(trolley.x, trolley.y), Mathf.Max(trolley.x, trolley.y)),
            height = Mathf.Clamp(pose.height, LowestHeight, HighestHeight),
        };

        /// <summary>Metres the hook hangs below its parked height: the <c>Crane</c> channel.</summary>
        public static float HookDropOf(float height) => Mathf.Max(-height, 0f);

        /// <summary>Metres the gantry stands up its towers: the <c>HoistLift</c> channel.</summary>
        public static float LiftOf(float height) => Mathf.Max(height, 0f);

        /// <summary>The height that stands for a hook let down <paramref name="hookDrop"/> with the gantry up <paramref name="lift"/>.</summary>
        public static float HeightOf(float hookDrop, float lift) => hookDrop > 0f ? -hookDrop : lift;
    }

    /// <summary>Where the hoist stands, in <see cref="WorkshopCraneReach"/>'s terms.</summary>
    [Serializable]
    public struct WorkshopCranePose : IEquatable<WorkshopCranePose>
    {
        public float bridge;
        public float trolley;
        public float height;

        public WorkshopCranePose(float bridge, float trolley, float height)
        {
            this.bridge = bridge;
            this.trolley = trolley;
            this.height = height;
        }

        public Vector3 ToWire() => new(bridge, trolley, height);

        public static WorkshopCranePose FromWire(Vector3 p) => new(p.x, p.y, p.z);

        public bool Equals(WorkshopCranePose other) =>
            bridge.Equals(other.bridge) && trolley.Equals(other.trolley) && height.Equals(other.height);

        public override bool Equals(object obj) => obj is WorkshopCranePose other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(bridge, trolley, height);
    }

    /// <summary>
    /// What the operator is asking the hoist to do: each axis -1, 0 or +1. Packed into one whole number so it rides a
    /// <c>VehicleStation</c> claim, whose request is a single float.
    /// </summary>
    public readonly struct WorkshopCraneCommand : IEquatable<WorkshopCraneCommand>
    {
        public readonly int Bridge;
        public readonly int Trolley;
        public readonly int Height;

        public WorkshopCraneCommand(int bridge, int trolley, int height)
        {
            Bridge = Math.Sign(bridge);
            Trolley = Math.Sign(trolley);
            Height = Math.Sign(height);
        }

        public bool IsIdle => Bridge == 0 && Trolley == 0 && Height == 0;

        /// <summary>0 .. 26: each axis as a base-3 digit.</summary>
        public float Pack() => (Bridge + 1) + 3 * (Trolley + 1) + 9 * (Height + 1);

        /// <summary>The command a packed number stands for; anything that is not one is idle.</summary>
        public static WorkshopCraneCommand Unpack(float packed)
        {
            int code = Mathf.RoundToInt(packed);
            if (code < 0 || code > 26) return default;
            return new WorkshopCraneCommand(code % 3 - 1, code / 3 % 3 - 1, code / 9 - 1);
        }

        public bool Equals(WorkshopCraneCommand other) =>
            Bridge == other.Bridge && Trolley == other.Trolley && Height == other.Height;

        public override bool Equals(object obj) => obj is WorkshopCraneCommand other && Equals(other);

        public override int GetHashCode() => (int)Pack();
    }
}
