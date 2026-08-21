using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Gameplay.Trading;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
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

    public class DialogInteraction : MonoBehaviour, IInteractable, IContextualInteractable
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


        [Header("Voice")]
        [Tooltip("The vocalisation played each time this character speaks a line. Set to None for " +
                 "someone who should read as silent — a terminal, or a mute character.")]
        [SerializeField] private SfxId voiceId = SfxId.NpcMumbleNeutral;
        [SerializeField] private EventReference voiceSound;

        /// <summary>
        /// Whether this character will talk to THIS person right now.
        ///
        /// <para>
        /// It will not while it is fighting them. The two halves of a character never spoke to each
        /// other before: <see cref="CanInteract()"/> answers from its own line counter, and
        /// <see cref="AgentTargeting"/> decides who the same character is chasing and swinging at
        /// without being asked about conversation. So a provoked Nomad ran the player down with
        /// "Press E" still lit on the crosshair, and pressing it opened a chat window mid-fight.
        /// </para>
        /// <para>
        /// Deliberately contextual rather than folded into <see cref="CanInteract()"/>: the answer
        /// differs per person. A character fighting one player must still be talkable by a second
        /// one standing behind them, and refusing everybody would also silence the character
        /// permanently for anyone watching the fight from outside it.
        /// </para>
        /// </summary>
        public bool CanInteract(Interactor interactor)
        {
            if (interactor == null) return true;

            return !IsFightingWith(interactor.transform);
        }

        /// <summary>
        /// Both halves of "in a fight with", because either can be true on its own.
        ///
        /// The acquired target is the live answer, but it is dropped the moment the player ducks
        /// behind a rock or steps past loseRange, and <see cref="ProvocationModule"/> re-asserts it
        /// on the next frame it can. Reading only the target opens a conversation in that gap; the
        /// grudge is what stays true across it.
        /// </summary>
        private bool IsFightingWith(Transform other)
        {
            if (other == null) return false;

            if (TryGetComponent(out AgentTargeting targeting) && targeting.IsFightingWith(other))
                return true;

            return TryGetComponent(out ProvocationModule provocation)
                   && provocation.IsProvoked
                   && provocation.Aggressor != null
                   && provocation.Aggressor.root == other.root;
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

            // Interactor asks this too before it gets here. Repeated because Interact is also
            // reachable from a UnityEvent and from InteractorRelay, and "cannot be talked to while
            // it is attacking you" has to hold on every route in, not only the one with a prompt.
            if (!CanInteract(interactor))
            {
                Debug.Log($"[DialogInteraction] '{name}' is fighting '{interactor.name}' — no conversation.");
                return;
            }

            // A trader asks about trade before it says anything else, because that is what the
            // player walked over for. Routed through here rather than TraderInteraction being its
            // own IInteractable: Interactor resolves ONE IInteractable per collider, so a second
            // one on the same character would make which of the two answers depend on component
            // order — silently, and differently per prefab.
            if (TryGetComponent(out TraderInteraction trader) && trader.TryOfferTrade(this, interactor))
            {
                return;
            }

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
                SpeakLine(line);
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
                SpeakLine(line);
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
                SpeakLine(step.text);
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
                ResolveTokens(step.text),
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

        /// <summary>
        /// Shows one line and gives it a voice.
        ///
        /// <para>
        /// Every dialog mode funnels through here. Playing at this transform rather than through the
        /// popup UI matters: the popup is a screen-space singleton with no position, so a mumble
        /// emitted there would come from nowhere and would not fall off as the player walks away
        /// from whoever is talking.
        /// </para>
        /// </summary>
        private void SpeakLine(string line)
        {
            NpcDialogPopupUI.Instance.Show(ResolveTokens(line), popupDuration);

            Sfx.Play(voiceId, transform.position, voiceSound, GetInstanceID());
        }

        // ─────────── Saying what this character is actually doing ───────────

        private NpcTaskModule taskModule;
        private bool taskModuleResolved;

        /// <summary>
        /// Fills in <c>{task}</c>, <c>{destination}</c> and <c>{doing}</c> from this character's
        /// current job.
        ///
        /// <para>
        /// The point of giving NPCs errands is that a player can find out about them, and a fixed
        /// line recited by someone three days into a journey hides the entire system. A token in an
        /// authored line is the cheapest possible way to let the writing ask.
        /// </para>
        /// <para>
        /// Resolved lazily and cached, including the null answer: most characters will never have a
        /// task module, and this runs on every line of every conversation.
        /// </para>
        /// </summary>
        private string ResolveTokens(string line)
        {
            if (!NpcSpeechTokens.HasToken(line)) return line;

            if (!taskModuleResolved)
            {
                taskModule = GetComponent<NpcTaskModule>();
                taskModuleResolved = true;
            }

            return NpcSpeechTokens.Resolve(line, taskModule);
        }

        // ─────────── Lending the question UI to other components ───────────

        /// <summary>
        /// Ask the player a yes/no question through this character, with the Y/N keys working.
        ///
        /// <para>
        /// Exposed because the keyboard half of a question lives here, not in the popup:
        /// <see cref="NpcDialogPopupUI"/> owns the buttons, and <see cref="Update"/> above owns Y
        /// and N — gated on <c>waitingForBranchChoice</c>. Anything else that wants to ask
        /// something (trading is the first) would otherwise get a question the player can only
        /// answer with the mouse, which no other prompt in the game requires.
        /// </para>
        /// <para>
        /// Returns false when there is no popup to ask through, so the caller can decide what a
        /// silent refusal means rather than being told a question was asked when it was not.
        /// </para>
        /// </summary>
        public bool AskQuestion(string question, string yesLabel, string noLabel,
                                System.Action onYes, System.Action onNo)
        {
            if (NpcDialogPopupUI.Instance == null || waitingForBranchChoice)
                return false;

            waitingForBranchChoice = true;
            BeginDialogueSession();

            NpcDialogPopupUI.Instance.ShowQuestion(
                ResolveTokens(question),
                yesLabel,
                noLabel,
                () =>
                {
                    waitingForBranchChoice = false;
                    lastInteractionTime = Time.time;
                    onYes?.Invoke();
                },
                () =>
                {
                    waitingForBranchChoice = false;
                    lastInteractionTime = Time.time;
                    EndDialogueSessionWithDelay();
                    onNo?.Invoke();
                });

            return true;
        }

        /// <summary>True while this character is waiting on an answer from the player.</summary>
        public bool IsAwaitingAnswer => waitingForBranchChoice;
    }
}
