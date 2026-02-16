using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyListSystem : MonoBehaviour
{
  [SerializeField]
  private GameObject lobbyElementContainer;

  [SerializeField]
  private GameObject lobbyElement;

  [SerializeField]
  private TextMeshProUGUI lobbyNameInputField;

  [SerializeField]
  private Toggle lobbyPrivateToggle;

  [SerializeField]
  private TextMeshProUGUI lobbyPasswordInputField;

  [SerializeField]
  private GameObject lobbyPasswordObject;

  [SerializeField]
  private GameObject lobbyScreen;

  [SerializeField]
  private GameObject playerDisplayElement;
  [SerializeField]
  private GameObject startGameButton;

  public void listNewLobby(Lobby lobby) {
    GameObject newLobbyElement = Instantiate(lobbyElement);
    LobbyElementController controller = newLobbyElement.GetComponent<LobbyElementController>();
    controller.setlobbyName(lobby.Name);
    controller.setLobbyId(lobby.Id);
    controller.setMaxPlayers(lobby.MaxPlayers);
    newLobbyElement.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = lobby.Name;
    newLobbyElement.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = lobby.Id;
    newLobbyElement.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = lobby.LobbyCode;
    newLobbyElement.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = lobby.MaxPlayers - lobby.AvailableSlots + "/" + lobby.MaxPlayers;
    newLobbyElement.transform.SetParent(lobbyElementContainer.transform, false);
  }

  public void clearPrevList()
  {
    foreach (Transform t in lobbyElementContainer.GetComponentInChildren<Transform>())
    {
      Destroy(t.gameObject);
    }
  }

  public string getLobbyNameInputText()
  {
    return lobbyNameInputField.text;
  }

  public bool getLobbyPrivate()
  {
    return lobbyPrivateToggle.isOn;
  }

  public string getLobbyPasswordInputText()
  {
    return lobbyPasswordInputField.text;
  }

    public void openLobbyScreen(string lobbyName)
    {
        TextMeshProUGUI lobbyScreenTitle = lobbyScreen.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        lobbyScreen.SetActive(true);
        lobbyScreenTitle.text = lobbyName;
    }

    public void showPlayerElements(string[] playerNames)
    {
        Transform playerList = lobbyScreen.transform.GetChild(2).GetChild(0);
        for (int i = 0; i < playerList.childCount; i++)
        {
            Destroy(playerList.GetChild(i).gameObject);
        }
        
        foreach (string pName in playerNames)
        {
            GameObject pNameInstance = Instantiate(playerDisplayElement, playerList);
            pNameInstance.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = pName;
        }
    }

    public void ShowStartButton()
    {
        startGameButton.SetActive(true);
    }

    public void HideStartButton()
    {
        startGameButton.SetActive(false);
    }
}
