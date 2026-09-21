// One rung of a questline: something the NPC says, and something it wants in its hand before it
// will say the next thing.
//
// Deliberately the whole vocabulary. There is no objective type, no condition graph, no flag
// system — a step is a line and an item, and a questline is a list of them. That is enough for
// "fetch me three of these and I'll tell you where the wreck is", which is the shape the game
// actually needs, and it is small enough that a designer can author one without reading any code.
using System;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Quests
{
    [Serializable]
    public class QuestStep
    {
        [TextArea(2, 5)]
        [Tooltip("What the NPC says when it wants this. Said as the ask when you have the item, " +
                 "and again as the reminder when you do not — so write it as a request, not as a " +
                 "reaction: \"The old relay's still out past the ridge. Bring me a power cell and " +
                 "I'll wake it up.\"")]
        public string text = string.Empty;

        [Tooltip("What the NPC must be given before this step is done. Required — a step with no " +
                 "item can never complete, and TownGenerator.Verify refuses a questline containing one.")]
        public InventoryItem required;

        [Tooltip("Optional. Handed back the moment the step completes. Leave empty for a step that " +
                 "only moves the story on.")]
        public InventoryItem reward;

        [TextArea(1, 3)]
        [Tooltip("Optional. Said on handing over, before the next step's line. Empty means the NPC " +
                 "goes straight on to what it wants next, which reads fine for a brisk character.")]
        public string thanksLine = string.Empty;

        public bool IsValid => required != null && !string.IsNullOrWhiteSpace(text);
    }
}
