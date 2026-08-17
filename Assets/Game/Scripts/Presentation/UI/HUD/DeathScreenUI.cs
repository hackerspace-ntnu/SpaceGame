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

        // Hide first. The overlay is this player's own HUD, and nothing below is guaranteed to
        // come back to it — leaving the screen up would swallow clicks over a player who is alive
        // again.
        deathScreen.gameObject.SetActive(false);

        var respawn = player.GetComponent<PlayerRespawn>();
        if (respawn == null)
        {
            Debug.LogError($"{name}: '{player.name}' has no PlayerRespawn, so this button cannot " +
                           "bring them back. Add one to the player prefab.", this);
            return;
        }

        // A request, not an order: the server decides whether the respawn happens and where. The
        // button is pressed on the machine that owns this body, which is the only one that could
        // be showing this screen.
        respawn.Request();
    }
}
