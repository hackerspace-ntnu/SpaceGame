// The arithmetic of a creature that has been picked up off its own NavMesh.
//
// Pure and static, so the three decisions that make a hoist work -- is this ask a lift, how fast
// is the body falling, has it arrived -- can be asked of the numbers without a NavMesh, a rope or
// a physics scene. The same reason JetpackLift is a static class rather than a method on the pack.
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// What a rope does to a NavMesh-driven body once the pull stops being along the ground.
    /// See <c>NavMeshAgentMotor.Carry.cs</c> for the state this arithmetic drives.
    /// </summary>
    public static class AgentCarry
    {
        /// <summary>
        /// Is this step's ask a LIFT rather than a haul along the sand?
        ///
        /// <para>
        /// Judged on the ask's SLOPE, not on its height: a rope's knot sits on a creature's back
        /// and the far knot sits in a player's hand about a metre up, so every flat drag has a
        /// small upward component and a height test would put an animal in the air for being
        /// walked. Over a two-metre rope that standing offset is a slope of about 0.45, which is
        /// why the threshold lives well above it.
        /// </para>
        /// <para>
        /// There is deliberately no matching test for coming back DOWN. Leaving the carry is
        /// landing and nothing else, so a body hanging at exactly the threshold cannot flicker
        /// between the two states -- the dead-zone lesson the pack's snapping already paid for.
        /// </para>
        /// </summary>
        public static bool IsLift(Vector3 ask, float minSlope)
        {
            if (ask.y <= 0f) return false;

            float length = ask.magnitude;
            if (length < 1e-6f) return false;

            return ask.y / length >= minSlope;
        }

        /// <summary>
        /// One step of fall for a body nothing is holding up, capped at
        /// <paramref name="maxFallSpeed"/>.
        ///
        /// <para>
        /// The cap is not for realism. It bounds how far one step can carry the body past the
        /// ground it is about to land on, and <see cref="HasLanded"/> sizes its own tolerance from
        /// the same figure -- so between them a body cannot fall through the world between two
        /// physics steps.
        /// </para>
        /// </summary>
        public static Vector3 Fall(Vector3 velocity, Vector3 gravity, float deltaTime,
                                   float maxFallSpeed)
        {
            velocity += gravity * deltaTime;

            if (velocity.y < -maxFallSpeed) velocity.y = -maxFallSpeed;

            return velocity;
        }

        /// <summary>
        /// One step of a motor strapped to a body that is still on its mesh, as a SPEED.
        ///
        /// <para>
        /// The mesh remembers nothing. A body being dragged along it has no momentum to keep —
        /// <c>NavMeshAgent.Move</c> is a distance and the next step starts from rest — so the
        /// speed a push has built up has to be carried by whoever is asking, which is what this
        /// accumulates. A body that has been lifted OFF the mesh needs none of it: there the
        /// velocity is measured back out of how far the body moved (see <see cref="Fall"/>), so
        /// one step of acceleration is a·dt² and the measurement carries it forward by itself.
        /// </para>
        /// <para>
        /// Capped, and the cap is the honest part. A creature skidding across the sand is the
        /// booster working; a creature crossing the map in a second is not something anyone can
        /// read, and it is indistinguishable from the animal despawning.
        /// </para>
        /// </summary>
        public static float ThrustDragSpeed(float speed, float acceleration, float deltaTime,
                                            float maxSpeed)
        {
            return Mathf.Min(Mathf.Max(0f, speed) + Mathf.Max(0f, acceleration) * deltaTime,
                             maxSpeed);
        }

        /// <summary>
        /// Has a body at <paramref name="bodyY"/> arrived at the ground at
        /// <paramref name="groundY"/>?
        ///
        /// <para>
        /// The tolerance grows with the fall, because a body doing 30 m/s crosses 0.6 m in one
        /// physics step and a fixed tolerance smaller than that is a body that lands on the frame
        /// it is already underground -- or never. Below the ground counts as arrived, which is
        /// what makes a step that overshoots land rather than keep going.
        /// </para>
        /// </summary>
        public static bool HasLanded(float bodyY, float groundY, float verticalSpeed,
                                     float deltaTime, float tolerance)
        {
            return bodyY - groundY <= Mathf.Max(tolerance, Mathf.Abs(verticalSpeed) * deltaTime);
        }
    }
}
