// What counts as a ledge the player can climb, asked from anywhere the player might climb one: standing
// at a wall, hanging on a grapple rope, or passing an edge in mid-air. One query, so all three agree.
//
// A ledge is an edge the hands can take: a walkable top with open space just above it for the hands and
// head to go over. Whether a whole body could stand up there is deliberately not asked — the climb goes
// over and the far side is whatever it is: a floor to stand on (climb up) or nothing within reach of the
// feet (vault, and drop on the other side). That is how grab-and-mantle games read edges (detect from the
// hands, not the feet), and it is what lets the same test hold for decks, railings, roofs, rocks, rims and
// hulls without each needing its own rule.
//
// Two ways to say where to look: on foot, from the face of the wall in front (Find); on a grapple, from
// the hook itself (FindAtHook), because the hook point IS the player's intent — they aimed at it and
// reeled themselves to it.
//
// Only the world is climbed. Another player, an NPC or a creature in front is a 3 m capsule with a walkable
// crown, which the search would happily read as a ledge — so it skips characters (IsCharacter).
using System;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;

namespace SpaceGame.Characters
{
    public enum LedgeKind { None, ClimbUp, Vault }

    /// <summary>A ledge the body can climb, and the two feet positions the climb steers through.</summary>
    public readonly struct Ledge
    {
        public static readonly Ledge None = default;

        public readonly LedgeKind Kind;

        /// <summary>Feet at the top of the pull: in front of the edge, just above the lip.</summary>
        public readonly Vector3 Hang;

        /// <summary>Feet at the end: standing on top, or just past the edge of a vault.</summary>
        public readonly Vector3 Landing;

        /// <summary>Flat, unit, into the wall.</summary>
        public readonly Vector3 Facing;

        public Ledge(LedgeKind kind, Vector3 hang, Vector3 landing, Vector3 facing)
        {
            Kind = kind;
            Hang = hang;
            Landing = landing;
            Facing = facing;
        }

        public bool Found => Kind != LedgeKind.None;
    }

    /// <summary>The ledge query. Serialized inside <see cref="LedgeClimber"/> so its distances are tuned in the Inspector.</summary>
    [Serializable]
    public sealed class LedgeProbe
    {
        [Tooltip("How far past the body's surface a wall may be and still be climbed, metres.")]
        [SerializeField, Min(0.05f)] private float wallProbeDistance = 1f;

        [Tooltip("Radius of the spheres the probe sweeps, metres. Small, so a railing's top bar is found.")]
        [SerializeField, Min(0.01f)] private float probeRadius = 0.1f;

        [Tooltip("Steepest top that counts as an edge to grab and stand on, degrees. Steeper is a wall.")]
        [SerializeField, Range(0f, 89f)] private float maxWalkableSlope = 45f;

        [Tooltip("How far past the wall face the first edge sample is taken, metres. Keep it within the " +
                 "probe radius of the face: a railing's top rail is a few centimetres deep, and a sample " +
                 "set further in looks straight past it.")]
        [SerializeField, Min(0f)] private float lipInset = 0.1f;

        [Tooltip("How far past the wall face edges are looked for, metres. Deep enough to reach a railing " +
                 "set back on a deck.")]
        [SerializeField, Min(0f)] private float lipSearchDepth = 1f;

        [Tooltip("Metres between edge samples along the facing.")]
        [SerializeField, Min(0.02f)] private float sampleStep = 0.1f;

        [Tooltip("Radius of the open space needed just above an edge for the hands and head to go over, metres.")]
        [SerializeField, Min(0.01f)] private float handRoom = 0.3f;

        [Tooltip("Metres above the lip the feet are lifted to before moving over it.")]
        [SerializeField, Min(0f)] private float lipClearance = 0.1f;

        [Tooltip("Metres from the edge to the body's centre while pulling up. More than the capsule radius.")]
        [SerializeField, Min(0f)] private float standoff = 0.7f;

