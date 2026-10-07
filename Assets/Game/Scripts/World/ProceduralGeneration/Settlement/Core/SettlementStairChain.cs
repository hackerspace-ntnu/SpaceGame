// A chain of stair flights that climbs several terraces at once: one flight per level, a landing
// after every few flights, all on one straight line down the hill along the street's tangent where
// its level changes. The geometry is measured from the chain's top (the top flight's origin, at the
// upper terrace's height) along its forward, downhill direction; Plan finds every chain of a street
// network, the terrace field sculpts the ground under them and SettlementStreetPaver places the pieces.
// Chains are never stretched: a fit factor would break the cheeks' and copings' continuity.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementStairChain
    {
        // How far the top of a chain reaches back into the upper terrace past the level change, covering
        // the terrace line's half-cell uncertainty so no gap opens behind it.
        public const float StairTuck = 0.5f;

        public readonly struct Piece
        {
            public readonly bool isLanding;
            /// <summary>Metres from the chain's top along its forward direction.</summary>
            public readonly float along;
            /// <summary>Metres below the chain's top.</summary>
            public readonly float drop;

            public Piece(bool isLanding, float along, float drop)
            {
                this.isLanding = isLanding;
                this.along = along;
                this.drop = drop;
            }
        }

        /// <summary>One chain of a street network, where it stands and what it takes up.</summary>
        public sealed class Placement
        {
            public int street;
            public int levels;
            public GameObject flight;
            /// <summary>World XZ of the top flight's origin, and the chain's downhill direction.</summary>
            public Vector2 top, forward;
            public float topY;
            /// <summary>Top to foot, horizontally.</summary>
            public float length;
            /// <summary>Half the width of the corridor the chain's ground is ramped over.</summary>
            public float halfWidth;
            /// <summary>The stretch of its street (arc from, to) the chain and its head take up.</summary>
            public Vector2 occupied;
        }

        public static int LandingsIn(int levels, int flightsPerLanding) =>
            levels <= 1 ? 0 : (levels - 1) / Mathf.Max(1, flightsPerLanding);

        /// <summary>Horizontal length of a chain climbing <paramref name="levels"/> terraces, from its top to its foot.</summary>
        public static float Length(int levels, float flightRun, float landingRun, int flightsPerLanding) =>
            levels * flightRun + LandingsIn(levels, flightsPerLanding) * landingRun;

        public static float Length(SettlementStreetStyle style, int levels) =>
            Length(levels, style.flightRun, style.landingRun, style.flightsPerLanding);

        /// <summary>Every flight and landing, top first.</summary>
        public static List<Piece> Pieces(int levels, float flightRun, float landingRun, float stepHeight, int flightsPerLanding)
        {
            var pieces = new List<Piece>();
            float along = 0f, drop = 0f;
            for (int i = 0; i < levels; i++)
            {
                pieces.Add(new Piece(false, along, drop));
                along += flightRun;
                drop += stepHeight;
                if ((i + 1) % Mathf.Max(1, flightsPerLanding) != 0 || i == levels - 1) continue;
                pieces.Add(new Piece(true, along, drop));
                along += landingRun;
            }
            return pieces;
        }

        public static List<Piece> Pieces(SettlementStreetStyle style, int levels) =>
            Pieces(levels, style.flightRun, style.landingRun, style.stepHeight, style.flightsPerLanding);

        /// <summary>
        /// Height of the chain's walking line <paramref name="along"/> metres from its top, below the top:
        /// down each flight's nosings, level across each landing. Before the top it is 0, past the foot
        /// the whole climb.
        /// </summary>
        public static float DropAt(float along, int levels, float flightRun, float landingRun, float stepHeight, int flightsPerLanding)
        {
            if (along <= 0f) return 0f;
            foreach (var piece in Pieces(levels, flightRun, landingRun, stepHeight, flightsPerLanding))
            {
                float run = piece.isLanding ? landingRun : flightRun;
                if (along > piece.along + run) continue;
                return piece.isLanding ? piece.drop : piece.drop + (along - piece.along) / flightRun * stepHeight;
            }
            return levels * stepHeight;
        }

        /// <summary>
        /// The flight a stretch of street climbs by: wide on a road or a slab street more than one slab wide,
        /// narrow on one slab, else whichever there is. Null on a stone path, which never has stairs.
        /// </summary>
        public static GameObject FlightFor(SettlementStreetStyle style, int order, SettlementStreetStyle.StreetSurface surface)
        {
            if (surface == SettlementStreetStyle.StreetSurface.StonePath) return null;
            bool wide = surface == SettlementStreetStyle.StreetSurface.Road || SettlementStreetPaver.SlabsAcross(style, order) > 1;
            return wide ? style.flightWide ? style.flightWide : style.flightNarrow
                        : style.flightNarrow ? style.flightNarrow : style.flightWide;
        }

        /// <summary>A chain at every level change of every street's road and slab stretches, standing on the lower side of the change.</summary>
        /// <param name="measure">A prefab's local footprint (x = X, y = Z).</param>
        /// <param name="sections">Every street's surfaces (<see cref="SettlementStreetSurfaces.Assign"/>).</param>
        public static List<Placement> Plan(SettlementStreetStyle style, SettlementStreetNetwork network,
                                           List<SettlementTerraceField.StreetProfile> profiles, SettlementTerraceField field,
                                           Func<GameObject, Rect> measure, List<SettlementStreetSurfaces.Section>[] sections)
        {
            var chains = new List<Placement>();
            for (int s = 0; s < network.streets.Count; s++)
            {
                var street = network.streets[s];
                foreach (var step in profiles[s].Steps())
                {
                    GameObject flight = FlightFor(style, street.order, SettlementStreetSurfaces.At(sections[s], step.arc));
                    if (flight == null) continue;
                    Rect flightRect = measure(flight);
                    // The chain's head reaches back past its top by the flight's tuck tread or the newel pillars, whichever is deeper.
                    float head = Mathf.Max(-flightRect.yMin, style.wallPillar ? -measure(style.wallPillar).yMin : 0f);
                    int levels = Mathf.Abs(step.Rise);
                    Vector2 uphill = street.TangentAt(step.arc) * Math.Sign(step.Rise);
                    float topArc = step.arc + StairTuck * Math.Sign(step.Rise);
                    float length = Length(style, levels);
                    chains.Add(new Placement
                    {
                        street = s,
                        levels = levels,
                        flight = flight,
                        top = street.PointAt(step.arc) + uphill * StairTuck,
                        forward = -uphill,
                        topY = field.HeightOf(Mathf.Max(step.fromLevel, step.toLevel)),
                        length = length,
                        halfWidth = Mathf.Max(street.halfWidth, flightRect.width * 0.5f),
                        occupied = step.Rise > 0
                            ? new Vector2(topArc - length, topArc + head)
                            : new Vector2(topArc - head, topArc + length),
                    });
                }
            }
            return chains;
        }
    }
}
