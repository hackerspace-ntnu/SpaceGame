using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

//do not try to read this code. it is just a bunch of features related to dialoge interactions. Not sure if it is worth it to decompose
public enum DialogMode
{
    PredefinedSequence,
    RandomFromGlobalPool,
    RandomFromPredefinedPool,
    BranchingSequence
}

public enum BranchStepType
{
    Line,
    Question
}

[System.Serializable]
public class BranchDialogStep
{
    public BranchStepType stepType = BranchStepType.Line;
    [TextArea(2, 5)] public string text;
    public int nextStepIndex = -1;
    public int yesNextStepIndex = -1;
    public int noNextStepIndex = -1;
    public string yesLabel = "Yes";
    public string noLabel = "No";
    public UnityEvent onStepShown;
    public UnityEvent onYesChosen;
    public UnityEvent onNoChosen;
}

public class DialogInteraction : MonoBehaviour, IInteractable
{
    [Header("Dialog")]
    [SerializeField] private DialogMode dialogMode = DialogMode.PredefinedSequence;
    [TextArea(2, 5)]
    [SerializeField] private string[] dialogLines =
    {
        "Hey there, traveler.",
        "The desert gets colder at night.",
        "Stay alert out here."
    };
    [TextArea(2, 5)]
    [SerializeField] private string[] predefinedRandomPool;
    [SerializeField] private DialogPool globalDialogPool;
    [SerializeField] private BranchDialogStep[] branchingSteps;
    [SerializeField] private bool loopDialogLines = true;
    [SerializeField] private bool allowRestartAfterEnd = true;
    [SerializeField] private bool finishCurrentLineOnInteractWhileTyping = true;
    [SerializeField] private float popupDuration = 2.5f;
    [SerializeField] private float interactionFocusDuration = 2.5f;
    [SerializeField] private float restartFromBeginningAfterSeconds = 10f;
    [Header("Interaction Delay")]
    [FormerlySerializedAs("useDelayBetweenRandomSentences")]
    [SerializeField] private bool useDelayBetweenDialogues = false;
    [FormerlySerializedAs("randomSentenceDelaySeconds")]
    [SerializeField] private float dialogueDelaySeconds = 3f;

    

    private int currentLineIndex;
    private float lastInteractionTime = -1f;
    private string[] randomCycleLines;
    private bool randomSentenceActive;
    private bool dialogueSessionActive;
    private float nextDialogueAvailableTime;
    private bool warnedMissingGlobalPoolAsset;
    private bool waitingForBranchChoice;