        [Tooltip("Extra metres past the edge (beyond the capsule radius) where the feet are set down.")]
        [SerializeField, Min(0f)] private float standInset = 0.15f;

        [Tooltip("How far above or below the lip the landing spot may be and still be the same top, metres. " +
                 "Nothing within it means the climb is a vault.")]
        [SerializeField, Min(0.01f)] private float topTolerance = 0.3f;

        [Header("At a grapple's hook")]
        [Tooltip("How far above the hook an edge may be and still be grabbed, metres.")]
        [SerializeField, Min(0f)] private float hookGrabAbove = 1.5f;

        [Tooltip("How far below the hook an edge may be, metres: a hook bitten into the top sits on it.")]
        [SerializeField, Min(0f)] private float hookGrabBelow = 0.3f;

        [Tooltip("How far short of the hook, toward the player, the edge is looked for, metres. A hook " +
                 "bitten into the top surface sits behind the edge it is on.")]
        [SerializeField, Min(0f)] private float hookSearchBack = 1f;

        [Tooltip("How far past the hook the edge is looked for, metres. A hook bitten into a face sits " +
                 "in front of the top above it.")]
        [SerializeField, Min(0f)] private float hookSearchAhead = 0.6f;

        /// <summary>Bounds the edge samples, whatever the Inspector says.</summary>
        private const int MaxSamples = 64;

        /// <summary>
        /// The edge in front of a body with its feet at <paramref name="feet"/>, facing
        /// <paramref name="facing"/>, between <paramref name="minHeight"/> and <paramref name="reach"/>
        /// metres above those feet — or <see cref="Ledge.None"/>.
        /// </summary>
        public Ledge Find(in PlayerBodyShape body, Vector3 feet, Vector3 facing, float minHeight, float reach)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f || reach <= minHeight) return Ledge.None;
            facing.Normalize();

            // A wall in front, swept over the whole height the climb rises through, so the face is whatever
            // stands out furthest. Swept at one height it found the recessed wall under a deck whose edge,
            // railing and stairs stand proud of it, and the body planned to rise straight up through them.
            Vector3 wallLow = feet + Vector3.up * Mathf.Max(probeRadius, minHeight * 0.5f);
            Vector3 wallHigh = feet + Vector3.up * reach;
            if (!Nearest(body, Physics.CapsuleCastAll(wallLow, wallHigh, probeRadius, facing,
                                                      body.Radius + wallProbeDistance,
                                                      Physics.DefaultRaycastLayers,
                                                      QueryTriggerInteraction.Ignore),
                         out RaycastHit wall) ||
                IsWalkable(wall.normal))
                return Ledge.None;

