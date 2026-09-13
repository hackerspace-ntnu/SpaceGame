// Which player a cutscene locks.
//
// The property that cannot be checked by playing alone: with one player in the session, "some
// player" and "my player" are the same object. In a six-player arrival every machine holds six
// PlayerControllers, and a director that searched the scene for the first one locked, blacked out
// and rigged a stranger's camera — the local player kept their HUD, got no shake, no concussion
// blur and no seated look, or got all of them, depending on which body the search happened to
// find first. So every fixture below builds MORE THAN ONE player.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class CutsceneSubjectResolutionTests
    {
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp() => GameplayMenuScope.ForgetLocalPlayer();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
            GameplayMenuScope.ForgetLocalPlayer();
        }

        /// <summary>
        /// A player body. Built with AddComponent rather than from the prefab so that this stays an
        /// EditMode test: Unity raises no Awake outside play mode, so PlayerController never runs
        /// the DisablePlayer that would dereference fields nothing wired here.
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
        /// Guards the fallback test, which offline is a scene-wide search. Run with persistentScene
        /// open in the editor it would find that scene's player rather than the fixture's.
        /// </summary>
        private void RequireSceneWithoutPlayers()
        {
            if (Object.FindFirstObjectByType<PlayerController>() != null)
                Assert.Ignore("A player is already open in the editor's scene; this test needs an empty one.");
        }

        [Test]
        public void ASubjectResolvesToItsOwnPlayerNotTheFirstOneInTheScene()
        {
            PlayerController first = NewPlayer("player-0");
            PlayerController second = NewPlayer("player-1");
            PlayerController third = NewPlayer("player-2");

            Assert.AreSame(third, CutsceneDirector.ResolvePlayer(third.gameObject));
            Assert.AreSame(second, CutsceneDirector.ResolvePlayer(second.gameObject));
            Assert.AreSame(first, CutsceneDirector.ResolvePlayer(first.gameObject));
        }

        [Test]
        public void ASubjectUnderThePlayerResolvesUpToThatPlayer()
        {
            NewPlayer("someone-else");
            PlayerController player = NewPlayer("player-1");

            // The seated body's camera or a trigger under the body: the same walk the HUD does.
            var camera = new GameObject("Main Camera");
            camera.transform.SetParent(player.transform, false);

            Assert.AreSame(player, CutsceneDirector.ResolvePlayer(camera));
        }

        [Test]
        public void WithoutASubjectTheDirectorLocksTheSessionsLocalPlayerNotTheFirstOneFound()
        {
            RequireSceneWithoutPlayers();

            // The session's answer is cached the first time it resolves. Offline that is a scene
            // search, so it is made BEFORE the other bodies exist — exactly the shape of a host
            // whose own body spawned first and then watched five crewmates arrive.
            PlayerController local = NewPlayer("local-player");
            Assume.That(GameplayMenuScope.FindLocalPlayer(), Is.SameAs(local));

            NewPlayer("crewmate-1");
            NewPlayer("crewmate-2");

            // Taken out of the scene search's reach without being destroyed: a raw
            // FindFirstObjectByType skips inactive objects and must now answer a crewmate, while
            // the session resolver still holds the local player. A director that asked the scene
            // instead of the session is caught here.
            local.gameObject.SetActive(false);
            Assume.That(Object.FindFirstObjectByType<PlayerController>(), Is.Not.SameAs(local));

            Assert.AreSame(local, CutsceneDirector.ResolvePlayer(null),
                "A subject-less cutscene must lock the player this machine drives, not whichever " +
                "PlayerController the scene search finds first.");
        }
    }
}
