// One place a plan segment can point at, gathered at runtime by SettlementSociety: a dwelling's door, a
// camp in the open, a spot a building or decoration brought along, or a trip point outside the settlement.
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    public sealed class SettlementPlace
    {
        public SettlementPlace(PlaceKind kind, SpotUse use, int group, int seatIndex, Vector3 position, Vector3? facePoint)
        {
            Kind = kind;
            Use = use;
            Group = group;
            SeatIndex = seatIndex;
            Position = position;
            FacePoint = facePoint;
        }

        public PlaceKind Kind { get; }

        /// <summary>What the spot is for; null for doors, camps and trip points.</summary>
        public SpotUse Use { get; }

        /// <summary>A post up a ladder: its worker is moved there rather than walking (<see cref="SpotUse.elevated"/>).</summary>
        public bool Elevated => Use != null && Use.elevated;

        /// <summary>The circle it belongs to; 0 = none.</summary>
        public int Group { get; }

        /// <summary>Which of the settlement's spots of the same use this is, in hierarchy order.</summary>
        public int SeatIndex { get; }

        public Vector3 Position { get; }

        /// <summary>What a resident holding here faces; null = keep heading.</summary>
        public Vector3? FacePoint { get; }
    }
}
