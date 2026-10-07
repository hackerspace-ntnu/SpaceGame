namespace SpaceGame.Items
{
    /// <summary>Where a burnt-out unit's fire is in its one life. Values are saved: append only.</summary>
    public enum ShipPartFirePhase : byte
    {
        /// <summary>Not lit yet. The clock may be running towards ignition.</summary>
        Dormant = 0,

        /// <summary>On fire.</summary>
        Burning = 1,

        /// <summary>Was on fire and has been put out. It never lights again.</summary>
        Out = 2,
    }
}
