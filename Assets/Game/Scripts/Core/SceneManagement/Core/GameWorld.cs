using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Core
{
    /// <summary>
    /// One playable world: the scene it is played in and the identity its saves carry.
    ///
    /// The pair used to live as loose fields on <see cref="MainMenuUI"/>, which made the menu the
    /// only thing that knew a scene WAS a world. Pressing Play in a world scene then loaded it bare —
    /// no session, no staged world, nobody spawned — because the bootstrapper had no way to tell it
    /// from a test scene. Kept under <c>Resources/Worlds</c> so <see cref="ForScene"/> can answer
    /// that question before any scene with a serialized reference has loaded.
    /// </summary>
    [CreateAssetMenu(fileName = "GameWorld", menuName = "Scene Management/Game World")]
    public class GameWorld : ScriptableObject
    {
        private const string ResourcesFolder = "Worlds";

        [Tooltip("The scene the world is played in, loaded Single by every route into it.")]
        [SerializeField] private SceneReference scene;

        [Tooltip("The world's save identity, recorded in every save's header so a save cannot be " +
                 "loaded into a different world. A world with no chunk streaming still needs one; " +
                 "its config lists no chunks and is never given to a streamer.")]
        [SerializeField] private WorldStreamingConfig identity;

        public string SceneName => scene != null ? scene.SceneName : null;

        public WorldStreamingConfig Identity => identity;

        /// <summary>The world played in <paramref name="sceneName"/>, or null when it is not a world scene.</summary>
        public static GameWorld ForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;

            foreach (GameWorld world in Resources.LoadAll<GameWorld>(ResourcesFolder))
            {
                if (world.SceneName == sceneName) return world;
            }

            return null;
        }
    }
}
