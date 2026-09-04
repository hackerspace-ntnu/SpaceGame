// Which player a per-peer piece of UI is talking about.
//
// This is the one property that cannot be checked by playing the game alone: with a single player
// in the session, a lookup that returns "some player" and a lookup that returns "my player" are
// indistinguishable, and every helmet HUD, map and hologram in the project used the first kind.
// Every player object carries the "Player" tag — it is inherited from the PlayerCharacter prefab
// that PlayerCharacterNetworked wraps — so FindGameObjectWithTag returned an arbitrary one and, in
// a three-player session, two people watched a stranger's health bar.
//
// The tests below therefore always build MORE THAN ONE player. A single-player fixture would pass
// against the bug.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    public class LocalPlayerResolutionTests
    {
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp()
        {
            // The resolver caches across calls, and the previous test's player is gone by now.
            GameplayMenuScope.ForgetLocalPlayer();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
            GameplayMenuScope.ForgetLocalPlayer();
        }

        /// <summary>
        /// A player body carrying what the HUD reads off it.
        ///
        /// Built with AddComponent rather than from the prefab, which is what keeps this an EditMode
        /// test: Unity does not raise Awake outside play mode, so PlayerController never runs the
        /// DisablePlayer that would dereference the camera and movement fields nothing wired here.
        /// </summary>
        private PlayerController NewPlayer(string name)
        {
            var go = new GameObject(name) { tag = "Player" };
            spawned.Add(go);

            go.AddComponent<HealthComponent>();
            go.AddComponent<EntityFaction>();
            return go.AddComponent<PlayerController>();
        }

        /// <summary>
        /// Guards the tests that exercise the session-wide fallback, which offline is a scene-wide
        /// search. Run with persistentScene open in the editor it would find that scene's player
        /// rather than the fixture's, and report a failure that is really just the editor's state.
        /// </summary>
        private void RequireSceneWithoutPlayers()
        {
            if (Object.FindFirstObjectByType<PlayerController>() != null)
                Assert.Ignore("A player is already open in the editor's scene; this test needs an empty one.");
        }

        /// <summary>The helmet HUD as it is assembled in PlayerCharacter: a child of the player.</summary>
        private HelmetHUDController NewHelmetUnder(PlayerController player)
        {
            var go = new GameObject("PlayerHUD", typeof(RectTransform));
            go.transform.SetParent(player.transform, false);
            return go.AddComponent<HelmetHUDController>();
        }

        // ─────────── The resolver ───────────

        [Test]
        public void ContextResolutionPicksTheOwningPlayerNotAnArbitraryOne()
        {
            PlayerController first = NewPlayer("player-0");
            PlayerController second = NewPlayer("player-1");
            PlayerController third = NewPlayer("player-2");

            // Asked from each player's own hierarchy, each must get itself back. Two of these three
            // answers were wrong before the fix, whichever one the tag search happened to find.
            Assert.AreSame(first, GameplayMenuScope.FindLocalPlayer(first.transform));
            Assert.AreSame(second, GameplayMenuScope.FindLocalPlayer(second.transform));
            Assert.AreSame(third, GameplayMenuScope.FindLocalPlayer(third.transform));
        }

        [Test]
        public void ContextResolutionReachesThroughNestedUi()
        {
            PlayerController player = NewPlayer("player-0");
            NewPlayer("someone-else");

            var canvas = new GameObject("Canvas", typeof(RectTransform));
            canvas.transform.SetParent(player.transform, false);
            var widget = new GameObject("Widget", typeof(RectTransform));
            widget.transform.SetParent(canvas.transform, false);

            Assert.AreSame(player, GameplayMenuScope.FindLocalPlayer(widget.transform),
                "The HUD sits several levels under the player; resolution has to walk the whole chain.");
        }

        [Test]
        public void ContextResolutionFallsBackWhenThereIsNoOwningPlayer()
        {
            RequireSceneWithoutPlayers();

            PlayerController player = NewPlayer("player-0");

            // The map hologram and MapService live in the persistent scene, under nobody. They get
            // the session's answer, which offline is the only player there is.
            var loose = new GameObject("MapHologram");
            spawned.Add(loose);

            Assert.AreSame(player, GameplayMenuScope.FindLocalPlayer(loose.transform));
        }

        [Test]
        public void AMissIsNeverCached()
        {
            RequireSceneWithoutPlayers();

            // Netcode publishes the local player object AFTER OnNetworkSpawn has run, and
            // OnNetworkSpawn is where this project switches the owner's HUD on — so the first ask
            // always comes back empty and everything depends on the second one working.
            Assert.IsNull(GameplayMenuScope.FindLocalPlayer(), "Nothing has been built yet.");

            PlayerController player = NewPlayer("player-0");

            Assert.AreSame(player, GameplayMenuScope.FindLocalPlayer(),
                "A resolver that latched the first null would leave every HUD blank for the session.");
        }

        [Test]
        public void ADestroyedPlayerIsNotHandedOutAgain()
        {
            RequireSceneWithoutPlayers();

            PlayerController player = NewPlayer("player-0");
            Assert.AreSame(player, GameplayMenuScope.FindLocalPlayer());

            // Held before the destroy: reading .gameObject off a destroyed component throws.
            GameObject body = player.gameObject;
            Object.DestroyImmediate(body);
            spawned.Remove(body);

            // Unity reports a destroyed object as null, which is what makes caching the hit safe
            // across a disconnect or a body that is replaced rather than revived.
            Assert.IsNull(GameplayMenuScope.FindLocalPlayer());

            PlayerController replacement = NewPlayer("player-0-again");
            Assert.AreSame(replacement, GameplayMenuScope.FindLocalPlayer());
        }

        [Test]
        public void LocalPlayerTransformFollowsTheResolvedPlayer()
        {
            RequireSceneWithoutPlayers();

            PlayerController player = NewPlayer("player-0");
            player.transform.position = new Vector3(120f, 3f, -40f);

            Assert.AreEqual(player.transform.position, GameplayMenuScope.LocalPlayerTransform.position);
        }

        // ─────────── The helmet HUD ───────────

        [Test]
        public void EveryHelmetBindsToItsOwnWearersHealth()
        {
            PlayerController[] players =
            {
                NewPlayer("player-0"),
                NewPlayer("player-1"),
                NewPlayer("player-2"),
                NewPlayer("player-3"),
            };

            foreach (PlayerController player in players)
            {
                HelmetHUDController helmet = NewHelmetUnder(player);
                helmet.RebindHealth();

                Assert.AreSame(player.GetComponent<HealthComponent>(), helmet.BoundHealth,
                    $"{player.name}'s visor is showing somebody else's health.");
            }
        }

        [Test]
        public void ASerializedHealthOverrideStillWins()
        {
            PlayerController wearer = NewPlayer("player-0");
            PlayerController other = NewPlayer("player-1");

            HelmetHUDController helmet = NewHelmetUnder(wearer);

            // PlayerHUD.prefab ships this null, so the resolver is the live path — but wiring it by
            // hand has to keep working, and a resolver that overwrote it would be a silent surprise.
            FieldInfo field = typeof(HelmetHUDController)
                .GetField("playerHealth", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(helmet, other.GetComponent<HealthComponent>());

            helmet.RebindHealth();

            Assert.AreSame(other.GetComponent<HealthComponent>(), helmet.BoundHealth);
        }

        [Test]
        public void RebindingIsIdempotent()
        {
            PlayerController player = NewPlayer("player-0");
            HelmetHUDController helmet = NewHelmetUnder(player);

            helmet.RebindHealth();
            helmet.RebindHealth();
            helmet.RebindHealth();

            // A bare += in the old subscribe path is how one hit flashes the visor three times.
            // Counting the delegate is the only way to see it without a live vignette.
            FieldInfo evt = typeof(HealthComponent)
                .GetField("OnDamage", BindingFlags.NonPublic | BindingFlags.Instance);
            var handlers = (System.Delegate)evt.GetValue(player.GetComponent<HealthComponent>());

            Assert.IsNotNull(handlers, "The visor never subscribed at all.");
            Assert.AreEqual(1, handlers.GetInvocationList().Length);
        }
    }
}
