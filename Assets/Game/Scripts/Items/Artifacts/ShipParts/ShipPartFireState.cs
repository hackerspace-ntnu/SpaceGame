using System;

namespace SpaceGame.Items
{
    /// <summary>The whole state of one unit's fire: what is saved and what the rules step.</summary>
    [Serializable]
    public struct ShipPartFireState
    {
        public ShipPartFirePhase phase;

        /// <summary>0..1 while burning; 0 otherwise.</summary>
        public float strength;

        /// <summary>Seconds the unit has sat seated in a landed ship, counting towards ignition.</summary>
        public float armedSeconds;

        public bool IsBurning => phase == ShipPartFirePhase.Burning;
    }
}
