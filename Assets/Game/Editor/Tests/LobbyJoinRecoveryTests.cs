using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using SpaceGame.Core.Lobbies;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="LobbyJoinRecovery"/>: stale membership recovery.
    ///
    /// A join is refused with 409 "player is already a member of the lobby" whenever this
    /// player's id is still listed in the lobby — which is what every session that ended any way
    /// other than pressing Leave leaves behind: the host crashed, Relay timed out, or the process
    /// was killed. Anonymous authentication hands back the SAME player id next launch, so those
    /// ghosts are still ours, and they pile up.
    ///
    /// The Lobby SDK carries its own 409 recovery and it cannot be relied on. Joining by id, it
    /// gives up unless GetJoinedLobbies returns EXACTLY one lobby, and it then joins whatever that
    /// one is rather than the lobby that was asked for. Two ghosts and it rethrows — which is
    /// precisely the state a couple of playtests leave behind.
    /// </summary>
    public class LobbyJoinRecoveryTests
    {
        [Test]
        public void LeavesNothingAloneWhenTheJoinSimplyWorks()
        {
            var released = new List<string>();
            int swept = 0;

            Lobby joined = Run(LobbyJoinRecovery.JoinAsync(
                () => Task.FromResult(new Lobby(id: "target")),
                () => { swept++; return Task.FromResult(new List<string> { "ghost" }); },
                id => { released.Add(id); return Task.CompletedTask; }));

            Assert.AreEqual("target", joined.Id);
            Assert.AreEqual(0, swept, "The happy path must not spend a request on the ghost sweep.");
            CollectionAssert.IsEmpty(released);
        }

        [Test]
        public void ReleasesEveryGhostThenJoinsAgain()
        {
            // THE regression test for this bug. Two ghosts is the case the SDK's own resolver
            // refuses to touch, so the 409 reached the player as a raw HttpException.
            var released = new List<string>();
            int attempts = 0;

            Lobby joined = Run(LobbyJoinRecovery.JoinAsync(
                () => ++attempts == 1 ? Conflict() : Task.FromResult(new Lobby(id: "target")),
                () => Task.FromResult(new List<string> { "target", "someOtherLobby" }),
                id => { released.Add(id); return Task.CompletedTask; }));

            Assert.AreEqual("target", joined.Id);
            Assert.AreEqual(2, attempts, "The join has to be retried once the ghosts are gone.");
            CollectionAssert.AreEquivalent(new[] { "target", "someOtherLobby" }, released);
        }

        [Test]
        public void RetriesOnceOnlyWhenTheConflictSurvives()
        {
            // No loop. A conflict that outlives the sweep is a real refusal and belongs in front of
            // the player, not in an endless round of removals against a rate limiter.
            int attempts = 0;

            Assert.Throws<LobbyServiceException>(() => Run(LobbyJoinRecovery.JoinAsync(
                () => { attempts++; return Conflict(); },
                () => Task.FromResult(new List<string> { "target" }),
                _ => Task.CompletedTask)));

            Assert.AreEqual(2, attempts);
        }

        [Test]
        public void RethrowsWhenThereIsNoGhostToRelease()
        {
            // Nothing was released, so nothing about a second attempt would differ. Rethrowing
            // keeps the service's own reason on screen instead of burning another join on it.
            int attempts = 0;

            Assert.Throws<LobbyServiceException>(() => Run(LobbyJoinRecovery.JoinAsync(
                () => { attempts++; return Conflict(); },
                () => Task.FromResult(new List<string>()),
                _ => Task.CompletedTask)));

            Assert.AreEqual(1, attempts);
        }

        [Test]
        public void KeepsSweepingWhenOneRemovalFails()
        {
            // Removals run against a rate limiter. One refusal must not strand the rest of the
            // sweep, or a player with two ghosts stays locked out by whichever one answered first.
            var released = new List<string>();
            int attempts = 0;

            Lobby joined = Run(LobbyJoinRecovery.JoinAsync(
                () => ++attempts == 1 ? Conflict() : Task.FromResult(new Lobby(id: "target")),
                () => Task.FromResult(new List<string> { "unreachable", "target" }),
                id =>
                {
                    if (id == "unreachable")
                        throw new LobbyServiceException(LobbyExceptionReason.RateLimited, "slow down");

                    released.Add(id);
                    return Task.CompletedTask;
                }));

            Assert.AreEqual("target", joined.Id);
            CollectionAssert.AreEqual(new[] { "target" }, released);
        }

        [Test]
        public void LeavesEveryOtherFailureAlone()
        {
            // A full lobby, a wrong code or a dead network is not a ghost. Sweeping this player's
            // memberships on any of those would sign them out of a lobby they are legitimately in.
            int swept = 0;

            Assert.Throws<LobbyServiceException>(() => Run(LobbyJoinRecovery.JoinAsync(
                () => Task.FromException<Lobby>(
                    new LobbyServiceException(LobbyExceptionReason.LobbyFull, "lobby is full")),
                () => { swept++; return Task.FromResult(new List<string> { "ghost" }); },
                _ => Task.CompletedTask)));

            Assert.AreEqual(0, swept);
        }

        /// <summary>The 409 the service answers a join with when this player is already listed.</summary>
        private static Task<Lobby> Conflict() => Task.FromException<Lobby>(
            new LobbyServiceException(LobbyExceptionReason.LobbyConflict,
                "player is already a member of the lobby"));

        /// <summary>
        /// Drains a task that has already finished.
        ///
        /// The fakes above never touch the network, so every await completes inline and there is
        /// nothing to pump. Unity's EditMode runner does not await test methods, and GetResult
        /// rethrows the real exception where Wait would hand back an AggregateException wrapping it.
        /// </summary>
        private static T Run<T>(Task<T> task) => task.GetAwaiter().GetResult();
    }
}
