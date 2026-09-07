// Who is standing under a storm, and which of them is tallest.
//
// THIS IS THE WHOLE RULE THE PLAYER HAS TO READ. The design's target selection is deliberately the
// simplest one that can be stated in a sentence — the highest body inside the circle gets hit — and
// it is written out here on its own so that it stays that way. A cleverer rule (nearest, most
// wounded, whoever threw the flask) would be invisible from the ground and would leave players
// guessing at causes rather than learning one (GDC-L1-SYS-0006, GDC-L1-SYS-0002: author the rule,
// let the play come out of it).
//
// A BODY IS ITS RECEIVER, AND ITS POSITION IS ITS ROOT'S. The overlap catches colliders, and a
// creature is a dozen of them hanging off its bones. Resolving each to its StatusReceiver and then
// asking THAT object where it is means a limb reaching under the rim is not the same claim as the
// creature standing there — and it is what makes "tallest" mean the tallest body rather than the
// highest kneecap.
//
// IT NEVER CREATES A RECEIVER. StatusReceiver.Of, never Ensure: a receiver added here would exist on
// the server alone, and a status message addressed to a body nothing has subscribed on is dropped
// without a word on every other machine. A body that can be struck, burned or dried says so on its
// own prefab — which is also exactly the set of bodies the design means by "whatever is tallest".
using System.Collections.Generic;
using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The sweep a <see cref="StormCloud"/> runs to find the bodies beneath it.
    /// </summary>
    public static class StormCloudTargets
    {
        /// <summary>
        /// Ceiling on colliders considered in one sweep. Generous for a twelve-metre column; a
        /// sweep that fills it is a mask that wants narrowing, not a bigger buffer.
        /// </summary>
        private const int MaxColliders = 64;

        /// <summary>Scratch for the column sweep. Nothing is held between calls.</summary>
        private static readonly Collider[] Column = new Collider[MaxColliders];

        private static bool warnedSaturated;

        // Statics outlive the world, the session and play mode, and a warning flag left set would
        // silence the one line that says why a storm is ignoring half a crowd in the NEXT session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => warnedSaturated = false;

        /// <summary>
        /// Fill <paramref name="bodies"/> with every <see cref="StatusReceiver"/> standing under the
        /// storm, once each.
        /// </summary>
        /// <param name="groundPoint">Where the rain lands — the centre of the circle.</param>
        /// <param name="radius">The storm's radius, measured horizontally and nothing else.</param>
        /// <param name="reachBelow">
        /// How far under the rain the storm still counts a body. Not zero: the ground point is one
        /// raycast hit, and somebody standing in a ditch beside it is still under the cloud.
        /// </param>
        /// <param name="reachAbove">
        /// How far over the rain the storm still counts a body, which is the cloud's own height —
        /// nothing above the cloud is underneath it.
        /// </param>
        public static void Under(Vector3 groundPoint, float radius, float reachBelow,
                                 float reachAbove, LayerMask mask, List<StatusReceiver> bodies)
        {
            if (bodies == null) return;

            bodies.Clear();

            float halfHeight = (reachAbove + reachBelow) * 0.5f;
            if (radius <= 0f || halfHeight <= 0f) return;

            var centre = new Vector3(groundPoint.x,
                                     groundPoint.y + (reachAbove - reachBelow) * 0.5f,
                                     groundPoint.z);

            // A box rather than a sphere, because the volume wanted is a column: a sphere wide
            // enough to reach the rim at ground level would also reach far outside it at cloud
            // height, and one sized to the height would miss the rim.
            int found = Physics.OverlapBoxNonAlloc(centre, new Vector3(radius, halfHeight, radius),
                                                   Column, Quaternion.identity, mask,
                                                   QueryTriggerInteraction.Ignore);

            WarnIfSaturated(found, radius);

            float bottom = groundPoint.y - reachBelow;
            float top = groundPoint.y + reachAbove;
            float radiusSquared = radius * radius;

            for (int i = 0; i < found; i++)
            {
                Collider collider = Column[i];
                if (collider == null) continue;

                StatusReceiver body = StatusReceiver.Of(collider.gameObject);

                // Contains, not a set: a storm holds a handful of bodies, and a linear scan over a
                // handful costs less than the hashing — and allocates nothing either way.
                if (body == null || bodies.Contains(body)) continue;

                Vector3 position = body.transform.position;
                if (position.y < bottom || position.y > top) continue;

                float across = position.x - groundPoint.x;
                float along = position.z - groundPoint.z;
                if (across * across + along * along > radiusSquared) continue;

                bodies.Add(body);
            }
        }

        /// <summary>
        /// The highest of <paramref name="bodies"/> by world height, or null when the list is empty.
        ///
        /// <para>
        /// World height, not height above the ground under each body: standing on a rock is what
        /// makes you the tallest thing under a storm, which is the design's point. Whoever uncorked
        /// the flask is in the list like anybody else — the cloud has no idea who they are.
        /// </para>
        /// </summary>
        public static StatusReceiver Tallest(List<StatusReceiver> bodies)
        {
            if (bodies == null) return null;

            StatusReceiver tallest = null;
            float highest = float.NegativeInfinity;

            for (int i = 0; i < bodies.Count; i++)
            {
                StatusReceiver body = bodies[i];
                if (body == null) continue;

                float height = body.transform.position.y;
                if (height <= highest) continue;

                highest = height;
                tallest = body;
            }

            return tallest;
        }

        /// <summary>
        /// A saturated query stops filling and drops the rest without a word, which for a storm
        /// reads as bolts that ignore half a crowd for no reason anybody can see. Said once: it
        /// would otherwise repeat every rain tick for the life of every cloud.
        /// </summary>
        private static void WarnIfSaturated(int found, float radius)
        {
            if (found < MaxColliders || warnedSaturated) return;

            warnedSaturated = true;

            Debug.LogWarning(
                $"[StormFlask] {MaxColliders} colliders inside a {radius:0.#} m storm column — some " +
                "were ignored, so the bolt may not be picking the tallest body. Narrow the storm's " +
                "body mask to the layers bodies actually live on.");
        }
    }
}
