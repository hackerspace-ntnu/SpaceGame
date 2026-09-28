namespace SpaceGame.Presentation
{
    /// <summary>
    /// Something that happened to or was done by a body, that it may show: the gameplay side of
    /// body language. Code raises a moment where it happens
    /// (<see cref="BodyLanguage.React(UnityEngine.Component, CharacterMoment)"/>); which cue — if any —
    /// the body answers with is data, in a <see cref="MomentReactions"/> table.
    ///
    /// <para>
    /// A value exists only where something raises it; a moment nothing raises is vocabulary that
    /// lies. Append only — reaction tables store the numbers.
    /// </para>
    /// </summary>
    public enum CharacterMoment
    {
        /// <summary>No reaction. What an interaction that shows nothing on the body answers.</summary>
        None = 0,

        /// <summary>Took damage. Raised on the server (<see cref="HurtReaction"/>).</summary>
        Hurt = 1,

        /// <summary>First line after a silence: the start of a conversation (<see cref="SpeechGestures"/>).</summary>
        ConversationStarted = 2,

        /// <summary>A line ending in a question mark (<see cref="SpeechGestures"/>).</summary>
        QuestionAsked = 3,

        /// <summary>A line ending in an exclamation mark (<see cref="SpeechGestures"/>).</summary>
        Exclaimed = 4,

        /// <summary>A beat inside a line being said — the hand gestures of speech (<see cref="SpeechGestures"/>).</summary>
        SpeechBeat = 5,

        /// <summary>Standing idle long enough to fidget (<see cref="IdleVariation"/>).</summary>
        IdleFidget = 6,

        /// <summary>Took something into the hands or pack (the player's interact press).</summary>
        PickedUp = 7,

        /// <summary>Operated something: a door, a lever, a dock (the player's interact press).</summary>
        Interacted = 8,

        /// <summary>Shouted a war cry (ChatterModule, every machine).</summary>
        WarCry = 9,

        /// <summary>Finished a work task at a site (NpcTaskModule, server).</summary>
        TaskFinished = 10,

        /// <summary>Caught a melee blow on its guard (MeleeDefense, server). Chance and cooldown are the module's; its row plays every time.</summary>
        Blocked = 11,

        /// <summary>Ducked or stepped out of a melee blow (MeleeDefense, server). Chance and cooldown are the module's; its row plays every time.</summary>
        Dodged = 12
    }
}
