#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Taiyun.SuckTheWater.Main;
using UnityEngine;

namespace Taiyun.SuckTheWater.Service.LobbyService
{
    /// <summary>
    /// Steam implementation of ILobbyProvider.
    /// Handles all Steam Matchmaking API interactions.
    /// </summary>
    public class SteamLobbyProvider : ILobbyProvider
    {
        #region Configuration

        public enum LobbyType
        {
            Private,
            FriendsOnly,
            Public,
        }

        public LobbyType lobbyType = LobbyType.Public;
        public int maxLobbiesToFind = 10;

        #endregion

        #region Events

        public event Action<string> OnLobbyJoinFailed;
        public event Action OnLobbyLeft;
        public event Action<Lobby> OnLobbyUpdated;
        public event Action<List<LobbyUser>> OnLobbyPlayerListUpdated;
        public event Action<List<FriendUser>> OnFriendListPulled;
        public event Action<string> OnError;
        public event Action<string> OnJoinLobbyRequested;

        #endregion

        #region Internal State

        private CSteamID _currentLobby = CSteamID.Nil;
        private bool _isInitialized;
        private bool _isSteamInitialized;
        private bool _isRelayReady;

        // Steam CallResults (for async operations)
        private CallResult<LobbyCreated_t> _lobbyCreatedResult;
        private CallResult<LobbyEnter_t> _lobbyEnterResult;
        private CallResult<LobbyMatchList_t> _lobbyMatchListResult;

        // Steam Callbacks (for events)
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCallback;
        private Callback<AvatarImageLoaded_t> _avatarImageLoadedCallback;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequestedCallback;
        private Callback<GameRichPresenceJoinRequested_t> _gameRichPresenceJoinRequestedCallback;

        #endregion

        #region Properties

        /// <summary>
        /// Local user's Steam ID.
        /// </summary>
        public ulong LocalUserId { get; private set; }

        /// <summary>
        /// Whether the Steam relay network is ready for P2P connections.
        /// </summary>
        public bool IsRelayReady => _isRelayReady;

