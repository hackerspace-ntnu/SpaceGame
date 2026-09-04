namespace SpaceGame.Gameplay
{
    /// <summary>
    /// An interactable that can say what it is and what working it would do, so the HUD can put
    /// that on screen when the crosshair lands on it.
    ///
    /// This exists because a rope winch is not a door. A door needs no explanation: you look at
    /// it, you press E, it opens. The dune foiler's deck carries four near-identical winches
    /// 1.5 m apart, each with a different meaning on the same two buttons, and nothing told the
    /// player which was which — you learned the rig by pressing things and watching what broke.
    /// The information to fix that was already being written (both DuneFoilRiggingStation and
    /// DeckBoarding have had a Prompt property since they were built); there was simply nothing
    /// anywhere in the project that read it.
    ///
    /// Optional. An interactable that does not implement this just gets the plain crosshair.
    /// </summary>
    public interface IInteractionReadout
    {
        /// <summary>What this control is, in two or three words. "Main sheet", "Helm".</summary>
        string Label { get; }

        /// <summary>What the buttons do. "RMB: ease   LMB: haul in".</summary>
        string Prompt { get; }

        /// <summary>
        /// Where the control currently sits, 0..1, or null for a control with no position —
        /// a door, a ladder, a boarding point. Drawn as a fill bar.
        /// </summary>
        float? Value01 { get; }

        /// <summary>
        /// The same value in whatever units the player thinks in — degrees of rake, percent of
        /// hoist. Empty to show none.
        /// </summary>
        string ValueText { get; }
    }
}
