using System;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The registration table of body language: for each <see cref="CharacterMoment"/>, the cue a
    /// body answers with, how often, and how soon it may again. Editing a row changes what every
    /// body using the table does, with no code touched.
    ///
    /// <para>
    /// One default table (<c>Resources/Animation/MomentReactions</c>) covers every humanoid. A body
    /// may carry its own table on <see cref="BodyLanguage"/>; a moment it has a row for uses that
    /// row instead, and a row with neither cue nor action silences the moment for that body.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Moment Reactions", fileName = "MomentReactions")]
    public sealed class MomentReactions : ScriptableObject
    {
        public const string ResourcePath = "Animation/MomentReactions";

        [Serializable]
        public sealed class Row
        {
            public CharacterMoment moment;

            [Tooltip("What the body expresses. One of the actions tagged with it plays, picked to fit " +
                     "the body's posture.")]
            public CharacterCue cue;

            [Tooltip("Optional: this exact action instead of a cue.")]
            public CharacterAction action;

            [Tooltip("Chance the body reacts at all, 0-1.")]
            [Range(0f, 1f)] public float chance = 1f;

            [Tooltip("Seconds after a reaction to this moment before the body reacts to it again.")]
            [Min(0f)] public float cooldown;

            /// <summary>A row that says to show nothing.</summary>
            public bool Silent => cue == null && action == null;
        }

        [SerializeField] private Row[] rows = Array.Empty<Row>();

        private static MomentReactions cached;
        private static bool loadAttempted;

        public Row[] Rows => rows;

        /// <summary>The table every body uses unless it has its own, or null (reported once) if missing.</summary>
        public static MomentReactions Default
        {
            get
            {
                if (cached != null || loadAttempted) return cached;

                loadAttempted = true;
                cached = Resources.Load<MomentReactions>(ResourcePath);
                if (cached == null) Debug.LogError($"[BodyLanguage] No reaction table at Resources/{ResourcePath}.");
                return cached;
            }
        }

        /// <summary>The row for <paramref name="moment"/>, or null when the table has none.</summary>
        public Row Find(CharacterMoment moment)
        {
            foreach (Row row in rows)
                if (row != null && row.moment == moment) return row;
            return null;
        }
    }
}
