// Activates when the agent loses its target. Moves to the last known position, searches
// briefly, then deactivates — passing control back to lower-priority modules.
// Sits between ChaseModule (Reactive=20) and WanderModule (Fallback=0).
//
// Reads AgentTargeting for both the lost target and the position to search, so it investigates the
// entity the agent was actually fighting rather than whichever one this module would have resolved
// for itself.
using UnityEngine;
using FMODUnity;
using SpaceGame.Audio;

namespace SpaceGame.Agents
{
    public class SearchModule : BehaviourModuleBase
    {
        [Header("Search")]
        [SerializeField] private float searchDuration = 4f;
        [SerializeField] private float stopDistance = 0.6f;
        [SerializeField] private float speedMultiplier = 1.1f;

        [Header("Audio")]
        [SerializeField] private EventReference searchSound;

        private bool isSearching;
        private float searchTimer;
        private Vector3 searchPosition;

        // The AgentTargeting.LostCount this module has already acted on. A loss is latched rather
        // than seen as an edge: Chase out-ranks this module and claims every frame a target is held,
        // so the one frame a target goes is never a frame this module is ticked on.
        private int handledLoss;

        // Set by RestoreSearch, consumed by the next OnEnable — see the comment there.
        private bool restoredSearch;

        // ── Persisted state ───────────────────────────────────────────────────────
        public bool IsSearching => isSearching;
        public float SearchTimer => searchTimer;
        public Vector3 SearchPosition => searchPosition;

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive - 1); // 19 — just below Chase

        private void OnEnable()
        {
            // A restore must survive this. The search state is what makes the last-known position
            // AgentTargeting persists mean anything — see RestoreSearch.
            if (restoredSearch)
            {
                restoredSearch = false;
                return;
            }

            isSearching = false;
            searchTimer = 0f;

            // Losses from before this module was running are history, not a reason to search now.
            handledLoss = CurrentLossCount();
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// A target the save held but the load cannot give back is a loss too: AgentTargeting's
        /// <c>RestoreMemory</c> counts it, after this runs, and the next tick starts a search from
        /// the restored last-known position.
        /// </summary>
        public void RestoreSearch(bool searching, float timer, Vector3 position)
        {
            searchTimer = Mathf.Max(0f, timer);

            // An expired search is not a search. Restoring one would have the agent stand at a
            // remembered position for exactly one frame before giving up.
            isSearching = searching && searchTimer > 0f;
            searchPosition = position;
            handledLoss = CurrentLossCount();
            restoredSearch = true;
        }

        private int CurrentLossCount() =>
            TryGetComponent(out AgentTargeting targeting) ? targeting.LostCount : 0;

        public override string ModuleDescription =>
            "When the agent loses its target, moves to the last known position and searches for a short time before giving up.\n\n" +
            "Reads AgentTargeting for the lost target and the last known position.\n\n" +
            "• searchDuration — how many seconds to search before returning to idle\n" +
            "• Automatically deactivates when a target is reacquired";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            AgentTargeting targeting = context.Targeting;
            if (targeting == null)
                return null;

            bool hasTarget = targeting.HasTarget;

            if (targeting.LostCount != handledLoss)
            {
                handledLoss = targeting.LostCount;

                if (!hasTarget && !isSearching && targeting.HasLastKnownPosition)
                {
                    searchPosition = targeting.LastKnownPosition;
                    isSearching = true;
                    searchTimer = searchDuration;

                    Sfx.Play(searchId, transform.position, searchSound, GetInstanceID());
                }
            }

            if (!isSearching)
                return null;

            // Abort if chase reacquired.
            if (hasTarget)
            {
                isSearching = false;
                return null;
            }

            searchTimer -= deltaTime;
            if (searchTimer <= 0f)
            {
                isSearching = false;
                return null;
            }

            return MoveIntent.MoveTo(searchPosition, stopDistance, speedMultiplier);
        }

        protected override void OnValidate()
        {
            searchDuration = Mathf.Max(0.1f, searchDuration);
            stopDistance = Mathf.Max(0.01f, stopDistance);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }
    }
}
