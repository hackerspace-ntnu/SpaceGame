namespace SpaceGame.Items
{
    /// <summary>
    /// Where one net is in its life.
    ///
    /// <para>
    /// An enum rather than the three booleans it replaces. <see cref="SnareCatch"/> used to infer
    /// its state from <c>landed</c>, <c>landedElapsed</c> and <c>rotElapsed &gt;= 0f</c>, which
    /// answers two states unambiguously and six not at all — "landed and still closing", "landed
    /// and nailed to a body", "landed on sand and still flattening" and "landed on sand and done"
    /// are all the same three flags.
    /// </para>
    /// <para>
    /// <b>The question each member answers is "what runs this frame".</b> That is the test for
    /// whether something deserves a member of its own: <see cref="Settling"/> and
    /// <see cref="Fallen"/> are the same event — a net that came down on nothing it can hold — but
    /// one still runs the solver, the drape and the grip and the other runs nothing at all. Folding
    /// them into one member plus a timer would be reintroducing exactly the shape this enum
    /// replaced, and it would take away the plainest thing the outside can ask — has this net
    /// stopped solving? — by leaving no member to point at. <see cref="Fallen"/> and
    /// <see cref="Bound"/> are the two live phases in which the solver is off. (A net that
    /// <see cref="Tearing"/> took out of either is frozen too, so this is not a partition of
    /// "frozen"; it is the answer for a net that is still holding something.)
    /// </para>
    /// <para>
    /// The order is roughly the order a net passes through them, but nothing reads it as a number:
    /// the transitions are explicit, because <see cref="Tearing"/> is reachable from every one of
    /// the others and the pairs (<see cref="Cinching"/>, <see cref="Bound"/>) and
    /// (<see cref="Settling"/>, <see cref="Fallen"/>) are alternative branches rather than steps
    /// along one line.
    /// </para>
    /// </summary>
    public enum SnarePhase
    {
        /// <summary>Carried along the closed-form arc, solver alive, draping as it goes.</summary>
        Flight,

        /// <summary>Closing around a body, solver alive plus the cinch constraint.</summary>
        Cinching,

        /// <summary>Closed and frozen, cord riding the captive's bones. Solver off.</summary>
        Bound,

        /// <summary>
        /// Came down on something it cannot hold, and still flattening onto it. Solver alive.
        ///
        /// A net that froze on the frame it made contact would freeze wherever the impact cast
        /// stopped it — which is the cast's own radius above the floor, with only its hem clamped —
        /// so it would keep the shape of a sheet arriving rather than one lying down. This is the
        /// window in which it becomes the second.
        /// </summary>
        Settling,

        /// <summary>Down, and finished moving. Solver off for good.</summary>
        Fallen,

        /// <summary>Given out. Everything is released and the net is on its way out of the world.</summary>
        Tearing,
    }
}
