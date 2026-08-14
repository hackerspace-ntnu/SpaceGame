using System.Reflection;
using NUnit.Framework;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Pins the method names LobbyMenu.unity binds its controls to.
    ///
    /// UnityEvent resolves targets by string at runtime and silently drops any it cannot find — no
    /// exception, no console entry — so renaming one of these turns its button into a dead control
    /// that nothing anywhere reports. The compiler cannot see the binding; this can.
    ///
    /// The lowercase names are deliberate. They are what the scene already contains.
    /// </summary>
    public class LobbyMenuWiringTests
    {
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

        [TestCase("createLobbyWithGivenOptions")]
        [TestCase("listLobbies")]
        [TestCase("JoinLobbyById")]
        [TestCase("JoinLobbyByCode")]
        [TestCase("JoinLobbyByPassword")]
        [TestCase("LeaveLobby")]
        [TestCase("StartLobbyGame")]
        public void LobbySystem_KeepsItsSceneBoundMethods(string methodName)
        {
            Assert.IsNotNull(typeof(LobbySystem).GetMethod(methodName, Public),
                $"LobbyMenu.unity binds a control to LobbySystem.{methodName} by name. " +
                "Removing or renaming it makes that control silently do nothing.");
        }

        [TestCase("closeJoinPrivateLobbyScreen")]
        [TestCase("changeStateOfPasswordInputFieldCreateLobby")]
        [TestCase("openLobbyScreen")]
        [TestCase("hideLobbyScreen")]
        [TestCase("showPlayerElements")]
        [TestCase("setStartGameButtonState")]
        [TestCase("clearPrevList")]
        [TestCase("listNewLobby")]
        public void LobbyListSystem_KeepsItsViewContract(string methodName)
        {
            Assert.IsNotNull(typeof(LobbyListSystem).GetMethod(methodName, Public),
                $"LobbySystem or LobbyMenu.unity calls LobbyListSystem.{methodName}.");
        }

        [Test]
        public void LobbySystem_ExposesTheGameSceneNameTheDirectPathAlsoUses()
        {
            Assert.IsNotNull(typeof(LobbySystem).GetProperty("GameSceneName", Public),
                "DirectConnectController reads the game scene from here so the two paths cannot drift.");
        }

        [Test]
        public void LobbyElementController_KeepsTheJoinEntryPoint()
        {
            // LobbyElement.prefab wires its row button to this by name.
            Assert.IsNotNull(typeof(LobbyElementController).GetMethod("attemptJoin", Public),
                "LobbyElement.prefab binds its Join button to attemptJoin by name.");
        }
    }
}
