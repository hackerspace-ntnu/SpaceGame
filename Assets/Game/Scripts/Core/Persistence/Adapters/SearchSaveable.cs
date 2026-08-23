using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a search in progress: where the agent was heading, how long it had left, and whether
    /// it was holding a target the frame before.
    ///
    /// <b>This is what makes <c>AgentStateSaveable</c>'s last-known position do anything.</b>
    /// <see cref="SearchModule"/> starts only on a falling edge — a target held last frame, gone this
    /// frame — and after a load its <c>hadTarget</c> was false, so the edge could never fire. The
    /// position an agent's memory was carefully carried across the save was then never walked to by
    /// anybody. Restoring <c>hadTarget</c> is the smaller half of the fix and the more important one:
    /// an agent whose saved target has since died or logged out now correctly notices it is gone and
    /// goes to look, instead of standing still with a memory it cannot act on.
    ///
    /// Nothing here is a reference, so it is applied in <see cref="RestoreState"/> rather than
    /// deferred.
    /// </summary>
    [RequireComponent(typeof(SearchModule))]
    public class SearchSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "search";          // written into save files — NEVER rename

        private SearchModule search;

        private SearchModule Search => search != null ? search : search = GetComponent<SearchModule>();

        public string SaveKey => Key;

        public struct State
        {
            public bool isSearching;
            public float searchTimer;
            public Vector3 searchPosition;

            /// <summary>
            /// Whether a target was held on the last tick. Not a detail — it is the edge the module
            /// starts on, and without it a restored agent can never begin a search at all.
            /// </summary>
            public bool hadTarget;
        }

        public object CaptureState()
        {
            if (Search == null) return null;

            // hadTarget alone is worth a record: an agent still in a fight is one frame away from
            // needing it, and it costs four bytes against an AI that cannot search after every load.
            if (!Search.IsSearching && !Search.HadTarget) return null;

            return new State
            {
                isSearching = Search.IsSearching,
                searchTimer = Search.SearchTimer,
                searchPosition = Search.SearchPosition,
                hadTarget = Search.HadTarget,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Search == null) return;

            if (state == null)
            {
                Search.RestoreSearch(false, 0f, Vector3.zero, false);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Search.RestoreSearch(restored.isSearching, restored.searchTimer,
                                 restored.searchPosition, restored.hadTarget);
        }
    }
}
