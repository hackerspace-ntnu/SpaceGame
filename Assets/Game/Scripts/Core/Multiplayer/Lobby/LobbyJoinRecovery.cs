using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// The retry policy wrapped around joining a lobby: a join refused because this player is
    /// still listed somewhere releases every membership they hold and tries once more.
    ///
    /// <para>
    /// A lobby membership outlives the session that created it. It is given up in exactly one
    /// place — pressing Leave — so a host that crashed, a Relay connection that timed out, or a
    /// process that was killed all leave this player's id sitting in a lobby they are no longer
    /// in. Anonymous authentication hands back the SAME player id on the next launch, so those
    /// ghosts are still ours and they accumulate; joining a lobby one of them occupies is
    /// answered with 409 <i>player is already a member of the lobby</i>.
    /// </para>
    ///
    /// <para>
    /// The Lobby SDK has its own 409 recovery and it cannot be leaned on. Joining by id, it
    /// gives up unless <c>GetJoinedLobbies</c> returns EXACTLY one lobby — and it then joins
    /// that lobby rather than the one that was asked for. Two ghosts and it rethrows the raw
    /// HttpException, which is exactly what a couple of playtests leave behind.
    /// </para>
    ///
    /// <para>
    /// The service calls arrive as delegates so this can be tested without one.
    /// </para>
    /// </summary>
    public static class LobbyJoinRecovery
    {
        /// <summary>
        /// Joins, sweeping stale memberships on a conflict. Retried once and no more: a conflict
        /// that outlives the sweep is a refusal the player needs to read, not something to keep
        /// hammering a rate limiter over.
        /// </summary>
        /// <param name="join">Performs the join. Called twice at most.</param>
        /// <param name="joinedLobbies">Ids of every lobby this player is still a member of.</param>
        /// <param name="leave">Removes this player from one lobby.</param>
        public static async Task<Lobby> JoinAsync(
            Func<Task<Lobby>> join,
            Func<Task<List<string>>> joinedLobbies,
            Func<string, Task> leave)
        {
            try
            {
                return await join();
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyConflict)
            {
                List<string> stale = await joinedLobbies();

                // Nothing to release means nothing about a second attempt would differ, so the
                // service's own reason is left to reach the player rather than spent on a retry.
                if (stale == null || stale.Count == 0) throw;

                Debug.LogWarning($"[LobbyJoinRecovery] Join refused — this player is still a member of " +
                                 $"{stale.Count} lobby/lobbies. Releasing them and retrying.");

                if (await ReleaseAsync(stale, leave) == 0) throw;

                return await join();
            }
        }

        /// <summary>
        /// Removes this player from every lobby in <paramref name="stale"/>, and counts how many
        /// actually let go.
        ///
        /// Removals run against a rate limiter, and one refusal must not strand the rest of the
        /// sweep: a player with two ghosts would stay locked out by whichever one happened to
        /// answer first.
        /// </summary>
        private static async Task<int> ReleaseAsync(List<string> stale, Func<string, Task> leave)
        {
            int released = 0;

            foreach (string lobbyId in stale)
            {
                try
                {
                    await leave(lobbyId);
                    released++;
                }
                catch (Exception removal)
                {
                    Debug.LogWarning($"[LobbyJoinRecovery] Could not release {lobbyId}: {removal.Message}");
                }
            }

            return released;
        }
    }
}