            Vector3 face = new Vector3(wall.point.x, feet.y, wall.point.z);
            return GrabEdge(body, face + facing * lipInset, facing, feet.y + minHeight, feet.y + reach,
                            lipSearchDepth);
        }

        /// <summary>
        /// The edge a grapple hooked at <paramref name="anchor"/> is on, climbing toward
        /// <paramref name="facing"/> — or <see cref="Ledge.None"/>. Looked for at the hook, not rediscovered
        /// from wherever the swing has left the feet: a winch arrives with the body metres off the face and
        /// at any angle, and the hook already says where the edge is.
        /// </summary>
        public Ledge FindAtHook(in PlayerBodyShape body, Vector3 anchor, Vector3 facing)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) return Ledge.None;
            facing.Normalize();

            return GrabEdge(body, anchor - facing * hookSearchBack, facing, anchor.y - hookGrabBelow,
                            anchor.y + hookGrabAbove, hookSearchBack + hookSearchAhead);
        }

        /// <summary>
        /// The first edge the hands can take, walking <paramref name="depth"/> metres along
        /// <paramref name="facing"/> from <paramref name="start"/>: a walkable top between
        /// <paramref name="low"/> and <paramref name="high"/> with room above it for hands and head. A top
        /// without that room (a deck edge with a railing standing on it, the floor of a slot) is passed over
        /// for the next one, which is how the railing's own rail gets found.
        /// </summary>
        private Ledge GrabEdge(in PlayerBodyShape body, Vector3 start, Vector3 facing, float low, float high,
                               float depth)
        {
            int samples = Mathf.Min(MaxSamples, Mathf.FloorToInt(depth / sampleStep) + 1);
            for (int i = 0; i < samples; i++)
            {
                Vector3 column = start + facing * (i * sampleStep);
                Vector3 origin = new Vector3(column.x, high + probeRadius, column.z);
                if (!Sweep(body, origin, Vector3.down, high - low, out RaycastHit top) || !IsWalkable(top.normal))
                    continue;

                float lipY = top.point.y;
                Vector3 edge = new Vector3(column.x, lipY, column.z);
                if (Overlaps(body, edge + Vector3.up * (handRoom + probeRadius), handRoom)) continue;

                Vector3 hang = edge - facing * standoff;
                hang.y = lipY + lipClearance;

                Vector3 landing = edge + facing * (body.Radius + standInset);
                if (TopAt(body, landing, lipY, out float landingY))
                {
                    landing.y = landingY + lipClearance;
                    return new Ledge(LedgeKind.ClimbUp, hang, landing, facing);
                }

                landing.y = hang.y;
                return new Ledge(LedgeKind.Vault, hang, landing, facing);
            }

            return Ledge.None;
        }

        private bool IsWalkable(Vector3 normal) => Vector3.Angle(normal, Vector3.up) <= maxWalkableSlope;

        /// <summary>Whether a walkable top is within <see cref="topTolerance"/> of <paramref name="lipY"/> under <paramref name="at"/>.</summary>
        private bool TopAt(in PlayerBodyShape body, Vector3 at, float lipY, out float y)
        {
            Vector3 origin = new Vector3(at.x, lipY + topTolerance, at.z);
            bool hit = Nearest(body, Physics.RaycastAll(origin, Vector3.down, topTolerance * 2f,
                                                        Physics.DefaultRaycastLayers,
                                                        QueryTriggerInteraction.Ignore),
                               out RaycastHit surface) &&
                       IsWalkable(surface.normal);
            y = hit ? surface.point.y : lipY;
            return hit;
        }

        private bool Sweep(in PlayerBodyShape body, Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest) =>
            Nearest(body, Physics.SphereCastAll(origin, probeRadius, direction, distance,
                                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                    out nearest);

        /// <summary>Whether anything of the world is inside the sphere at <paramref name="at"/>.</summary>
        private static bool Overlaps(in PlayerBodyShape body, Vector3 at, float radius)
        {
            foreach (Collider c in Physics.OverlapSphere(at, radius, Physics.DefaultRaycastLayers,
                                                         QueryTriggerInteraction.Ignore))
                if (IsWorld(body, c))
                    return true;
            return false;
        }

        /// <summary>The nearest of <paramref name="hits"/> that is world, not a contact the query starts in.</summary>
        private static bool Nearest(in PlayerBodyShape body, RaycastHit[] hits, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (!IsWorld(body, hit.collider) || hit.distance <= 0f) continue;
                if (found && hit.distance >= nearest.distance) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        private static bool IsWorld(in PlayerBodyShape body, Collider c) => !body.IsSelf(c) && !IsCharacter(c);

        /// <summary>
        /// Part of a player, an NPC or a creature (a rideable one included). Everything that can go limp
        /// carries a <see cref="RagdollRig"/> on its root, and vehicles, buildings and the sky fleet's hulls
        /// don't, so it is the marker. Not "is the body dynamic": a remote player's body is kinematic.
        /// </summary>
        private static bool IsCharacter(Collider c) => c.GetComponentInParent<RagdollRig>() != null;
    }
}
