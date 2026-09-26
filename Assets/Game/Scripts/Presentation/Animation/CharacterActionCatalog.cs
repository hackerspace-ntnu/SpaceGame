using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Every <see cref="CharacterAction"/> the humanoid controller was built with, in build order.
    ///
    /// <para>
    /// Written by the controller builder, never by hand: it collects every action under
    /// <c>Assets/Game/ScriptableObjects/Animation/Actions/</c>, sorted by path, and generates the
    /// controller from the same list, so the catalog and the controller cannot disagree about which
    /// actions exist.
    /// </para>
    /// <para>
    /// The index is how an action crosses the wire (<c>NetMsg.CharacterActed</c>, a melee
    /// variant). It is stable only within one build — every machine in a session runs the same
    /// one — and nothing saves it, so reordering the folder is safe.
    /// </para>
    /// </summary>
    public sealed class CharacterActionCatalog : ScriptableObject
    {
        public const string ResourcePath = "Animation/CharacterActionCatalog";

        [SerializeField] private List<CharacterAction> actions = new List<CharacterAction>();

        [Tooltip("How many idles the Base Layer's idle blend holds (IdleIndex 0..n-1). Written by " +
                 "the builder from the locomotion set.")]
        [SerializeField, Min(1)] private int idleVariantCount = 1;

        private Dictionary<CharacterAction, int> indexOf;
        private Dictionary<string, CharacterAction> byName;
        private Dictionary<CharacterCue, List<CharacterAction>> byCue;
        private Dictionary<string, CharacterCue> cueByName;
        private List<CharacterCue> cues;

        private static readonly IReadOnlyList<CharacterAction> None = new CharacterAction[0];

        private static CharacterActionCatalog cached;
        private static bool loadAttempted;

        /// <summary>The catalog the controller was built with, or null (reported once) if missing.</summary>
        public static CharacterActionCatalog Default
        {
            get
            {
                if (cached != null || loadAttempted) return cached;

                loadAttempted = true;
                cached = Resources.Load<CharacterActionCatalog>(ResourcePath);
                if (cached == null)
                {
                    Debug.LogError($"[CharacterActions] No catalog at Resources/{ResourcePath}. Run " +
                                   "Tools/SpaceGame/Animation/Rebuild Humanoid Controller.");
                }
                return cached;
            }
        }

        public IReadOnlyList<CharacterAction> Actions => actions;
        public int IdleVariantCount => idleVariantCount;

        /// <summary>The wire id of <paramref name="action"/>, or -1 if it is not in the build.</summary>
        public int IndexOf(CharacterAction action)
        {
            if (action == null) return -1;
            if (indexOf == null) BuildLookups();
            return indexOf.TryGetValue(action, out int index) ? index : -1;
        }

        /// <summary>The action with wire id <paramref name="index"/>, or null for an id not in the build.</summary>
        public CharacterAction At(int index) =>
            index >= 0 && index < actions.Count ? actions[index] : null;

        /// <summary>By asset name, case-insensitive — how dialogue text names a gesture (<c>{wave}</c>).</summary>
        public bool TryFind(string actionName, out CharacterAction action)
        {
            if (byName == null) BuildLookups();
            return byName.TryGetValue(actionName ?? string.Empty, out action);
        }

        /// <summary>Every action tagged with <paramref name="cue"/>, in catalog order. Empty for none.</summary>
        public IReadOnlyList<CharacterAction> ActionsFor(CharacterCue cue)
        {
            if (cue == null) return None;
            if (byCue == null) BuildLookups();
            return byCue.TryGetValue(cue, out List<CharacterAction> list) ? list : None;
        }

        /// <summary>Every cue at least one action is tagged with, sorted by name — the live vocabulary.</summary>
        public IReadOnlyList<CharacterCue> Cues
        {
            get
            {
                if (cues == null) BuildLookups();
                return cues;
            }
        }

        /// <summary>A cue by its word, case-insensitive — how dialogue text and chat name one.</summary>
        public bool TryFindCue(string word, out CharacterCue cue)
        {
            if (cueByName == null) BuildLookups();
            return cueByName.TryGetValue(word ?? string.Empty, out cue);
        }

        private void BuildLookups()
        {
            indexOf = new Dictionary<CharacterAction, int>(actions.Count);
            byName = new Dictionary<string, CharacterAction>(actions.Count, System.StringComparer.OrdinalIgnoreCase);
            byCue = new Dictionary<CharacterCue, List<CharacterAction>>();
            cueByName = new Dictionary<string, CharacterCue>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < actions.Count; i++)
            {
                CharacterAction a = actions[i];
                if (a == null) continue;
                indexOf[a] = i;
                byName[a.name] = a;

                foreach (CharacterCue cue in a.Cues)
                {
                    if (cue == null) continue;
                    if (!byCue.TryGetValue(cue, out List<CharacterAction> list)) byCue[cue] = list = new List<CharacterAction>();
                    if (!list.Contains(a)) list.Add(a);
                    cueByName[cue.name] = cue;
                }
            }

            cues = new List<CharacterCue>(byCue.Keys);
            cues.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        }

        private void OnValidate()
        {
            indexOf = null;
            byName = null;
            byCue = null;
            cueByName = null;
            cues = null;
        }
    }
}
