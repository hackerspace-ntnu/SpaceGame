using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using SpaceGame.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The parts of <see cref="LobbySession"/> that hold without a live Lobby service.
    ///
    /// Creating and joining need Unity Gaming Services and two machines, and are covered by playing
    /// the game. The option objects handed to those calls do not — and every bug that made the
    /// lobby unusable lived in one of them.
    /// </summary>
    public class LobbySessionTests
    {
        [Test]
        public void CreateOptions_CarriesTheRelayCodeAtCreation()
        {
            // Written at creation rather than by a follow-up UpdateLobbyAsync. A client that polled
            // in the gap between the two saw a lobby with no join code and read straight past the
            // missing key.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, "RELAY99", "Ferdinand", 0);

            Assert.IsTrue(options.Data.ContainsKey(LobbySession.KeyRelayJoinCode));
            Assert.AreEqual("RELAY99", options.Data[LobbySession.KeyRelayJoinCode].Value);
        }

        [Test]
        public void CreateOptions_StartsInTheWaitingState()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, "RELAY99", "Ferdinand", 0);

            Assert.AreEqual(LobbySession.StateWaiting, options.Data[LobbySession.KeyGameState].Value);
        }

        [Test]
        public void CreateOptions_PublishesGameStateToNonMembers()
        {
            // The lobby browser labels rows the player has not joined, so this key has to be
            // visible to non-members. Member visibility would render every row blank.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, "RELAY99", "Ferdinand", 0);

            Assert.AreEqual(DataObject.VisibilityOptions.Public,
                options.Data[LobbySession.KeyGameState].Visibility);
        }

        [Test]
        public void CreateOptions_CarriesThePlayerName()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, "RELAY99", "Ferdinand", 0);

            Assert.AreEqual("Ferdinand", options.Player.Data[LobbySession.KeyPlayerName].Value);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CreateOptions_NeverSetAPassword(bool isPrivate)
        {
            // Sessions are reached by their code or from the browser, never by a password. Lobby
            // also rejects an empty-string password outright rather than ignoring it, so the only
            // safe value to send is none at all.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(isPrivate, "RELAY99", "Ferdinand", 0);

            Assert.IsNull(options.Password);
        }

        [Test]
        public void CreateOptions_DelistAPrivateLobby()
        {
            // Compared against true/false rather than asserted directly: IsPrivate is a bool?, and
            // spelling the comparison out keeps this off NUnit's nullable overloads.
            Assert.IsTrue(LobbySession.BuildCreateOptions(true, "RELAY99", "Ferdinand", 0).IsPrivate == true);
            Assert.IsTrue(LobbySession.BuildCreateOptions(false, "RELAY99", "Ferdinand", 0).IsPrivate == false);
        }

        [Test]
        public void BeginGameOptions_NeverLockTheLobby()
        {
            // THE regression test for this feature. Locking the lobby at game start is what made it
            // impossible to join a session already in progress: a locked lobby refuses every join,
            // and the host is usually playing alone when the first friend tries.
            UpdateLobbyOptions options = LobbySession.BuildBeginGameOptions();

            Assert.IsTrue(options.IsLocked == null || options.IsLocked == false,
                "Locking the lobby at start makes late join impossible.");
        }

        [Test]
        public void BeginGameOptions_MarkTheLobbyInGame()
        {
            UpdateLobbyOptions options = LobbySession.BuildBeginGameOptions();

            Assert.AreEqual(LobbySession.StateInGame, options.Data[LobbySession.KeyGameState].Value);
        }

        [Test]
        public void PrivacyOptions_DelistTheLobbyWhenTurnedPrivate()
        {
            UpdateLobbyOptions options = LobbySession.BuildPrivacyOptions(true);

            Assert.IsTrue(options.IsPrivate == true);
        }

        [Test]
        public void PrivacyOptions_ListTheLobbyAgainWhenTurnedPublic()
        {
            UpdateLobbyOptions options = LobbySession.BuildPrivacyOptions(false);

            Assert.IsTrue(options.IsPrivate == false);
        }

        [Test]
        public void PrivacyOptions_NeverSendAPassword()
        {
            // There are no passwords in this flow. Sending one here would be worse than useless:
            // UpdateLobbyOptions reads null as "leave it alone", so a stray value would lock a lobby
            // behind a secret nothing in the UI can collect and nothing in the UI can clear.
            Assert.IsNull(LobbySession.BuildPrivacyOptions(true).Password);
            Assert.IsNull(LobbySession.BuildPrivacyOptions(false).Password);
        }

        [Test]
        public void Occupancy_CountsTakenSlotsNotFreeOnes()
        {
            // Lobby reports FREE slots. Reading that as the taken count is how every row in the
            // browser came to claim it was empty.
            Assert.AreEqual("3/4", LobbySession.DescribeOccupancy(4, 1));
            Assert.AreEqual("0/4", LobbySession.DescribeOccupancy(4, 4));
            Assert.AreEqual("4/4", LobbySession.DescribeOccupancy(4, 0));
        }

        // ─────────────────────────────────────────────
        //  Stale membership recovery
        // ─────────────────────────────────────────────
        //
        // A join is refused with 409 "player is already a member of the lobby" whenever this
        // player's id is still listed in the lobby — which is what every session that ended any way
        // other than pressing Leave leaves behind: the host crashed, Relay timed out, or the
        // process was killed. Anonymous authentication hands back the SAME player id next launch,
        // so those ghosts are still ours, and they pile up.
        //
        // The Lobby SDK carries its own 409 recovery and it cannot be relied on. Joining by id, it
        // gives up unless GetJoinedLobbies returns EXACTLY one lobby, and it then joins whatever
        // that one is rather than the lobby that was asked for. Two ghosts and it rethrows — which
        // is precisely the state a couple of playtests leave behind.

        [Test]
        public void JoinRecovery_LeavesNothingAloneWhenTheJoinSimplyWorks()
        {
            var released = new List<string>();
            int swept = 0;

            Lobby joined = Run(LobbySession.JoinWithConflictRecoveryAsync(
                () => Task.FromResult(new Lobby(id: "target")),
                () => { swept++; return Task.FromResult(new List<string> { "ghost" }); },
                id => { released.Add(id); return Task.CompletedTask; }));

            Assert.AreEqual("target", joined.Id);
            Assert.AreEqual(0, swept, "The happy path must not spend a request on the ghost sweep.");
            CollectionAssert.IsEmpty(released);
        }

        [Test]
        public void JoinRecovery_ReleasesEveryGhostThenJoinsAgain()
        {
            // THE regression test for this bug. Two ghosts is the case the SDK's own resolver
            // refuses to touch, so the 409 reached the player as a raw HttpException.
            var released = new List<string>();
            int attempts = 0;

            Lobby joined = Run(LobbySession.JoinWithConflictRecoveryAsync(
                () => ++attempts == 1 ? Conflict() : Task.FromResult(new Lobby(id: "target")),
                () => Task.FromResult(new List<string> { "target", "someOtherLobby" }),
                id => { released.Add(id); return Task.CompletedTask; }));

            Assert.AreEqual("target", joined.Id);
            Assert.AreEqual(2, attempts, "The join has to be retried once the ghosts are gone.");
            CollectionAssert.AreEquivalent(new[] { "target", "someOtherLobby" }, released);
        }

        [Test]
        public void JoinRecovery_RetriesOnceOnlyWhenTheConflictSurvives()
        {
            // No loop. A conflict that outlives the sweep is a real refusal and belongs in front of
            // the player, not in an endless round of removals against a rate limiter.
            int attempts = 0;

            Assert.Throws<LobbyServiceException>(() => Run(LobbySession.JoinWithConflictRecoveryAsync(
                () => { attempts++; return Conflict(); },
                () => Task.FromResult(new List<string> { "target" }),
                _ => Task.CompletedTask)));

            Assert.AreEqual(2, attempts);
        }

        [Test]
        public void JoinRecovery_RethrowsWhenThereIsNoGhostToRelease()
        {
            // Nothing was released, so nothing about a second attempt would differ. Rethrowing
            // keeps the service's own reason on screen instead of burning another join on it.
            int attempts = 0;

            Assert.Throws<LobbyServiceException>(() => Run(LobbySession.JoinWithConflictRecoveryAsync(
                () => { attempts++; return Conflict(); },
                () => Task.FromResult(new List<string>()),
                _ => Task.CompletedTask)));

            Assert.AreEqual(1, attempts);
        }

        [Test]
        public void JoinRecovery_KeepsSweepingWhenOneRemovalFails()
        {
            // Removals run against a rate limiter. One refusal must not strand the rest of the
            // sweep, or a player with two ghosts stays locked out by whichever one answered first.
            var released = new List<string>();
            int attempts = 0;

            Lobby joined = Run(LobbySession.JoinWithConflictRecoveryAsync(
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
        public void JoinRecovery_LeavesEveryOtherFailureAlone()
        {
            // A full lobby, a wrong code or a dead network is not a ghost. Sweeping this player's
            // memberships on any of those would sign them out of a lobby they are legitimately in.
            int swept = 0;

            Assert.Throws<LobbyServiceException>(() => Run(LobbySession.JoinWithConflictRecoveryAsync(
                () => Task.FromException<Lobby>(
                    new LobbyServiceException(LobbyExceptionReason.LobbyFull, "lobby is full")),
                () => { swept++; return Task.FromResult(new List<string> { "ghost" }); },
                _ => Task.CompletedTask)));

            Assert.AreEqual(0, swept);
        }

        // ───────────────────────────────────────────── the SDK's own error path

        [Test]
        public void SdkErrorPath_RecognisesFramesFromInsideTheLobbyPackage()
        {
            // WrappedLobbyService.TryCatchRequest does `he.ActualError.Code`, and ActualError is
            // null whenever the service answers an HTTP error with a body the SDK cannot parse —
            // which is what its rate limiter sends. So a refused query does not arrive as a
            // LobbyServiceException carrying a reason; it arrives as a bare null dereference with
            // these frames under it.
            Assert.IsTrue(LobbySession.IsLobbyPackageStack(
                "Unity.Services.Lobbies.Internal.WrappedLobbyService.TryCatchRequest[TRequest,TReturn]" +
                " (at Library/PackageCache/com.unity.services.multiplayer/Runtime/Lobbies/SDK/" +
                "WrappedLobbyService.cs:572)"));
        }

        [Test]
        public void SdkErrorPath_DoesNotExcuseOurOwnNulls()
        {
            Assert.IsFalse(LobbySession.IsLobbyPackageStack(
                "SpaceGame.Core.LobbySession.QueryAsync () (at Assets/Game/Scripts/Core/Multiplayer/" +
                "LobbySession.cs:214)"));
        }

        [Test]
        public void SdkErrorPath_SurvivesAnExceptionThatWasNeverThrown()
        {
            // StackTrace is null until the runtime fills it in, and the catch clauses that reach
            // this also see exceptions our own code constructed.
            Assert.IsFalse(LobbySession.IsSdkErrorPathFailure(new NullReferenceException()));
        }

        [Test]
        public void SdkErrorPath_IgnoresExceptionsThatCarryTheirOwnReason()
        {
            // A LobbyServiceException already says what went wrong and must keep saying it.
            Assert.IsFalse(LobbySession.IsSdkErrorPathFailure(
                new LobbyServiceException(LobbyExceptionReason.RateLimited, "rate limited")));
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
