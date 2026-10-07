using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Ship spawn placement chosen at runtime, overriding whatever the arena's asset says.
    ///
    /// <para>
    /// A static for the reason <see cref="VersusSession"/> spells out: whoever picks a layout — a
    /// lobby, a dev tool, a test — is destroyed by the very scene load that needs the answer, so
    /// there is no object to hang it off. Like that session it outlives a return to the menu, which
    /// is why <see cref="Clear"/> exists and why leaving one standing is how the next match starts
    /// on the last one's ring.
    /// </para>
    ///
    /// <para>
    /// An override layer rather than writes into <see cref="VersusShipSpawnConfig"/>. Editing the
    /// asset would behave differently in the two places it matters: in the Editor the change
    /// survives play mode and quietly becomes the authored value, and in a build it survives
    /// nothing at all.
    /// </para>
    /// </summary>
    public static class VersusShipSpawns
    {
        private enum Source
        {
            None,
            Ring,
            Explicit
        }

        private static Source source = Source.None;
        private static Vector2 ringCenterXZ;
        private static float ringRadius;
        private static ShipSpawnPoint[] explicitPoints = System.Array.Empty<ShipSpawnPoint>();

        /// <summary>Whether a runtime layout has been set and is standing in for the asset.</summary>
        public static bool HasOverride => source != Source.None;

        /// <summary>
        /// Place the teams on a circle. The whole layout from a centre and a radius, whatever the
        /// team count turns out to be.
        /// </summary>
        public static void UseRing(Vector2 centerXZ, float radius)
        {
            source = Source.Ring;
            ringCenterXZ = centerXZ;
            ringRadius = radius;
        }

        /// <summary>
        /// Place the teams at named points.
        ///
        /// Copied rather than aliased, matching <see cref="VersusSession.Begin"/>: the caller is
        /// tooling that keeps its own list around to keep editing, and a later edit must not reach
        /// back into this static and change what a match already loaded on.
        /// </summary>
        public static void UseExplicit(IReadOnlyList<ShipSpawnPoint> points)
        {
            source = Source.Explicit;
            explicitPoints = new ShipSpawnPoint[points?.Count ?? 0];

            for (int i = 0; i < explicitPoints.Length; i++)
                explicitPoints[i] = points[i];
        }

        /// <summary>Drops the override, so the arena's own asset is authoritative again.</summary>
        public static void Clear()
        {
            source = Source.None;
            ringCenterXZ = Vector2.zero;
            ringRadius = 0f;
            explicitPoints = System.Array.Empty<ShipSpawnPoint>();
        }

        /// <summary>
        /// Where the teams start: the runtime override if one is set, otherwise
        /// <paramref name="config"/>. False with a reason fit to log when neither can answer.
        /// </summary>
        public static bool TryResolve(VersusShipSpawnConfig config, int teamCount,
                                      out IReadOnlyList<ShipSpawnPoint> points, out string refusal)
        {
            switch (source)
            {
                case Source.Ring:
                    return TryRing(ringCenterXZ, ringRadius, teamCount, out points, out refusal);

                case Source.Explicit:
                    bool valid = ShipSpawnLayout.TryValidateExplicit(explicitPoints, teamCount,
                                                                     out ShipSpawnPoint[] ordered, out refusal);
                    points = ordered;
                    return valid;
            }

            if (config != null)
                return config.TryPoints(teamCount, out points, out refusal);

            points = System.Array.Empty<ShipSpawnPoint>();
            refusal = "no spawn config is assigned and no runtime layout has been set.";
            return false;
        }

        /// <summary>
        /// A ring layout with the team count checked first, shared by the runtime override and the
        /// asset so both refuse a nonsensical match the same way rather than one of them handing
        /// back an empty list that reads as success.
        /// </summary>
        internal static bool TryRing(Vector2 centerXZ, float radius, int teamCount,
                                     out IReadOnlyList<ShipSpawnPoint> points, out string refusal)
        {
            if (teamCount <= 0)
            {
                points = System.Array.Empty<ShipSpawnPoint>();
                refusal = "the match has no teams.";
                return false;
            }

            points = ShipSpawnLayout.Ring(centerXZ, radius, teamCount);
            refusal = null;
            return true;
        }
    }
}
