// Activates when the agent loses its target. Moves to the last known position, searches
// briefly, then deactivates — passing control back to lower-priority modules.
// Sits between ChaseModule (Reactive=20) and WanderModule (Fallback=0).
//
// Reads AgentTargeting for both the "target lost" edge and the position to search, so it
// investigates the entity the agent was actually fighting rather than whichever one this
// module would have resolved for itself.
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
        [SerializeField] private SfxId searchId = SfxId.EntitySearch;
        [SerializeField] private EventReference searchSound;

        private bool isSearching;
        private float searchTimer;
        private Vector3 searchPosition;
        private bool hadTarget; // tracks edge: target just became lost

        // Set by RestoreSearch, consumed by the next OnEnable — see the comment there.
        private bool restoredSearch;

        // ── Persisted state ───────────────────────────────────────────────────────
        public bool IsSearching => isSearching;
        public float SearchTimer => searchTimer;
        public Vector3 SearchPosition => searchPosition;

        /// <summary>Whether a target was held last frame. This is the edge this module starts on.</summary>
        public bool HadTarget => hadTarget;

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
            hadTarget = false;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <paramref name="hadTargetLastFrame"/> matters as much as the search itself. This module
        /// only ever starts on a falling edge — a target held last frame and gone this one — so an
        /// agent that reloaded with <c>hadTarget</c> at false could never start searching no matter
        /// what it remembered. That is why <c>AgentTargeting.LastKnownPosition</c> was being saved
        /// faithfully and then never acted on.
        /// </summary>
        public void RestoreSearch(bool searching, float timer, Vector3 position, bool hadTargetLastFrame)
        {
            searchTimer = Mathf.Max(0f, timer);

            // An expired search is not a search. Restoring one would have the agent stand at a
            // remembered position for exactly one frame before giving up.
            isSearching = searching && searchTimer > 0f;
            searchPosition = position;
            hadTarget = hadTargetLastFrame;
            restoredSearch = true;
        }

        public override string ModuleDescription =>
            "When the agent loses its target, moves to the last known position and searches for a short time before giving up.\n\n" +
            "Reads AgentTargeting for the lost-target edge and the last known position.\n\n" +
            "• searchDuration — how many seconds to search before returning to idle\n" +
            "• Automatically deactivates when a target is reacquired";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            AgentTargeting targeting = context.Targeting;
            if (targeting == null)
                return null;

            bool hasTarget = targeting.HasTarget;

            // Detect falling edge: had target last frame, lost it this frame.
            if (hadTarget && !hasTarget && !isSearching && targeting.HasLastKnownPosition)
            {
                searchPosition = targeting.LastKnownPosition;
                isSearching = true;
                searchTimer = searchDuration;

                Sfx.Play(searchId, transform.position, searchSound, GetInstanceID());
            }

            hadTarget = hasTarget;

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
