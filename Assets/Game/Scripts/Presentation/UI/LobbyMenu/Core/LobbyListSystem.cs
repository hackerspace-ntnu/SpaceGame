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
    private TMP_InputField passwordInputField;

    [SerializeField]
    private GameObject lobbyPasswordObject;

    [SerializeField]
    private GameObject lobbyScreen;

    [SerializeField]
    [Tooltip("The lobby name shown on the in-lobby screen.")]
    private TextMeshProUGUI lobbyScreenTitle;

    [SerializeField]
    [Tooltip("The 'Code: ABC123' line on the in-lobby screen.")]
    private TextMeshProUGUI lobbyScreenCode;

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
        if (lobbyElementContainer == null || lobbyElement == null) return;

        GameObject newLobbyElement = Instantiate(lobbyElement, lobbyElementContainer.transform, false);

        // Through the controller only. The previous version ALSO wrote the same two labels by
        // child index, so a row had two sources of truth that could disagree — and the index walk
        // broke the moment anyone reordered the prefab's children.
        LobbyElementController controller = newLobbyElement.GetComponent<LobbyElementController>();
        controller.setlobbyName(lobby.Name);
        controller.setLobbyId(lobby.Id);
        controller.setOccupancy(lobby.MaxPlayers, lobby.AvailableSlots);
        controller.setPlaying(SpaceGame.Core.LobbySession.IsPlaying(lobby));
    }

    public void clearPrevList()
    {
        if (lobbyElementContainer == null) return;

        // Iterating the container's own Transform yields its children. The previous version went
        // through GetComponentInChildren<Transform>(), which returns the container's own transform
        // — so it happened to work, by accident, through a call that reads as if it does something
        // else entirely.
        foreach (Transform t in lobbyElementContainer.transform)
            Destroy(t.gameObject);
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

    /// <summary>
    /// Shows the in-lobby screen.
    ///
    /// The title and code are serialized rather than reached through lobbyScreen.GetChild(0) and
    /// GetChild(1). That chain ran on every lobby poll, so reordering the screen's children in the
    /// inspector — a thing anyone editing this menu will do — turned it into an exception twice a
    /// second. FindPlayerListContainer below already documents the same fault.
    /// </summary>
    public void openLobbyScreen(string lobbyName, string lobbyCode)
    {
        if (lobbyScreen == null) return;

        lobbyScreen.SetActive(true);

        if (lobbyScreenTitle != null) lobbyScreenTitle.text = lobbyName;
        if (lobbyScreenCode != null) lobbyScreenCode.text = "Code: " + lobbyCode;
    }

    public void showPlayerElements(string[] playerNames)
    {
        // The session outlives this scene, so it can still push a roster after the canvas has been
        // destroyed. A destroyed GameObject compares equal to null, which makes this both the
        // liveness check and the null check — and unlike the scene-name test it replaced, it does
        // not break when the scene is renamed.
        if (lobbyScreen == null || playerDisplayElement == null) return;

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
        if (startGameButton == null) return;
        startGameButton.SetActive(state);
    }

    public void hideLobbyScreen()
    {
        if (lobbyScreen == null) return;
        lobbyScreen.SetActive(false);
    }
}