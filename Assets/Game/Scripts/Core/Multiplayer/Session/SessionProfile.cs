using System;
using System.Text;

namespace SpaceGame.Core
{
    /// <summary>
    /// Which Unity Gaming Services profile this process signs in under.
    ///
    /// Pure, so it can be tested without a live service — see SessionProfileTests. It is
    /// <see cref="SessionLauncher"/> that acts on the answer, once, when the services are
    /// initialised: the profile selects which cached credential the sign-in restores, and it is
    /// fixed for the lifetime of the services.
    /// </summary>
    public static class SessionProfile
    {
        /// <summary>
        /// Signs this process in under its own UGS profile: <c>-sgprofile client</c>.
        ///
        /// Needed to run two instances of the game on ONE machine. Every instance on a machine
        /// shares a single PlayerPrefs file, the anonymous credential is cached in it, and
        /// anonymous sign-in restores the SAME PlayerId from that cache — so without this the
        /// second instance is not a second player. Lobby memberships are keyed by PlayerId, so it
        /// is then refused from a lobby it is already a member of (409, see
        /// LobbyJoinRecovery) and the recovery sweep hands back the
        /// membership the first instance is hosting on.
        ///
        /// A profile is just a namespace inside PlayerPrefs, so a distinct one buys a distinct
        /// cached credential and therefore a distinct player.
        /// </summary>
        public const string Arg = "-sgprofile";

        /// <summary>
        /// Marks an extra editor instance launched by Multiplayer Play Mode; <c>-name</c> carries
        /// its instance name ("Player2"…). The Authentication SDK reads this exact pair itself
        /// (AuthenticationPackageInitializer.GetProfile) but only inside UNITY_EDITOR and only
        /// while the profile is still "default", so resolving it here — to the same name — keeps
        /// one visible owner of the decision rather than a silent fallback.
        /// </summary>
        private const string EditorModeArg = "-editor-mode";
        private const string NameArg = "-name";

        /// <summary>A ParrelSync clone lives in "&lt;project&gt;_clone_N", sharing the original's PlayerPrefs.</summary>
        private const string CloneMarker = "_clone_";

        /// <summary>UGS rejects a profile outside <c>^[a-zA-Z0-9_-]{1,30}$</c>.</summary>
        private const int MaxProfileLength = 30;

        /// <summary>
        /// The UGS profile this process should sign in under, or null to leave the SDK default
        /// alone. See <see cref="Arg"/> for why any of this exists.
        ///
        /// Three ways an instance can be told apart, most explicit first:
        ///   1. <c>-sgprofile &lt;name&gt;</c>  — the only one that works for a BUILT player run
        ///      beside the editor, which is otherwise indistinguishable from it.
        ///   2. MPPM's <c>-editor-mode -name Player2</c>.
        ///   3. A ParrelSync clone folder.
        ///
        /// Returning null for an ordinary launch is the important case: a real player's PlayerId
        /// has to survive relaunching the game, which means keeping the default profile.
        /// </summary>
        public static string Resolve(string[] args, string projectPath)
        {
            string explicitProfile = CommandLineArgs.Value(args, Arg);
            if (explicitProfile != null) return Sanitise(explicitProfile);

            // -name alone is a stock Unity argument; only the pair means a virtual player.
            if (CommandLineArgs.Has(args, EditorModeArg))
            {
                string instanceName = CommandLineArgs.Value(args, NameArg);
                if (instanceName != null) return Sanitise(instanceName);
            }

            int marker = projectPath == null ? -1 : projectPath.LastIndexOf(CloneMarker, StringComparison.Ordinal);
            if (marker < 0) return null;

            // "…/SpaceGame_clone_0/Assets" → "clone_0". Cut at the separator or the profile
            // carries the rest of the path, which sanitising would turn into underscores.
            string tail = projectPath.Substring(marker + 1);
            int separator = tail.IndexOfAny(new[] { '/', '\\' });

            return Sanitise(separator < 0 ? tail : tail.Substring(0, separator));
        }

        /// <summary>
        /// Forces a name into what SetProfile accepts. It throws on anything else, and it is
        /// called from the one path in <see cref="SessionLauncher"/> that is not allowed to throw
        /// (rule 1 on that class). ASCII only on purpose: char.IsLetterOrDigit passes 'é', the
        /// SDK's regex does not.
        /// </summary>
        private static string Sanitise(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var builder = new StringBuilder(MaxProfileLength);
            foreach (char c in raw.Trim())
            {
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                               (c >= '0' && c <= '9') || c == '-' || c == '_';

                builder.Append(allowed ? c : '_');

                if (builder.Length == MaxProfileLength) break;
            }

            return builder.ToString();
        }
    }
}
