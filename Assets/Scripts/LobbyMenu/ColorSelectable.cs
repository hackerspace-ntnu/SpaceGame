using UnityEngine;
using UnityEngine.UI;

public class ColorSelectable : MonoBehaviour
{
    private LobbySystem lobbySystem;
    void Start()
    {
        lobbySystem = GameObject.FindAnyObjectByType<LobbySystem>();
        GetComponent<Button>().onClick.AddListener(() => SetPlayerColor(GetComponent<Image>().color));
    }
    
    private void SetPlayerColor(Color color)
    {
        lobbySystem.UpdatePlayerColor(color);
    }
}
