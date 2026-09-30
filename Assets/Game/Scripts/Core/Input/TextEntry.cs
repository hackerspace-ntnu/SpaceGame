// Whether the player is typing into a text field right now.
//
// Any key that does something has to stand down while this is true — otherwise typing a name with
// a "v" in it opens the microphone, and a chat line with an "i" in it opens the gear screen.
// Extracted from ChatUI, PauseMenuUI, BodyInventoryUI and DevInventoryUI, which each carried an
// identical private copy, when voice became the fifth thing that needed it.
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SpaceGame.Core
{
    /// <summary>The one answer to "is a key press right now actually typing?".</summary>
    public static class TextEntry
    {
        public static bool IsTyping
        {
            get
            {
                GameObject selected = EventSystem.current != null
                    ? EventSystem.current.currentSelectedGameObject
                    : null;

                return selected != null
                       && selected.TryGetComponent(out TMP_InputField field)
                       && field.isFocused;
            }
        }
    }
}
