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
using Unity.VisualScripting;
using NUnit.Framework;
using UnityEditor.PackageManager;

/// <summary>
/// Manages the processes required for creating, joining, leaving, and starting
/// a lobby. Utilizes Unity's Lobby system in tandem with the Relay service
/// and netcode for game objects (NGO).
/// </summary>
public class LobbySystem : NetworkBehaviour
{
    //Holds the reference to the main game scene.
    [SerializeField] SceneReference gameScene;

    //Unity relay join code
    private const string KEY_RELAY_JOIN_CODE = "RelayJoinCode";

    //The lobby this client is considered the host of
    private Lobby hostLobby;

    //The lobby this client is currently in
    private Lobby joinedLobby;

    //Timer used by a lobby host to send a heartbeat to
    //The lobby so it does not automatically close.
    private float heartBeatTimer;

    //Timer used to update the lobby internally, so that
    //clients are able to see changes such as color selection
    //and lobby member changes.
    private float lobbyUpdateTimer;

    //Timer used when refreshing the lobbies in the lobby list.
    private float refreshLobbyListTimer;

    //Maximum amount of players allowed in a lobby at one time.
    public static int maxPlayers = 4;

    //Object responsible for controlling the UI layer of the lobby system.
    private LobbyListSystem lobbyList;

    //This clients' name.
    private string playerName;

    //Custom warning system used to display exceptions to the
    //end user in a panel.
    private LobbyWarningSystem warningSystem;

    //Game settings object used to send information in the lobby
    //to the in game network
    [SerializeField]
    private GameSettings gameSettings;

    //This clients' id in the network.
    private ulong clientId = 0;

    //This clients' chosen color.
    private Color chosenColor = Color.grey;

    /// <summary>
    /// - Sets the player name to "player" plus a random number.
    /// - Initializes components
    /// - Subscribes to standard network events.
    /// </summary>
    public async void Start()
    {
        playerName = "Player" + UnityEngine.Random.Range(10, 99);
        lobbyList = GetComponent<LobbyListSystem>();
        await UnityServices.InitializeAsync();
        warningSystem = GetComponent<LobbyWarningSystem>();

        AuthenticationService.Instance.SignedIn += () =>
        {
            Debug.Log("Signed in " + AuthenticationService.Instance.PlayerId);
        };

        await AuthenticationService.Instance.SignInAnonymouslyAsync();

        NetworkManager.Singleton.OnClientConnectedCallback += (id) =>
        {
            Debug.Log($"[NETCODE] Successfully Connected! ID: {id}");

            if(id == NetworkManager.Singleton.LocalClientId)
            {
                clientId = id;
                UpdatePlayerNetworkId(clientId);
                Debug.Log("Local ID: " + clientId);
            }
        };

        NetworkManager.Singleton.OnClientDisconnectCallback += (id) =>
        {
            Debug.Log(id);
            if(!NetworkManager.Singleton.IsHost && id == NetworkManager.Singleton.LocalClientId)
            {
                string dcReason = NetworkManager.Singleton.DisconnectReason;

                if(dcReason == "Disconnected due to host shutting down.")
                {
                    LeaveLobby();
                    warningSystem.warn("Host left! Leaving lobby...");
                }

                //Server disconnect (restart network)
                //restartNetwork();
            }
            Debug.Log($"[NETCODE] Disconnected. Reason: {NetworkManager.Singleton.DisconnectReason}");
        };

    }

    /// <summary>
    /// Restarts the network by allocating a new host.
    /// (Unused)
    /// </summary>
    private void restartNetwork()
    {
        // Find new host (use Lobby service auto migration)
        // Restart network with new host as host
        // Connect all clients to same network
        /*if(joinedLobby != null)
        {
            await LobbyService.Instance.
        }
        warningSystem.warn("Host left the game!");*/
    }

    /// <summary>
    /// Handles automatic synchronizations
    /// </summary>
    private void Update() {
        //HandleLobbyRefreshList();
        HandleLobbyHeartBeat();
        HandleLobbyPollForUpdates();
    }

    /// <summary>
    /// Sends a heart beat ping to the lobby if the
    /// client is the host of a lobby.s
    /// </summary>
    private async void HandleLobbyHeartBeat() {
        if(hostLobby != null && joinedLobby != null) {
            heartBeatTimer -= Time.deltaTime;
            if(heartBeatTimer <= 0f) {
                float heartBeatTimerMax = 15f;
                heartBeatTimer = heartBeatTimerMax;
                await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
            }
        }
    }

    /// <summary>
    /// Handles polling for changes in the lobby, such as
    /// clients and data such as color.
    /// </summary>
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
                if (isPlayerLobbyHost())
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

    /// <summary>
    /// Refreshes the lobby list automatically
    /// (unused due to bandwidth usage)
    /// </summary>
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

