namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Something in the world that can be taken back into the inventory, bound to Retrieve (Q)
    /// alongside the usual Interact (E).
    ///
    /// <para>
    /// "Put it down" and "pick it back up" are opposite halves of one verb, and the half that
    /// undoes a placement should never be the same button as the half that *operates* the thing:
    /// a placed lamp wants E to switch it on and Q to take it away, and a player who has to guess
    /// which one a single key will do this time will eventually pocket the lamp they meant to
    /// light. This is the same reasoning as <see cref="ISecondaryInteractable"/>, one button
    /// further along.
    /// </para>
    /// <para>
    /// Implement alongside <see cref="IInteractable"/>: <see cref="Interactor"/> resolves the
    /// crosshair through the IInteractable path only, so a component that implements this and
    /// nothing else is never found. Retrieval reaches whatever the crosshair is already on, which
    /// means it inherits line of sight and reach for free.
    /// </para>
    /// </summary>
    public interface IRetrievable
    {
        /// <summary>Whether this can be picked up right now.</summary>
        public bool CanRetrieve();

        /// <summary>Take it out of the world and give it back to whoever asked.</summary>
        public void Retrieve(Interactor interactor);
    }
}
