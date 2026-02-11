using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;

    public void StartSinglePlayer()
    {
        SceneManager.LoadScene(gameScene.SceneName);
    }
    
    public void StartMultiPlayer()
    {
        
    }
    
    public void OpenSettings()
    {
        
    }
    
    public void QuitGame()
    {
        Application.Quit();
    }
}
