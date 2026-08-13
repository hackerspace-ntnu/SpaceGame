using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Controls the on-screen prompt for whatever interactable sits on this GameObject.
    ///
    /// Prompts are on by default for EVERY interactable in the game — a door, a lever, a pickup,
    /// a rope winch — because a control the player cannot identify is not a control. This
    /// component exists for the two cases the default cannot cover:
    ///
    ///   * <b>turning a prompt off.</b> Some things should stay silent: a proxy collider, a
    ///     trigger volume that is only interactable as plumbing, an object whose whole point is
    ///     that you have to work out what it does.
    ///   * <b>saying it better.</b> The automatic label is derived from the component's type
    ///     name, which gives "Door" and "Repair Workstation" for free but also gives
    ///     "Pickupable Item" where "Scrap Plating" was wanted.
    ///
    /// Optional everywhere. An interactable with no <see cref="InteractionPrompt"/> still gets a
    /// prompt; this only overrides one.
    ///
    /// Interactables that want a LIVE readout — a value, a fill bar, text that changes as the
    /// player works the control — implement <see cref="IInteractionReadout"/> instead, and can
    /// still carry one of these to be switched off or relabelled.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionPrompt : MonoBehaviour
    {
        [Header("Visibility")]
        [Tooltip("Off hides the prompt for this interactable entirely. The interaction still " +
                 "works; the player simply gets no help with it.")]
        [SerializeField] private bool showPrompt = true;

        [Tooltip("Hide the prompt while the interactable is refusing interaction. On by default: " +
                 "a prompt for something that will not answer is worse than no prompt.")]
        [SerializeField] private bool hideWhenUnavailable = true;

        [Header("Text")]
        [Tooltip("What to call this. Empty derives it from the component's type name — " +
                 "'DoorInteraction' becomes 'Door'.")]
        [SerializeField] private string label;

        [Tooltip("Use this GameObject's name as the label instead. Useful for pickups and props, " +
                 "where the object knows what it is and the component does not.")]
        [SerializeField] private bool useGameObjectName;

        [Tooltip("What the buttons do. Empty derives it from which interfaces the component " +
                 "implements — 'E: interact', plus a Use line when it takes a secondary action.")]
        [SerializeField] private string prompt;

        /// <summary>Whether this interactable should be described on screen at all.</summary>
        public bool ShowPrompt { get => showPrompt; set => showPrompt = value; }

        /// <summary>Whether to go quiet while the interactable is refusing.</summary>
        public bool HideWhenUnavailable => hideWhenUnavailable;

        /// <summary>Authored label, or empty to derive one.</summary>
        public string Label => useGameObjectName ? name : label;

        /// <summary>Authored prompt, or empty to derive one.</summary>
        public string Prompt => prompt;
    }
}
