using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NpcDialogPopupUI : MonoBehaviour
{
    public static NpcDialogPopupUI Instance { get; private set; }
    public bool IsTyping => isTyping;
    public bool IsVisible => popupRoot != null && popupRoot.activeSelf;
    public bool IsQuestionActive => isQuestionActive;

    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TMP_Text dialogText;
    [SerializeField] private float defaultDuration = 2.5f;
    [Header("Choice UI")]
    [SerializeField] private GameObject choiceRoot;
    [SerializeField] private TMP_Text optionAText;
    [SerializeField] private TMP_Text optionBText;
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;
    [SerializeField] private TMP_Text yesButtonText;
    [SerializeField] private TMP_Text noButtonText;
    [Header("Typewriter")]
    [SerializeField] private bool useTypewriter = true;
    [SerializeField] private float charactersPerSecond = 28f;
    [SerializeField] private float holdAfterTyping = 0.8f;
    [SerializeField] private float punctuationExtraPause = 0.08f;

    private Coroutine showRoutine;
    private bool isTyping;
    private bool skipTypingRequested;
    private bool isQuestionActive;
    private Action yesChoiceCallback;
    private Action noChoiceCallback;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (!popupRoot)
        {
            popupRoot = gameObject;
        }

        popupRoot.SetActive(false);
        SetChoiceUIActive(false);

        if (yesButton)
        {
            yesButton.onClick.AddListener(HandleYesClicked);
        }

        if (noButton)
        {
            noButton.onClick.AddListener(HandleNoClicked);
        }
    }

    private void OnDestroy()
    {
        if (yesButton)
        {
            yesButton.onClick.RemoveListener(HandleYesClicked);
        }

        if (noButton)
        {
            noButton.onClick.RemoveListener(HandleNoClicked);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Show(string message, float duration = -1f)
    {
        ClearQuestionState();
        ShowInternal(message, duration, autoHide: true);
    }

    public void ShowQuestion(string message, string yesLabel, string noLabel, Action onYes, Action onNo)
    {
        yesChoiceCallback = onYes;
        noChoiceCallback = onNo;
        isQuestionActive = true;

        string yesDisplay = string.IsNullOrWhiteSpace(yesLabel) ? "(Y) Yes" : $"(Y) {yesLabel}";
        string noDisplay = string.IsNullOrWhiteSpace(noLabel) ? "(N) No" : $"(N) {noLabel}";

        if (yesButtonText)
        {
            yesButtonText.text = yesDisplay;
        }

        if (noButtonText)
        {
            noButtonText.text = noDisplay;
        }

        if (optionAText)
        {
            optionAText.text = yesDisplay;
        }

        if (optionBText)
        {
            optionBText.text = noDisplay;
        }

        SetChoiceUIActive(true);
        ShowInternal(message, defaultDuration, autoHide: false);
    }

    private void ShowInternal(string message, float duration, bool autoHide)
    {
        if (!popupRoot || !dialogText)
        {
            Debug.LogWarning("NpcDialogPopupUI is missing popupRoot or dialogText reference.");
            return;
        }

        float showDuration = duration > 0f ? duration : defaultDuration;

        popupRoot.SetActive(true);
        isTyping = false;
        skipTypingRequested = false;

        if (autoHide)
        {
            // Regular line display must not keep question alternatives visible.
            SetChoiceUIActive(false);
        }

        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
        }

        showRoutine = StartCoroutine(ShowRoutine(message, showDuration, autoHide));
    }

    public void Hide()
    {
        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
            showRoutine = null;
        }

        isTyping = false;
        skipTypingRequested = false;
        isQuestionActive = false;
        yesChoiceCallback = null;
        noChoiceCallback = null;
        SetChoiceUIActive(false);

        if (popupRoot)
        {
            popupRoot.SetActive(false);
        }
    }

    public void CompleteCurrentLine()
    {
        if (!isTyping)
        {
            return;
        }

        skipTypingRequested = true;
    }

    private IEnumerator ShowRoutine(string message, float duration, bool autoHide)
    {
        if (message == null)
        {
            message = string.Empty;
        }

        dialogText.text = message;
        dialogText.maxVisibleCharacters = useTypewriter && charactersPerSecond > 0f ? 0 : int.MaxValue;

        float elapsed = 0f;
        isTyping = false;
        skipTypingRequested = false;

        if (useTypewriter && charactersPerSecond > 0f)
        {
            isTyping = true;
            float baseDelay = 1f / charactersPerSecond;
            for (int i = 0; i < message.Length; i++)
            {
                if (skipTypingRequested)
                {
                    dialogText.maxVisibleCharacters = int.MaxValue;
                    break;
                }

                char character = message[i];
                dialogText.maxVisibleCharacters = i + 1;

                float delay = baseDelay;
                if (character == '.' || character == ',' || character == '!' || character == '?' || character == ';' || character == ':')
                {
                    delay += punctuationExtraPause;
                }

                elapsed += delay;
                yield return new WaitForSeconds(delay);
            }
            isTyping = false;
            skipTypingRequested = false;
        }
        else
        {
            dialogText.text = message;
            dialogText.maxVisibleCharacters = int.MaxValue;
        }

        if (autoHide)
        {
            float waitAfterTyping = Mathf.Max(holdAfterTyping, duration - elapsed);
            if (waitAfterTyping > 0f)
            {
                yield return new WaitForSeconds(waitAfterTyping);
            }

            popupRoot.SetActive(false);
            SetChoiceUIActive(false);
            isQuestionActive = false;
            yesChoiceCallback = null;
            noChoiceCallback = null;
        }
        showRoutine = null;
    }

    private void HandleYesClicked()
    {
        ChooseYes();
    }

    private void HandleNoClicked()
    {
        ChooseNo();
    }

    public void ChooseYes()
    {
        if (isTyping)
        {
            CompleteCurrentLine();
            return;
        }

        if (!isQuestionActive)
        {
            return;
        }

        Action callback = yesChoiceCallback;
        ClearQuestionState();
        callback?.Invoke();
    }

    public void ChooseNo()
    {
        if (isTyping)
        {
            CompleteCurrentLine();
            return;
        }

        if (!isQuestionActive)
        {
            return;
        }

        Action callback = noChoiceCallback;
        ClearQuestionState();
        callback?.Invoke();
    }

    private void ClearQuestionState()
    {
        isQuestionActive = false;
        yesChoiceCallback = null;
        noChoiceCallback = null;
        SetChoiceUIActive(false);

        if (optionAText)
        {
            optionAText.text = string.Empty;
        }

        if (optionBText)
        {
            optionBText.text = string.Empty;
        }
    }

    private void SetChoiceUIActive(bool isActive)
    {
        if (choiceRoot)
        {
            choiceRoot.SetActive(isActive);
        }
    }

    private void OnValidate()
    {
        defaultDuration = Mathf.Max(0f, defaultDuration);
        charactersPerSecond = Mathf.Max(1f, charactersPerSecond);
        holdAfterTyping = Mathf.Max(0f, holdAfterTyping);
        punctuationExtraPause = Mathf.Max(0f, punctuationExtraPause);
    }
}
