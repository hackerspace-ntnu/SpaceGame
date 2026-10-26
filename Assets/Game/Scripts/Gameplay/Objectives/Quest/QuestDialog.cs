using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What a character says depends on where the crew are in a quest: a villager's hint before
    /// anybody has met the elder, the elder's plea and the offer, his reaction when the crew report
    /// the hut, his thanks once the oath is reforged.
    ///
    /// <para>
    /// <b>Routed through <see cref="DialogInteraction"/>, never beside it.</b> The interactor resolves
    /// one <c>IInteractable</c> per collider, so this is asked first by <c>DialogInteraction.Interact</c>
    /// (as a trader is) and only speaks when one of its entries matches. When none does, the character
    /// falls back to its ordinary lines.
    /// </para>
    /// <para>
    /// <b>One line per press</b>, in order. Finishing an entry's lines tells the server this player
    /// talked to the character (<see cref="QuestRequests.Talked"/>), which is what a "speak with the
    /// elder" step waits for. An entry that offers a chain asks its question after its last line.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(DialogInteraction), typeof(QuestActor))]
    public class QuestDialog : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("When these lines apply. The first entry whose window holds the chain's state speaks.")]
            public QuestStepWindow window;

            [TextArea(2, 5)] public string[] lines = Array.Empty<string>();

            [Header("Offer")]
            [Tooltip("Ask the player to accept this entry's chain after the last line. Only asked " +
                     "while nobody has accepted it.")]
            public bool offerChain;

            [TextArea(2, 4)] public string question = string.Empty;
            public string yesLabel = "Yes";
            public string noLabel = "No";

            [TextArea(2, 4)] public string acceptedLine = string.Empty;
            [TextArea(2, 4)] public string declinedLine = string.Empty;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private DialogInteraction dialog;
        private QuestActor actor;

        private Entry speaking;
        private int nextLine;

        private void Awake()
        {
            dialog = GetComponent<DialogInteraction>();
            actor = GetComponent<QuestActor>();
        }

        /// <summary>SERVER: whether some entry here offers <paramref name="chainId"/>.</summary>
        public bool Offers(string chainId)
        {
            foreach (Entry entry in entries)
                if (entry.offerChain && entry.window.chain != null && entry.window.chain.Id == chainId)
                    return true;

            return false;
        }

        /// <summary>
        /// Says the next quest line to <paramref name="interactor"/>. False when no entry matches the
        /// quest as it stands, so the caller can fall back to the character's ordinary lines.
        /// </summary>
        public bool TrySpeak(Interactor interactor)
        {
            Entry entry = Matching();
            if (entry == null)
            {
                speaking = null;
                return false;
            }

            // The quest moved on since the last press: start the new lines from the top.
            if (entry != speaking)
            {
                speaking = entry;
                nextLine = 0;
            }

            if (dialog.IsAwaitingAnswer) return true;

            if (nextLine >= entry.lines.Length)
            {
                if (WillOffer(entry))
                {
                    Offer(entry, interactor);
                    return true;
                }

                // Past the last line, and somebody else has since accepted: start the lines again.
                nextLine = 0;
                if (entry.lines.Length == 0) return false;
            }

            dialog.Say(entry.lines[nextLine++], interactor);
            if (nextLine == entry.lines.Length && !WillOffer(entry)) Finish(interactor);
            return true;
        }

        private Entry Matching()
        {
            foreach (Entry entry in entries)
                if (entry.window.IsInside) return entry;

            return null;
        }

        private static bool WillOffer(Entry entry)
        {
            return entry.offerChain && entry.window.TryGetPosition(out int position) && position < 0;
        }

        private void Offer(Entry entry, Interactor interactor)
        {
            bool asked = dialog.AskQuestion(entry.question, entry.yesLabel, entry.noLabel,
                onYes: () =>
                {
                    QuestRequests.Accepted(actor, entry.window.chain.Id, interactor.transform);
                    Reply(entry.acceptedLine, interactor);
                    Finish(interactor);
                },
                onNo: () => Reply(entry.declinedLine, interactor));

            if (!asked) Debug.LogWarning($"[Quest] {name} could not ask its question: no dialog popup.", this);
        }

        private void Reply(string line, Interactor interactor)
        {
            nextLine = 0;
            if (!string.IsNullOrWhiteSpace(line)) dialog.Say(line, interactor);
        }

        private void Finish(Interactor interactor)
        {
            nextLine = 0;
            QuestRequests.Talked(actor, interactor.transform);
        }
    }
}
