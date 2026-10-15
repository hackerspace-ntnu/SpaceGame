// A stone path: loose stepping stones scattered down a street's corridor rather than a paved road.
// The walk steps along the centre line by a jittered spacing; each stone strays sideways by a
// centre-weighted amount (the mean of two uniform draws), so the stones meander instead of filling a
// band, and now and then a second one sits beside it. A stone that would lie too close to another, or
// on anything already laid, gets a couple of new sideways draws and is otherwise left out -- which is
// also how two stone paths merge where they meet, and how a stone path stops at a road's kerb. At a
// dead end the last few thin out and shrink. Every draw comes from the rng passed in, in order, so the
// same seed always scatters the same stones. Pure placement; SettlementStreetPaver grounds and spawns them.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementStonePath
    {
        public readonly struct Stone
        {
            public readonly int piece;
            public readonly Vector2 position;
            /// <summary>Degrees about +Y.</summary>
            public readonly float yaw;
            public readonly float scale;
            /// <summary>Metres sunk into the ground below the piece's own origin.</summary>
            public readonly float sink;
            public readonly SettlementFootprint footprint;

            public Stone(int piece, Vector2 position, float yaw, float scale, float sink, SettlementFootprint footprint)
            {
                this.piece = piece;
                this.position = position;
                this.yaw = yaw;
                this.scale = scale;
                this.sink = sink;
                this.footprint = footprint;
            }
        }

        /// <param name="line">The path's centre line, world XZ.</param>
        /// <param name="blocked">Arc intervals of the line (x = from, y = to) that get no stones, such as a flight of stairs.</param>
        /// <param name="halfWidth">Half the path's corridor width.</param>
        /// <param name="fadeAtEnd">Whether the line's far end is a dead end, where the stones thin out.</param>
        /// <param name="pieceFootprints">Each piece's measured local footprint (x = X, y = Z), by index into <c>settings.pieces</c>.</param>
        /// <param name="isClear">Whether a stone there lies clear of everything already laid.</param>
        public static List<Stone> Scatter(IReadOnlyList<Vector2> line, IReadOnlyList<Vector2> blocked, float halfWidth, bool fadeAtEnd,
                                          SettlementStreetStyle.StoneScatter settings, Rect[] pieceFootprints,
                                          Func<SettlementFootprint, bool> isClear, ref SettlementPlacementUtil.SeededRng rng)
        {
            var stones = new List<Stone>();
            float length = SettlementPolyline.Length(line);
            if (length <= 0f) return stones;
            float fadeLength = fadeAtEnd ? settings.fadeStones * settings.spacing : 0f;

            float s = settings.spacing * 0.5f;
            while (s < length)
            {
                float resume = BlockedUntil(s, blocked);
                if (resume > s)
                {
                    s = resume + settings.spacing * 0.5f;
                    continue;
                }

                float fade = fadeLength > 0f ? Mathf.Clamp01((s - (length - fadeLength)) / fadeLength) : 0f;
                float size = Mathf.Lerp(1f, settings.fadeScale, fade);
                Vector2 tangent = SettlementPolyline.TangentAt(line, s, settings.spacing);
                Vector2 side = new Vector2(tangent.y, -tangent.x);
                Vector2 centre = SettlementPolyline.PointAt(line, s);

                float first = TryStone(stones, centre, side, 0f, size, halfWidth, settings, pieceFootprints, isClear, ref rng);
                if (rng.NextChance(settings.pairChance))
                    TryStone(stones, centre, side, first >= 0f ? -1f : 1f, size, halfWidth, settings, pieceFootprints, isClear, ref rng);

                s += settings.spacing * (1f + settings.spacingJitter * (2f * rng.NextFloat01() - 1f)) / size;
            }
            return stones;
        }

        // Places one stone beside the centre line; returns its sideways offset (0 when none was placed).
        // `towards` forces the side: +1 or -1, 0 for either.
        private static float TryStone(List<Stone> stones, Vector2 centre, Vector2 side, float towards, float size, float halfWidth,
                                      SettlementStreetStyle.StoneScatter settings, Rect[] pieceFootprints,
                                      Func<SettlementFootprint, bool> isClear, ref SettlementPlacementUtil.SeededRng rng)
        {
            int piece = SettlementStreetStyle.WeightedPiece.PickIndex(settings.pieces, ref rng);
            if (piece < 0) return 0f;
            float yaw = rng.NextFloat01() * 360f;
            float scale = (1f + settings.scaleJitter * (2f * rng.NextFloat01() - 1f)) * size;
            float sink = rng.NextFloat01() * settings.sinkMax;

            for (int attempt = 0; attempt <= settings.retries; attempt++)
            {
                float lateral = halfWidth * settings.lateral * (rng.NextFloat01() + rng.NextFloat01() - 1f);
                if (towards != 0f) lateral = Mathf.Abs(lateral) * towards;
                Vector2 position = centre + side * lateral;
                SettlementFootprint footprint = FootprintOf(pieceFootprints[piece], position, yaw, scale);
                if (!ClearOf(stones, footprint, settings.minGap) || !isClear(footprint)) continue;
                stones.Add(new Stone(piece, position, yaw, scale, sink, footprint));
                return lateral;
            }
            return 0f;
        }

        public static SettlementFootprint FootprintOf(Rect local, Vector2 position, float yaw, float scale)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 offset = rotation * new Vector3(local.center.x * scale, 0f, local.center.y * scale);
            return new SettlementFootprint(position + new Vector2(offset.x, offset.z), local.size * scale, rotation);
        }

        private static bool ClearOf(List<Stone> stones, in SettlementFootprint footprint, float minGap)
        {
            foreach (var other in stones)
            {
                float reach = footprint.Circumradius + other.footprint.Circumradius + minGap;
                if ((footprint.center - other.footprint.center).sqrMagnitude > reach * reach) continue;
                if (SettlementFootprint.Gap(footprint, other.footprint) < minGap) return false;
            }
            return true;
        }

        // The end of the blocked interval holding `s`, or `s` itself when none does.
        private static float BlockedUntil(float s, IReadOnlyList<Vector2> blocked)
        {
            float until = s;
            foreach (var interval in blocked)
            {
                if (s >= interval.x && s <= interval.y) until = Mathf.Max(until, interval.y);
            }
            return until;
        }
    }
}
