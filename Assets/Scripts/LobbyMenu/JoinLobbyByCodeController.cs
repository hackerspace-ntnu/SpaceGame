
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JoinLobbyByCodeController : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    private Button selfButton;
    private LobbySystem lobbySystem;


    private void Start()
    {
        lobbySystem = FindAnyObjectByType<LobbySystem>();
        selfButton = GetComponent<Button>();
        selfButton.onClick.AddListener(() => QueryJoinLobbyWithCode(inputField.text.ToUpper()));
    }

    private void QueryJoinLobbyWithCode(string code)
    {
        lobbySystem.JoinLobbyByCode(code);
    }
}
