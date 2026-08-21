using NUnit.Framework;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Which UGS profile a process signs in under — see <see cref="SessionLauncher.ResolveProfileName"/>.
    ///
    /// The bug this guards: every instance on one machine reads the same PlayerPrefs file, so
    /// without a per-instance profile they all restore the same cached anonymous credential and
    /// are therefore the SAME PlayerId. A lobby membership is keyed by PlayerId, so the second
    /// instance is refused from a lobby it is already a member of — the 409 that LobbySessionTests
    /// covers the recovery for. A profile is a namespace inside PlayerPrefs, so a distinct one
    /// gives the instance its own credential and its own player.
    /// </summary>
    public class SessionProfileTests
    {
        private const string NormalPath = "/Users/dev/SpaceGame/Assets";

        [Test]
        public void NoFlags_KeepsTheDefaultProfile()
        {
            // A shipped game must be unaffected: the player's PlayerId has to survive relaunches,
            // and null here means the SDK's own "default" is left alone.
            Assert.IsNull(SessionLauncher.ResolveProfileName(new[] { "SpaceGame.exe" }, NormalPath));
        }

        [Test]
        public void ExplicitFlag_NamesTheProfile()
        {
            Assert.AreEqual("client",
                SessionLauncher.ResolveProfileName(new[] { "SpaceGame.exe", "-sgprofile", "client" }, NormalPath));
        }

        [Test]
        public void ExplicitFlag_BeatsEveryOtherSource()
        {
            string[] args = { "Unity", "-editor-mode", "-name", "Player2", "-sgprofile", "chosen" };

            Assert.AreEqual("chosen", SessionLauncher.ResolveProfileName(args, NormalPath));
        }

        [Test]
        public void ExplicitFlag_WithNoValue_IsIgnored()
        {
            // Trailing flag: reading args[i + 1] off the end is the obvious way to write this.
            Assert.IsNull(SessionLauncher.ResolveProfileName(new[] { "SpaceGame.exe", "-sgprofile" }, NormalPath));
        }

        [Test]
        public void VirtualPlayer_UsesItsInstanceName()
        {
            string[] args = { "Unity", "-editor-mode", "-name", "Player2" };

            Assert.AreEqual("Player2", SessionLauncher.ResolveProfileName(args, NormalPath));
        }

        [Test]
        public void NameAlone_IsNotAVirtualPlayer()
        {
            // -name is a plain Unity argument. Only -editor-mode marks an MPPM instance, and
            // treating a bare -name as one would silently re-profile unrelated tooling.
            Assert.IsNull(SessionLauncher.ResolveProfileName(new[] { "Unity", "-name", "Player2" }, NormalPath));
        }

        [Test]
        public void CloneProject_ProfilesOffTheCloneSuffix()
        {
            // ParrelSync clones are separate folders with the same company/product name, so they
            // share the original's PlayerPrefs file and cannot be told apart by any argument.
            Assert.AreEqual("clone_0",
                SessionLauncher.ResolveProfileName(new[] { "Unity" }, "/Users/dev/SpaceGame_clone_0/Assets"));
        }

        [Test]
        public void CloneProject_StopsAtThePathSeparator()
        {
            Assert.AreEqual("clone_1",
                SessionLauncher.ResolveProfileName(new[] { "Unity" }, "/Users/dev/SpaceGame_clone_1/Assets/Sub"));
        }

        [Test]
        public void Profile_IsSanitisedToWhatTheSdkAccepts()
        {
            // SetProfile throws on anything outside ^[a-zA-Z0-9_-]{1,30}$, and it is called inside
            // the one method in this codebase that is not allowed to throw.
            Assert.AreEqual("Player_2_x",
                SessionLauncher.ResolveProfileName(new[] { "x", "-sgprofile", "Player 2:x" }, NormalPath));
        }

        [Test]
        public void Profile_IsTruncatedToThirtyCharacters()
        {
            string overlong = new string('a', 45);

            string resolved = SessionLauncher.ResolveProfileName(new[] { "x", "-sgprofile", overlong }, NormalPath);

            Assert.AreEqual(30, resolved.Length);
        }

        [Test]
        public void Profile_WithNothingUsableInIt_IsIgnored()
        {
            Assert.IsNull(SessionLauncher.ResolveProfileName(new[] { "x", "-sgprofile", "   " }, NormalPath));
        }
    }
}
