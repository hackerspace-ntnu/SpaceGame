// Why a player who was dead when the world loaded still gets a death screen.
//
// The bug these pin: quit while dead, load that save, and you woke up at 0 health with full
// control and no death screen. Nothing about the save was wrong — the record said 0 and 0 is what
// came back. What was wrong is that "you are dead" was announced at a moment when neither of the
// two things that care about it existed yet:
//
//   • PlayerController drops its HealthComponent.OnDeath handler in DisablePlayer, which Awake
//     calls before any spawn, and only re-takes it in EnablePlayer (OnNetworkSpawn). The save
//     restore lands in that window, so OnDeath fired into an empty delegate and isDead stayed
//     false on a body with no health left.
//   • DeathScreenUI subscribed in Start, which cannot run before EnablePlayer switches the HUD
//     GameObject on — a frame after the announcement — and then hid the screen unconditionally.
//
// Both are fixed the same way: read the state, do not just wait for the event. These tests drive
// the lifecycle by hand because edit mode does not deliver Awake/OnEnable/Start to a plain
// MonoBehaviour, and they live in Editor/ because DeathScreenUI is an Assembly-CSharp type that an
// asmdef cannot reference.
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class DeathOnLoadTests
    {
        private GameObject player;

        private PlayerController controller;
        private HealthComponent health;
        private DeathScreenUI screen;
        private GameObject overlay;

        [SetUp]
        public void SetUp() => Build();

        [TearDown]
        public void TearDown()
        {
            if (player != null) Object.DestroyImmediate(player);
        }

        // ------------------------------------------------------------------ the bug

        /// <summary>
        /// The premise. A save restoring a dead body announces the death before the controller is
        /// listening, and that announcement is not repeated — so nothing but the health value is
        /// left to tell the controller what it missed.
        /// </summary>
        [Test]
        public void ADeathRestoredBeforeOwnership_IsNotHeardByTheController()
        {
            health.RestoreHealth(0);

            Assert.IsFalse(controller.IsDead,
                "This test's premise no longer holds: the controller heard the death after all. " +
                "If PlayerController now subscribes before the restore, the catch-up in " +
                "EnablePlayer is belt-and-braces rather than the fix, and these tests should say so.");
        }

        [Test]
        public void EnablingAPlayerAtZeroHealth_MarksThemDead()
        {
            health.RestoreHealth(0);

            controller.EnablePlayer();

            Assert.IsTrue(controller.IsDead,
                "A player enabled at 0 health was left alive as far as PlayerController is " +
                "concerned, so nothing freezes them and nothing tells the HUD.");
        }

        [Test]
        public void APlayerWhoLoadedInDead_GetsTheDeathScreen()
        {
            health.RestoreHealth(0);

            // The real order. EnablePlayer activates the HUD, and Unity runs OnEnable synchronously
            // inside that SetActive — so the overlay binds itself BEFORE the controller works out
            // that this body is a corpse.
            screen.Present();
            controller.EnablePlayer();

            Assert.IsTrue(overlay.activeSelf,
                "Loading a save you had died in left the player at 0 health with no death screen.");
        }

        /// <summary>
        /// The other ordering: the HUD comes up after the controller already knows. Nothing will be
        /// announced from here on, so the overlay has to decide from state alone. This is also every
        /// later re-enable — dismounting, a scene handover — while still dead.
        /// </summary>
        [Test]
        public void AHudRaisedAfterTheDeath_StillShowsTheScreen()
        {
            health.RestoreHealth(0);
            controller.EnablePlayer();

            screen.Present();

            Assert.IsTrue(overlay.activeSelf,
                "The overlay only reacts to the death event, so a HUD that appears after the death " +
                "shows nothing.");
        }

        // ------------------------------------------------------------------ coming back

        /// <summary>
        /// The refusal PlayerRespawn actually implements: it is asked to bring someone back, and
        /// there is no spawn position to bring them back to — in the world, because the chunk under
        /// the spawn point has not finished streaming, which is most likely immediately after a
        /// load, which is exactly when a player who died last session is pressing the button.
        /// </summary>
        [Test]
        public void ARefusedRespawn_LeavesTheScreenUp()
        {
            Assume.That(SpawnManager.Instance, Is.Null,
                "This test needs a world with no spawn point in it.");

            PlayerRespawn respawn = player.AddComponent<PlayerRespawn>();
            Call(respawn, "Awake");
            Call(respawn, "OnEnable");   // registers the handler the request is dispatched to

            health.RestoreHealth(0);
            screen.Present();
            controller.EnablePlayer();

            LogAssert.Expect(LogType.Error, new Regex("No valid spawn position"));
            screen.Respawn();

            Assert.IsTrue(controller.IsDead, "The server had nowhere to put them, so they stay down.");
            Assert.IsTrue(overlay.activeSelf,
                "The screen hid itself on the click, so a refused respawn leaves the player frozen " +
                "behind nothing, with no button left to press.");
        }

        [Test]
        public void ComingBackToLife_TakesTheScreenDown()
        {
            health.RestoreHealth(0);
            screen.Present();
            controller.EnablePlayer();
            Assume.That(overlay.activeSelf, Is.True);

            health.ResetToFull();

            Assert.IsFalse(controller.IsDead);
            Assert.IsFalse(overlay.activeSelf,
                "The overlay outlived the death, so a revived player is looking at their own death " +
                "screen.");
        }

        [Test]
        public void APlayerWhoLoadedInAlive_SeesNothing()
        {
            screen.Present();
            controller.EnablePlayer();

            Assert.IsFalse(controller.IsDead);
            Assert.IsFalse(overlay.activeSelf, "A healthy player was shown a death screen.");
        }

        // ------------------------------------------------------------------ fixture

        /// <summary>
        /// The smallest body PlayerController.EnablePlayer will touch, plus the HUD hierarchy the
        /// overlay lives in. Awake is called by hand — edit mode does not run it — because it is
        /// what resolves the input manager that EnablePlayer switches on and off.
        /// </summary>
        private void Build()
        {
            player = new GameObject("Player");
            player.AddComponent<SpaceGame.Core.PlayerInputManager>();
            health = player.AddComponent<HealthComponent>();

            var movement = player.AddComponent<PlayerMovement>();
            var look = player.AddComponent<PlayerLook>();
            var feedback = player.AddComponent<DamageFeedback>();

            var camera = new GameObject("Camera");
            camera.transform.SetParent(player.transform);

            var hud = new GameObject("HUD");
            hud.transform.SetParent(player.transform);

            overlay = new GameObject("DeathScreen", typeof(RectTransform));
            overlay.transform.SetParent(hud.transform);
            overlay.SetActive(false);

            controller = player.AddComponent<PlayerController>();
            Set(controller, "playerCamera", camera);
            Set(controller, "playerHUD", hud);
            Set(controller, "playerMovement", movement);
            Set(controller, "playerLook", look);
            Set(controller, "damageFeedback", feedback);
            Set(controller, "playerHealth", health);

            screen = hud.AddComponent<DeathScreenUI>();
            Set(screen, "deathScreen", overlay.GetComponent<RectTransform>());
            Set(screen, "player", controller);

            Call(controller, "Awake");

            // Awake enables the player outright when there is no network, and an EditMode fixture
            // has none. Put it back into the state a real session is in while the save is being
            // applied: spawned, but not yet owned by this machine, so the controller is holding no
            // health handlers. That window is where the bug lives, and skipping it would test
            // nothing.
            controller.DisablePlayer();
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(
                field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(info, $"{target.GetType().Name} has no field '{field}'. Renamed?");
            info.SetValue(target, value);
        }

        /// <summary>
        /// Edit mode does not deliver Awake/OnEnable/Start to a plain MonoBehaviour, so anything
        /// these tests depend on being wired up has to be called by hand.
        /// </summary>
        private static void Call(MonoBehaviour behaviour, string method)
        {
            MethodInfo info = behaviour.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(info, $"{behaviour.GetType().Name} has no method '{method}'. Renamed?");
            info.Invoke(behaviour, null);
        }
    }
}
