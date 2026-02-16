using System.Collections;
using TMPro;
using UnityEngine;

public class NpcDialogPopupUI : MonoBehaviour
{
    public static NpcDialogPopupUI Instance { get; private set; }

    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TMP_Text dialogText;
    [SerializeField] private float defaultDuration = 2.5f;
    [Header("Typewriter")]
    [SerializeField] private bool useTypewriter = true;
    [SerializeField] private float charactersPerSecond = 28f;
    [SerializeField] private float holdAfterTyping = 0.8f;
    [SerializeField] private float punctuationExtraPause = 0.08f;

    private Coroutine showRoutine;

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
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Show(string message, float duration = -1f)
    {
        if (!popupRoot || !dialogText)
        {
            Debug.LogWarning("NpcDialogPopupUI is missing popupRoot or dialogText reference.");
            return;
        }

        float showDuration = duration > 0f ? duration : defaultDuration;

        popupRoot.SetActive(true);

        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
        }

        showRoutine = StartCoroutine(ShowRoutine(message, showDuration));
    }

    public void Hide()
    {
        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
            showRoutine = null;
        }

        if (popupRoot)
        {
            popupRoot.SetActive(false);
        }
    }

    private IEnumerator ShowRoutine(string message, float duration)
    {
        dialogText.text = string.Empty;
        float elapsed = 0f;

        if (useTypewriter && charactersPerSecond > 0f)
        {
            float baseDelay = 1f / charactersPerSecond;
            for (int i = 0; i < message.Length; i++)
            {
                char character = message[i];
                dialogText.text += character;

                float delay = baseDelay;
                if (character == '.' || character == ',' || character == '!' || character == '?' || character == ';' || character == ':')
                {
                    delay += punctuationExtraPause;
                }

                elapsed += delay;
                yield return new WaitForSeconds(delay);
            }
        }
        else
        {
            dialogText.text = message;
        }

        float waitAfterTyping = Mathf.Max(holdAfterTyping, duration - elapsed);
        if (waitAfterTyping > 0f)
        {
            yield return new WaitForSeconds(waitAfterTyping);
        }
        popupRoot.SetActive(false);
        showRoutine = null;
    }

    private void OnValidate()
    {
        defaultDuration = Mathf.Max(0f, defaultDuration);
        charactersPerSecond = Mathf.Max(1f, charactersPerSecond);
        holdAfterTyping = Mathf.Max(0f, holdAfterTyping);
        punctuationExtraPause = Mathf.Max(0f, punctuationExtraPause);
    }
}
