using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Everything the server says about the workshop's story, in one int: <see cref="Bits"/> in NetArg.A. Every machine
    /// works out the rest (lines, the trail, the visor) from it and the camp's site (<see cref="GearThiefCampSite"/>).
    /// </summary>
    public struct WorkshopMissionState : IEquatable<WorkshopMissionState>
    {
        private const int PhaseMask = 0xF;
        private const int CarriedBit = 1 << 4;
        private const int BandBit = 1 << 5;
        /// <summary>Not part of the state: on the wire it says the change just happened (see <see cref="WithLive"/>).</summary>
        private const int LiveBit = 1 << 6;
        private const int CarrierBit = 1 << 7;
        private const int ClueShift = 8;
        private const int ClueMask = 0xF;
        private const int CoreBit = 1 << 12;
        private const int FittedBit = 1 << 13;

        public WorkshopMissionPhase phase;

        /// <summary>A player has the stolen gear in their hotbar; in the vault, the reactor core.</summary>
        public bool carried;

        /// <summary>The thieves and the gear have been spawned at their camp: never again.</summary>
        public bool bandSpawned;

        /// <summary>The thieves' carrier has the gear in his bag: he snatched it up and ran.</summary>
        public bool carrierHasGear;

        /// <summary>How many clues of the raiders' trail have been examined.</summary>
        public int clues;

        /// <summary>The reactor core has been laid in the vault: it is a record of its own now, never laid again.</summary>
        public bool coreSpawned;

        /// <summary>The hoist has fitted the core to a ship (before the Shellbacks offered it; after, the story is over).</summary>
        public bool coreFitted;

        public int Bits => (int)phase | (carried ? CarriedBit : 0) | (bandSpawned ? BandBit : 0) |
                           (carrierHasGear ? CarrierBit : 0) | (Mathf.Clamp(clues, 0, ClueMask) << ClueShift) |
                           (coreSpawned ? CoreBit : 0) | (coreFitted ? FittedBit : 0);

        /// <summary>The bits with the "it just happened" flag, for the wire.</summary>
        public int WithLive(bool live) => Bits | (live ? LiveBit : 0);

        public static bool IsLive(int wire) => (wire & LiveBit) != 0;

        public static WorkshopMissionState FromWire(int wire) => new()
        {
            phase = (WorkshopMissionPhase)(wire & PhaseMask),
            carried = (wire & CarriedBit) != 0,
            bandSpawned = (wire & BandBit) != 0,
            carrierHasGear = (wire & CarrierBit) != 0,
            clues = (wire >> ClueShift) & ClueMask,
            coreSpawned = (wire & CoreBit) != 0,
            coreFitted = (wire & FittedBit) != 0,
        };

        public bool Equals(WorkshopMissionState other) => Bits == other.Bits;

        public override bool Equals(object obj) => obj is WorkshopMissionState other && Equals(other);

        public override int GetHashCode() => Bits;
    }
}
