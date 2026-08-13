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
    private TMP_InputField passwordInputField;

    [SerializeField]
    private GameObject lobbyPasswordObject;

    [SerializeField]
    private GameObject lobbyScreen;

    [SerializeField]
    private GameObject playerDisplayElement;

    [SerializeField]
    private GameObject startGameButton;

    [SerializeField]
    [Tooltip("The 'enter the password' panel. Left unassigned it is located by name at runtime.")]
    private GameObject joinPrivateLobbyPanel;

    /// <summary>
    /// Hides the password prompt.
    ///
    /// The password field's OnEndEdit in LobbyMenu.unity has always called this, but the method did
    /// not exist on this class. UnityEvent resolves targets by name at runtime and silently drops
    /// any it cannot find — no exception, no console entry — so the prompt simply never closed and
    /// nothing anywhere said why.
    /// </summary>
    public void closeJoinPrivateLobbyScreen()
    {
        GameObject panel = ResolveJoinPrivateLobbyPanel();

        if (panel == null)
        {
            Debug.LogWarning("[LobbyListSystem] No JoinLobbyByPasswordPanel to close — assign " +
                             "joinPrivateLobbyPanel in the inspector.");
            return;
        }

        panel.SetActive(false);
    }

    /// <summary>
    /// Falls back to a name lookup so the button works whether or not the field was ever wired.
    /// Inactive objects are included: this panel is usually already hidden when first asked for.
    /// </summary>
    private GameObject ResolveJoinPrivateLobbyPanel()
    {
        if (joinPrivateLobbyPanel != null) return joinPrivateLobbyPanel;

        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name != "JoinLobbyByPasswordPanel") continue;
            if (t.gameObject.scene != gameObject.scene) continue;

            joinPrivateLobbyPanel = t.gameObject;
            return joinPrivateLobbyPanel;
        }

        return null;
    }

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
        return passwordInputField.text;
    }

    public void openLobbyScreen(string lobbyName, string lobbyCode)
    {
        TextMeshProUGUI lobbyScreenTitle = lobbyScreen.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI lobbyScreenId = lobbyScreen.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        lobbyScreen.SetActive(true);
        lobbyScreenTitle.text = lobbyName;
        lobbyScreenId.text = "Code: " + lobbyCode;
    }

    public void showPlayerElements(string[] playerNames)
    {
        if(SceneManager.GetActiveScene().name != "LobbyMenu")
        {
            return;
        }
        Transform playerList = FindPlayerListContainer();
        if (playerList == null) return;

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

    /// <summary>
    /// The scroll content the player name entries go into.
    ///
    /// Located by name rather than by walking GetChild(3).GetChild(0).GetChild(0). That chain is
    /// re-evaluated on every lobby poll, so reordering the lobby screen's children in the inspector
    /// — a thing anyone editing this menu will do — turned it into an IndexOutOfRangeException
    /// twice a second, which killed the poll and froze the roster rather than shifting it.
    /// </summary>
    private Transform FindPlayerListContainer()
    {
        if (lobbyScreen == null) return null;

        foreach (Transform t in lobbyScreen.GetComponentsInChildren<Transform>(true))
            if (t.name == "PlayerList")
                return t;

        Debug.LogWarning("[LobbyListSystem] No 'PlayerList' object under the lobby screen — " +
                         "the player roster cannot be shown.");
        return null;
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