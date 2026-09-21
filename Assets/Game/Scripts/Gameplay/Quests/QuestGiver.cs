// A character with an errand for you. Ask, receive, move on a step.
//
// Deliberately NOT an IInteractable, and the reason is the one TraderInteraction's header already
// records: Interactor resolves exactly ONE IInteractable per collider via GetComponent, so a second
// one on a character that already has a DialogInteraction would make which of the two answers
// depend on component order — silently, per prefab, and differently the moment anybody reorders
// the inspector. Instead DialogInteraction asks this component whether it wants the conversation,
// which also buys the existing question popup, the existing Y/N keys and the existing typewriter
// for nothing.
//
// Progress is SHARED world state, not per player: one player hands in the item and the step
// advances for the whole session, the way a trader's stock is shared. It is saved by
// QuestGiverSaveable under the key "quest".
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.Quests
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DialogInteraction))]
    public class QuestGiver : MonoBehaviour, IPersistentEntity
    {
        [Header("Errand")]
        [Tooltip("What this character wants doing. Assigned by TownGenerator, or set by hand.")]
        [SerializeField] private Questline questline;

        [Header("Manners")]
        [Tooltip("Seconds before bringing the quest up again after being turned down, or after " +
                 "reminding you what it wants. Without it every attempt at an ordinary " +
                 "conversation is intercepted by the same request.")]
        [SerializeField] private float declineCooldown = 45f;

        [Tooltip("How long a one-off line stays on screen.")]
        [SerializeField] private float lineSeconds = 3f;

        /// <summary>
        /// How far along. Written ONLY by <see cref="OnStepSet"/> — which every machine runs,
        /// including the server and an offline session — so there is exactly one assignment site
        /// for it in the whole class.
        /// </summary>
        private int stepIndex;

        private float nextOfferTime;
        private bool loggedBadStep;

        public Questline Line => questline;
        public int StepIndex => stepIndex;
        public string QuestlineId => questline != null ? questline.ID : string.Empty;
        public bool IsFinished => QuestProgress.IsDone(stepIndex, questline);

        private void OnEnable()
        {
            this.NetOn(NetMsg.QuestHandIn, OnHandInRequested);
            this.NetOn(NetMsg.QuestStepSet, OnStepSet);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.QuestHandIn, OnHandInRequested);
            this.NetOff(NetMsg.QuestStepSet, OnStepSet);
        }

        /// <summary>Server or offline. A quest giver is ordinary chunk scenery with no authority of its own.</summary>
        private static bool ThisMachineDecides => !Network.IsNetworked || Network.Server;

        // ── Authoring ────────────────────────────────────────────────────────────

        /// <summary>Give this character an errand. Used by TownGenerator at edit time.</summary>
        public void Assign(Questline line)
        {
            questline = line;
            stepIndex = 0;
        }

        /// <summary>Put the progress back to a known value. For the save adapter only.</summary>
        public void RestoreStep(int step) =>
            stepIndex = questline == null ? 0 : Mathf.Clamp(step, 0, questline.StepCount);

        // ── Being talked to ──────────────────────────────────────────────────────

        /// <summary>
        /// Bring the errand up, if now is the moment. True means this component has taken over the
        /// conversation and <see cref="DialogInteraction"/> should not also say a line.
        /// </summary>
        public bool TryOfferQuest(DialogInteraction dialog, Interactor interactor)
        {
            if (dialog == null || interactor == null) return false;
            if (questline == null || IsFinished) return false;
            if (Time.time < nextOfferTime) return false;

            QuestStep step = QuestProgress.Current(stepIndex, questline);
            if (step == null) return false;

            if (step.required == null)
            {
                // Verify refuses this before a town ships, so reaching it means somebody hand-edited
                // the asset. Say so once rather than every time the player walks past.
                if (!loggedBadStep)
                {
                    loggedBadStep = true;
                    Debug.LogError($"[QuestGiver] '{name}' is on step {stepIndex} of '{questline.name}', " +
                                   "which asks for no item and can therefore never complete.", this);
                }

                return false;
            }

            IPlayerInventory inventory = InventoryOf(interactor);
            if (inventory == null) return false;

            // Not carrying it: the step's own line IS the reminder. It was written as a request
            // ("bring me a power cell"), which is exactly what somebody who has not brought one
            // yet needs to hear again.
            if (InventoryQuery.CountHeld(inventory, step.required) < 1)
            {
                if (NpcDialogPopupUI.Instance == null) return false;

                NpcDialogPopupUI.Instance.Show(step.text, lineSeconds);
                nextOfferTime = Time.time + declineCooldown;
                return true;
            }

            // Carrying it: ask. AskQuestion returns false when there is no popup or this character
            // is already waiting on an answer — propagate that rather than swallowing it, or the
            // conversation goes silent instead of falling through to ordinary dialog.
            return dialog.AskQuestion(
                step.text,
                "Hand it over",
                "Not yet",
                onYes: () => RequestHandIn(interactor),
                onNo: () => nextOfferTime = Time.time + declineCooldown);
        }

        private static IPlayerInventory InventoryOf(Interactor interactor)
        {
            PlayerController player = interactor.GetComponentInParent<PlayerController>();
            return player != null ? player.PlayerInventory : null;
        }

        // ── Handing over ─────────────────────────────────────────────────────────

        private void RequestHandIn(Interactor interactor)
        {
            PlayerController player = interactor != null
                ? interactor.GetComponentInParent<PlayerController>()
                : null;

            if (player == null) return;

            if (ThisMachineDecides)
            {
                // A direct call rather than a self-addressed message, which is TraderInteraction's
                // rule and holds for the same reason: sending to the server FROM the server only
                // dispatches locally if the entity has a NetChannel to dispatch through, and a
                // giver that never registered one would drop it in silence.
                ApplyHandIn(stepIndex, player.gameObject);
                return;
            }

            this.NetToServer(NetMsg.QuestHandIn,
                             new NetArg { A = stepIndex }.With(player.gameObject));
        }

        private void OnHandInRequested(in NetArg arg, ulong sender)
        {
            if (!ThisMachineDecides) return;

            GameObject player = arg.Resolve();
            if (player == null) return;

            ApplyHandIn(arg.A, player);
        }

        /// <summary>
        /// The whole decision, on the server alone: is this request about the step we are actually
        /// on, is the item really there, and is there room for what comes back.
        ///
        /// A refusal still broadcasts, with <c>B = 0</c>. That is not noise — it is how a stale
        /// client learns the real step index, and it is why a client can never lose an item to
        /// being out of date: nothing is taken before the check passes.
        /// </summary>
        private void ApplyHandIn(int requestedStep, GameObject player)
        {
            if (questline == null) return;

            if (requestedStep != stepIndex)
            {
                Broadcast(stepIndex, success: false, player);
                return;
            }

            QuestStep step = QuestProgress.Current(stepIndex, questline);
            if (step == null) return;

            IPlayerInventory inventory = player.GetComponent<PlayerController>()?.PlayerInventory;

            if (!QuestProgress.CanHandIn(inventory, step, out string why))
            {
                Debug.Log($"[QuestGiver] '{name}' refused a hand-in: {why}.", this);
                Broadcast(stepIndex, success: false, player);
                return;
            }

            // Take, then give. CanHandIn has already established there is room, so the player
            // cannot be left having paid for nothing.
            //
            // The server writes the player's hotbar directly, including a REMOTE player's:
            // Network.Simulates is true on the server for everything, so PlayerInventoryNetwork
            // takes its direct path and its NetworkList replicates the result back to the owner.
            // There is nothing to send.
            inventory.TryRemoveItem(InventoryQuery.FindHeld(inventory, step.required));
            if (step.reward != null) inventory.TryAddItem(step.reward);

            Broadcast(QuestProgress.NextStep(stepIndex, questline), success: true, player);
        }

        private void Broadcast(int step, bool success, GameObject player)
        {
            var arg = new NetArg { A = step, B = success ? 1 : 0 }.With(player);

            if (!Network.IsNetworked)
            {
                // Offline there is nobody to send to and no relay to send through, so apply it
                // here. Online the server applies its own broadcast along with everyone else.
                OnStepSet(arg, 0UL);
                return;
            }

            this.NetToAll(NetMsg.QuestStepSet, arg);
        }

        /// <summary>
        /// Every machine, including the server. The single writer of <see cref="stepIndex"/>.
        /// </summary>
        private void OnStepSet(in NetArg arg, ulong sender)
        {
            int previous = stepIndex;
            stepIndex = questline == null ? 0 : Mathf.Clamp(arg.A, 0, questline.StepCount);
            nextOfferTime = 0f;

            if (arg.B != 1 || stepIndex <= previous) return;

            // Only the machine whose own player handed over says anything. Everyone else just
            // moves their copy of the index and stays quiet.
            GameObject handedOver = arg.Resolve();
            if (handedOver == null || !IsLocalPlayer(handedOver)) return;

            SpeakThanks(previous);
        }

        private static bool IsLocalPlayer(GameObject candidate)
        {
            PlayerController local = GameplayMenuScope.FindLocalPlayer();
            return local != null && local.gameObject == candidate;
        }

        private void SpeakThanks(int completedStep)
        {
            if (NpcDialogPopupUI.Instance == null) return;

            QuestStep step = questline.StepAt(completedStep);
            string line = step != null && !string.IsNullOrWhiteSpace(step.thanksLine)
                ? step.thanksLine
                : IsFinished ? questline.completionLine : string.Empty;

            if (!string.IsNullOrWhiteSpace(line))
                NpcDialogPopupUI.Instance.Show(line, lineSeconds);
        }

        private void OnValidate()
        {
            declineCooldown = Mathf.Max(0f, declineCooldown);
            lineSeconds = Mathf.Max(0.5f, lineSeconds);
        }
    }
}
