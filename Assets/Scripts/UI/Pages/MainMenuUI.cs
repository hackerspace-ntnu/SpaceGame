using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;
    [SerializeField] private SceneReference lobbyScene;
    [SerializeField] private SceneReference minigameScene;

    public void StartSinglePlayer()
    {
        NetworkManager.Singleton.StartHost();
        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }

    public void StartMultiPlayer()
    {
        SceneManager.LoadScene(lobbyScene.SceneName, LoadSceneMode.Single);
    }

    public void StartMinigame()
    {
        // Tell NetworkGameManager's auto-spawn coroutine to hold off until minigameScene has
        // loaded and gone active, otherwise it spawns the player at persistentScene's own
        // SpawnPoint the instant persistentScene finishes loading. Must be set before StartHost()
        // so it's in place before OnNetworkSpawn fires.
        NetworkGameManager.PendingSceneNameToWaitFor = minigameScene.SceneName;
        Debug.Log($"[MainMenuUI DEBUG] Set PendingSceneNameToWaitFor='{NetworkGameManager.PendingSceneNameToWaitFor}' (minigameScene={(minigameScene != null ? minigameScene.name : "NULL")})");

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
