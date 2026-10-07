using System;
using NUnit.Framework;
using SpaceGame.Core.Lobbies;
using Unity.Services.Lobbies;

namespace SpaceGame.Tests
{
    /// <summary>
    /// <see cref="LobbyServiceErrors"/>: telling the SDK's own error path apart from our nulls.
    /// </summary>
    public class LobbyServiceErrorsTests
    {
        [Test]
        public void RecognisesFramesFromInsideTheLobbyPackage()
        {
            // WrappedLobbyService.TryCatchRequest does `he.ActualError.Code`, and ActualError is
            // null whenever the service answers an HTTP error with a body the SDK cannot parse —
            // which is what its rate limiter sends. So a refused query does not arrive as a
            // LobbyServiceException carrying a reason; it arrives as a bare null dereference with
            // these frames under it.
            Assert.IsTrue(LobbyServiceErrors.IsLobbyPackageStack(
                "Unity.Services.Lobbies.Internal.WrappedLobbyService.TryCatchRequest[TRequest,TReturn]" +
                " (at Library/PackageCache/com.unity.services.multiplayer/Runtime/Lobbies/SDK/" +
                "WrappedLobbyService.cs:572)"));
        }

        [Test]
        public void DoesNotExcuseOurOwnNulls()
        {
            Assert.IsFalse(LobbyServiceErrors.IsLobbyPackageStack(
                "SpaceGame.Core.Lobbies.LobbySession.QueryAsync () (at Assets/Game/Scripts/Core/Multiplayer/" +
                "Lobby/LobbySession.Browsing.cs:48)"));
        }

        [Test]
        public void SurvivesAnExceptionThatWasNeverThrown()
        {
            // StackTrace is null until the runtime fills it in, and the catch clauses that reach
            // this also see exceptions our own code constructed.
            Assert.IsFalse(LobbyServiceErrors.IsSdkErrorPathFailure(new NullReferenceException()));
        }

        [Test]
        public void IgnoresExceptionsThatCarryTheirOwnReason()
        {
            // A LobbyServiceException already says what went wrong and must keep saying it.
            Assert.IsFalse(LobbyServiceErrors.IsSdkErrorPathFailure(
                new LobbyServiceException(LobbyExceptionReason.RateLimited, "rate limited")));
        }

        [Test]
        public void DescribeKeepsTheServiceReasonWhenThereIsOne()
        {
            string line = LobbyServiceErrors.Describe(
                new LobbyServiceException(LobbyExceptionReason.LobbyFull, "lobby is full"),
                "Could not join that lobby.");

            StringAssert.StartsWith("Could not join that lobby.", line);
            StringAssert.Contains("LobbyFull", line);
        }
    }
}
