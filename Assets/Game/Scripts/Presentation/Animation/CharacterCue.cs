using System;
using System.Collections.Generic;
using SpaceGame.Items;
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

        [Tooltip("The body language of an empty hand: a wave, a shrug, a talk beat. While the body holds an " +
                 "item, only an action that leaves the occupied arm alone is picked for it. Leave off for " +
                 "work, combat and anything that is meant to be done with the item.")]
        [SerializeField] private bool needsFreeHands;

        [Tooltip("Station cues only: the hand tools this cue's clips are made for (a ladle for stirring, a pickaxe for mining). A " +
                 "resident working a spot that holds this cue draws one of them if it carries it. Empty = no particular tool.")]
        [SerializeField] private InventoryItem[] tools = Array.Empty<InventoryItem>();

        [Tooltip("Station cues only: the clips are done with empty hands (wiping, rummaging), so a resident working a spot that holds " +
                 "this cue puts its tool on the belt for as long as it does.")]
        [SerializeField] private bool bareHands;

        public string Meaning => meaning;
        public IReadOnlyList<InventoryItem> Tools => tools;
        public bool BareHands => bareHands;
        public bool NeedsFreeHands => needsFreeHands;
        public CharacterCue Fallback => fallback;
    }
}
