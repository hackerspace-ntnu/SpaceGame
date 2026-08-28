using System.Reflection;
using NUnit.Framework;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Pins what is still resolved by string rather than by the compiler.
    ///
    /// This file used to be much longer, because LobbyMenu.unity bound every control in the lobby
    /// to a method name through a UnityEvent — which resolves at runtime and silently drops any
    /// target it cannot find, no exception and no console entry. Those bindings are gone with the
    /// scene: <c>LobbyUI</c> calls <c>LobbySession</c> directly, so a rename is a build
    /// error and needs no test to catch it.
    ///
    /// What is left is the boundary that is still authored in a scene. MainMenu.unity binds its
    /// entries to MainMenuUI by name, and the multiplayer routes are reached through a chain where
    /// only the first link is compiled.
    /// </summary>
    public class LobbyMenuWiringTests
    {
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

        [TestCase("StartStory")]
        [TestCase("StartVersus")]
        [TestCase("StartMultiPlayer")]
        [TestCase("StartSinglePlayer")]
        [TestCase("StartMinigame")]
        [TestCase("QuitGame")]
        public void MainMenuUI_KeepsItsSceneBoundMethods(string methodName)
        {
            Assert.IsNotNull(typeof(MainMenuUI).GetMethod(methodName, Public),
                $"MainMenu.unity binds a menu entry to MainMenuUI.{methodName} by name. " +
                "Removing or renaming it makes that entry silently do nothing.");
        }

        /// <summary>
        /// Each fork is a chain of three calls, and only the first is by name. MainMenu.unity →
        /// StartStory/StartVersus/StartMultiPlayer (string) → MenuChoiceUI (compiled) →
        /// HostMultiplayer/JoinMultiplayer/HostVersus/JoinVersus (compiled), and HostVersus's own
        /// screen calls back into EnterVersusLobby the same way. Pinning the far endpoints is what
        /// keeps a rename from turning "Host a game" back into a dead button.
        /// </summary>
        [TestCase("HostMultiplayer")]
        [TestCase("JoinMultiplayer")]
        [TestCase("HostVersus")]
        [TestCase("JoinVersus")]
        [TestCase("EnterVersusLobby")]
        public void MainMenuUI_KeepsTheRoutesItsChoicePagesCallBackInto(string methodName)
        {
            Assert.IsNotNull(typeof(MainMenuUI).GetMethod(methodName, Public),
                $"MenuChoiceUI or VersusRulesUI calls MainMenuUI.{methodName}.");
        }

        /// <summary>
        /// MenuChoiceUI is built at runtime rather than authored into a scene, but MainMenuUI's own
        /// StartStory/StartVersus/StartMultiPlayer call it by name — a rename here breaks all three
        /// with a compile error, which is the point, but only if Open still exists to be renamed.
        /// </summary>
        [Test]
        public void MenuChoiceUI_KeepsItsStaticOpen()
        {
            Assert.IsNotNull(typeof(MenuChoiceUI).GetMethod("Open",
                    BindingFlags.Public | BindingFlags.Static),
                "MainMenuUI.StartStory/StartVersus/StartMultiPlayer call MenuChoiceUI.Open.");
        }

        /// <summary>
        /// Both multiplayer screens read these off the menu. They are properties rather than
        /// serialized fields on each screen because the screens are built at runtime and have no
        /// Inspector of their own — so the menu is the only place the references can live.
        /// </summary>
        [TestCase("MenuButtonPrefab")]
        [TestCase("GameSceneName")]
        [TestCase("WorldConfig")]
        public void MainMenuUI_LendsWhatTheRuntimeScreensNeed(string propertyName)
        {
            Assert.IsNotNull(typeof(MainMenuUI).GetProperty(propertyName, Public),
                $"The screens MainMenuUI opens read {propertyName} from it. Without it they build " +
                "unstyled entries, or load the wrong scene.");
        }

        /// <summary>
        /// The lobby is a page over the menu, not a scene. EnterLobby is what routes into it and is
        /// called from two places — MainMenuUI.JoinMultiplayer and WorldSelectUI's host route.
        /// </summary>
        [Test]
        public void MainMenuUI_KeepsTheLobbyEntryPoint()
        {
            Assert.IsNotNull(typeof(MainMenuUI).GetMethod("EnterLobby", Public),
                "WorldSelectUI finishes the host route by calling MainMenuUI.EnterLobby.");
        }
    }
}
