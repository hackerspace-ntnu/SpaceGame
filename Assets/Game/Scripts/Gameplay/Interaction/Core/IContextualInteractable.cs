namespace SpaceGame.Gameplay
{
    /// <summary>
    /// An interactable whose availability depends on WHO is looking at it.
    ///
    /// <see cref="IInteractable.CanInteract"/> asks one question of the world — "is this usable at
    /// all" — and for almost everything that is the right question. It is the wrong one for
    /// anything that acts on the player who used it, because then the answer is different for
    /// different people standing in the same room.
    ///
    /// The case this exists for: the dune foiler's hull offers "climb aboard" from anywhere you
    /// can see it, and the hull colliders reach up under the deck you are standing on. So once you
    /// were aboard, looking at the mast, the rail or the planks under your feet put a prompt on
    /// screen offering to put you where you already were — and pressing it teleported you back
    /// amidships mid-passage. Refusing it for everybody is not the fix either: a second player on
    /// the sand still needs a way up.
    ///
    /// Implement alongside <see cref="IInteractable"/>. The <see cref="Interactor"/> requires both
    /// to agree before it will light the crosshair or run the interaction, so a contextual refusal
    /// hides the prompt as well as blocking the press — which matters, because a prompt that
    /// appears and then does nothing is worse than no prompt at all.
    /// </summary>
    public interface IContextualInteractable
    {
        /// <summary>Can this particular interactor use it right now?</summary>
        bool CanInteract(Interactor interactor);
    }
}
