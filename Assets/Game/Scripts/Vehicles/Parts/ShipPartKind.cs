namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Which module a <see cref="ShipPartSocket"/> takes, and which module a carried part is.
    ///
    /// <para>
    /// A kind, not a socket: the hull carries mirrored pairs — two nuclear motors, two reactor
    /// cores — and one salvaged motor fits either mount. Keying the match on the kind rather than
    /// on the socket is what stops a player hoarding a port motor they can never install.
    /// </para>
    /// <para>
    /// Values are persisted in saves and replicated as bit positions in a mask, so DO NOT renumber
    /// or reorder existing entries. New kinds are APPENDED.
    /// </para>
    /// </summary>
    public enum ShipPartKind : byte
    {
        /// <summary>The anti-gravity spine along the starboard flank.</summary>
        AntiGravity = 0,

        /// <summary>The two long nuclear motors on the roof — the ship's main drive.</summary>
        NuclearMotor = 1,

        /// <summary>The two reactor cores riding above the aft roof. The fuel, not the engine.</summary>
        ReactorCore = 2,

        /// <summary>The two small belly turbines.</summary>
        SmallMotor = 3,

        /// <summary>The nose air intake.</summary>
        AirIntake = 4,

        /// <summary>The two long flank turbines.</summary>
        LongTurbine = 5,

        /// <summary>The starboard gun.</summary>
        Gun = 6,
    }
}
