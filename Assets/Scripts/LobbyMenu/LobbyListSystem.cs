using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    public void listNewLobby(Lobby lobby)
    {
        GameObject newLobbyElement = Instantiate(lobbyElement);
        LobbyElementController controller = newLobbyElement.GetComponent<LobbyElementController>();
        controller.setlobbyName(lobby.Name);
        controller.setLobbyId(lobby.Id);
        controller.setMaxPlayers(lobby.MaxPlayers);
        newLobbyElement.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = lobby.Name;
        newLobbyElement.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = lobby.MaxPlayers - lobby.AvailableSlots + "/" + lobby.MaxPlayers;
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

    public void openLobbyScreen(string lobbyName, string lobbyId)
    {
        TextMeshProUGUI lobbyScreenTitle = lobbyScreen.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI lobbyScreenId = lobbyScreen.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        lobbyScreen.SetActive(true);
        lobbyScreenTitle.text = lobbyName;
        lobbyScreenId.text = "Id: " + lobbyId;
    }

    public void showPlayerElements(string[] playerNames)
    {
        if(SceneManager.GetActiveScene().name != "LobbyMenu")
        {
            return;
        }
        Transform playerList = lobbyScreen.transform.GetChild(3).GetChild(0).GetChild(0);
        for (int i = 0; i < playerList.childCount; i++)
        {
            Destroy(playerList.GetChild(i).gameObject);
        }

        foreach (string pName in playerNames)
        {
            GameObject pNameInstance = Instantiate(playerDisplayElement, playerList);
            pNameInstance.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = pName;
        }
    }

    public void changeStateOfPasswordInputFieldCreateLobby()
    {
        if(lobbyPasswordObject.activeSelf)
        {
            lobbyPasswordObject.SetActive(false);
        } else
        {
            lobbyPasswordObject.SetActive(true);
        }
    }

    public void setStartGameButtonState(bool state)
    {
        if (SceneManager.GetActiveScene().name != "LobbyMenu")
        {
            return;
        }
        startGameButton.SetActive(state);
    }

    public void hideLobbyScreen()
    {
        lobbyScreen.SetActive(false);
    }
}