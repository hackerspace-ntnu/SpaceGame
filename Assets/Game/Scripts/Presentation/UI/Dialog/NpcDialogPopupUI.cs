using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Presentation.Speech;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The screen-space dialog box: lines addressed to this machine's player, and yes/no questions.
    ///
    /// <para>
    /// A view of <see cref="Speech.Speaker"/>. A line with a speaker is said through that
    /// character's <see cref="Speech.Speaker"/> on the <see cref="SpeechChannel.Dialog"/> channel and
    /// shown here when it arrives on <see cref="Speech.Speaker.AnySaid"/> — so the mouth, the hands
    /// and the box all run off the one line, and a resident's reply sent to this player (said with
    /// <c>showPopup</c>) lands here the same way. Reveal timing is <see cref="Typewriter"/>'s, the
    /// clock the speaker's own line runs on.
    /// </para>
    /// </summary>
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

        private bool isTyping;
        private bool isQuestionActive;
        private bool presentingQuestion;
        private Action yesChoiceCallback;
        private Action noChoiceCallback;

        // The line on screen, on the typewriter clock, and the speaker whose line it is (null for a
        // line nobody in the world says). The speaker's line number tells this line from a later one.
        private string lineText = string.Empty;
        private float lineStartedAt;
        private float lineEndsAt;
        private float showAtLeastUntil;
        private bool lineAutoHides;
        private Speaker lineSpeaker;
        private int lineSpeakerNumber;

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

            Speaker.AnySaid += HandleSpeakerSaid;
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
                Speaker.AnySaid -= HandleSpeakerSaid;
                Instance = null;
            }
        }

        /// <param name="speaker">The character saying it, whose mouth moves while it types. Null
        /// for a line nobody in the world says.</param>
        public void Show(string message, float duration = -1f, Transform speaker = null)
        {
            ClearQuestionState();
            Say(message, duration > 0f ? duration : defaultDuration, speaker);
        }

        public void ShowQuestion(string message, string yesLabel, string noLabel, Action onYes, Action onNo,
                                 Transform speaker = null)
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

            presentingQuestion = true;
            Say(message, defaultDuration, speaker);
            presentingQuestion = false;
        }

        // Through the character's Speaker when there is one, so the line arrives back here on
        // AnySaid like any other line meant for this player; straight onto the box when not.
        private void Say(string message, float showAtLeast, Transform speaker)
        {
            Speaker voice = Speaker.Of(speaker);
            if (voice != null)
            {
                voice.Say(message, SpeechChannel.Dialog, showPopup: true, showAtLeast);
                return;
            }

            Present(message ?? string.Empty, Time.time, showAtLeast, null);
        }

        private void HandleSpeakerSaid(Speaker voice, string text, SpeechChannel channel)
        {
            if (!voice.OnPopup) return;

            // A question owns the box until it is answered: a line arriving from elsewhere would
            // leave the asker waiting on an answer the player can no longer give.
            if (isQuestionActive && !presentingQuestion) return;

            Present(text, Time.time, voice.LineEndsAt - Time.time, voice);
        }

        private void Present(string message, float startedAt, float showAtLeast, Speaker voice)
        {
            if (!popupRoot || !dialogText)
            {
                Debug.LogWarning("NpcDialogPopupUI is missing popupRoot or dialogText reference.");
                return;
            }

            lineText = message;
            lineStartedAt = startedAt;
            showAtLeastUntil = startedAt + showAtLeast;
            lineEndsAt = Mathf.Max(startedAt + Typewriter.DurationFor(message), showAtLeastUntil);
            lineAutoHides = !isQuestionActive;
            lineSpeaker = voice;
            lineSpeakerNumber = voice != null ? voice.LineNumber : 0;

            if (lineAutoHides)
            {
                // Regular line display must not keep question alternatives visible.
                SetChoiceUIActive(false);
            }

            popupRoot.SetActive(true);
            isTyping = Typewriter.TypingSeconds(message) > 0f;
            dialogText.text = message;
            dialogText.maxVisibleCharacters = isTyping ? Typewriter.VisibleChars(message, 0f) : int.MaxValue;
        }

        private void Update()
        {
            if (!IsVisible || !dialogText) return;

            if (isTyping)
            {
                float elapsed = Time.time - lineStartedAt;
                isTyping = elapsed < Typewriter.TypingSeconds(lineText);
                dialogText.maxVisibleCharacters = isTyping
                    ? Typewriter.VisibleChars(lineText, elapsed)
                    : int.MaxValue;
            }

            if (lineAutoHides && !isTyping && Time.time >= lineEndsAt)
            {
                popupRoot.SetActive(false);
                SetChoiceUIActive(false);
                isQuestionActive = false;
                yesChoiceCallback = null;
                noChoiceCallback = null;
            }
        }

        /// <summary>The speaker of the line on screen, while that is still the line it is saying.</summary>
        private Speaker CurrentLineSpeaker =>
            lineSpeaker != null && lineSpeaker.LineNumber == lineSpeakerNumber ? lineSpeaker : null;

        public void Hide()
        {
            CurrentLineSpeaker?.Hush();
            lineSpeaker = null;

            isTyping = false;
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

            isTyping = false;
            dialogText.maxVisibleCharacters = int.MaxValue;

            // A spoken line ends when its speaker's does, so the box and the mouth stop together.
            Speaker voice = CurrentLineSpeaker;
            if (voice != null)
            {
                voice.FinishLine();
                lineEndsAt = voice.LineEndsAt;
            }
            else
            {
                lineEndsAt = Mathf.Max(Time.time + Typewriter.HoldAfterTyping, showAtLeastUntil);
            }
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
        }
    }
}
