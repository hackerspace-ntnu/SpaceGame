using UnityEngine;

public class DeathScreenUI : MonoBehaviour
{
    
    [SerializeField] private RectTransform deathScreen;
    [SerializeField] private PlayerController player;


    private void Start()
    {
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
        NetworkGameManager.Instance.Respawn();  
        
        deathScreen.gameObject.SetActive(false);
    }
}