    /// <summary>
    /// Gets an allocation from the relay service
    /// </summary>
    /// <returns>Task<Allocation> the allocation gotten from the relay service</returns>
    private async Task<Allocation> AllocateRelay()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            return allocation;
        } catch (RelayServiceException e)
        {
            warningSystem.warn(e.Message);
            return default;
        }
        
    }

    /// <summary>
    /// Gets a relay join code from a given allocation
    /// </summary>
    /// <param name="allocation"> The allocation to get the join code from</param>
    /// <returns>The join code gotten as a Task<String> object.</returns>
    private async Task<String> GetRelayJoinCode(Allocation allocation)
    {
        try
        {
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            return joinCode;
        } catch (RelayServiceException e)
        {
            warningSystem.warn(e.Message);
            return default;
        }
        
    }

    /// <summary>
    /// Joins a specific relay using a join code
    /// </summary>
    /// <param name="joinCode">The join code to join the relay using</param>
    /// <returns>the join allocation gotten from the relay service</returns>
    private async Task<JoinAllocation> JoinRelay(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            return joinAllocation;
        } catch (RelayServiceException e)
        {
            warningSystem.warn(e.Message);
            return default;
        }
        
    }

    /// <summary>
    /// Creates a lobby using the options provided
    /// in the create lobby UI panel.
    /// </summary>
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
                hostLobby.Players[0].Data["PlayerName"].Value
            };
            List<string> playerColors = new List<string>
            {
                hostLobby.Players[0].Data["PlayerColor"].Value
            };
            List<string> playerNetworkIds = new List<string>
            {
                hostLobby.Players[0].Data["PlayerNetworkId"].Value
            };
            lobbyList.openLobbyScreen(joinedLobby.Name, joinedLobby.LobbyCode);
            lobbyList.showPlayerElements(playerNames.ToArray(), playerColors.ToArray());
            listLobbies();
            lobbyList.setStartGameButtonState(true);
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Lists all public lobbies in the lobby list.
    /// </summary>
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

        lobbyList.clearPrevList();

        foreach (Lobby lobby in queryResponse.Results)
        {
            lobbyList.listNewLobby(lobby);
        }
        } catch (LobbyServiceException e) {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Join a lobby by a given id.
    /// </summary>
    /// <param name="id">the id of the lobby.</param>
    public async void JoinLobbyById(string id) {
        var joinOptions = new JoinLobbyByIdOptions
        {
            Player = GetPlayer()
        };
        try {
            LobbyJoinRelayLayer(await LobbyService.Instance.JoinLobbyByIdAsync(id, joinOptions));
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Joins a lobby by a given code.
    /// Used when joining private lobbies, as passwords are non-unique by nature.
    /// </summary>
    /// <param name="lobbyCode">The code to join by.</param>
    public async void JoinLobbyByCode(string lobbyCode)
    {
        var joinOptions = new JoinLobbyByCodeOptions
        {
            Player = GetPlayer()
        };
        try
        {
            LobbyJoinRelayLayer(await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, joinOptions));
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Join a lobby using a given password.
    /// </summary>
    /// <param name="lobbyPassword">The password used.</param>
    public async void JoinLobbyByPassword(string lobbyPassword)
    {
        var idOptions = new JoinLobbyByIdOptions{
            Password = lobbyPassword,
            Player = GetPlayer()
        };

        try
        {
            LobbyJoinRelayLayer(await LobbyService.Instance.JoinLobbyByIdAsync("lobbyId", idOptions));
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// The relay layer required for seperating networks in several networks,
    /// called when joining a lobby.
    /// </summary>
    /// <param name="lobby">The lobby to join.</param>
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
            warningSystem.warn(e.Message);
        }

    }

    /// <summary>
    /// Joins a given lobby.
    /// </summary>
    /// <param name="lobby">The lobby to join</param>
    private void JoinLobby(Lobby lobby)
    {
        joinedLobby = lobby;
        AuthenticationService.Instance.UpdatePlayerNameAsync("Player_" + (lobby.Players.Count + 1));
        lobbyList.openLobbyScreen(joinedLobby.Name, joinedLobby.LobbyCode);
        lobbyList.setStartGameButtonState(false);
        UpdatePlayerListInLobby();
    }

    /// <summary>
    /// Updates the player list for the client in a lobby.
    /// </summary>
    private void UpdatePlayerListInLobby()
    {
        if(joinedLobby != null)
        {
            List<string> playerNames = new List<string>();
            List<string> playerColors = new List<string>();
            
            foreach (Player p in joinedLobby.Players)
            {
                playerNames.Add(p.Data["PlayerName"].Value);
                playerColors.Add(p.Data["PlayerColor"].Value);
            }
            lobbyList.showPlayerElements(playerNames.ToArray(), playerColors.ToArray());
        }
    }

    /// <summary>
    /// Creates a player object with given data values.
    /// </summary>
    /// <returns>A Player object with a corresponding Data dictionary.</returns>
    private Player GetPlayer()
    {
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {"PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)},
                {"PlayerColor", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, UnityEngine.ColorUtility.ToHtmlStringRGB(chosenColor))},
                {"PlayerNetworkId", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, clientId.ToString())}
            }
        };
    }

    /// <summary>
    /// Updates the client player object with new data
    /// </summary>
    /// <param name="data">The data to update the player with.</param>
    private void UpdatePlayer(Dictionary<string, PlayerDataObject> data)
    {
        UpdatePlayerData(joinedLobby.Id, AuthenticationService.Instance.PlayerId, data);
    }

    /// <summary>
    /// Updates the clients' color data field
    /// </summary>
    /// <param name="color">The new color to set for the player.</param>
    public void UpdatePlayerColor(Color color)
    {
        chosenColor = color;
        Dictionary<string, PlayerDataObject> playerInfo = new Dictionary<string, PlayerDataObject>
            {
                {"PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)},
                {"PlayerColor", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, UnityEngine.ColorUtility.ToHtmlStringRGB(chosenColor))},
                {"PlayerNetworkId", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, clientId.ToString())}
            };
        UpdatePlayer(playerInfo);
    }


    /// <summary>
    /// Updates the clients' network id field.
    /// </summary>
    /// <param name="networkId">The new client id to assign to the player.</param>
    public void UpdatePlayerNetworkId(ulong networkId)
    {
        clientId = networkId;

        Dictionary<string, PlayerDataObject> playerInfo = new Dictionary<string, PlayerDataObject>
            {
                {"PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)},
                {"PlayerColor", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, UnityEngine.ColorUtility.ToHtmlStringRGB(chosenColor))},
                {"PlayerNetworkId", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, clientId.ToString())}
            };
        UpdatePlayer(playerInfo);
    }

    /// <summary>
    /// Updates a givens players' data in a given lobby and given data.
    /// </summary>
    /// <param name="lobbyId">The lobby the player should be updated in</param>
    /// <param name="playerId">The id of the player to update</param>
    /// <param name="updateData">The data to update by</param>
    public async void UpdatePlayerData(string lobbyId, string playerId, Dictionary<string, PlayerDataObject> updateData)
    {
        try
        {
            UpdatePlayerOptions options = new UpdatePlayerOptions
            {
                Data = updateData
            };
            // Update lobby with new player data.
            var lobby = await LobbyService.Instance.UpdatePlayerAsync(lobbyId, playerId, options);
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Leaves the lobby, making joinedlobby null
    /// </summary>
    public async void LeaveLobby() {
        try {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            joinedLobby = null;
            hostLobby = null;
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                NetworkManager.Singleton.Shutdown();
                /*
                if (isPlayerLobbyHost())
                {
                    
                    foreach (Player p in joinedLobby.Players)
                    {
                        if(p.Id != AuthenticationService.Instance.PlayerId)
                        {
                            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, p.Id);
                        }
                    }
                }
                else
                {
                    NetworkManager.Singleton.DisconnectClient(NetworkManager.Singleton.LocalClientId);
                }
                */
            }
            
            lobbyList.hideLobbyScreen();
            lobbyList.setStartGameButtonState(false);
            listLobbies();
        } catch (LobbyServiceException e) {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Kicks a selected player from the lobby
    /// </summary>
    /// <param name="playerId">The id of the player to kick</param>
    public async void KickPlayer(string playerId)
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, playerId);
            joinedLobby = null;
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Migrates the lobby host to a new host.
    /// </summary>
    /// <param name="newHostId">The id of the player who will become the new host.</param>
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
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Helper method to check if the client is the lobby host.
    /// </summary>
    /// <returns>true if host, false otherwise</returns>
    private bool isPlayerLobbyHost()
    {
        //Checks if the player is the host of the lobby.
        return joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }

    /// <summary>
    /// Method for starting the game with the entire lobby.
    /// Initializes the network and calls LoadScene from the network manager object.
    /// </summary>
    public async void StartLobbyGame()
    {
        if (joinedLobby.HostId != AuthenticationService.Instance.PlayerId)
        {
            warningSystem.warn("Only the host can start the game!");
            return;
        }

        if(NetworkManager.Singleton.ConnectedClientsIds.Count != joinedLobby.Players.Count)
        {
            warningSystem.warn("Amount of clients dont match amount of lobby members!" + " " + NetworkManager.Singleton.ConnectedClientsIds.Count + " client(s), but " + joinedLobby.Players.Count + " lobby member(s)!");
            return;
        }
        try
        {
            UpdateLobbyOptions options = new UpdateLobbyOptions
            {
                IsLocked = true
            };
            await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, options);
            foreach (Player p in joinedLobby.Players)
            {
                if (ulong.TryParse(p.Data["PlayerNetworkId"].Value, out ulong id))
                {
                    string hex = p.Data["PlayerColor"].Value;

                    if (UnityEngine.ColorUtility.TryParseHtmlString("#" + hex, out Color col))
                    {
                        Debug.Log(id + ": " + col);
                        gameSettings.setPlayerColor(id, col);
                    }
                }
            }
            joinedLobby = null;
            hostLobby = null;

            NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
        }
        catch (LobbyServiceException e)
        {
            warningSystem.warn(e.Message);
        }
    }

    /// <summary>
    /// Returns the joined lobby
    /// </summary>
    /// <returns>The joined lobby</returns>
    public Lobby getJoinedLobby()
    {
        return joinedLobby;
    }
}
