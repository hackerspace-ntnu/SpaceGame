namespace SpaceGame.Presentation
{
    /// <summary>
    /// The hint front door, kept as an adapter over <see cref="SystemMessages"/>.
    ///
    /// <para>
    /// This used to own a canvas of its own and draw a textbox at the top of the screen. It does
    /// not any more: hints, warnings, pickups and system events are one channel now, drawn on the
    /// visor by <see cref="VisorMessageStack"/>. Two surfaces both claiming to be "the one place
    /// text appears" is how a player ends up reading two boxes in two type sizes.
    /// </para>
    /// <para>
    /// The API is unchanged on purpose, so every existing caller — <c>SeatPromptUI</c> most of all
    /// — keeps working without edits. Hints post at <see cref="MessageSeverity.Notice"/>, which is
    /// the severity that means "you should act on this", and which holds until it is taken down.
    /// </para>
    /// <para>
    /// Built for the lesson-at-the-moment-it-matters pattern (<c>GDC-L1-UX-0001</c>): a system that
    /// knows the player needs telling something calls <see cref="Show"/> when the moment arrives
    /// and <see cref="Hide"/> when it has passed. Hints are addressed by id so an owner only ever
    /// takes down its own — a late <c>Hide</c> cannot erase somebody else's newer hint.
    /// </para>
    /// </summary>
    public static class PlayerHints
    {
        /// <summary>
        /// Prefix on every id this class forwards, so a hint and a system message that happen to
        /// pick the same name cannot take each other down.
        /// </summary>
        private const string IdPrefix = "hint:";

        /// <summary>Shows <paramref name="text"/> until <see cref="Hide"/> is called with the same id.</summary>
        public static void Show(string id, string text) =>
            SystemMessages.Post(IdPrefix + id, text, MessageSeverity.Notice, float.PositiveInfinity);

        /// <summary>Shows <paramref name="text"/>, taking itself down after <paramref name="seconds"/>.</summary>
        public static void Show(string id, string text, float seconds) =>
            SystemMessages.Post(IdPrefix + id, text, MessageSeverity.Notice, seconds);

        /// <summary>Takes down the hint with this id. A hint someone else has shown stays up.</summary>
        public static void Hide(string id) => SystemMessages.Clear(IdPrefix + id);
    }
}
