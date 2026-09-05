using System.Text;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>Everything the HUD needs to describe one interactable, already resolved.</summary>
    public readonly struct InteractionDisplay
    {
        public readonly string Label;
        public readonly string Prompt;

        /// <summary>Where a continuous control sits, 0..1, or null for something with no state.</summary>
        public readonly float? Value01;

        /// <summary>The same value in the player's units. Empty to show none.</summary>
        public readonly string ValueText;

        public InteractionDisplay(string label, string prompt, float? value01, string valueText)
        {
            Label = label;
            Prompt = prompt;
            Value01 = value01;
            ValueText = valueText;
        }
    }

    /// <summary>
    /// Turns any <see cref="IInteractable"/> into something the HUD can draw.
    ///
    /// The rule this encodes is that a prompt is the DEFAULT, not an opt-in. The project has
    /// eighteen interactable types across half a dozen assemblies, with no shared base class to
    /// hang a field on, and asking each of them to describe itself would have meant most of them
    /// never doing it — which is exactly how the dune foiler ended up with four identical winches
    /// and no way to tell them apart. So this works from what is already knowable: the component's
    /// type name, and which interfaces it implements.
    ///
    /// Three sources, most specific first:
    ///   1. <see cref="InteractionPrompt"/> on the same GameObject — authored text, and the only
    ///      way to switch a prompt OFF.
    ///   2. <see cref="IInteractionReadout"/> on the component — live label, prompt and value.
    ///   3. The type name, humanised.
    ///
    /// They compose rather than shadow: a component can implement the readout for its live value
    /// and still carry an <see cref="InteractionPrompt"/> that renames it.
    /// </summary>
    public static class InteractionPromptResolver
    {
        /// <summary>Shown when nothing more specific is known.</summary>
        public const string DefaultPrompt = "RMB: interact";

        /// <summary>Appended when the interactable also takes a Use action.</summary>
        public const string SecondarySuffix = "   LMB: use";

        /// <summary>Appended when the interactable can also be picked up.</summary>
        public const string RetrieveSuffix = "   Q: pick up";

        // Trailing words that describe the plumbing rather than the thing. "DoorInteraction" is a
        // door; "MountModule" is a mount. Order matters only in that longer suffixes are listed
        // first so "Interactable" is not half-eaten by "Interact".
        //
        // "Workstation" is deliberately NOT here. It looks like the same kind of noise as
        // "Station" and it is not: stripping it turned RepairWorkstation into "Repair", which
        // names the verb instead of the thing.
        private static readonly string[] NoiseSuffixes =
        {
            "Interactable", "Interaction", "Component", "Behaviour",
            "Station", "Module", "Object", "Trigger", "Proxy", "View",
        };

        // What is left when a type name was ALL plumbing. InteractableTrigger strips down to
        // "Interactable", which tells the player nothing they cannot already see. These types are
        // generic wrappers, so the GameObject they were put on is the more informative name.
        private static readonly string[] EmptyResidues = { "Interactable", "Interact", "Base" };

        private static readonly StringBuilder Builder = new StringBuilder(64);

        /// <summary>
        /// Resolve what to draw for this interactable, or return false to draw nothing.
        /// </summary>
        public static bool TryResolve(IInteractable interactable, out InteractionDisplay display)
        {
            display = default;
            if (interactable == null) return false;

            Component component = interactable as Component;
            InteractionPrompt authored = component != null
                ? component.GetComponent<InteractionPrompt>()
                : null;

            if (authored != null)
            {
                if (!authored.ShowPrompt) return false;
                if (authored.HideWhenUnavailable && !SafeCanInteract(interactable)) return false;
            }

            IInteractionReadout readout = interactable as IInteractionReadout;

            string label = FirstNonEmpty(
                authored != null ? authored.Label : null,
                readout != null ? readout.Label : null,
                DeriveLabel(interactable));

            string prompt = FirstNonEmpty(
                authored != null ? authored.Prompt : null,
                readout != null ? readout.Prompt : null,
                DerivePrompt(interactable));

            display = new InteractionDisplay(
                label,
                prompt,
                readout != null ? readout.Value01 : null,
                readout != null ? readout.ValueText : string.Empty);
            return true;
        }

        /// <summary>
        /// A readable name for an interactable that never said what it was.
        /// "DoorInteraction" becomes "Door", "RepairWorkstation" becomes "Repair Workstation",
        /// "InteriorEntrance" becomes "Interior Entrance". A purely generic wrapper such as
        /// "InteractableTrigger" falls through to the GameObject's own name.
        /// </summary>
        public static string DeriveLabel(IInteractable interactable)
        {
            if (interactable == null) return string.Empty;

            string typeName = interactable.GetType().Name;
            string name = typeName;

            foreach (string suffix in NoiseSuffixes)
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    break;
                }
            }

            if (IsMeaningless(name))
            {
                Component component = interactable as Component;
                if (component != null && !string.IsNullOrWhiteSpace(component.name))
                    return Humanise(component.name);
                name = typeName;
            }

            return Humanise(name);
        }

        private static bool IsMeaningless(string residue)
        {
            if (string.IsNullOrWhiteSpace(residue)) return true;
            foreach (string empty in EmptyResidues)
                if (string.Equals(residue, empty, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>"RMB: interact", plus the Use line when the component takes one.</summary>
        public static string DerivePrompt(IInteractable interactable)
        {
            string prompt = DefaultPrompt;
            if (interactable is ISecondaryInteractable) prompt += SecondarySuffix;
            if (interactable is IRetrievable) prompt += RetrieveSuffix;
            return prompt;
        }

        /// <summary>Split a PascalCase identifier into words. "RepairWorkstation" -> "Repair Workstation".</summary>
        public static string Humanise(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return string.Empty;

            Builder.Clear();
            for (int i = 0; i < identifier.Length; i++)
            {
                char c = identifier[i];

                // GameObject names arrive here too, and those are written by hand: "Crate_01",
                // "cargo-hatch". Separators become spaces rather than being carried on screen.
                if (c == '_' || c == '-')
                {
                    if (Builder.Length > 0 && Builder[Builder.Length - 1] != ' ') Builder.Append(' ');
                    continue;
                }

                // Break before a capital that starts a new word, but not inside a run of them:
                // "RuinSecret" splits, "NPCDoor" does not become "N P C Door".
                bool startsWord = i > 0 && char.IsUpper(c) &&
                                  (!char.IsUpper(identifier[i - 1]) ||
                                   (i + 1 < identifier.Length && char.IsLower(identifier[i + 1])));

                if (startsWord && Builder.Length > 0 && Builder[Builder.Length - 1] != ' ')
                    Builder.Append(' ');
                Builder.Append(c);
            }
            return Builder.ToString().Trim();
        }

        /// <summary>
        /// CanInteract is author-written and reaches into whatever state the interactable owns, so
        /// it can throw while things are still spawning. The HUD asking a question must never be
        /// what breaks a frame.
        /// </summary>
        private static bool SafeCanInteract(IInteractable interactable)
        {
            try { return interactable.CanInteract(); }
            catch { return true; }
        }

        private static string FirstNonEmpty(string a, string b, string c)
        {
            if (!string.IsNullOrWhiteSpace(a)) return a;
            if (!string.IsNullOrWhiteSpace(b)) return b;
            return c ?? string.Empty;
        }
    }
}
