using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// An interactable that says what the pressing body shows. The interact press raises this
    /// moment on the player's <see cref="BodyLanguage"/>; an interactable without this interface
    /// shows <see cref="CharacterMoment.Interacted"/> (a reach to operate it).
    ///
    /// <para>
    /// Answer <see cref="CharacterMoment.None"/> where the press does something the body shows by
    /// other means, or where a reach would play in the wrong place: mounting and seating move the
    /// body at once, a terminal takes the camera, a conversation and petting have their own motion.
    /// </para>
    /// </summary>
    public interface IInteractionMoment
    {
        CharacterMoment InteractionMoment { get; }
    }

    public static class InteractionMoments
    {
        /// <summary>The moment pressing <paramref name="interactable"/> shows on the presser.</summary>
        public static CharacterMoment Of(IInteractable interactable) =>
            interactable is IInteractionMoment says ? says.InteractionMoment : CharacterMoment.Interacted;
    }
}
