using UnityEngine;

public class DeathScreenUI : MonoBehaviour
{
    
    [SerializeField] private RectTransform deathScreen;
    [SerializeField] private PlayerController player;


    private void Start()
    {
        if (deathScreen == null || player == null)
        {
            Debug.LogWarning($"{name}: DeathScreenUI is missing required references.", this);
            return;
        }

        deathScreen.gameObject.SetActive(false);
        player.OnPlayerDeath += ShowDeathScreen;
    }
    
    private void ShowDeathScreen()
    {
        Debug.Log("Show death screen invoked");
        deathScreen.gameObject.SetActive(true);
    }
    
    public void Respawn()
    {
        if (deathScreen == null)
        {
            return;
        }

        NetworkGameManager.Instance.Respawn();  
        
        deathScreen.gameObject.SetActive(false);
    }
}
