using UnityEngine;

public class NpcDialogInteraction : MonoBehaviour, IInteractable
{
    [Header("Dialog")]
    [SerializeField] private string[] dialogLines =
    {
        "Hey there, traveler.",
        "The desert gets colder at night.",
        "Stay alert out here."
    };
    [SerializeField] private bool loopDialogLines = true;
    [SerializeField] private bool allowRestartAfterEnd = true;
    [SerializeField] private float popupDuration = 2.5f;
    [SerializeField] private float interactionFocusDuration = 2.5f;

    

    private int currentLineIndex;


    public bool CanInteract()
    {
        if (dialogLines == null || dialogLines.Length == 0)
        {
            return false;
        }

        if (loopDialogLines)
        {
            return true;
        }

        if (allowRestartAfterEnd)
        {
            return true;
        }

        return currentLineIndex < dialogLines.Length;
    }

    public void Interact(Interactor interactor)
    {
        Debug.Log($"[NpcDialogInteraction] Interact called on '{name}' by '{interactor.name}'.");

        if (!CanInteract())
        {
            Debug.LogWarning($"[NpcDialogInteraction] '{name}' cannot interact: dialog unavailable or already completed.");
            return;
        }

        if (!loopDialogLines && currentLineIndex >= dialogLines.Length)
        {
            if (NpcDialogPopupUI.Instance != null)
            {
                NpcDialogPopupUI.Instance.Hide();
            }

            if (allowRestartAfterEnd)
            {
                currentLineIndex = 0;
            }

            return;
        }

        string line = GetNextLine();
        Debug.Log($"[NpcDialogInteraction] Showing dialog line: \"{line}\"");

        if (NpcDialogPopupUI.Instance != null)
        {
            Debug.Log("[NpcDialogInteraction] Found NpcDialogPopupUI instance. Showing popup.");
            NpcDialogPopupUI.Instance.Show(line, popupDuration);
        }
        else
        {
            Debug.LogWarning("[NpcDialogInteraction] NpcDialogPopupUI instance not found in scene. Add it to your UI Canvas.");
            Debug.Log($"[NpcDialogInteraction] Fallback dialog log: {line}");
        }

        if (TryGetComponent(out NpcBrain npcBrain))
        {
            npcBrain.FocusOn(interactor.transform, interactionFocusDuration);
        }
        else if (TryGetComponent(out SimpleNpcWander wander))
        {
            wander.Pause(interactionFocusDuration);
        }
    }

    private string GetNextLine()
    {
        string line = dialogLines[currentLineIndex];

        if (loopDialogLines)
        {
            currentLineIndex = (currentLineIndex + 1) % dialogLines.Length;
        }
        else
        {
            currentLineIndex++;
        }

        return line;
    }
}
