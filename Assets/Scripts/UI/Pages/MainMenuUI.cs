using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;
    [SerializeField] private SceneReference lobbyMenuScene;

    public void StartSinglePlayer()
    {
        SceneManager.LoadScene(gameScene.SceneName);
    }
    
    public void StartMultiPlayer()
    {
        SceneManager.LoadScene(lobbyMenuScene.SceneName);
    }
    
    public void OpenSettings()
    {
        
    }
    
    public void QuitGame()
    {
        Application.Quit();
    }
}
