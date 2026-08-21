using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// One place an NPC can be sent to.
    ///
    /// <para>
    /// A position and a label, and deliberately nothing else — no Transform, no GameObject, no
    /// scene. That is the whole reason this type exists rather than NPCs holding Transform
    /// references: the world streams in 48 chunk scenes, so a Transform to somewhere 2 km away is
    /// null for almost the entire time an NPC is walking toward it. A record survives the chunk
    /// that produced it being unloaded, which is exactly the span a long journey covers.
    /// </para>
    /// </summary>
    public readonly struct WorldSite
    {
        /// <summary>Stable across the session, so a task can say "not the one I just left".</summary>
        public readonly string Id;

        public readonly SiteKind Kind;
        public readonly Vector3 Position;

        /// <summary>
        /// How big the place is. An NPC counts as arrived anywhere inside it, and wanders within it
        /// while it works — which is what makes "dwelling at a site" need no code of its own.
        /// </summary>
        public readonly float Radius;

        /// <summary>Shown to the player in chatter and dialog. May be empty.</summary>
        public readonly string Name;

        public WorldSite(string id, SiteKind kind, Vector3 position, float radius, string name)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Radius = Mathf.Max(1f, radius);
            Name = name ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        /// <summary>
        /// Horizontal distance only. Everything that asks this question is asking about travel over
        /// a heightmap, where the vertical component is noise: a site on top of a mesa is not
        /// "further away" than one at its foot in any sense an NPC's pathfinder agrees with.
        /// </summary>
        public float FlatDistanceTo(Vector3 from)
        {
            float dx = Position.x - from.x;
            float dz = Position.z - from.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public override string ToString() =>
            string.IsNullOrEmpty(Name) ? $"{Kind} @ {Position}" : $"{Name} ({Kind})";
    }
}
