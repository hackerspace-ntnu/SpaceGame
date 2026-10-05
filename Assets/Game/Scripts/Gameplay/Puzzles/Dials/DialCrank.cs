// A crank on a dial puzzle: right-click to turn its dial one step. On the crank's own collider, so
// the plinth and pole — solid colliders with no interactable — block the ray rather than answering
// for the crank.
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    public class DialCrank : MonoBehaviour, IInteractable, IInteractionReadout
    {
        [Tooltip("Which of the set's dials this crank turns.")]
        [SerializeField] private int dial;

        [SerializeField] private string label = "Crank";
        [SerializeField] private string prompt = "RMB: turn";

        private DialSet set;

        // Lazy: resolved on first use so a crank works however its hierarchy was assembled, and an
        // EditMode test that never runs Awake can still ask it questions.
        private DialSet Set => set != null ? set : set = GetComponentInParent<DialSet>();

        public bool CanInteract() => Set != null && Set.CanTurn;

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;
            Set.RequestTurn(dial);
        }

        public string Label => label;

        public string Prompt => prompt;

        public float? Value01 => Set != null ? Set.Progress : null;

        public string ValueText => Set != null && Set.IsKnown ? Set.Describe(dial) : string.Empty;
    }
}
