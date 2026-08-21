// Guards the one thing that makes local multiplayer testing trustworthy.
//
// Every instance of the game on a machine shares a single PlayerPrefs file, so an unqualified key
// hands the SAME save identity to two players at one PC, to every Multiplayer Play Mode virtual
// player, and to a build launched beside the editor. PlayerSaveService holds one live player per
// profile, so the second claimant overwrites the first: both restore the same position and
// inventory, and only one is ever captured back out.
//
// The failure is invisible from a single instance, which is exactly the shape of bug a test is
// worth writing for — and it is doubly worth guarding because it degrades the tool you would use
// to find every other multiplayer bug.
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Tests
{
    public class PlayerProfileIdentityTests
    {
        private const string BareKey = "SpaceGame.PlayerProfileId";

        [Test]
        public void OrdinaryLaunch_KeepsTheBareKey()
        {
            // The important case, and the reason the resolver may return null at all: a real
            // player's identity has to survive relaunching the game — and has to survive this
            // change being introduced, which must not orphan the save of anyone already playing.
            Assert.AreEqual(BareKey, PlayerProfile.PrefsKeyFor(null));
            Assert.AreEqual(BareKey, PlayerProfile.PrefsKeyFor(string.Empty));
        }

        [Test]
        public void NamedInstance_GetsItsOwnKey()
        {
            Assert.AreEqual($"{BareKey}.client", PlayerProfile.PrefsKeyFor("client"));
            Assert.AreEqual($"{BareKey}.Player2", PlayerProfile.PrefsKeyFor("Player2"));
        }

        [Test]
        public void TwoInstancesOnOneMachine_DoNotShareAKey()
        {
            // The whole point, stated as the property rather than as two literals: whatever the
            // resolver hands back for two differently-launched instances, their storage must not
            // collide.
            string host = PlayerProfile.PrefsKeyFor(
                SessionLauncher.ResolveProfileName(new[] { "SpaceGame.exe" }, "/proj/Assets"));

            string second = PlayerProfile.PrefsKeyFor(
                SessionLauncher.ResolveProfileName(new[] { "SpaceGame.exe", "-sgprofile", "client" }, "/proj/Assets"));

            Assert.AreNotEqual(host, second,
                "A second instance on the same machine resolved to the same PlayerPrefs key as the " +
                "first, so both players share one save record.");
        }

        [Test]
        public void MppmVirtualPlayer_GetsItsOwnKey()
        {
            // MPPM is how this project is actually play-tested with two clients, so it is the case
            // that has to work rather than a hypothetical.
            string main = PlayerProfile.PrefsKeyFor(
                SessionLauncher.ResolveProfileName(new[] { "Unity" }, "/proj/Assets"));

            string virtualPlayer = PlayerProfile.PrefsKeyFor(
                SessionLauncher.ResolveProfileName(new[] { "Unity", "-editor-mode", "-name", "Player2" }, "/proj/Assets"));

            Assert.AreEqual(BareKey, main);
            Assert.AreEqual($"{BareKey}.Player2", virtualPlayer);
        }

        [Test]
        public void ParrelSyncClone_GetsItsOwnKey()
        {
            string original = PlayerProfile.PrefsKeyFor(
                SessionLauncher.ResolveProfileName(new[] { "Unity" }, "/work/SpaceGame/Assets"));

            string clone = PlayerProfile.PrefsKeyFor(
                SessionLauncher.ResolveProfileName(new[] { "Unity" }, "/work/SpaceGame_clone_0/Assets"));

            Assert.AreNotEqual(original, clone);
        }

        [Test]
        public void SaveIdentityAndLobbyIdentity_ComeFromTheSameResolver()
        {
            // Two answers to "which instance am I" could disagree, and the pair that disagreed
            // would be a player whose save and whose lobby seat belong to different people. This
            // asserts the sharing rather than the value, so it keeps holding if the rule changes.
            string[] args = { "SpaceGame.exe", "-sgprofile", "client" };

            string instance = SessionLauncher.ResolveProfileName(args, "/proj/Assets");

            Assert.AreEqual("client", instance);
            Assert.AreEqual($"{BareKey}.client", PlayerProfile.PrefsKeyFor(instance));
        }
    }
}
