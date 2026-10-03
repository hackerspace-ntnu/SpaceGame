// One place a plan segment can point at, gathered at runtime by SettlementSociety: a dwelling's door, a
// camp in the open, a spot a building or decoration brought along, or a trip point outside the settlement.
//
// A spot is authored where a person should be, but a resident can only stand on the NavMesh, so a spot place
// carries the point it was MEASURED to: Position is the validated stand point once the society has resolved
// it, and a spot whose stand point cannot be validated stays in the list — indices are the same on every
// machine and across a save — flagged unusable so the planner skips it. Nothing here is saved: it is derived.
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
            Resolved = use == null;
        }

        public PlaceKind Kind { get; }

        /// <summary>What the spot is for; null for doors, camps and trip points.</summary>
        public SpotUse Use { get; }

        /// <summary>A post up a ladder: its worker walks the ladder, or is moved there when no path leads up (<see cref="SpotUse.elevated"/>).</summary>
        public bool Elevated => Use != null && Use.elevated;

        /// <summary>The circle it belongs to; 0 = none.</summary>
        public int Group { get; }

        /// <summary>Which of the settlement's spots of the same use this is, in hierarchy order.</summary>
        public int SeatIndex { get; }

        /// <summary>Where a resident stands: the validated NavMesh stand point of a resolved spot, else the point the place was made at.</summary>
        public Vector3 Position { get; private set; }

        /// <summary>What a resident holding here faces; null = keep heading.</summary>
        public Vector3? FacePoint { get; }

        /// <summary>False for a spot whose stand point has not been measured yet (no NavMesh); everything else is born resolved.</summary>
        public bool Resolved { get; private set; }

        /// <summary>False when the spot was measured and cannot be stood at. An unresolved place counts as usable: not knowing is not a fault.</summary>
        public bool Usable { get; private set; } = true;

        /// <summary>A dwelling's doorway on the NavMesh, where a resident steps in and out of the building; null when none is walkable.</summary>
        public Vector3? Threshold { get; private set; }

        /// <summary>The spot is a sit: its sitter is lifted onto <see cref="SeatSurfaceY"/>.</summary>
        public bool Seated => Use != null && Use.seated;

        /// <summary>World height of the surface the sitter sits on (a bench, a stool, the floor).</summary>
        public float SeatSurfaceY { get; private set; }

        /// <summary>Records a measurement. True when the place became usable or unusable, which is what the planner must hear about.</summary>
        public bool Resolve(Vector3 stand, bool usable)
        {
            bool flipped = usable != Usable;
            Resolved = true;
            Usable = usable;
            Position = stand;
            return flipped;
        }

        public void SetThreshold(Vector3? threshold) => Threshold = threshold;

        public void SetSeatSurface(float worldY) => SeatSurfaceY = worldY;
    }
}
