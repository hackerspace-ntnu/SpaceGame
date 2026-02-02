
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JoinLobbyByPasswordButton : MonoBehaviour
{
  [SerializeField] private TMP_InputField inputField;
  private Button selfButton;
  private LobbySystem lobbySystem;


  private void Start()
  {
    lobbySystem = FindAnyObjectByType<LobbySystem>();
    selfButton = GetComponent<Button>();
    selfButton.onClick.AddListener(() => QueryJoinLobbyWithGivenPassword(inputField.text));
  }

  private void QueryJoinLobbyWithGivenPassword(string password)
  {
    lobbySystem.JoinLobbyByPassword(password);
  }
}
