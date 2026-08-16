using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// ====================================================================================
    /// STAGE 1 — THE CHAMBER GRAPH PLAN.
    /// ====================================================================================
    ///
    /// Lays out the top-level structure of the ArchingCave complex BEFORE any rock is carved —
    /// "first top-order structures, then placing the right stuff at the right places", exactly as
    /// the project lead mandated. This is the direct analogue of the cave system's
    /// <see cref="CaveGraphGenerator"/>: a guided random walk drops CHAMBERS (open cavities) one at
    /// a time, each at a random direction+distance from an existing chamber, then connects them into
    /// ONE connected graph of walkable passages. Stage 3 carves this whole cavity out of solid rock.
    ///
    /// Every chamber carries CONTINUOUS parameters (openness, pillar density, canopy amount, height
    /// scale) — there are no discrete zone types. Radii are scattered non-uniformly so "no two zones
    /// alike, no uniform sizes" holds. The graph is guaranteed connected (a minimum spanning pass
    /// over the chambers) so the carved cavity is one connected, walkable space.
    ///
    /// Fully deterministic: everything is driven by a single <see cref="System.Random"/> seeded off
    /// <see cref="FeatureContext.Seed"/>.
    /// </summary>
    public static class ArchingCavePlanner
    {
        /// <summary>
        /// Builds the zone graph. <paramref name="footprint"/> is the local-XZ region the zones must
        /// stay inside (the feature's footprint polygon bounds). <paramref name="floorY"/> is the
        /// local-Y the walkable floor sits at.
        /// </summary>
        public static ArchingCavePlan Plan(
            ArchingCaveSettings settings, FeatureContext context, Bounds footprint, float floorY)
        {
            var plan = new ArchingCavePlan
            {
                FloorY = floorY,
                // Solid rock ceiling height — canopied chambers roof over at this Y; open chambers
                // carve straight past it. Scaled by the overall-height knob.
                CeilingY = floorY + settings.ceilingHeight * settings.overallHeight,
            };
            var rng = new System.Random(context.Seed * 31 + 4801);

            Vector2 centre = new Vector2(footprint.center.x, footprint.center.z);
            Vector2 half = new Vector2(footprint.extents.x, footprint.extents.z);
            float minHalf = Mathf.Max(4f, Mathf.Min(half.x, half.y));

            // Average zone radius and the non-uniform spread band around it.
            float avgRadius = minHalf * settings.zoneRadiusFraction;
            float radiusSpread = avgRadius * settings.zoneSizeVariation;

            int target = Mathf.Clamp(settings.zoneCount, 3, 24);

            // --- 1) Seed the first zone near the footprint centre ----------------------------------
            plan.Zones.Add(MakeZone(0,
                centre + RandomInDisc(rng, minHalf * 0.25f),
                SampleRadius(rng, avgRadius, radiusSpread), rng, context.Seed));

            // --- 2) Guided random walk: each new zone hangs off an existing one --------------------
            int attempts = 0;
            int maxAttempts = target * 30;
            while (plan.Zones.Count < target && attempts < maxAttempts)
            {
                attempts++;

                // Bias toward the most-recent zones so the site slithers into an organic sprawl
                // rather than a fat blob (same trick as CaveGraphGenerator).
                int anchorIdx = plan.Zones.Count - 1
                    - (int)(rng.NextDouble() * rng.NextDouble() * plan.Zones.Count);
                anchorIdx = Mathf.Clamp(anchorIdx, 0, plan.Zones.Count - 1);
                var anchor = plan.Zones[anchorIdx];

                float newRadius = SampleRadius(rng, avgRadius, radiusSpread);

                // Place the new zone so its corridor is short enough to walk: between just-touching
                // and a generous reach, in a random compass direction.
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float gap = Mathf.Lerp(anchor.Radius + newRadius * 0.4f,
                                       anchor.Radius + newRadius + avgRadius * 1.2f,
                                       (float)rng.NextDouble());
                Vector2 candidate = anchor.Center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * gap;

                // Keep the zone (its full radius) inside the footprint.
                if (Mathf.Abs(candidate.x - centre.x) + newRadius > half.x) continue;
                if (Mathf.Abs(candidate.y - centre.y) + newRadius > half.y) continue;

                // Reject zones that sit almost on top of an existing one — corridors do the joining,
                // not heavy overlap.
                if (TooClose(plan.Zones, candidate, newRadius * 0.55f)) continue;

                plan.Zones.Add(MakeZone(plan.Zones.Count, candidate, newRadius, rng, context.Seed));
            }

            // --- 3) Connect the graph: nearest-neighbour edges + connectivity guarantee ------------
            ConnectGraph(plan, rng);

            return plan;
        }

        // -------------------------------------------------------------------------
        // Zone construction — all continuous parameters, deterministic per zone.
        // -------------------------------------------------------------------------

        /// <summary>Builds one chamber with freely-blended continuous parameters. Openness drives
        /// canopy inversely (an open chamber's ceiling is carved away, a closed one keeps a full roof)
        /// so the site is a genuine mix of open-to-sky and canopied chambers.</summary>
        static ArchingCaveZone MakeZone(int id, Vector2 center, float radius, System.Random rng, int seed)
        {
            float openness = (float)rng.NextDouble();
            return new ArchingCaveZone
            {
                Id = id,
                Center = center,
                Radius = radius,
                Openness = openness,
                // Pillar density varies independently of openness so a dense grove of emergent
                // columns can be open OR canopied — no systematic coupling between the knobs.
                PillarDensity = 0.25f + (float)rng.NextDouble() * 0.75f,
                // Canopy is the inverse of openness, softened, so closed chambers roof over and only
                // those get skylight holes punched through their surviving rock ceiling.
                CanopyAmount = Mathf.SmoothStep(0f, 1f, 1f - openness),
                HeightScale = 0.7f + (float)rng.NextDouble() * 0.8f,
            };
        }

        /// <summary>Samples a non-uniform zone radius around <paramref name="avg"/> within
        /// ±<paramref name="spread"/>, clamped to stay positive.</summary>
        static float SampleRadius(System.Random rng, float avg, float spread)
        {
            float r = avg + ((float)rng.NextDouble() * 2f - 1f) * spread;
            return Mathf.Max(avg * 0.35f, r);
        }

        // -------------------------------------------------------------------------
        // Graph connectivity
        // -------------------------------------------------------------------------

        /// <summary>
        /// Joins zones with corridors. Pass A links every zone to its two nearest neighbours; pass B
        /// runs a union-find and adds the cheapest inter-component edge until the whole graph is one
        /// connected component — guaranteeing the ground floor is traversable end to end.
        /// </summary>
        static void ConnectGraph(ArchingCavePlan plan, System.Random rng)
        {
            int n = plan.Zones.Count;
            var edges = new HashSet<long>();

            // Pass A — two nearest neighbours each.
            for (int i = 0; i < n; i++)
            {
                foreach (int j in TwoNearest(plan, i))
                    TryAddEdge(plan, edges, i, j);
            }

            // Pass B — union-find connectivity guarantee.
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }
            foreach (var e in plan.Edges) Union(e.FromZone, e.ToZone);

            int safety = n * n;
            while (safety-- > 0)
            {
                // Find the closest pair spanning two different components.
                float bestD = float.MaxValue;
                int bestA = -1, bestB = -1;
                for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (Find(i) == Find(j)) continue;
                    float d = Vector2.Distance(plan.Zones[i].Center, plan.Zones[j].Center);
                    if (d < bestD) { bestD = d; bestA = i; bestB = j; }
                }
                if (bestA < 0) break;   // fully connected
                TryAddEdge(plan, edges, bestA, bestB);
                Union(bestA, bestB);
            }
        }

        /// <summary>Indices of the two zones nearest to <paramref name="self"/>.</summary>
        static List<int> TwoNearest(ArchingCavePlan plan, int self)
        {
            var list = new List<(int idx, float d)>();
            for (int i = 0; i < plan.Zones.Count; i++)
            {
                if (i == self) continue;
                list.Add((i, Vector2.Distance(plan.Zones[self].Center, plan.Zones[i].Center)));
            }
            list.Sort((a, b) => a.d.CompareTo(b.d));
            var result = new List<int>();
            for (int i = 0; i < Mathf.Min(2, list.Count); i++) result.Add(list[i].idx);
            return result;
        }

        static void TryAddEdge(ArchingCavePlan plan, HashSet<long> edges, int a, int b)
        {
            if (a == b) return;
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!edges.Add(key)) return;
            plan.Edges.Add(new ArchingCaveEdge { FromZone = a, ToZone = b });
        }

        static bool TooClose(List<ArchingCaveZone> zones, Vector2 p, float minDist)
        {
            foreach (var z in zones)
                if (Vector2.Distance(z.Center, p) < minDist) return true;
            return false;
        }

        static Vector2 RandomInDisc(System.Random rng, float radius)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float r = radius * Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
        }
    }
}
