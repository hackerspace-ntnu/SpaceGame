using TMPro;
using UnityEngine;

/// <summary>
/// One row in the lobby browser. Holds the id so the row can join itself when clicked.
/// </summary>
public class LobbyElementController : MonoBehaviour
{
  private string lobbyId;
  private string lobbyName;
  private int maxPlayers;

  [SerializeField]
  private TextMeshProUGUI lobbyNameUI;

  [SerializeField]
  private TextMeshProUGUI maxPlayersUI;

  [SerializeField]
  [Tooltip("Shows 'waiting' or 'in game' for this row.")]
  private TextMeshProUGUI lobbyStateUI;

  public void setlobbyName(string newLobbyName)
  {
    lobbyName = newLobbyName;
    if (lobbyNameUI != null) lobbyNameUI.text = newLobbyName;
  }

  /// <summary>
  /// "3/4" — taken over total. Lobby reports FREE slots, and the previous version hardcoded the
  /// taken count to zero, so every row in the browser claimed to be empty.
  /// </summary>
  public void setOccupancy(int newMaxPlayers, int availableSlots)
  {
    maxPlayers = newMaxPlayers;
    if (maxPlayersUI != null)
      maxPlayersUI.text = SpaceGame.Core.LobbySession.DescribeOccupancy(newMaxPlayers, availableSlots);
  }

  /// <summary>Late join is allowed, so a running session is worth labelling rather than hiding.</summary>
  public void setPlaying(bool playing)
  {
    if (lobbyStateUI != null) lobbyStateUI.text = playing ? "in game" : "waiting";
  }

  public void setLobbyId(string newLobbyId)
  {
    lobbyId = newLobbyId;
  }

  public string getLobbyId()
  {
    return lobbyId;
  }

  /// <summary>Bound by name from LobbyElement.prefab's row button.</summary>
  public void attemptJoin()
  {
    LobbySystem l = FindAnyObjectByType<LobbySystem>();
    if (l == null)
    {
      Debug.LogWarning($"[LobbyElement] No LobbySystem in the scene — cannot join '{lobbyName}'.");
      return;
    }

    l.JoinLobbyById(lobbyId);
  }
}
