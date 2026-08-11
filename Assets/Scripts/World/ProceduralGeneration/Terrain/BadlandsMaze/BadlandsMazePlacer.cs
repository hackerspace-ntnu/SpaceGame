using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ====================================================================================
/// STAGE 2 — STRUCTURE PLACEMENT.
/// ====================================================================================
///
/// Given the planned channel graph from <see cref="BadlandsMazePlanner"/>, this decides WHAT goes
/// WHERE in the surviving rock: it scatters MESA anchors into the rock that sits BETWEEN the
/// carved channels (each with its own top height and overhang lean, so the skyline is jagged and
/// the walls undercut), and it litters BOULDERS across the open channel floors.
///
/// The analogue is <see cref="ArchingCavePlacer"/>, but inverted: ArchingCave placed pillars
/// INSIDE its zones; BadlandsMaze places mesas in the rock BETWEEN its channels — a mesa anchor is
/// only kept if it lands clear of every carved chamber and channel, i.e. on solid rock.
///
/// All placement is deterministic off <see cref="FeatureContext.Seed"/> and non-uniform — mesa
/// positions are jittered, heights vary continuously, boulder sizes pick per-rock. The output is a
/// concrete list of primitives appended to the <see cref="BadlandsMazePlan"/> for the SDF builder.
/// </summary>
public static class BadlandsMazePlacer
{
    /// <summary>Fills the plan's Mesas / Boulders lists from its channel graph.</summary>
    public static void Place(
        BadlandsMazePlan plan, BadlandsMazeSettings settings, FeatureContext context, Bounds footprint)
    {
        var rng = new System.Random(context.Seed * 31 + 55127);
        PlaceMesas(plan, settings, footprint, rng);
        if (settings.enableBoulders)
            PlaceBoulders(plan, settings, rng);
    }

    // -------------------------------------------------------------------------
    // Mesas — the surviving rock lumps between the channels.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scatters mesa anchors across the footprint, keeping only those that land on SOLID ROCK —
    /// clear of every carved chamber and channel. Each kept mesa picks a continuous top height
    /// (jagged skyline) and a salt; its overhanging silhouette is produced later by the shared
    /// <see cref="RockBodyProfile"/> model, the same one <see cref="MesaFeature"/> uses.
    /// </summary>
    static void PlaceMesas(
        BadlandsMazePlan plan, BadlandsMazeSettings settings, Bounds footprint, System.Random rng)
    {
        Vector2 centre = new Vector2(footprint.center.x, footprint.center.z);
        Vector2 half = new Vector2(footprint.extents.x, footprint.extents.z);
        float minHalf = Mathf.Max(4f, Mathf.Min(half.x, half.y));

        // Mesa radius band — comfortably larger than a channel half-width so the maze walls read
        // as substantial massif, not thin fins.
        float mesaMin = Mathf.Max(settings.channelWidth * 0.6f, minHalf * 0.06f);
        float mesaMax = minHalf * 0.2f;

        // Count from footprint area / nominal spacing — a dense field of overlapping lumps that
        // SmoothMin-blend into one cohesive massif riddled with channels.
        float spacing = (mesaMin + mesaMax);
        float area = footprint.size.x * footprint.size.z;
        int target = Mathf.Clamp(Mathf.RoundToInt(area / (spacing * spacing)), 6, 220);

        int attempts = target * 14;
        var placed = new List<Vector2>();
        while (placed.Count < target && attempts-- > 0)
        {
            Vector2 p = new Vector2(
                centre.x + ((float)rng.NextDouble() * 2f - 1f) * half.x,
                centre.y + ((float)rng.NextDouble() * 2f - 1f) * half.y);

            float radius = Mathf.Lerp(mesaMin, mesaMax, (float)rng.NextDouble());

            // Reject anchors whose centre sits inside (or barely outside) a carved void — those
            // would be eroded away and contribute nothing.
            if (CarvedDistance(plan, settings, p) < radius * 0.25f) continue;

            // Loose minimum separation so lumps still overlap and blend but are not all stacked.
            bool clash = false;
            foreach (var q in placed)
                if (Vector2.Distance(p, q) < radius * 0.6f) { clash = true; break; }
            if (clash) continue;

            // Per-mesa top height — varied for a jagged skyline.
            float heightT = (float)rng.NextDouble();
            float top = plan.RimY + settings.massifHeight
                        * Mathf.Lerp(1f - settings.massifHeightVariation, 1f, heightT);

            placed.Add(p);
            plan.Mesas.Add(new BadlandsMazeMesa
            {
                Center = p,
                Radius = radius,
                TopY = top,
                Salt = rng.Next(),
            });
        }
    }

