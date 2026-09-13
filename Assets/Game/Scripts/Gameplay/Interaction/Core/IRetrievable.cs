namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Something in the world that can be taken back into the inventory, reached by the same
    /// interact button (RMB) that operates everything else.
    ///
    /// <para>
    /// "Put it down" and "pick it back up" are opposite halves of one verb, and the two halves are
    /// two buttons: LMB places, RMB takes back. That is the binding loose salvage has always used
    /// — <c>PickupableItem</c> is an ordinary <see cref="IInteractable"/> — so a lantern the player
    /// put down comes back the same way the crate they never touched does. It used to be Q, which
    /// meant the player had to know which kind of object they were looking at before they knew
    /// which button to press, and Q is also the left gauntlet's trigger, so pocketing a lamp fired
    /// a worn device with it.
    /// </para>
    /// <para>
    /// The primary verb still wins where there is one: <see cref="Interactor.PressPicksUp"/> only
    /// picks up what has nothing to operate. A placeable that <i>does</i> something puts that on
    /// LMB (<see cref="ISecondaryInteractable"/>) rather than taking the interact button back.
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
