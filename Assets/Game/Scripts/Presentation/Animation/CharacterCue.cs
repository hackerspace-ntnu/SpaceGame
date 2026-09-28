using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A word in the body-language vocabulary: <c>greet</c>, <c>talk</c>, <c>hurt</c>, <c>pickup</c>.
    /// What a body should express, as opposed to the clip it expresses it with.
    ///
    /// <para>
    /// Actions list the cues they can express (<see cref="CharacterAction"/>'s Cues), so a cue is
    /// answered by every action tagged with it — adding a fourth greeting is tagging a fourth
    /// action, and every caller that asks for <c>greet</c> gets it with no code change. Gameplay
    /// asks for cues through <see cref="BodyLanguage"/>, which picks the tagged action that fits
    /// the body right now (a full-body bow only while standing still, an arm wave on the move).
    /// </para>
    /// <para>
    /// The assets live in <c>Assets/Game/ScriptableObjects/Animation/Cues/</c>. The asset's name is
    /// the word — what dialogue text and the chat command match against.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Animation/Character Cue", fileName = "cue")]
    public sealed class CharacterCue : ScriptableObject
    {
        [Tooltip("What the cue means and when gameplay should ask for it. Shown in the Animation Library.")]
        [SerializeField, TextArea] private string meaning;

        [Tooltip("Asked instead when no action tagged with this cue fits the body — 'laugh' falling " +
                 "back to 'celebrate' while walking. Optional.")]
        [SerializeField] private CharacterCue fallback;

        public string Meaning => meaning;
        public CharacterCue Fallback => fallback;
    }
}
