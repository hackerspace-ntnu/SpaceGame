using UnityEngine;
using UnityEngine.UI;

public class ColorSelectable : MonoBehaviour
{
    private LobbySystem lobbySystem;

    private PlayerColorSync playerColorSync;
    void Start()
    {
        playerColorSync = FindAnyObjectByType<PlayerColorSync>();
        lobbySystem = FindAnyObjectByType<LobbySystem>();
        GetComponent<Button>().onClick.AddListener(() => {
            SetPlayerColor(GetComponent<Image>().color);
            playerColorSync.updateColorLocal();
        });
    }
    
    private void SetPlayerColor(Color color)
    {
        lobbySystem.UpdatePlayerColor(color);
    }
}