    // -------------------------------------------------------------------------
    // Boulders — small rocks scattered on the channel floors.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scatters boulders and small rocks across the open channel floors. A boulder is only kept if
    /// it lands INSIDE a carved void (a chamber or channel) — i.e. on the walkable floor where the
    /// player would actually see it — and clear enough of the void edge that it does not fuse into
    /// the mesa wall. Sizes are biased small with a long tail of large boulders.
    /// </summary>
    static void PlaceBoulders(
        BadlandsMazePlan plan, BadlandsMazeSettings settings, System.Random rng)
    {
        // Total carved-floor area (chambers + channels) drives the boulder count.
        float floorArea = 0f;
        for (int i = 0; i < plan.Chambers.Count; i++)
        {
            float r = plan.Chambers[i].Radius;
            floorArea += Mathf.PI * r * r;
        }
        for (int i = 0; i < plan.Channels.Count; i++)
        {
            var ch = plan.Channels[i];
            float len = ApproxChannelLength(plan, ch);
            floorArea += len * ch.HalfWidth * 2f;
        }

        // One boulder per ~this-many square metres of floor, scaled by the density knob.
        float perBoulderArea = 90f / Mathf.Max(0.05f, settings.boulderDensity);
        int count = Mathf.Clamp(Mathf.RoundToInt(floorArea / perBoulderArea), 0, 600);

        // Sample bounds from the chambers' overall extent.
        int attempts = count * 16;
        Bounds floorBounds = ComputeFloorBounds(plan);

        while (plan.Boulders.Count < count && attempts-- > 0)
        {
            Vector2 p = new Vector2(
                Mathf.Lerp(floorBounds.min.x, floorBounds.max.x, (float)rng.NextDouble()),
                Mathf.Lerp(floorBounds.min.z, floorBounds.max.z, (float)rng.NextDouble()));

            // Keep boulders on the walkable floor: inside a carved void by at least a small margin.
            float carved = CarvedDistance(plan, settings, p);   // <0 = inside a void
            if (carved > -1.5f) continue;

            // Size: biased small (product of two randoms), with the band from the settings.
            float sizeT = (float)rng.NextDouble() * (float)rng.NextDouble();
            float radius = Mathf.Lerp(settings.boulderSize.x, settings.boulderSize.y, sizeT);

            // Skip if the boulder would not fit clear of the void edge (touching the wall is fine,
            // poking through it is not — it would just merge into the mesa).
            if (-carved < radius * 0.4f) continue;

            // Squash so boulders are not perfect spheres — flatter or taller at random.
            Vector3 squash = new Vector3(
                0.8f + (float)rng.NextDouble() * 0.5f,
                0.55f + (float)rng.NextDouble() * 0.7f,
                0.8f + (float)rng.NextDouble() * 0.5f);

            // Rest the boulder on the floor: centre half-sunk so it beds into the ground.
            float centreY = plan.FloorY + radius * squash.y * 0.55f;

            plan.Boulders.Add(new BadlandsMazeBoulder
            {
                Center = new Vector3(p.x, centreY, p.y),
                Radius = radius,
                Squash = squash,
                Salt = rng.Next(),
            });
        }
    }

    // -------------------------------------------------------------------------
    // Carved-void geometry helpers — shared "where did the river cut" math.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signed XZ distance to the carved void network (chambers + channels). Negative = inside a
    /// carved void (the walkable floor), positive = on solid rock. This is the 2D footprint of the
    /// subtraction the SDF performs in 3D, so the placer and the SDF agree on what is rock.
    /// </summary>
    public static float CarvedDistance(BadlandsMazePlan plan, BadlandsMazeSettings settings, Vector2 p)
    {
        float d = 1e6f;
        for (int i = 0; i < plan.Chambers.Count; i++)
        {
            var c = plan.Chambers[i];
            d = Mathf.Min(d, Vector2.Distance(p, c.Center) - c.Radius);
        }
        for (int i = 0; i < plan.Channels.Count; i++)
        {
            var ch = plan.Channels[i];
            d = Mathf.Min(d, ChannelDistance(plan, ch, p) - ch.HalfWidth);
        }
        return d;
    }

    /// <summary>Distance from <paramref name="p"/> to a channel's meandering centre-line (a
    /// quadratic Bezier through its mid control point), sampled as a poly-line.</summary>
    public static float ChannelDistance(BadlandsMazePlan plan, BadlandsMazeChannel ch, Vector2 p)
    {
        Vector2 a = plan.Chambers[ch.FromChamber].Center;
        Vector2 b = plan.Chambers[ch.ToChamber].Center;
        const int Segments = 12;
        float best = 1e6f;
        Vector2 prev = a;
        for (int i = 1; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            Vector2 cur = Bezier(a, ch.Mid, b, t);
            best = Mathf.Min(best, DistToSegment(p, prev, cur));
            prev = cur;
        }
        return best;
    }

    static float ApproxChannelLength(BadlandsMazePlan plan, BadlandsMazeChannel ch)
    {
        Vector2 a = plan.Chambers[ch.FromChamber].Center;
        Vector2 b = plan.Chambers[ch.ToChamber].Center;
        const int Segments = 8;
        float len = 0f;
        Vector2 prev = a;
        for (int i = 1; i <= Segments; i++)
        {
            Vector2 cur = Bezier(a, ch.Mid, b, i / (float)Segments);
            len += Vector2.Distance(prev, cur);
            prev = cur;
        }
        return len;
    }

    static Bounds ComputeFloorBounds(BadlandsMazePlan plan)
    {
        var b = new Bounds(new Vector3(plan.Chambers[0].Center.x, plan.FloorY, plan.Chambers[0].Center.y), Vector3.zero);
        for (int i = 0; i < plan.Chambers.Count; i++)
        {
            var c = plan.Chambers[i];
            b.Encapsulate(new Vector3(c.Center.x - c.Radius, plan.FloorY, c.Center.y - c.Radius));
            b.Encapsulate(new Vector3(c.Center.x + c.Radius, plan.FloorY, c.Center.y + c.Radius));
        }
        return b;
    }

    static Vector2 Bezier(Vector2 a, Vector2 m, Vector2 b, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * m + t * t * b;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        => Vector2.Distance(p, ClosestOnSegment(p, a, b));

    static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = Vector2.Dot(ab, ab);
        float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
        return a + ab * t;
    }
}