        public bool IsSteamClientAvailable
        {
            get
            {
                if (!_isSteamInitialized) return false;

                try
                {
                    InteropHelp.TestIfAvailableClient();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public CSteamID CurrentLobbyId => _currentLobby;

        #endregion

        #region Lifecycle

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            // Initialize Steam API
            bool steamOk = await InitializeSteam();
            if (!steamOk)
            {
                Debug.LogError("[SteamLobbyProvider] Failed to initialize Steam!");
                OnError?.Invoke("Steam initialization failed. Make sure Steam is running.");
                return;
            }

            // Get local user ID
            LocalUserId = SteamUser.GetSteamID().m_SteamID;

            // Store in LobbyDataHolder for easy access
            LobbyDataHolder.Instance.SetLocalUserId(LocalUserId.ToString());

            // Register callbacks
            _avatarImageLoadedCallback = Callback<AvatarImageLoaded_t>.Create(OnAvatarImageLoaded);
            _lobbyDataUpdateCallback = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            _gameRichPresenceJoinRequestedCallback = Callback<GameRichPresenceJoinRequested_t>.Create(OnGameRichPresenceJoinRequested);

            // Subscribe to SupremeManager's Update for running Steam callbacks
            if (SupremeManager.Instance != null)
            {
                SupremeManager.Instance.OnUpdate += RunSteamCallbacks;
            }
            else
            {
                Debug.LogError("[SteamLobbyProvider] SupremeManager.Instance is null! Steam callbacks won't run!");
            }

            _isInitialized = true;
            Debug.Log($"[SteamLobbyProvider] Initialized. UserID: {LocalUserId}, RelayReady: {_isRelayReady}");
        }

        private async Task<bool> InitializeSteam()
        {
            // Check if already initialized
            if (_isSteamInitialized)
            {
                Debug.Log("[Steam] Already initialized");
                return true;
            }

            // Initialize Steam API
            if (!SteamAPI.Init())
            {
                Debug.LogError("[Steam] SteamAPI.Init() failed! Is Steam running?");
                return false;
            }

            _isSteamInitialized = true;

            Debug.Log($"[Steam] Initialized. User: {SteamUser.GetSteamID()}");

            // Initialize relay network access for P2P
            SteamNetworkingUtils.InitRelayNetworkAccess();
            Debug.Log("[Steam] Relay network access initialization requested");

            // Wait for relay network to be ready
            _isRelayReady = await WaitForRelayNetworkAsync(10f);

            if (!_isRelayReady)
            {
                Debug.LogWarning("[Steam] Relay network not ready after 10s, P2P connections may fail!");
            }
            else
            {
                Debug.Log("[Steam] Relay network ready!");
            }

            // Log detailed relay status for debugging
            LogRelayStatus();

            return true;
        }

        private async Task<bool> WaitForRelayNetworkAsync(float timeoutSeconds)
        {
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                var status = SteamNetworkingUtils.GetRelayNetworkStatus(out var details);

                // Log progress every 2 seconds
                if (elapsed % 2f < 0.1f)
                {
                    Debug.Log($"[Steam] Waiting for relay... Status: {status}, Elapsed: {elapsed:F1}s");
                }

                if (status == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
                {
                    Debug.Log($"[Steam] Relay ready after {elapsed:F1}s");
                    return true;
                }

                if (status == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_CannotTry ||
                    status == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Failed)
                {
                    Debug.LogError($"[Steam] Relay network failed: {status}");
                    return false;
                }

                await Task.Delay(100);
                elapsed += 0.1f;

                // IMPORTANT: Run callbacks while waiting so Steam can process relay setup
                if (_isSteamInitialized)
                {
                    SteamAPI.RunCallbacks();
                }
            }

            // Log final status on timeout
            var finalStatus = SteamNetworkingUtils.GetRelayNetworkStatus(out _);
            Debug.LogWarning($"[Steam] Relay wait timeout. Final status: {finalStatus}");

            return false;
        }

        private void LogRelayStatus()
        {
            var status = SteamNetworkingUtils.GetRelayNetworkStatus(out var details);

            Debug.Log($"[Steam] === Relay Network Status ===\n" +
                      $"  Availability: {status}\n" +
                      $"  AvailNetworkConfig: {details.m_eAvailNetworkConfig}\n" +
                      $"  AvailAnyRelay: {details.m_eAvailAnyRelay}\n" +
                      $"  PingMeasurementInProgress: {details.m_bPingMeasurementInProgress}\n" +
                      $"  DebugMsg: {details.m_debugMsg}");
        }

        private void RunSteamCallbacks()
        {
            if (_isSteamInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        /// <summary>
        /// Ensures relay is ready before P2P operations. Call this before hosting or joining.
        /// </summary>
        public async Task<bool> EnsureRelayReadyAsync()
        {
            if (_isRelayReady)
            {
                // Double-check current status
                var status = SteamNetworkingUtils.GetRelayNetworkStatus(out _);
                if (status == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
                {
                    return true;
                }
            }

            Debug.Log("[Steam] Relay not ready, attempting to initialize...");
            SteamNetworkingUtils.InitRelayNetworkAccess();

            _isRelayReady = await WaitForRelayNetworkAsync(5f);

            if (_isRelayReady)
            {
                Debug.Log("[Steam] Relay is now ready!");
            }
            else
            {
                Debug.LogWarning("[Steam] Relay still not ready after retry");
                LogRelayStatus();
            }

            return _isRelayReady;
        }

        public void Shutdown()
        {
            if (_currentLobby != CSteamID.Nil)
            {
                ClearRichPresence();
                SteamMatchmaking.LeaveLobby(_currentLobby);
                _currentLobby = CSteamID.Nil;
            }

            if (SupremeManager.Instance != null)
            {
                SupremeManager.Instance.OnUpdate -= RunSteamCallbacks;
            }

            _isInitialized = false;
            _isSteamInitialized = false;
            _isRelayReady = false;

            Debug.Log("[SteamLobbyProvider] Shutdown");
        }

    #endregion

        #region Rich Presence

        /// <summary>
        /// Sets Rich Presence data for the current lobby.
        /// This enables "Join Game" button on friend profiles.
        /// </summary>
        public void SetRichPresence(string lobbyId)
        {
            if (!IsSteamClientAvailable) return;

            // Set the connect string - this is what gets passed to OnGameRichPresenceJoinRequested
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {lobbyId}");
            
            // Set status for display in friends list
            SteamFriends.SetRichPresence("status", "In Lobby");
            
            // Steam key for displaying in overlay
            SteamFriends.SetRichPresence("steam_display", "#Status_InLobby");
            
            Debug.Log($"[SteamLobbyProvider] Rich Presence set for lobby: {lobbyId}");
        }

        /// <summary>
        /// Updates Rich Presence to show in-game status.
        /// </summary>
        public void SetRichPresenceInGame(string lobbyId, int playerCount, int maxPlayers)
        {
            if (!IsSteamClientAvailable) return;

            SteamFriends.SetRichPresence("connect", $"+connect_lobby {lobbyId}");
            SteamFriends.SetRichPresence("status", $"Playing ({playerCount}/{maxPlayers})");
            SteamFriends.SetRichPresence("steam_player_group", lobbyId);
            SteamFriends.SetRichPresence("steam_player_group_size", playerCount.ToString());
            
            Debug.Log($"[SteamLobbyProvider] Rich Presence updated: In Game ({playerCount}/{maxPlayers})");
        }

        /// <summary>
        /// Clears all Rich Presence data.
        /// </summary>
        public void ClearRichPresence()
        {
            if (!IsSteamClientAvailable) return;

            SteamFriends.ClearRichPresence();
            Debug.Log("[SteamLobbyProvider] Rich Presence cleared");
        }

        #endregion

        #region Lobby Management

        public async Task<Lobby> CreateLobbyAsync(int maxPlayers, Dictionary<string, string> lobbyProperties = null)
        {
            if (!IsSteamClientAvailable)
                return LobbyFactory.CreateInvalid();

            _lobbyCreatedResult ??= CallResult<LobbyCreated_t>.Create();

            var tcs = new TaskCompletionSource<bool>();
            CSteamID lobbyId = CSteamID.Nil;
            var lobbyName = $"{SteamFriends.GetPersonaName()}'s Lobby";
            var localUserId = SteamUser.GetSteamID().m_SteamID.ToString();

            var handle = SteamMatchmaking.CreateLobby((ELobbyType)lobbyType, maxPlayers);
            _lobbyCreatedResult.Set(handle, (result, ioError) =>
            {
                if (!ioError && result.m_eResult == EResult.k_EResultOK)
                {
                    lobbyId = new CSteamID(result.m_ulSteamIDLobby);

                    // Set essential lobby data
                    SteamMatchmaking.SetLobbyData(lobbyId, LobbyConstants.NAME_KEY, lobbyName);
                    SteamMatchmaking.SetLobbyData(lobbyId, LobbyConstants.HOST_ID_KEY, localUserId);
                    SteamMatchmaking.SetLobbyData(lobbyId, LobbyConstants.STARTED_KEY, "false");
                    SteamMatchmaking.SetLobbyData(lobbyId, LobbyConstants.SERVER_READY_KEY, "false");

                    tcs.TrySetResult(true);
                }
                else
                {
                    Debug.LogError($"[SteamLobbyProvider] Create lobby failed: {result.m_eResult}");
                    tcs.TrySetResult(false);
                }
            });

            if (!await tcs.Task)
                return LobbyFactory.CreateInvalid();

            _currentLobby = lobbyId;

            // Set custom properties
            if (lobbyProperties != null)
            {
                foreach (var prop in lobbyProperties)
                {
                    SteamMatchmaking.SetLobbyData(lobbyId, prop.Key, prop.Value);
                }
            }

            // Set Rich Presence so friends can join via "Join Game" button
            SetRichPresence(lobbyId.m_SteamID.ToString());

            var lobby = LobbyFactory.Create(
                lobbyName,
                lobbyId.m_SteamID.ToString(),
                maxPlayers,
                true, // IsOwner
                GetLobbyUsers(lobbyId),
                GetLobbyProperties(lobbyId)
            );

            Debug.Log($"[SteamLobbyProvider] Lobby created: {lobbyId.m_SteamID}");
            OnLobbyUpdated?.Invoke(lobby);

            return lobby;
        }

        public async Task<Lobby> JoinLobbyAsync(string lobbyId)
        {
            if (!IsSteamClientAvailable || string.IsNullOrEmpty(lobbyId))
                return LobbyFactory.CreateInvalid();

            _lobbyEnterResult ??= CallResult<LobbyEnter_t>.Create();

            var tcs = new TaskCompletionSource<bool>();
            var cLobbyId = new CSteamID(ulong.Parse(lobbyId));
            var handle = SteamMatchmaking.JoinLobby(cLobbyId);

            _lobbyEnterResult.Set(handle, (result, ioError) =>
            {
                if (result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
                {
                    _currentLobby = new CSteamID(result.m_ulSteamIDLobby);
                    tcs.TrySetResult(true);
                }
                else
                {
                    Debug.LogError($"[SteamLobbyProvider] Join lobby failed: {result.m_EChatRoomEnterResponse}");
                    tcs.TrySetResult(false);
                }
            });

            if (!await tcs.Task)
            {
                OnLobbyJoinFailed?.Invoke($"Failed to join lobby {lobbyId}");
                return LobbyFactory.CreateInvalid();
            }

            var ownerId = SteamMatchmaking.GetLobbyOwner(_currentLobby).m_SteamID.ToString();
            var localId = SteamUser.GetSteamID().m_SteamID.ToString();
            var isOwner = localId == ownerId;

            // Set Rich Presence so we show as "in lobby" to friends
            SetRichPresence(lobbyId);

            var lobby = LobbyFactory.Create(
                SteamMatchmaking.GetLobbyData(_currentLobby, LobbyConstants.NAME_KEY),
                lobbyId,
                SteamMatchmaking.GetLobbyMemberLimit(_currentLobby),
                isOwner,
                GetLobbyUsers(_currentLobby),
                GetLobbyProperties(_currentLobby)
            );

            Debug.Log($"[SteamLobbyProvider] Joined lobby: {lobbyId}, IsOwner: {isOwner}");
            OnLobbyUpdated?.Invoke(lobby);

            return lobby;
        }

        public Task LeaveLobbyAsync()
        {
            if (!IsSteamClientAvailable || _currentLobby == CSteamID.Nil)
                return Task.CompletedTask;

            // Clear Rich Presence
            ClearRichPresence();

            SteamMatchmaking.LeaveLobby(_currentLobby);
            _currentLobby = CSteamID.Nil;

            Debug.Log("[SteamLobbyProvider] Left lobby");
            OnLobbyLeft?.Invoke();

            return Task.CompletedTask;
        }

        public Task LeaveLobbyAsync(string lobbyId)
        {
            if (IsSteamClientAvailable && !string.IsNullOrEmpty(lobbyId) && ulong.TryParse(lobbyId, out var id))
            {
                var cLobbyId = new CSteamID(id);
                SteamMatchmaking.LeaveLobby(cLobbyId);

                if (_currentLobby.m_SteamID == id)
                {
                    ClearRichPresence();
                    _currentLobby = CSteamID.Nil;
                }
            }

            return Task.CompletedTask;
        }

        public async Task<List<Lobby>> SearchLobbiesAsync(int maxRoomsToFind = 10, Dictionary<string, string> filters = null)
        {
            if (!IsSteamClientAvailable)
                return new List<Lobby>();

            var tcs = new TaskCompletionSource<List<Lobby>>();
            var results = new List<Lobby>();

            // Apply filters
            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    SteamMatchmaking.AddRequestLobbyListStringFilter(
                        filter.Key, filter.Value, ELobbyComparison.k_ELobbyComparisonEqual);
                }
            }

            // Only show lobbies that haven't started
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                LobbyConstants.STARTED_KEY, "false", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(maxRoomsToFind);

            _lobbyMatchListResult ??= CallResult<LobbyMatchList_t>.Create();
            _lobbyMatchListResult.Set(SteamMatchmaking.RequestLobbyList(), (result, ioError) =>
            {
                int totalLobbies = (int)result.m_nLobbiesMatching;

                for (int i = 0; i < totalLobbies; i++)
                {
                    var lId = SteamMatchmaking.GetLobbyByIndex(i);
                    var lobbyProps = GetLobbyProperties(lId);
                    int maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(lId);

                    results.Add(new Lobby
                    {
                        Name = SteamMatchmaking.GetLobbyData(lId, LobbyConstants.NAME_KEY),
                        IsValid = true,
                        LobbyId = lId.m_SteamID.ToString(),
                        MaxPlayers = maxPlayers,
                        Properties = lobbyProps,
                        Members = GetLobbyUsers(lId)
                    });
                }

                tcs.TrySetResult(results);
            });

            return await tcs.Task;
        }

        #endregion

        #region Player State

        public Task SetIsReadyAsync(string userId, bool isReady)
        {
            if (IsSteamClientAvailable && !string.IsNullOrEmpty(userId) && ulong.TryParse(userId, out var id)
                && SteamUser.GetSteamID().m_SteamID == id) // Can only set own ready state
            {
                SteamMatchmaking.SetLobbyMemberData(_currentLobby, "IsReady", isReady.ToString());
                // Trigger update for other clients
                SteamMatchmaking.SetLobbyData(_currentLobby, "UpdateTrigger", DateTime.UtcNow.Ticks.ToString());
            }

            return Task.CompletedTask;
        }

        public Task<List<LobbyUser>> GetLobbyMembersAsync()
        {
            if (!IsSteamClientAvailable || _currentLobby == CSteamID.Nil)
                return Task.FromResult(new List<LobbyUser>());

            return Task.FromResult(GetLobbyUsers(_currentLobby));
        }

        public Task<string> GetLocalUserIdAsync()
        {
            if (!IsSteamClientAvailable)
                return Task.FromResult(string.Empty);

            return Task.FromResult(SteamUser.GetSteamID().m_SteamID.ToString());
        }

        #endregion

        #region Lobby Data

        public Task SetLobbyDataAsync(string key, string value)
        {
            if (IsSteamClientAvailable && _currentLobby != CSteamID.Nil)
                SteamMatchmaking.SetLobbyData(_currentLobby, key, value);

            return Task.CompletedTask;
        }

        public Task<string> GetLobbyDataAsync(string key)
        {
            if (!IsSteamClientAvailable || _currentLobby == CSteamID.Nil)
                return Task.FromResult(string.Empty);

            return Task.FromResult(SteamMatchmaking.GetLobbyData(_currentLobby, key));
        }

        public Task SetAllReadyAsync()
        {
            // Implementation depends on game needs
            return Task.CompletedTask;
        }

        public Task SetLobbyStartedAsync()
        {
            if (IsSteamClientAvailable && _currentLobby != CSteamID.Nil)
            {
                SteamMatchmaking.SetLobbyGameServer(_currentLobby, 0, 0, SteamUser.GetSteamID());
                SteamMatchmaking.SetLobbyData(_currentLobby, LobbyConstants.STARTED_KEY, "true");
                
                // Update Rich Presence to show in-game
                int playerCount = SteamMatchmaking.GetNumLobbyMembers(_currentLobby);
                int maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(_currentLobby);
                SetRichPresenceInGame(_currentLobby.m_SteamID.ToString(), playerCount, maxPlayers);
            }

            return Task.CompletedTask;
        }

        public Task SetServerReadyAsync()
        {
            if (IsSteamClientAvailable && _currentLobby != CSteamID.Nil)
            {
                SteamMatchmaking.SetLobbyData(_currentLobby, LobbyConstants.SERVER_READY_KEY, "true");
                Debug.Log("[SteamLobbyProvider] Server ready flag set");
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Friends

        public Task<List<FriendUser>> GetFriendsAsync(FriendFilter filter)
        {
            if (!IsSteamClientAvailable)
                return Task.FromResult(new List<FriendUser>());

            var friends = new List<FriendUser>();
            int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);

            for (int i = 0; i < friendCount; i++)
            {
                var steamID = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                bool shouldAdd = filter switch
                {
                    FriendFilter.InThisGame => SteamFriends.GetFriendGamePlayed(steamID, out FriendGameInfo_t gameInfo) &&
                                               gameInfo.m_gameID.AppID() == SteamUtils.GetAppID(),
                    FriendFilter.Online => SteamFriends.GetFriendPersonaState(steamID) == EPersonaState.k_EPersonaStateOnline,
                    FriendFilter.All => true,
                    _ => false
                };

                if (shouldAdd)
                    friends.Add(CreateFriendUser(steamID));
            }

            return Task.FromResult(friends);
        }

        public Task InviteFriendAsync(FriendUser user)
        {
            if (IsSteamClientAvailable && !string.IsNullOrEmpty(user.Id) && ulong.TryParse(user.Id, out var id))
            {
                var steamID = new CSteamID(id);
                SteamMatchmaking.InviteUserToLobby(_currentLobby, steamID);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Steam Callbacks

        private void OnLobbyDataUpdate(LobbyDataUpdate_t callback)
        {
            if (_currentLobby.m_SteamID != callback.m_ulSteamIDLobby)
                return;

            var ownerId = SteamMatchmaking.GetLobbyOwner(_currentLobby).m_SteamID.ToString();
            var localId = SteamUser.GetSteamID().m_SteamID.ToString();
            var isOwner = localId == ownerId;

            var updatedLobby = LobbyFactory.Create(
                SteamMatchmaking.GetLobbyData(_currentLobby, LobbyConstants.NAME_KEY),
                _currentLobby.m_SteamID.ToString(),
                SteamMatchmaking.GetLobbyMemberLimit(_currentLobby),
                isOwner,
                GetLobbyUsers(_currentLobby),
                GetLobbyProperties(_currentLobby)
            );

            OnLobbyUpdated?.Invoke(updatedLobby);
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
        {
            if (_currentLobby.m_SteamID != callback.m_ulSteamIDLobby)
                return;

            var stateChange = (EChatMemberStateChange)callback.m_rgfChatMemberStateChange;

            // Log state changes
            if (stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeEntered))
            {
                Debug.Log($"[SteamLobbyProvider] User {callback.m_ulSteamIDUserChanged} joined");
            }
            else if (stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeLeft) ||
                     stateChange.HasFlag(EChatMemberStateChange.k_EChatMemberStateChangeDisconnected))
            {
                Debug.Log($"[SteamLobbyProvider] User {callback.m_ulSteamIDUserChanged} left");
            }

            // Rebuild and notify
            var ownerId = SteamMatchmaking.GetLobbyOwner(_currentLobby).m_SteamID.ToString();
            var localId = SteamUser.GetSteamID().m_SteamID.ToString();
            var isOwner = localId == ownerId;

            var updatedLobby = LobbyFactory.Create(
                SteamMatchmaking.GetLobbyData(_currentLobby, LobbyConstants.NAME_KEY),
                _currentLobby.m_SteamID.ToString(),
                SteamMatchmaking.GetLobbyMemberLimit(_currentLobby),
                isOwner,
                GetLobbyUsers(_currentLobby),
                GetLobbyProperties(_currentLobby)
            );

            OnLobbyUpdated?.Invoke(updatedLobby);
            OnLobbyPlayerListUpdated?.Invoke(updatedLobby.Members);
        }

        /// <summary>
        /// Called when user clicks "Join" on a Steam invite notification.
        /// This is the traditional invite system via SteamMatchmaking.InviteUserToLobby().
        /// </summary>
        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            var lobbyId = callback.m_steamIDLobby;
            Debug.Log($"[SteamLobbyProvider] Invite accepted (GameLobbyJoinRequested), joining lobby {lobbyId.m_SteamID}");
            OnJoinLobbyRequested?.Invoke(lobbyId.m_SteamID.ToString());
        }

        /// <summary>
        /// Called when user clicks "Join Game" on a friend's profile or via Rich Presence.
        /// This is the modern Rich Presence join system.
        /// The connect string format is: "+connect_lobby {lobbyId}"
        /// </summary>
        private void OnGameRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback)
        {
            var friendId = callback.m_steamIDFriend;
            var connectString = callback.m_rgchConnect;
            
            Debug.Log($"[SteamLobbyProvider] Rich Presence join requested from friend {friendId.m_SteamID}");
            Debug.Log($"[SteamLobbyProvider] Connect string: {connectString}");

            // Parse the connect string to extract lobby ID
            // Format: "+connect_lobby {lobbyId}"
            string lobbyId = ParseConnectString(connectString);
            
            if (!string.IsNullOrEmpty(lobbyId))
            {
                Debug.Log($"[SteamLobbyProvider] Parsed lobby ID: {lobbyId}");
                OnJoinLobbyRequested?.Invoke(lobbyId);
            }
            else
            {
                Debug.LogWarning($"[SteamLobbyProvider] Could not parse lobby ID from connect string: {connectString}");
                OnError?.Invoke("Invalid join request - could not parse lobby ID");
            }
        }

        /// <summary>
        /// Parses the Rich Presence connect string to extract the lobby ID.
        /// Expected format: "+connect_lobby {lobbyId}"
        /// </summary>
        private string ParseConnectString(string connectString)
        {
            if (string.IsNullOrEmpty(connectString))
                return null;

            // Handle different formats
            const string prefix = "+connect_lobby ";
            
            if (connectString.StartsWith(prefix))
            {
                return connectString.Substring(prefix.Length).Trim();
            }

            // Try to find lobby ID in other formats
            // Some games use: "connect_lobby:{lobbyId}" or similar
            if (connectString.Contains("connect_lobby"))
            {
                var parts = connectString.Split(new[] { ' ', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i].Contains("connect_lobby") && ulong.TryParse(parts[i + 1], out _))
                    {
                        return parts[i + 1];
                    }
                }
            }

            // If the string is just a number, assume it's the lobby ID
            if (ulong.TryParse(connectString.Trim(), out _))
            {
                return connectString.Trim();
            }

            return null;
        }

        private void OnAvatarImageLoaded(AvatarImageLoaded_t callback)
        {
            if (callback.m_iImage == -1)
                return;

            // Update avatar for the user if they're in our lobby
            if (_currentLobby == CSteamID.Nil)
                return;

            // Trigger a lobby update to refresh avatars
            var ownerId = SteamMatchmaking.GetLobbyOwner(_currentLobby).m_SteamID.ToString();
            var localId = SteamUser.GetSteamID().m_SteamID.ToString();
            var isOwner = localId == ownerId;

            var updatedLobby = LobbyFactory.Create(
                SteamMatchmaking.GetLobbyData(_currentLobby, LobbyConstants.NAME_KEY),
                _currentLobby.m_SteamID.ToString(),
                SteamMatchmaking.GetLobbyMemberLimit(_currentLobby),
                isOwner,
                GetLobbyUsers(_currentLobby),
                GetLobbyProperties(_currentLobby)
            );

            OnLobbyUpdated?.Invoke(updatedLobby);
        }

        #endregion

        #region Helper Methods

        private List<LobbyUser> GetLobbyUsers(CSteamID lobbyId)
        {
            var users = new List<LobbyUser>();
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);

            for (int i = 0; i < memberCount; i++)
            {
                var steamId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                users.Add(CreateLobbyUser(steamId, lobbyId));
            }

            return users;
        }

        private LobbyUser CreateLobbyUser(CSteamID steamId, CSteamID lobbyId)
        {
            var displayName = SteamFriends.GetFriendPersonaName(steamId);
            var isReadyString = SteamMatchmaking.GetLobbyMemberData(lobbyId, steamId, "IsReady");
            var isReady = !string.IsNullOrEmpty(isReadyString) && isReadyString == "True";

            var avatarHandle = SteamFriends.GetLargeFriendAvatar(steamId);
            Texture2D avatar = null;

            if (avatarHandle != -1 && SteamUtils.GetImageSize(avatarHandle, out uint width, out uint height))
            {
                byte[] imageBuffer = new byte[width * height * 4];
                if (SteamUtils.GetImageRGBA(avatarHandle, imageBuffer, imageBuffer.Length))
                {
                    avatar = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                    avatar.LoadRawTextureData(imageBuffer);
                    FlipTextureVertically(avatar);
                    avatar.Apply();
                }
            }

            return new LobbyUser
            {
                Id = steamId.m_SteamID.ToString(),
                DisplayName = displayName,
                IsReady = isReady,
                Avatar = avatar
            };
        }

        private FriendUser CreateFriendUser(CSteamID steamId)
        {
            var displayName = SteamFriends.GetFriendPersonaName(steamId);
            var avatarHandle = SteamFriends.GetLargeFriendAvatar(steamId);
            Texture2D avatar = null;

            if (avatarHandle != -1 && SteamUtils.GetImageSize(avatarHandle, out uint width, out uint height))
            {
                byte[] imageBuffer = new byte[width * height * 4];
                if (SteamUtils.GetImageRGBA(avatarHandle, imageBuffer, imageBuffer.Length))
                {
                    avatar = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                    avatar.LoadRawTextureData(imageBuffer);
                    FlipTextureVertically(avatar);
                    avatar.Apply();
                }
            }

            return new FriendUser
            {
                Id = steamId.m_SteamID.ToString(),
                DisplayName = displayName,
                Avatar = avatar
            };
        }

        private Dictionary<string, string> GetLobbyProperties(CSteamID lobbyId)
        {
            var properties = new Dictionary<string, string>();
            int propertyCount = SteamMatchmaking.GetLobbyDataCount(lobbyId);

            for (int i = 0; i < propertyCount; i++)
            {
                bool success = SteamMatchmaking.GetLobbyDataByIndex(
                    lobbyId, i, out string key, 256, out string value, 256);

                if (success)
                {
                    key = key.TrimEnd('\0');
                    value = value.TrimEnd('\0');
                    properties[key] = value;
                }
            }

            return properties;
        }

        private void FlipTextureVertically(Texture2D texture)
        {
            var pixels = texture.GetPixels();
            int width = texture.width;
            int height = texture.height;

            for (int y = 0; y < height / 2; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var topPixel = pixels[y * width + x];
                    var bottomPixel = pixels[(height - 1 - y) * width + x];

                    pixels[y * width + x] = bottomPixel;
                    pixels[(height - 1 - y) * width + x] = topPixel;
                }
            }

            texture.SetPixels(pixels);
        }

        #endregion
    }
}

#endif