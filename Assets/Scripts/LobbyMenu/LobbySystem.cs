using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using System.Collections.Generic;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using System.Threading.Tasks;
using UnityEngine.Rendering;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using System;
using UnityEngine.SceneManagement;

public class LobbySystem : NetworkBehaviour
{
    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";
    private Lobby hostLobby;
    private Lobby joinedLobby;
    private float heartBeatTimer;
    private float lobbyUpdateTimer;
    private float refreshLobbyListTimer;
    public static int maxPlayers = 4;
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

        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            Debug.Log($"[NETCODE] Successfully Connected! ID: {id}");
        };

        NetworkManager.Singleton.OnClientDisconnectCallback += (id) =>
        {
            // This is the most important debug line!
            Debug.Log($"[NETCODE] Disconnected. Reason: {NetworkManager.Singleton.DisconnectReason}");
        };

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
                if (isPlayerHost())
                {
                    lobbyList.setStartGameButtonState(true);
                }
                else
                {
                    lobbyList.setStartGameButtonState(false);
                }
            }
        }
    }
    private void HandleLobbyRefreshList()
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

    private async Task<Allocation> AllocateRelay()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            return allocation;
        } catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
        
    }


    private async Task<String> GetRelayJoinCode(Allocation allocation)
    {
        try
        {
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return joinCode;
        } catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
        
    }


    private async Task<JoinAllocation> JoinRelay(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            return joinAllocation;
        } catch (RelayServiceException e)
        {
            Debug.Log(e);
            return default;
        }
        
    }
    public async void createLobbyWithGivenOptions()
    {
        try
        {
            string lobbyName = lobbyList.getLobbyNameInputText();
            
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

            Allocation allocation = await AllocateRelay();
            string relayJoinCode = await GetRelayJoinCode(allocation);

            Lobby testLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, createLobbyOptions);

            hostLobby = testLobby;
            joinedLobby = hostLobby;

            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {KEY_RELAY_JOIN_CODE, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)}
                }
            });

            
            RelayServerData relayData = new RelayServerData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes, allocation.ConnectionData, allocation.ConnectionData, allocation.Key, false);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayData);

            await AuthenticationService.Instance.UpdatePlayerNameAsync("Player_1");

            
            NetworkManager.Singleton.StartHost();
            List<string> playerNames = new List<string>
            {
                hostLobby.Players[0].Data["PlayerName"].Value,
            };
            lobbyList.openLobbyScreen(joinedLobby.Name);
            lobbyList.showPlayerElements(playerNames.ToArray());
            listLobbies();
            lobbyList.setStartGameButtonState(true);
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
        //Debug.Log("Lobbies found: " + queryResponse.Results.Count);

        lobbyList.clearPrevList();

        foreach (Lobby lobby in queryResponse.Results)
        {
            lobbyList.listNewLobby(lobby);
            //Debug.Log(lobby.Name + " " + lobby.MaxPlayers);
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
            LobbyJoinRelayLayer(await LobbyService.Instance.JoinLobbyByIdAsync(id, joinOptions));
        }
        catch (LobbyServiceException e)
        {
        Debug.Log(e);
        }
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        //Debug.Log("Joining lobby: " + lobbyCode);
        try
        {
            LobbyJoinRelayLayer(await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode));
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
        //Debug.Log("Joining lobby: " + lobbyPassword);

        try
        {
            LobbyJoinRelayLayer(await LobbyService.Instance.JoinLobbyByIdAsync("lobbyId", idOptions));
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private async void LobbyJoinRelayLayer(Lobby lobby)
    {
        try
        {
            JoinAllocation joinAllocation = await JoinRelay(lobby.Data[KEY_RELAY_JOIN_CODE].Value);
            
            RelayServerData relayData = new RelayServerData(joinAllocation.RelayServer.IpV4, (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes, joinAllocation.ConnectionData, joinAllocation.HostConnectionData, joinAllocation.Key, false);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayData);
            NetworkManager.Singleton.StartClient();
            JoinLobby(lobby);
        } catch(RelayServiceException e)
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
        lobbyList.setStartGameButtonState(false);
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
            
            if(isPlayerHost())
            {
                NetworkManager.Singleton.Shutdown();
                foreach (Player p in joinedLobby.Players)
                {
                    await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, p.Id);
                }
            }
            joinedLobby = null;
            NetworkManager.Singleton.DisconnectClient(NetworkManager.Singleton.LocalClientId);
            lobbyList.setStartGameButtonState(false);
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

    public async void StartLobbyGame()
    {
        if (joinedLobby.HostId != AuthenticationService.Instance.PlayerId)
        {
            Debug.LogWarning("Only the host can start the game!");
            return;
        }

        if(NetworkManager.Singleton.ConnectedClientsIds.Count != joinedLobby.Players.Count)
        {
            Debug.LogWarning("Amount of clients dont match amount of lobby members!" + " " + NetworkManager.Singleton.ConnectedClientsIds.Count + " client(s), but " + joinedLobby.Players.Count + " lobby member(s)!");
            return;
        }
        try
        {
            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                IsLocked = true
            };
            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, options);
            joinedLobby = null;
            hostLobby = null;
            NetworkManager.Singleton.SceneManager.LoadScene("Tommy test scene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to lock lobby: {e.Message}");
        }
    }
}
