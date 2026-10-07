using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a search in progress: where the agent was heading and how long it had left.
    ///
    /// A search that has not started yet needs nothing here. An agent whose saved target has since
    /// died or logged out is handed that as a lost target by <c>AgentStateSaveable</c>
    /// (<c>AgentTargeting.RestoreMemory</c> counts it), and <see cref="SearchModule"/> starts a search
    /// on its next tick.
    ///
    /// Older saves also carry a <c>hadTarget</c> field. It is ignored on read
    /// (<c>MissingMemberHandling.Ignore</c>); the lost-target count replaced it.
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
        }

        public object CaptureState()
        {
            if (Search == null || !Search.IsSearching) return null;

            return new State
            {
                isSearching = Search.IsSearching,
                searchTimer = Search.SearchTimer,
                searchPosition = Search.SearchPosition,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Search == null) return;

            if (state == null)
            {
                Search.RestoreSearch(false, 0f, Vector3.zero);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Search.RestoreSearch(restored.isSearching, restored.searchTimer, restored.searchPosition);
        }
    }
}
