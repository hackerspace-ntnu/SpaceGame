using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// One receptacle on an <see cref="OxygenGenerator"/>: the round collar a bottle plugs into, or
    /// the rectangular slot a power cell lies in. It owns no state — it is the thing the crosshair
    /// lands on and the label the HUD reads, and every decision is the generator's.
    ///
    /// <para>
    /// <b>Why the collider under this is a TRIGGER, and why it stands proud of the machine.</b> The
    /// interaction ray takes the nearest hit and a SOLID collider answers with the first
    /// <c>IInteractable</c> on itself or above it — so the generator's own body box, which encloses
    /// both receptacles, would answer for the whole machine and neither dock could ever be aimed
    /// at. A trigger answers only when the interactable is on the trigger's own GameObject and is
    /// otherwise see-through, and the box is fitted to the ITEM that goes in the dock and then
    /// pushed clear of the machine's own front face, so the ray meets it first. That is the ship's
    /// seat volumes' arrangement, for the same reason.
    /// </para>
    /// <para>
    /// Two docks means two GameObjects rather than one prompt that changes meaning, because the
    /// receptacle is the signifier for its own verb: a round collar cannot take a cell and the slot
    /// cannot take a bottle, and the player can see that before any text appears
    /// (<c>GDC-L1-UX-0004</c>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class OxygenGeneratorDock : MonoBehaviour, IInteractable, IInteractionReadout
    {
        [Tooltip("The machine this receptacle belongs to. Serialized rather than searched: a dock " +
                 "that quietly found the wrong parent would work and be wrong.")]
        [SerializeField] private OxygenGenerator generator;

        [SerializeField] private OxygenGenerator.DockKind dock;

        /// <summary>Which receptacle this is.</summary>
        public OxygenGenerator.DockKind Dock => dock;

        /// <summary>The machine this belongs to.</summary>
        public OxygenGenerator Generator => generator;

        /// <summary>
        /// A wired dock always offers something: fit, or take. Which of the two, and whether the
        /// player is holding the right thing for it, is the prompt's job and the press's — a
        /// machine that only lights up once you already hold the answer cannot teach anyone what it
        /// wants, so a wrong hand is refused out loud instead (the repair station's bargain).
        /// </summary>
        public bool CanInteract() => generator != null;

        public void Interact(Interactor interactor)
        {
            if (generator == null) return;

            generator.Interact(dock, interactor);
        }

        public string Label => generator != null ? generator.LabelFor(dock) : string.Empty;

        public string Prompt => generator != null ? generator.PromptFor(dock) : string.Empty;

        public float? Value01 => generator != null ? generator.Value01(dock) : null;

        public string ValueText => generator != null ? generator.ValueText(dock) : string.Empty;
    }
}