    private void Update()
    {
        if (!waitingForBranchChoice)
        {
            return;
        }

        if (NpcDialogPopupUI.Instance == null || !NpcDialogPopupUI.Instance.IsQuestionActive)
        {
            return;
        }

        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.yKey.wasPressedThisFrame)
        {
            NpcDialogPopupUI.Instance.ChooseYes();
        }
        else if (Keyboard.current.nKey.wasPressedThisFrame)
        {
            NpcDialogPopupUI.Instance.ChooseNo();
        }
    }


    public bool CanInteract()
    {
        if (useDelayBetweenDialogues && !dialogueSessionActive && Time.time < nextDialogueAvailableTime)
        {
            return false;
        }

        if (dialogMode == DialogMode.BranchingSequence)
        {
            if (waitingForBranchChoice)
            {
                return true;
            }

            int branchCount = branchingSteps != null ? branchingSteps.Length : 0;
            if (branchCount == 0)
            {
                return false;
            }

            if (loopDialogLines || allowRestartAfterEnd)
            {
                return true;
            }

            return currentLineIndex >= 0 && currentLineIndex < branchCount;
        }

        if (IsRandomMode())
        {
            if (randomSentenceActive)
            {
                return true;
            }
        }

        int lineCount = GetLineCount();
        if (lineCount == 0)
        {
            return false;
        }

        if (loopDialogLines || allowRestartAfterEnd)
        {
            return true;
        }

        return currentLineIndex < lineCount;
    }

    public void Interact(Interactor interactor)
    {
        Debug.Log($"[DialogInteraction] Interact called on '{name}' by '{interactor.name}'.");

        if (ShouldRestartFromBeginning())
        {
            ResetProgress();
        }

        if (!CanInteract())
        {
            Debug.LogWarning($"[DialogInteraction] '{name}' cannot interact: dialog unavailable or already completed.");
            return;
        }

        if (finishCurrentLineOnInteractWhileTyping &&
            NpcDialogPopupUI.Instance != null &&
            NpcDialogPopupUI.Instance.IsTyping)
        {
            NpcDialogPopupUI.Instance.CompleteCurrentLine();
            return;
        }

        if (IsRandomMode())
        {
            HandleRandomPoolInteraction(interactor);
            return;
        }

        if (dialogMode == DialogMode.BranchingSequence)
        {
            HandleBranchingInteraction(interactor);
            return;
        }

        int lineCount = GetLineCount();
        if (!loopDialogLines && currentLineIndex >= lineCount)
        {
            if (NpcDialogPopupUI.Instance != null)
            {
                NpcDialogPopupUI.Instance.Hide();
            }

            EndDialogueSessionWithDelay();

            if (allowRestartAfterEnd)
            {
                ResetProgress();
            }

            return;
        }

        string line = GetNextLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        Debug.Log($"[DialogInteraction] Showing dialog line: \"{line}\"");

        if (NpcDialogPopupUI.Instance != null)
        {
            Debug.Log("[DialogInteraction] Found NpcDialogPopupUI instance. Showing popup.");
            NpcDialogPopupUI.Instance.Show(line, popupDuration);
        }
        else
        {
            Debug.LogWarning("[DialogInteraction] NpcDialogPopupUI instance not found in scene. Add it to your UI Canvas.");
            Debug.Log($"[DialogInteraction] Fallback dialog log: {line}");
        }

        FocusOnInteractor(interactor);

        lastInteractionTime = Time.time;
    }

    private void HandleRandomPoolInteraction(Interactor interactor)
    {
        if (randomSentenceActive && NpcDialogPopupUI.Instance != null && !NpcDialogPopupUI.Instance.IsVisible)
        {
            randomSentenceActive = false;
            EndDialogueSessionWithDelay();
        }

        if (randomSentenceActive)
        {
            if (NpcDialogPopupUI.Instance != null)
            {
                NpcDialogPopupUI.Instance.Hide();
            }

            randomSentenceActive = false;
            EndDialogueSessionWithDelay();
            lastInteractionTime = Time.time;
            return;
        }

        string line = GetNextLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (NpcDialogPopupUI.Instance != null)
        {
            NpcDialogPopupUI.Instance.Show(line, popupDuration);
            randomSentenceActive = true;
            BeginDialogueSession();
        }
        else
        {
            Debug.LogWarning("[DialogInteraction] NpcDialogPopupUI instance not found in scene. Add it to your UI Canvas.");
            Debug.Log($"[DialogInteraction] Fallback dialog log: {line}");
        }

            FocusOnInteractor(interactor);

        lastInteractionTime = Time.time;
    }

    private void HandleBranchingInteraction(Interactor interactor)
    {
        if (waitingForBranchChoice)
        {
            return;
        }

        int stepCount = branchingSteps != null ? branchingSteps.Length : 0;
        if (stepCount == 0)
        {
            return;
        }

        if (currentLineIndex < 0 || currentLineIndex >= stepCount)
        {
            if (NpcDialogPopupUI.Instance != null)
            {
                NpcDialogPopupUI.Instance.Hide();
            }

            EndDialogueSessionWithDelay();

            if (allowRestartAfterEnd || loopDialogLines)
            {
                ResetProgress();
            }

            return;
        }

        BranchDialogStep step = branchingSteps[currentLineIndex];
        if (step == null || string.IsNullOrWhiteSpace(step.text))
        {
            currentLineIndex = ResolveNextIndex(step != null ? step.nextStepIndex : -1, currentLineIndex + 1);
            return;
        }

        if (step.stepType == BranchStepType.Question)
        {
            ShowBranchQuestion(step, interactor);
            return;
        }

        if (NpcDialogPopupUI.Instance != null)
        {
            NpcDialogPopupUI.Instance.Show(step.text, popupDuration);
            BeginDialogueSession();
        }
        else
        {
            Debug.Log(step.text);
        }

        step.onStepShown?.Invoke();
        currentLineIndex = ResolveNextIndex(step.nextStepIndex, currentLineIndex + 1);
        lastInteractionTime = Time.time;

        FocusOnInteractor(interactor);
    }

    private void ShowBranchQuestion(BranchDialogStep step, Interactor interactor)
    {
        waitingForBranchChoice = true;
        BeginDialogueSession();
        step.onStepShown?.Invoke();

        FocusOnInteractor(interactor);

        if (NpcDialogPopupUI.Instance == null)
        {
            waitingForBranchChoice = false;
            Debug.LogWarning("[DialogInteraction] NpcDialogPopupUI instance not found in scene. Question cannot be answered.");
            return;
        }

        NpcDialogPopupUI.Instance.ShowQuestion(
            step.text,
            step.yesLabel,
            step.noLabel,
            () =>
            {
                waitingForBranchChoice = false;
                step.onYesChosen?.Invoke();
                currentLineIndex = ResolveNextIndex(step.yesNextStepIndex, currentLineIndex + 1);
                lastInteractionTime = Time.time;
                HandleBranchingInteraction(interactor);
            },
            () =>
            {
                waitingForBranchChoice = false;
                step.onNoChosen?.Invoke();
                currentLineIndex = ResolveNextIndex(step.noNextStepIndex, currentLineIndex + 1);
                lastInteractionTime = Time.time;
                HandleBranchingInteraction(interactor);
            });
    }

    private int ResolveNextIndex(int configuredIndex, int fallbackIndex)
    {
        if (configuredIndex >= 0)
        {
            return configuredIndex;
        }

        return fallbackIndex;
    }

    private string GetNextLine()
    {
        int lineCount = GetLineCount();
        if (lineCount == 0)
        {
            return string.Empty;
        }

        if (currentLineIndex >= lineCount)
        {
            if (!loopDialogLines)
            {
                return string.Empty;
            }

            if (dialogMode == DialogMode.PredefinedSequence)
            {
                EndDialogueSessionWithDelay();
                ResetProgress();
                if (useDelayBetweenDialogues)
                {
                    return string.Empty;
                }
            }
            else
            {
                ResetProgress();
            }
            lineCount = GetLineCount();
            if (lineCount == 0)
            {
                return string.Empty;
            }
        }

        string line = GetLineAt(currentLineIndex);
        currentLineIndex++;
        BeginDialogueSession();
        return line;
    }

    private int GetLineCount()
    {
        if (IsRandomMode())
        {
            EnsureRandomCycle();
            return randomCycleLines != null ? randomCycleLines.Length : 0;
        }

        return dialogLines != null ? dialogLines.Length : 0;
    }

    private string GetLineAt(int index)
    {
        if (IsRandomMode())
        {
            EnsureRandomCycle();
            if (randomCycleLines == null || index < 0 || index >= randomCycleLines.Length)
            {
                return string.Empty;
            }

            return randomCycleLines[index];
        }

        if (dialogLines == null || index < 0 || index >= dialogLines.Length)
        {
            return string.Empty;
        }

        return dialogLines[index];
    }

    private void EnsureRandomCycle()
    {
        if (!IsRandomMode())
        {
            return;
        }

        string[] poolLines = GetActiveRandomPoolLines();
        if (poolLines == null || poolLines.Length == 0)
        {
            randomCycleLines = null;
            return;
        }

        if (randomCycleLines != null && randomCycleLines.Length == poolLines.Length)
        {
            return;
        }

        randomCycleLines = (string[])poolLines.Clone();
        Shuffle(randomCycleLines);
    }

    private void ResetProgress()
    {
        currentLineIndex = 0;
        waitingForBranchChoice = false;
        dialogueSessionActive = false;

        if (IsRandomMode())
        {
            string[] poolLines = GetActiveRandomPoolLines();
            randomCycleLines = poolLines != null ? (string[])poolLines.Clone() : null;
            if (randomCycleLines != null)
            {
                Shuffle(randomCycleLines);
            }
        }
    }

    private string[] GetActiveRandomPoolLines()
    {
        if (dialogMode == DialogMode.RandomFromGlobalPool)
        {
            if (globalDialogPool != null && globalDialogPool.lines != null && globalDialogPool.lines.Length > 0)
            {
                warnedMissingGlobalPoolAsset = false;
                return globalDialogPool.lines;
            }

            if (!warnedMissingGlobalPoolAsset)
            {
                Debug.LogWarning($"{name}: RandomFromGlobalPool has no DialogPool assigned. Using built-in global defaults.", this);
                warnedMissingGlobalPoolAsset = true;
            }

            return DialogPool.GetDefaultLines();
        }

        return predefinedRandomPool;
    }

    private bool IsRandomMode()
    {
        return dialogMode == DialogMode.RandomFromGlobalPool || dialogMode == DialogMode.RandomFromPredefinedPool;
    }

    private static void Shuffle(string[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            (array[i], array[swapIndex]) = (array[swapIndex], array[i]);
        }
    }

    private bool ShouldRestartFromBeginning()
    {
        if (restartFromBeginningAfterSeconds <= 0f)
        {
            return false;
        }

        if (currentLineIndex <= 0 || lastInteractionTime < 0f)
        {
            return false;
        }

        return Time.time - lastInteractionTime >= restartFromBeginningAfterSeconds;
    }

    private void FocusOnInteractor(Interactor interactor)
    {
        if (interactor == null)
            return;

        if (TryGetComponent(out InteractionFocusModule focusModule))
        {
            focusModule.FocusOn(interactor.transform, interactionFocusDuration);
        }

        if (TryGetComponent(out NpcBrain npcBrain))
        {
            npcBrain.FocusOn(interactor.transform, interactionFocusDuration);
        }
    }

    private void OnValidate()
    {
        popupDuration = Mathf.Max(0f, popupDuration);
        interactionFocusDuration = Mathf.Max(0f, interactionFocusDuration);
        restartFromBeginningAfterSeconds = Mathf.Max(0f, restartFromBeginningAfterSeconds);
        dialogueDelaySeconds = Mathf.Max(0f, dialogueDelaySeconds);
    }

    private void BeginDialogueSession()
    {
        dialogueSessionActive = true;
    }

    private void EndDialogueSessionWithDelay()
    {
        dialogueSessionActive = false;

        if (!useDelayBetweenDialogues)
        {
            return;
        }

        nextDialogueAvailableTime = Time.time + dialogueDelaySeconds;
    }
}
