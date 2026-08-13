using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;
    [SerializeField] private SceneReference lobbyScene;
    [SerializeField] private SceneReference minigameScene;

    public void StartSinglePlayer()
    {
        // Up before the load starts, and held until terrain streaming and the NavMesh bake have
        // finished — those run after the scene reports loaded and are what makes the first few
        // seconds stutter.
        LoadingScreenUI.ShowUntilReady(gameScene.SceneName);

        NetworkManager.Singleton.StartHost();
        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }

    public void StartMultiPlayer()
    {
        SceneManager.LoadScene(lobbyScene.SceneName, LoadSceneMode.Single);
    }

    // Wired to the menu's Minigame button. The match is configured first — the
    // config screen calls LaunchMinigame() once the host has picked a gamemode.
    public void StartMinigame()
    {
        MinigameConfigUI.Open(this);
    }

    public void LaunchMinigame()
    {
        // Tell NetworkGameManager's auto-spawn coroutine to hold off until minigameScene has
        // loaded and gone active, otherwise it spawns the player at persistentScene's own
        // SpawnPoint the instant persistentScene finishes loading. Must be set before StartHost()
        // so it's in place before OnNetworkSpawn fires.
        NetworkGameManager.PendingSceneNameToWaitFor = minigameScene.SceneName;

        // Waits on the arena, not gameScene: the arena is loaded additively on top and is the
        // scene the player actually ends up in.
        LoadingScreenUI.ShowUntilReady(minigameScene.SceneName, "Entering Arena");

        NetworkManager.Singleton.StartHost();

        void OnLoaded(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            if (sceneName != gameScene.SceneName) return;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoaded;
            NetworkManager.Singleton.SceneManager.LoadScene(minigameScene.SceneName, LoadSceneMode.Additive);
        }

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoaded;
        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }
    
    public void OpenSettings()
    {
        
    }
    
    public void QuitGame()
    {
        Application.Quit();
    }
}
