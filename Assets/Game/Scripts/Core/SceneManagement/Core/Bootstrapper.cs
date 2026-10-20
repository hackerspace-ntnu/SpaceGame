using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Core
{
    public static class Bootstrapper
    {
        static int targetScene = -1;
    
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad()
        {
            if (IsSceneLoaded(0))
                return;
        
            targetScene = SceneManager.GetActiveScene().buildIndex;
        
            if (targetScene == 0)
                return;
        
            SceneManager.LoadScene(0, LoadSceneMode.Single);
        }
    
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static async void AfterSceneLoad()
        {

            if (targetScene <= 0)
                targetScene = 1; // Default to main menu if no target specified

            // Play pressed in a world scene: enter it the way the menu's New World does — hosted,
            // with a fresh world staged — instead of loading it bare, where nobody hosts it, nobody
            // spawns and nothing saves.
            GameWorld world = GameWorld.ForScene(SceneNameAt(targetScene));
            if (world != null)
            {
                targetScene = -1;
                await EnterAsNewWorld(world);
                return;
            }

            await SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
        
            if (IsSceneLoaded(targetScene))
            {
                Scene scene = SceneManager.GetSceneByBuildIndex(targetScene);
                SceneManager.SetActiveScene(scene);   
            }
        
            targetScene = -1;
        }

        /// <summary>
        /// Starts <paramref name="world"/> as a new world named <c>Editor &lt;World&gt;</c> — one save
        /// slot per world, replaced on every Play.
        /// </summary>
        static async Awaitable EnterAsNewWorld(GameWorld world)
        {
            // Up now, not when the session starts: the first frame arrives only after the whole
            // opening load (the world scene the editor had open, then Bootstrap), and with no camera
            // in it ViewGuard reads that one long frame as a black screen that long — an error,
            // which Error Pause turns into a frozen game before anybody spawns.
            LoadingScreenUI.ShowUntilReady(world.SceneName);

            // AfterSceneLoad runs before Start on Bootstrap's objects; the menu route only ever hosts
            // after they have started, so this one does too.
            await Awaitable.NextFrameAsync();

            // A new world must not land on an existing file — the menu refuses a taken name for the
            // same reason. Its write-on-entry would replace the last run's world with one nobody has
            // spawned in yet, which SaveManager refuses as lost progress, loudly. The slot is this
            // route's own, so the last run's world is dropped instead.
            string displayName = $"Editor {world.name}";
            new SaveSlots(SaveManager.DefaultRoot).Delete(WorldIdentity.IdFor(displayName));

            WorldSession.StageNew(displayName, world.Identity);
            SessionEntry.EnterWorld(world.SceneName);
        }

        static string SceneNameAt(int buildIndex) =>
            Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(buildIndex));

        static bool IsSceneLoaded(int buildIndex)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).buildIndex == buildIndex)
                    return true;
            }
            return false;
        }
    }
}
