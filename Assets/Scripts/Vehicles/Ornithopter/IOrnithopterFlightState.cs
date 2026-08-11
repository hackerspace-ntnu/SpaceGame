namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// What the wing animator needs to know about the flight, and no more.
    ///
    /// The animator lives in this assembly with the physics; the motor that implements this lives
    /// in Assembly-CSharp with <c>IRiderControllable</c> and <c>MountModule</c>, which an assembly
    /// definition cannot reference. This interface is the seam. It also means the animator can be
    /// driven by a test double, which is how the wing motion gets exercised without a Rigidbody.
    /// </summary>
    public interface IOrnithopterFlightState
    {
        /// <summary>Speed along the flight path, m/s.</summary>
        float Airspeed { get; }

        /// <summary>Beat position, 0..1. Must arrive unfiltered — see the animator.</summary>
        float FlapPhase { get; }

        /// <summary>How hard the wings are beating, 0..1.</summary>
        float FlapEffort { get; }

        /// <summary>How far the wings are unfolded, 0 folded to 1 open.</summary>
        float WingSpread { get; }

        /// <summary>Bank angle in degrees, + is right wing down.</summary>
        float BankAngle { get; }

        /// <summary>Pitch stick, -1..1. Drives the tail boom.</summary>
        float PitchInput { get; }

        /// <summary>Turn demand, -1..1. Splays the tail fan asymmetrically.</summary>
        float TurnInput { get; }

        /// <summary>True while the wing is stalled.</summary>
        bool IsStalled { get; }
    }
}
