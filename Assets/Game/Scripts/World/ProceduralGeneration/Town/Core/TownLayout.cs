// Where everything goes, decided from a recipe and one integer.
//
// Pure: no scene, no Physics, no Terrain, no UnityEngine.Random, no GameObject. It is handed a
// clearance function so it can keep buildings off each other without knowing what a MeshFilter is,
// and it hands back a list of TownSlot. That is what makes the whole layout testable in EditMode —
// the same split SettlementSiteScore already uses, and the reason this is a separate file from
// TownGenerator rather than a region inside it.
//
// ONE System.Random threaded through every pass. RobotSettlementGenerator draws from global
// UnityEngine.Random and only seeds it when useSeed happens to be ticked, which is why two runs of
// the same town could differ; every other generator in this project threads a seeded rng and so
// does this.
//
// DRAW ORDER IS PART OF THE SEED. Sections are consumed in TownSection order, groups within a
// section in list order, instances within a group in order. Appending a group to the end of a
// section leaves everything drawn before it untouched; INSERTING one, reordering them, or adding a
// section moves every town that recipe has ever produced. Append only, the way the Clanker recipe's
// outriders were appended after the town had shipped.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Towns
{
    public static class TownLayout
    {
        /// <summary>
        /// Decide the whole town.
        ///
        /// <paramref name="clearanceOf"/> answers "how much room does this prefab need, as a
        /// radius". In play that is <see cref="TownPlacement.ClearanceRadius"/>; in a test it is a
        /// lambda, which is the point.
        /// </summary>
        public static List<TownSlot> Build(TownRecipe recipe, int seed, Func<GameObject, float> clearanceOf)
        {
            var slots = new List<TownSlot>();
            if (recipe == null) return slots;

            clearanceOf ??= _ => recipe.minStructureSpacing * 0.5f;

            var rng = new System.Random(seed);

            // Reserved discs, in the order they were claimed. Only clearance-reserving groups add
            // to this and only they test against it, so a guard standing in the square never pushes
            // a building aside.
            var reserved = new List<(Vector2 xz, float radius)>();

            PlaceCentrepiece(recipe, rng, clearanceOf, slots, reserved);

            foreach (TownSection section in EmitOrder)
            {
                List<TownGroup> groups = recipe.GroupsFor(section);
                if (groups == null) continue;

                for (int g = 0; g < groups.Count; g++)
                    PlaceGroup(recipe, section, g, groups[g], rng, clearanceOf, slots, reserved);
            }

            return slots;
        }

        /// <summary>Fixed, and part of the seed. Append only.</summary>
        private static readonly TownSection[] EmitOrder =
        {
            TownSection.Building,
            TownSection.Prop,
            TownSection.Scatter,
            TownSection.Person,
        };

        // ── Passes ───────────────────────────────────────────────────────────────

        private static void PlaceCentrepiece(TownRecipe recipe, System.Random rng,
                                             Func<GameObject, float> clearanceOf,
                                             List<TownSlot> slots,
                                             List<(Vector2, float)> reserved)
        {
            if (recipe.centrepiecePrefab == null) return;

            slots.Add(new TownSlot
            {
                Section = TownSection.Centrepiece,
                GroupIndex = -1,
                PrefabIndex = 0,
                LocalXZ = Vector2.zero,
                Yaw = Quarter(rng),
                Scale = 1f,
            });

            reserved.Add((Vector2.zero, clearanceOf(recipe.centrepiecePrefab)));
        }

        private static void PlaceGroup(TownRecipe recipe, TownSection section, int groupIndex,
                                       TownGroup group, System.Random rng,
                                       Func<GameObject, float> clearanceOf,
                                       List<TownSlot> slots,
                                       List<(Vector2, float)> reserved)
        {
            if (group == null || !group.HasPrefabs) return;

            int clusters = RangeInclusive(rng, group.count);
            if (clusters <= 0) return;

            Vector2 band = recipe.ResolveBand(group);

            // Organised placement only makes sense for things that claim space. Spacing scatter
            // evenly round a ring would produce a suspiciously tidy pile of scrap.
            bool organised = recipe.organizedLayout && group.reservesClearance;
            float ringRadius = (band.x + band.y) * 0.5f;
            float startAngle = organised ? (float)(rng.NextDouble() * 360.0) : 0f;
            float step = organised && clusters > 0 ? 360f / clusters : 0f;

            for (int c = 0; c < clusters; c++)
            {
                Vector2 centre = organised
                    ? RingSlot(rng, recipe, startAngle + step * c, ringRadius)
                    : PointInAnnulus(rng, band.x, band.y);

                int members = RangeInclusive(rng, group.clusterSize);
                if (members <= 0) members = 1;

                for (int m = 0; m < members; m++)
                {
                    // The cluster's first member sits on the centre; the rest scatter around it, so
                    // clusterSize 1 is exactly "one thing here" with no wasted draw.
                    Vector2 xz = m == 0
                        ? centre
                        : centre + InsideUnitCircle(rng) * group.clusterSpread;

                    int prefabIndex = PickPrefab(rng, group);
                    if (prefabIndex < 0) continue;

                    GameObject prefab = group.prefabs[prefabIndex];
                    float clearance = clearanceOf(prefab);

                    if (group.reservesClearance)
                    {
                        if (!Settle(ref xz, clearance, reserved, rng, recipe, band, organised,
                                    startAngle + step * c, ringRadius))
                            continue;

                        reserved.Add((xz, clearance));
                    }

                    slots.Add(new TownSlot
                    {
                        Section = section,
                        GroupIndex = groupIndex,
                        PrefabIndex = prefabIndex,
                        LocalXZ = xz,
                        Yaw = ResolveYaw(group.yaw, xz, rng),
                        Scale = ScaleFrom(rng, group.scaleRange),
                    });
                }
            }
        }

        /// <summary>
        /// Find this thing somewhere it does not overlap anything already claimed.
        ///
        /// Two strategies, matching how the position was chosen in the first place: an organised
        /// slot nudges along its own ring, keeping the ring readable; a scattered one re-rolls
        /// inside its band. Both give up after <c>maxPlacementAttempts</c> and the caller drops the
        /// instance — a town one building short is better than two buildings inside each other, and
        /// <c>Verify</c> reports the shortfall rather than letting it pass unmentioned.
        /// </summary>
        private static bool Settle(ref Vector2 xz, float clearance,
                                   List<(Vector2 xz, float radius)> reserved, System.Random rng,
                                   TownRecipe recipe, Vector2 band, bool organised,
                                   float slotAngle, float ringRadius)
        {
            if (!Overlaps(xz, clearance, reserved)) return true;

            int attempts = Mathf.Max(1, recipe.maxPlacementAttempts);
            for (int i = 1; i <= attempts; i++)
            {
                Vector2 candidate = organised
                    ? RingSlot(rng, recipe, slotAngle + i * (360f / (attempts + 1)), ringRadius)
                    : PointInAnnulus(rng, band.x, band.y);

                if (!Overlaps(candidate, clearance, reserved))
                {
                    xz = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool Overlaps(Vector2 xz, float radius, List<(Vector2 xz, float radius)> reserved)
        {
            for (int i = 0; i < reserved.Count; i++)
            {
                float min = reserved[i].radius + radius;
                if ((reserved[i].xz - xz).sqrMagnitude < min * min) return true;
            }

            return false;
        }

        // ── Dice ─────────────────────────────────────────────────────────────────
        //
        // Every draw goes through one of these, so the sequence is auditable. Nothing below may
        // call UnityEngine.Random.

        private static int PickPrefab(System.Random rng, TownGroup group)
        {
            // A null in the array is a configuration error that Verify refuses outright, so the
            // only job here is to not crash on one. The pick itself always spends its draw; the
            // caller then abandons that instance, which skips its yaw and scale draws — one more
            // reason a null is refused rather than tolerated.
            int index = rng.Next(0, group.prefabs.Length);
            return group.prefabs[index] != null ? index : -1;
        }

        private static int RangeInclusive(System.Random rng, Vector2Int range)
        {
            int min = Mathf.Min(range.x, range.y);
            int max = Mathf.Max(range.x, range.y);
            return rng.Next(min, max + 1);
        }

        private static float ScaleFrom(System.Random rng, Vector2 range)
        {
            float min = Mathf.Min(range.x, range.y);
            float max = Mathf.Max(range.x, range.y);
            if (min <= 0f && max <= 0f) return 1f;
            return Mathf.Approximately(min, max) ? min : Lerp(rng, min, max);
        }

        /// <summary>Uniform in AREA, not in radius — otherwise everything crowds the inner edge.</summary>
        private static Vector2 PointInAnnulus(System.Random rng, float minR, float maxR)
        {
            float lo = Mathf.Min(minR, maxR);
            float hi = Mathf.Max(minR, maxR);
            float r = Mathf.Sqrt(Lerp(rng, lo * lo, hi * hi));
            float a = (float)(rng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        private static Vector2 RingSlot(System.Random rng, TownRecipe recipe, float angleDeg, float radius)
        {
            float ang = (angleDeg + Lerp(rng, -recipe.slotAngularJitter, recipe.slotAngularJitter))
                        * Mathf.Deg2Rad;
            float r = radius + Lerp(rng, -recipe.slotRadialJitter, recipe.slotRadialJitter);
            return new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
        }

        private static Vector2 InsideUnitCircle(System.Random rng)
        {
            float r = Mathf.Sqrt((float)rng.NextDouble());
            float a = (float)(rng.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        private static float Quarter(System.Random rng) => rng.Next(0, 4) * 90f;

        private static float Lerp(System.Random rng, float a, float b) =>
            a + (b - a) * (float)rng.NextDouble();

        private static float ResolveYaw(TownYaw mode, Vector2 xz, System.Random rng)
        {
            switch (mode)
            {
                case TownYaw.Keep:
                    return 0f;

                case TownYaw.RandomQuarter:
                    return Quarter(rng);

                case TownYaw.FaceCentre:
                case TownYaw.FaceCentreSnapped:
                {
                    // Dead centre has no direction to face; fall back to a quarter turn. This one
                    // branch does spend a draw that the ordinary path does not, so the sequence
                    // depends on whether a slot landed on the origin — still fully determined by
                    // the seed, since the position came from the same rng, but worth knowing if you
                    // are ever reading the draw order off by hand.
                    if (xz.sqrMagnitude <= 0.0001f) return Quarter(rng);

                    float yaw = Mathf.Atan2(-xz.x, -xz.y) * Mathf.Rad2Deg;
                    return mode == TownYaw.FaceCentreSnapped
                        ? Mathf.Round(yaw / 90f) * 90f
                        : yaw;
                }

                default:
                    return (float)(rng.NextDouble() * 360.0);
            }
        }
    }
}
