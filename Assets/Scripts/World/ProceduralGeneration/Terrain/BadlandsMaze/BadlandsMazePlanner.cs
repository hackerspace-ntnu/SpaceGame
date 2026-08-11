using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// STAGE 1 — THE CHANNEL NETWORK PLAN.
/// ====================================================================================
///
/// Lays out the top-level structure of the BadlandsMaze BEFORE any rock is shaped — "first
/// top-order structures, then placing the right stuff at the right places", exactly as the
/// project lead mandated for ArchingCave. This is the direct analogue of
/// <see cref="ArchingCavePlanner"/>: a guided random walk drops open chambers one at a time, each
/// at a random direction+distance from an existing chamber, then connects them into ONE connected
/// graph of walkable channels.
///
/// The INVERSION from ArchingCave: there, the graph IS the walkable space and rock fills the gaps.
/// Here, the chambers and channels are the carved void — the "wide river system that ran through"
/// — and the leftover rock between them becomes the maze. So this planner is laying out where the
/// water cut, not where the rock stands.
///
/// Every chamber radius is scattered non-uniformly so "no two alike, no uniform sizes" holds, and
/// every channel gets a meander control point so the river beds snake. The graph is guaranteed
/// connected (a union-find pass) so the player can walk the whole maze end to end.
///
/// Fully deterministic: everything is driven by a single <see cref="System.Random"/> seeded off
/// <see cref="FeatureContext.Seed"/>.
/// </summary>
public static class BadlandsMazePlanner
{
    /// <summary>
    /// Builds the channel graph. <paramref name="footprint"/> is the local-XZ region the maze must
    /// stay inside (the feature's footprint polygon bounds). <paramref name="floorY"/> is the
    /// local-Y the walkable channel floor sits at; <paramref name="rimY"/> the surrounding terrain
    /// top.
    /// </summary>
    public static BadlandsMazePlan Plan(
        BadlandsMazeSettings settings, FeatureContext context,
        Bounds footprint, float floorY, float rimY)
    {
        var plan = new BadlandsMazePlan { FloorY = floorY, RimY = rimY };
        var rng = new System.Random(context.Seed * 31 + 17239);

        Vector2 centre = new Vector2(footprint.center.x, footprint.center.z);
        Vector2 half = new Vector2(footprint.extents.x, footprint.extents.z);
        float minHalf = Mathf.Max(4f, Mathf.Min(half.x, half.y));

        // Average chamber radius and the non-uniform spread band around it.
        float avgRadius = minHalf * settings.chamberRadiusFraction;
        float radiusSpread = avgRadius * settings.chamberSizeVariation;

        int target = Mathf.Clamp(settings.chamberCount, 4, 30);

        // --- 1) Seed the first chamber near the footprint centre -------------------------------
        plan.Chambers.Add(new BadlandsMazeChamber
        {
            Id = 0,
            Center = centre + RandomInDisc(rng, minHalf * 0.2f),
            Radius = SampleRadius(rng, avgRadius, radiusSpread),
        });

        // --- 2) Guided random walk: each new chamber hangs off an existing one -----------------
        int attempts = 0;
        int maxAttempts = target * 30;
        while (plan.Chambers.Count < target && attempts < maxAttempts)
        {
            attempts++;

            // Bias toward the most-recent chambers so the maze slithers into an organic sprawl
            // rather than a fat blob (same trick as ArchingCavePlanner / CaveGraphGenerator).
            int anchorIdx = plan.Chambers.Count - 1
                - (int)(rng.NextDouble() * rng.NextDouble() * plan.Chambers.Count);
            anchorIdx = Mathf.Clamp(anchorIdx, 0, plan.Chambers.Count - 1);
            var anchor = plan.Chambers[anchorIdx];

            float newRadius = SampleRadius(rng, avgRadius, radiusSpread);

            // Place the new chamber so the channel between them is a walkable length: from the
            // chambers nearly touching out to a generous reach, in a random compass direction.
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float gap = Mathf.Lerp(anchor.Radius + newRadius + avgRadius * 0.4f,
                                   anchor.Radius + newRadius + avgRadius * 2.4f,
                                   (float)rng.NextDouble());
            Vector2 candidate = anchor.Center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * gap;

            // Keep the chamber (its full radius) inside the footprint.
            if (Mathf.Abs(candidate.x - centre.x) + newRadius > half.x) continue;
            if (Mathf.Abs(candidate.y - centre.y) + newRadius > half.y) continue;

            // Reject chambers that sit almost on top of an existing one — channels do the joining,
            // not heavy overlap (overlapping carves would merge the maze into one big void).
            if (TooClose(plan.Chambers, candidate, newRadius * 0.9f)) continue;

            plan.Chambers.Add(new BadlandsMazeChamber
            {
                Id = plan.Chambers.Count,
                Center = candidate,
                Radius = newRadius,
            });
        }

        // --- 3) Connect the graph: nearest-neighbour edges + connectivity guarantee ------------
        ConnectGraph(plan, settings, rng);

        return plan;
    }

