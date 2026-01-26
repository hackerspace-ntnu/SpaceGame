using TMPro;
using UnityEngine;

public class LobbyElementController : MonoBehaviour
{
  private string lobbyId;
  private string lobbyName;
  private int maxPlayers;
  private string lobbyCode;

  [SerializeField]
  private TextMeshProUGUI lobbyNameUI;

  [SerializeField]
  private TextMeshProUGUI lobbyIdUI;

  [SerializeField]
  private TextMeshProUGUI maxPlayersUI;

  [SerializeField]
  private TextMeshProUGUI lobbyCodeUI;

  public void setlobbyName(string newLobbyName){
    lobbyNameUI.text = newLobbyName;
    lobbyName = newLobbyName;
    lobbyNameUI.text = newLobbyName;
  }

  public void setMaxPlayers(int newMaxPlayers) {
    maxPlayersUI.text = "0/" + newMaxPlayers.ToString();
    maxPlayers = newMaxPlayers;
  }

  public void setLobbyId(string newLobbyId) {
    lobbyIdUI.text = newLobbyId;
    lobbyId = newLobbyId;
  }

  public void setlobbyCode(string newLobbyCode)
  {
    lobbyCodeUI.text = newLobbyCode;
    lobbyName = newLobbyCode;
  }

  public string getLobbyName() {
    return lobbyName;
  }

  public int getMaxPlayers() {
    return maxPlayers;
  }

  public string getLobbyId() {
    return lobbyId;
  }

  public void attemptJoin(){
    LobbySystem l = FindAnyObjectByType<LobbySystem>();
    l.JoinLobbyById(lobbyId);
  }
}
