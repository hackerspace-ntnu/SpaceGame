using UnityEngine;
using UnityEngine.SceneManagement;

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
        
        await SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
        
        if (IsSceneLoaded(targetScene))
        {
            Scene scene = SceneManager.GetSceneByBuildIndex(targetScene);
            SceneManager.SetActiveScene(scene);   
        }
        
        targetScene = -1;
    }

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