    /// <summary>Samples a non-uniform chamber radius around <paramref name="avg"/> within
    /// ±<paramref name="spread"/>, clamped to stay positive.</summary>
    static float SampleRadius(System.Random rng, float avg, float spread)
    {
        float r = avg + ((float)rng.NextDouble() * 2f - 1f) * spread;
        return Mathf.Max(avg * 0.4f, r);
    }

    // -------------------------------------------------------------------------
    // Graph connectivity
    // -------------------------------------------------------------------------

    /// <summary>
    /// Joins chambers with channels. Pass A links every chamber to its two nearest neighbours
    /// (giving the maze loops and dead-ends); pass B runs a union-find and adds the cheapest
    /// inter-component channel until the whole graph is one connected component — guaranteeing the
    /// player can walk the whole maze end to end. Each channel gets a meander mid control point.
    /// </summary>
    static void ConnectGraph(BadlandsMazePlan plan, BadlandsMazeSettings settings, System.Random rng)
    {
        int n = plan.Chambers.Count;
        var edges = new HashSet<long>();

        // Pass A — two nearest neighbours each.
        for (int i = 0; i < n; i++)
        {
            foreach (int j in TwoNearest(plan, i))
                TryAddChannel(plan, settings, edges, i, j, rng);
        }

        // Pass B — union-find connectivity guarantee.
        int[] parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[ra] = rb; }
        foreach (var e in plan.Channels) Union(e.FromChamber, e.ToChamber);

        int safety = n * n;
        while (safety-- > 0)
        {
            float bestD = float.MaxValue;
            int bestA = -1, bestB = -1;
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (Find(i) == Find(j)) continue;
                float d = Vector2.Distance(plan.Chambers[i].Center, plan.Chambers[j].Center);
                if (d < bestD) { bestD = d; bestA = i; bestB = j; }
            }
            if (bestA < 0) break;   // fully connected
            TryAddChannel(plan, settings, edges, bestA, bestB, rng);
            Union(bestA, bestB);
        }
    }

    /// <summary>Indices of the two chambers nearest to <paramref name="self"/>.</summary>
    static List<int> TwoNearest(BadlandsMazePlan plan, int self)
    {
        var list = new List<(int idx, float d)>();
        for (int i = 0; i < plan.Chambers.Count; i++)
        {
            if (i == self) continue;
            list.Add((i, Vector2.Distance(plan.Chambers[self].Center, plan.Chambers[i].Center)));
        }
        list.Sort((a, b) => a.d.CompareTo(b.d));
        var result = new List<int>();
        for (int i = 0; i < Mathf.Min(2, list.Count); i++) result.Add(list[i].idx);
        return result;
    }

    /// <summary>Adds a channel between two chambers if not already present, computing its meander
    /// mid control point: the segment midpoint pushed sideways by a seeded amount scaled by the
    /// <see cref="BadlandsMazeSettings.channelMeander"/> knob, so the river bed snakes.</summary>
    static void TryAddChannel(
        BadlandsMazePlan plan, BadlandsMazeSettings settings, HashSet<long> edges,
        int a, int b, System.Random rng)
    {
        if (a == b) return;
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (!edges.Add(key)) return;

        Vector2 pa = plan.Chambers[a].Center;
        Vector2 pb = plan.Chambers[b].Center;
        Vector2 mid = (pa + pb) * 0.5f;

        // Perpendicular meander offset — pushes the mid control point off the straight line.
        Vector2 dir = (pb - pa);
        float len = Mathf.Max(0.01f, dir.magnitude);
        Vector2 perp = new Vector2(-dir.y, dir.x) / len;
        float meander = ((float)rng.NextDouble() * 2f - 1f) * settings.channelMeander * len * 0.35f;
        mid += perp * meander;

        plan.Channels.Add(new BadlandsMazeChannel
        {
            FromChamber = a,
            ToChamber = b,
            Mid = mid,
            HalfWidth = settings.channelWidth * 0.5f * (0.8f + (float)rng.NextDouble() * 0.4f),
        });
    }

    static bool TooClose(List<BadlandsMazeChamber> chambers, Vector2 p, float minDist)
    {
        foreach (var c in chambers)
            if (Vector2.Distance(c.Center, p) < minDist) return true;
        return false;
    }

    static Vector2 RandomInDisc(System.Random rng, float radius)
    {
        float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float r = radius * Mathf.Sqrt((float)rng.NextDouble());
        return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
    }
}
