using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;
    [SerializeField] private SceneReference lobbyScene;

    public void StartSinglePlayer()
    {
        NetworkManager.Singleton.StartHost();
        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }
    
    public void StartMultiPlayer()
    {
        SceneManager.LoadScene(lobbyScene.SceneName);
    }
    
    public void OpenSettings()
    {
        
    }
    
    public void QuitGame()
    {
        Application.Quit();
    }
}
