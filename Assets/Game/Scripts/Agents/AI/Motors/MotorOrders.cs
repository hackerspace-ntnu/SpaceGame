// What an AI brain last asked a self-steering motor to do: go to a point (with a stop distance and a
// speed), turn to face a point, or nothing. A motor that owns its own body -- MonowheelMotor,
// TrackedHullMotor -- latches the MoveIntent here on AgentController's clock and reads it back on its
// own (physics) clock, so the bookkeeping of the IMovementMotor contract lives in one place: which
// intent clears what, how a suggestion differs from an order, what "arrived" means when the order
// gave no stop distance, and how the points ride through a teleport.
//
// Arrival is measured FLAT. A hull rides metres above the NavMesh point it was sent to, so a 3D
// distance would never reach a stop distance sized for the ground.
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Agents
{
    public sealed class MotorOrders
    {
        /// <summary>Where the motor is going, or null when it is stopped or only facing.</summary>
        public Vector3? Destination { get; private set; }

        /// <summary>The point to turn toward while stopped, or null.</summary>
        public Vector3? FacePoint { get; private set; }

        /// <summary>The order's own stop distance; 0 when it gave none (a suggestion, a bare intent).</summary>
        public float StopDistance { get; private set; }

        /// <summary>The order's speed multiplier; 0 or less means full speed.</summary>
        public float SpeedMultiplier { get; private set; }

        /// <summary>Latch one frame's intent. Anything but a move or a face clears both.</summary>
        public void Take(in MoveIntent intent)
        {
            switch (intent.Type)
            {
                case AgentIntentType.MoveToPosition:
                    Destination = intent.TargetPosition;
                    StopDistance = intent.StopDistance;
                    SpeedMultiplier = intent.SpeedMultiplier;
                    FacePoint = null;
                    break;

                case AgentIntentType.StopAndFacePosition:
                    Destination = null;
                    FacePoint = intent.FacePosition;
                    break;

                default:
                    Clear();
                    break;
            }
        }

        /// <summary>Forget the destination and the face point.</summary>
        public void Clear()
        {
            Destination = null;
            FacePoint = null;
        }

        /// <summary>Bias the destination without replacing it. No-op while stopped.</summary>
        public void Nudge(Vector3 offset)
        {
            if (Destination.HasValue)
                Destination = Destination.Value + offset;
        }

        /// <summary>
        /// Head for <paramref name="position"/> on someone else's say-so. A suggestion carries no stop
        /// distance, and the last order's would not fit it, so the motor's default applies.
        /// </summary>
        public void Suggest(Vector3 position)
        {
            Destination = position;
            StopDistance = 0f;
        }

        /// <summary>Carry the destination and the face point through a teleport.</summary>
        public void Rebase(in TeleportMove move)
        {
            if (Destination.HasValue)
                Destination = move.Point(Destination.Value);
            if (FacePoint.HasValue)
                FacePoint = move.Point(FacePoint.Value);
        }

        /// <summary>The order's stop distance, or <paramref name="fallback"/> when it gave none.</summary>
        public float EffectiveStopDistance(float fallback) => StopDistance > 0f ? StopDistance : fallback;

        /// <summary>
        /// Flat distance still to cover from <paramref name="position"/> before the stop distance is
        /// reached. Zero or less means arrived; infinity when there is no destination to cover.
        /// </summary>
        public float Remaining(Vector3 position, float fallbackStopDistance) =>
            Destination.HasValue
                ? FlatDistance(Destination.Value, position) - EffectiveStopDistance(fallbackStopDistance)
                : float.PositiveInfinity;

        /// <summary>True when there is no destination, or <paramref name="position"/> is within its stop distance.</summary>
        public bool HasReached(Vector3 position, float fallbackStopDistance) =>
            !Destination.HasValue || Remaining(position, fallbackStopDistance) <= 0f;

        /// <summary>Distance between two points ignoring height.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
