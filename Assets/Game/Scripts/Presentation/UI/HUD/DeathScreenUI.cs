using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
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

    private void OnDestroy()
    {
        if (player != null)
            player.OnPlayerDeath -= ShowDeathScreen;
    }

    private void ShowDeathScreen()
    {
        deathScreen.gameObject.SetActive(true);
    }

    public void Respawn()
    {
        if (deathScreen == null || player == null)
        {
            return;
        }

        // Hide before respawning, not after: the singleplayer path destroys this player object,
        // and this HUD hangs off it. Once RespawnPlayer returns, the line after it may be running
        // on a destroyed object and never reaches the screen — leaving the death overlay up and
        // swallowing clicks over a player who is alive again.
        deathScreen.gameObject.SetActive(false);

        if (SpawnManager.Instance == null)
        {
            Debug.LogError($"{name}: no SpawnManager to respawn through.", this);
            return;
        }

        SpawnManager.Instance.RespawnPlayer(player.gameObject);
    }
}
