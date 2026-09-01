using System;
using Unity.Services.Lobbies;

namespace SpaceGame.Core.Lobbies
{
    /// <summary>
    /// Turns whatever the Lobby service threw into a line a player can read — and recognises the
    /// one shape of exception that is the SDK falling over rather than a fault on this side.
    /// </summary>
    public static class LobbyServiceErrors
    {
        private const string LobbyPackageNamespace = "Unity.Services.Lobbies";

        /// <summary>
        /// A line the player can read, from whatever the service threw.
        ///
        /// The SDK's own error path is spelled out rather than quoted: it arrives as a bare
        /// NullReferenceException with a message about an object reference, which describes the
        /// package's bug instead of the refusal that caused it. See
        /// <see cref="IsSdkErrorPathFailure"/>.
        /// </summary>
        public static string Describe(Exception e, string headline) =>
            IsSdkErrorPathFailure(e)
                ? $"{headline}\n(The lobby service refused the request. Try again in a moment.)"
                : e is LobbyServiceException lobbyException
                    ? $"{headline}\n({lobbyException.Reason}: {lobbyException.Message})"
                    : $"{headline}\n({e.GetType().Name}: {e.Message})";

        /// <summary>
        /// Whether an exception is the Lobby SDK falling over on its own error path, rather than a
        /// fault on this side of the boundary.
        ///
        /// <para>
        /// <c>WrappedLobbyService.TryCatchRequest</c> answers an <c>HttpException&lt;ErrorStatus&gt;</c>
        /// with <c>he.ActualError.Code</c>, and <c>ActualError</c> is whatever
        /// <c>ResponseHandler.TryDeserializeResponse</c> made of the response body — which is
        /// <b>null</b> whenever the service answers an HTTP error with an empty or unparseable one.
        /// Its rate limiter does exactly that. So a refused request does not arrive as
        /// <see cref="LobbyServiceException"/> with a reason on it; it arrives as a raw
        /// <see cref="NullReferenceException"/> thrown from inside the package, and the status code
        /// that would have said <i>which</i> refusal it was is destroyed by the same dereference.
        /// </para>
        ///
        /// <para>
        /// Matched on the stack rather than on the type alone, so a genuine null bug in our own
        /// code is still reported as one instead of being excused as a busy service.
        /// </para>
        /// </summary>
        public static bool IsSdkErrorPathFailure(Exception e) =>
            e is NullReferenceException && IsLobbyPackageStack(e.StackTrace);

        /// <summary>
        /// Whether these frames come from inside the Lobby package.
        ///
        /// Split out because <see cref="Exception.StackTrace"/> is filled in by the runtime as an
        /// exception is thrown and cannot be set, so this is the half a test can reach.
        /// </summary>
        public static bool IsLobbyPackageStack(string stackTrace) =>
            stackTrace != null && stackTrace.Contains(LobbyPackageNamespace);
    }
}
