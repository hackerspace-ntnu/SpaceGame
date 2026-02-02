using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using System.Collections.Generic;

public class LobbySystem : MonoBehaviour
{
    private Lobby hostLobby;
    private Lobby joinedLobby;
    private float heartBeatTimer;
    private float lobbyUpdateTimer;
    private float refreshLobbyListTimer;
    private LobbyListSystem lobbyList;
    private string playerName; 
    public async void Start()
    {
        playerName = "Player" + UnityEngine.Random.Range(10, 99);
        lobbyList = GetComponent<LobbyListSystem>();
        await UnityServices.InitializeAsync();

        AuthenticationService.Instance.SignedIn += () =>
        {
        Debug.Log("Signed in " + AuthenticationService.Instance.PlayerId);
        };

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void Update() {
        //HandleLobbyRefreshList();
        HandleLobbyHeartBeat();
        HandleLobbyPollForUpdates();
    }

    private async void HandleLobbyHeartBeat() {
        if(hostLobby != null) {
            heartBeatTimer -= Time.deltaTime;
            if(heartBeatTimer <= 0f) {
                float heartBeatTimerMax = 15f;
                heartBeatTimer = heartBeatTimerMax;
                await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
            }
        }
    }
    private async void HandleLobbyPollForUpdates()
    {
        if (joinedLobby != null)
        {
            lobbyUpdateTimer -= Time.deltaTime;
            if (lobbyUpdateTimer <= 0f)
            {
                float lobbyUpdateTimerMax = 2f;
                lobbyUpdateTimer = lobbyUpdateTimerMax;
                Lobby lobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                joinedLobby = lobby;
                UpdatePlayerListInLobby();
            }
        }
    }
    private async void HandleLobbyRefreshList()
    {
        if(UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn)
        {
            refreshLobbyListTimer -= Time.deltaTime;
            if(refreshLobbyListTimer < 0f )
            {
                float refreshLobbyListTimerMax = 5f;
                refreshLobbyListTimer = refreshLobbyListTimerMax;
                listLobbies();
            }
        }
    }
    public async void createLobbyWithGivenOptions()
    {
        try
        {
        string lobbyName = lobbyList.getLobbyNameInputText();
        int maxPlayers = 4;

        CreateLobbyOptions createLobbyOptions = new CreateLobbyOptions
        {
            IsPrivate = false,
            Player = GetPlayer(),
        };

        if(lobbyList.getLobbyPrivate())
        {
            createLobbyOptions.IsPrivate = true;
            createLobbyOptions.Password = lobbyList.getLobbyPasswordInputText();
        }

        Lobby testLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, createLobbyOptions);
        await AuthenticationService.Instance.UpdatePlayerNameAsync("Player_1");
        
        hostLobby = testLobby;
        joinedLobby = hostLobby;
        List<string> playerNames = new List<string>
        {
            hostLobby.Players[0].Data["PlayerName"].Value,
        };
        lobbyList.openLobbyScreen(joinedLobby.Name);
        lobbyList.showPlayerElements(playerNames.ToArray());
        listLobbies();
        Debug.Log("Created Lobby! " + testLobby.Name + " " + testLobby.MaxPlayers + " " + testLobby.Id + " " + testLobby.LobbyCode);
        printPlayers(hostLobby);
        }
        catch (LobbyServiceException e)
        {
        Debug.Log(e);
        }
    }

    public async void listLobbies()
    {
        try
        {
        QueryLobbiesOptions queryLobbiesOptions = new QueryLobbiesOptions
        {
            Count = 25,
            Filters = new List<QueryFilter>
            {
            new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
            },
            Order = new List<QueryOrder>
            {
            new QueryOrder(false, QueryOrder.FieldOptions.Created)
            }
        };

        QueryResponse queryResponse = await LobbyService.Instance.QueryLobbiesAsync(queryLobbiesOptions);
        Debug.Log("Lobbies found: " + queryResponse.Results.Count);

        lobbyList.clearPrevList();

        foreach (Lobby lobby in queryResponse.Results)
        {
            lobbyList.listNewLobby(lobby);
            Debug.Log(lobby.Name + " " + lobby.MaxPlayers);
        }
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public async void JoinLobbyById(string id) {
        Debug.Log("Joining lobby: " + id);
        var joinOptions = new JoinLobbyByIdOptions
        {
            Player = GetPlayer()
        };
        try {
        JoinLobby(await LobbyService.Instance.JoinLobbyByIdAsync(id, joinOptions));
        }
        catch (LobbyServiceException e)
        {
        Debug.Log(e);
        }
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        Debug.Log("Joining lobby: " + lobbyCode);
        try
        {
        JoinLobby(await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode));
        }
        catch (LobbyServiceException e)
        {
        Debug.Log(e);
        }
    }

    public async void JoinLobbyByPassword(string lobbyPassword)
    {
        var idOptions = new JoinLobbyByIdOptions{
            Password = lobbyPassword,
            Player = GetPlayer()
        };
        Debug.Log("Joining lobby: " + lobbyPassword);

        try
        {
            JoinLobby(await LobbyService.Instance.JoinLobbyByIdAsync("lobbyId", idOptions));
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private void JoinLobby(Lobby lobby)
    {
        Debug.Log("Succesfully joined Lobby!");
        joinedLobby = lobby;
        
        AuthenticationService.Instance.UpdatePlayerNameAsync("Player_" + (lobby.Players.Count + 1));
        lobbyList.openLobbyScreen(joinedLobby.Name);
        UpdatePlayerListInLobby();

    }

    private void UpdatePlayerListInLobby()
    {
        List<string> playerNames = new List<string>();
        foreach (Player p in joinedLobby.Players)
        {
            playerNames.Add(p.Data["PlayerName"].Value);
        }
        lobbyList.showPlayerElements(playerNames.ToArray());
    }
    private void printPlayers()
    {
        printPlayers(joinedLobby);
    }
    private void printPlayers(Lobby lobby)
    {
        Debug.Log("Player in lobby:" + lobby.Name);
        foreach(Player p in lobby.Players)
        {
            Debug.Log(p.Id + " " + p.Data["PlayerName"].Value);
        }
    }


    private Player GetPlayer()
    {
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {"PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)}
            }
        };
    }

    public async void LeaveLobby() {
        try {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            joinedLobby = null;
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }

    public async void KickPlayer(string playerId)
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, playerId);
            joinedLobby = null;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    public async void MigrateLobbyHost(string newHostId)
    {
        try
        {
            hostLobby = await LobbyService.Instance.UpdateLobbyAsync(hostLobby.Id, new UpdateLobbyOptions
            {
                HostId = newHostId
            });
            joinedLobby = hostLobby;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private bool isPlayerHost()
    {
        return joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }
}
