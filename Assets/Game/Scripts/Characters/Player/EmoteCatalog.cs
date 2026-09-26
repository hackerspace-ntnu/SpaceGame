using System;
using System.Collections.Generic;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Every emote the player can do, in wheel and wire order: the chat word that types it, the
    /// <see cref="CharacterAction"/> it plays, what the chat says about it, the wheel page it sits
    /// on and an optional icon.
    ///
    /// <para>
    /// Data rather than a table in code, so an emote is added by appending an entry here — the chat
    /// command (<see cref="PlayerEmoteCommands"/>) and the emote wheel both read this list and
    /// nothing else. The action is a reference, not a name, so a renamed action asset cannot
    /// silently orphan its emote.
    /// </para>
    /// <para>
    /// <b>The index is the wire format.</b> <see cref="Core.NetMsg.Emote"/> carries an index into
    /// <see cref="Entries"/>, which every machine reads from the same build, so it is stable within
    /// a session. Nothing saves it; reordering is safe between builds but still reshuffles the
    /// wheel under the players' thumbs, so append.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Emote Catalog", fileName = "EmoteCatalog")]
    public sealed class EmoteCatalog : ScriptableObject
    {
        public const string ResourcePath = "Animation/EmoteCatalog";

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The chat word, without the slash: /wave. Unique, case-insensitive.")]
            public string word;

            [Tooltip("The action this emote plays. Must be in the humanoid action catalog, i.e. " +
                     "built into the controller.")]
            public CharacterAction action;

            [Tooltip("What the chat says when the command is typed, with {0} for the player's name. " +
                     "Empty for silence.")]
            public string line;

            [Tooltip("The emote wheel page this sits on. Pages appear in the order their category " +
                     "first appears in this list; a category with more than one wheel's worth of " +
                     "emotes spills onto a second page of the same name.")]
            public string category;

            [Tooltip("Optional. Drawn above the word on the emote wheel.")]
            public Sprite icon;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private static EmoteCatalog cached;
        private static bool loadAttempted;

        /// <summary>The emote list the build ships with, or null (reported once) if missing.</summary>
        public static EmoteCatalog Default
        {
            get
            {
                if (cached != null || loadAttempted) return cached;

                loadAttempted = true;
                cached = Resources.Load<EmoteCatalog>(ResourcePath);
                if (cached == null)
                    Debug.LogError($"[EmoteCatalog] No emote catalog at Resources/{ResourcePath}.");
                return cached;
            }
        }

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>The entry with wire id <paramref name="index"/>, or null for an id not in the list.</summary>
        public Entry At(int index) => index >= 0 && index < entries.Count ? entries[index] : null;

        /// <summary>The index for a typed word, or -1. Case-insensitive; the chat is.</summary>
        public int IndexOf(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return -1;

            string wanted = word.Trim();
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].word, wanted, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }
    }
}
