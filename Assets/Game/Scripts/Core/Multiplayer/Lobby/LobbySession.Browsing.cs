using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The browser's half of <see cref="LobbySession"/>: the query behind the session list, and
    /// the one place its rate budget is spent.
    /// </summary>
    public partial class LobbySession
    {
        /// <summary>How many sessions one query asks for.</summary>
        private const int QueryPageSize = 25;

        /// <summary>
        /// The floor between two QueryLobbies calls, whoever asked for them.
        ///
        /// Lobby allows one query per second. The browser's automatic refresh already sits on that
        /// ceiling, so the Refresh button — which is a second query issued at a moment of the
        /// player's choosing, usually right in the middle of the automatic one's interval — is what
        /// pushes it over. 1.1s buys back the timer jitter that made it a coin toss.
        /// </summary>
        private const float QuerySpacing = 1.1f;

        /// <summary>When the last query was ISSUED. Rate limiters count arrivals, not completions.</summary>
        private float lastQueryAt = float.NegativeInfinity;

        /// <summary>
        /// Public lobbies with room, newest first. Private ones are reachable only by code.
        ///
        /// <para>
        /// Returns <b>null</b> when the query failed and an empty list when it succeeded and found
        /// nothing. The two used to be the same answer, which was harmless while the list was only
        /// fetched when the player asked for it — but the browser now refreshes every second, and a
        /// failure indistinguishable from "no sessions" empties the screen on every hiccup and puts
        /// it back on the next one. The caller keeps what it has when this returns null.
        /// </para>
        /// </summary>
        public async Task<List<Lobby>> QueryAsync()
        {
            try
            {
                await SpaceQueryAsync();

                QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = QueryPageSize,
                    Filters = new List<QueryFilter>
                    {
                        new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                    },
                    Order = new List<QueryOrder> { new(false, QueryOrder.FieldOptions.Created) }
                });

                return response.Results;
            }
            catch (Exception e) when (LobbyServiceErrors.IsSdkErrorPathFailure(e))
            {
                // Not our null. The service refused the request and the SDK threw this dereferencing
                // an error body it never managed to parse. Logged as a warning rather than an
                // exception because the stack points into the package and says nothing about the
                // actual refusal, and reported to the player as what it almost certainly is: one
                // query too many, seconds away from working again.
                Debug.LogWarning("[LobbySession] The lobby service refused the query and the SDK " +
                                 "threw on its own error path. Treating it as rate limiting.");

                Fail("Could not fetch the lobby list.\n(Too many requests — trying again shortly.)");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(LobbyServiceErrors.Describe(e, "Could not fetch the lobby list."));
                return null;
            }
        }

        /// <summary>
        /// Holds a query back until <see cref="QuerySpacing"/> has passed since the last one.
        ///
        /// Enforced here rather than in the browser because it is the browser having two callers —
        /// its own timer and its Refresh button — that trips the limiter, and neither of them can
        /// see the other's request. Held rather than dropped: the player pressed a button, and a
        /// refusal to refresh is worse than a refresh that takes a moment.
        ///
        /// <para>
        /// Waits by yielding frames rather than with <c>Task.Delay</c>. The continuation has to come
        /// back on the main thread — the request under it is a UnityWebRequest and throws anywhere
        /// else — and yielding is the form that cannot be resumed on a pool thread. It also reads
        /// <see cref="Time.unscaledTime"/>, which is main-thread only.
        /// </para>
        /// </summary>
        private async Task SpaceQueryAsync()
        {
            // Claimed before the first await, not after: two queries arriving in the same frame must
            // not both read the old stamp, both decide they are clear, and leave together.
            float sendAt = Mathf.Max(Time.unscaledTime, lastQueryAt + QuerySpacing);
            lastQueryAt = sendAt;

            while (Time.unscaledTime < sendAt) await Task.Yield();
        }
    }
}
