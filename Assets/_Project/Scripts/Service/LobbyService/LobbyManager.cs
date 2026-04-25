using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Taiyun.SuckTheWater.Service.LobbyService
{
    /// <summary>
    /// Central manager for lobby operations.
    /// Wraps the platform-specific ILobbyProvider and provides a clean API
    /// for the rest of the game to interact with lobbies.
    /// 
    /// Responsibilities:
    /// - Manages the current lobby state via LobbyDataHolder
    /// - Coordinates between provider and UI
    /// - Handles queued actions for thread safety
    /// - Tracks "all ready" state for game start
    /// </summary>
    public class LobbyManager
    {
        #region Events
        
        /// <summary>
        /// Fired when a lobby is successfully joined (as host or client).
        /// </summary>
        public event Action<Lobby> OnRoomJoined;
        
        /// <summary>
        /// Fired when joining a lobby fails.
        /// </summary>
        public event Action<string> OnRoomJoinFailed;
        
        /// <summary>
        /// Fired when leaving a lobby.
        /// </summary>
        public event Action OnRoomLeft;
        
        /// <summary>
        /// Fired when lobby state changes (members, properties, etc.).
        /// </summary>
        public event Action<Lobby> OnRoomUpdated;
        
        /// <summary>
        /// Fired when the player list changes.
        /// </summary>
        public event Action<List<LobbyUser>> OnPlayerListUpdated;
        
        /// <summary>
        /// Fired when lobby search returns results.
        /// </summary>
        public event Action<List<Lobby>> OnRoomSearchResults;
        
        /// <summary>
        /// Fired when all players are ready.
        /// </summary>
        public event Action OnAllReady;
        
        /// <summary>
        /// Fired on errors.
        /// </summary>
        public event Action<string> OnError;
        
        /// <summary>
        /// Fired when an invite is accepted via platform overlay.
        /// </summary>
        public event Action<string> OnJoinLobbyRequested;
        
        /// <summary>
        /// Fired when the provider is initialized.
        /// </summary>
        public event Action OnInitialized;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The underlying platform provider.
        /// </summary>
        public ILobbyProvider Provider { get; private set; }
        
        /// <summary>
        /// The current lobby we're in.
        /// </summary>
        public Lobby CurrentLobby => _dataHolder?.CurrentLobby ?? default;
        
        /// <summary>
        /// Whether we're the host of the current lobby.
        /// </summary>
        public bool IsHost => CurrentLobby.IsOwner;
        
        /// <summary>
        /// Whether we're currently in a valid lobby.
        /// </summary>
        public bool IsInLobby => CurrentLobby.IsValid;
        
        #endregion
        
        #region Internal State
        
        private LobbyDataHolder _dataHolder;
        private readonly Queue<Action> _delayedActions = new Queue<Action>();
        private int _taskLock;
        private bool _isStarting;
        private Lobby _lastKnownState;
        
        // Default room settings
        public int DefaultMaxPlayers = 4;
        public Dictionary<string, string> DefaultRoomProperties = new Dictionary<string, string>();
        
        #endregion
        
        #region Initialization
        
        public LobbyManager()
        {
            _lastKnownState = new Lobby { IsValid = false };
        }
        
        /// <summary>
        /// Sets up the lobby manager with a provider.
        /// Should be called during service initialization.
        /// </summary>
        public async Task Initialize(ILobbyProvider provider)
        {
            if (provider == null)
            {
                Debug.LogError("[LobbyManager] Provider is null!");
                return;
            }
            
            Provider = provider;
            SetupDataHolder();
            SubscribeToProviderEvents();
            
            await Provider.InitializeAsync();
            
            Debug.Log("[LobbyManager] Initialized");
            OnInitialized?.Invoke();
        }
        
        private void SetupDataHolder()
        {
            _dataHolder = LobbyDataHolder.Instance;
            
            // If we have a stale lobby from a previous session, leave it
            if (_dataHolder.CurrentLobby.IsValid)
            {
                Provider?.LeaveLobbyAsync(_dataHolder.CurrentLobby.LobbyId);
                _dataHolder.Clear();
            }
        }
        
        private void SubscribeToProviderEvents()
        {
            if (Provider == null) return;
            
            Provider.OnLobbyJoinFailed += msg => InvokeDelayed(() => OnRoomJoinFailed?.Invoke(msg));
            
            Provider.OnLobbyLeft += () => InvokeDelayed(() =>
            {
                _dataHolder.Clear();
                _isStarting = false;
                OnRoomLeft?.Invoke();
            });
            
            Provider.OnLobbyUpdated += lobby => InvokeDelayed(() =>
            {
                if (!_lastKnownState.HasChanged(lobby) || lobby.Members.Count <= 0 || !lobby.IsValid)
                    return;
                
                _lastKnownState = lobby;
                _dataHolder.SetCurrentLobby(lobby);
                OnRoomUpdated?.Invoke(lobby);
                
                // Check if all players are ready
                if (!_isStarting && lobby.Members.TrueForAll(x => x.IsReady))
                {
                    _isStarting = true;
                    CallOnAllReady();
                }
            });
            
            Provider.OnLobbyPlayerListUpdated += players => InvokeDelayed(() => OnPlayerListUpdated?.Invoke(players));
            Provider.OnError += error => InvokeDelayed(() => OnError?.Invoke(error));
            Provider.OnJoinLobbyRequested += lobbyId => InvokeDelayed(() => OnJoinLobbyRequested?.Invoke(lobbyId));
        }
        
        /// <summary>
        /// Call this every frame to process delayed actions.
        /// </summary>
        public void Update()
        {
            while (_delayedActions.Count > 0)
            {
                _delayedActions.Dequeue()?.Invoke();
            }
        }
        
        public void Shutdown()
        {
            Provider?.Shutdown();
            _dataHolder?.Clear();
            Debug.Log("[LobbyManager] Shutdown");
        }
        
        #endregion
        
        #region Lobby Operations
        
        /// <summary>
        /// Creates a new lobby with default settings.
        /// </summary>
        public void CreateRoom()
        {
            CreateRoom(DefaultMaxPlayers, DefaultRoomProperties);
        }
        
        /// <summary>
        /// Creates a new lobby with custom settings.
        /// </summary>
        public void CreateRoom(int maxPlayers, Dictionary<string, string> roomProperties = null)
        {
            RunTask(async () =>
            {
                EnsureProviderSet();
                var room = await Provider.CreateLobbyAsync(maxPlayers, roomProperties);
                if (room.IsValid)
                {
                    _dataHolder.SetCurrentLobby(room);
                    OnRoomUpdated?.Invoke(room);
                }
            });
        }
        
        /// <summary>
        /// Creates a lobby and returns it asynchronously.
        /// </summary>
        public async Task<Lobby> CreateRoomAsync(int maxPlayers = 4, Dictionary<string, string> roomProperties = null)
        {
            EnsureProviderSet();
            var room = await Provider.CreateLobbyAsync(maxPlayers, roomProperties);
            if (room.IsValid)
            {
                _dataHolder.SetCurrentLobby(room);
            }
            return room;
        }
        
        /// <summary>
        /// Joins an existing lobby by ID.
        /// </summary>
        public void JoinLobby(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                OnRoomJoinFailed?.Invoke("Null or empty room ID.");
                return;
            }
            
            RunTask(async () =>
            {
                EnsureProviderSet();
                var room = await Provider.JoinLobbyAsync(roomId);
                if (room.IsValid)
                {
                    _dataHolder.SetCurrentLobby(room);
                    OnRoomJoined?.Invoke(room);
                }
                else
                {
                    OnRoomJoinFailed?.Invoke($"Failed to join room {roomId}");
                }
            });
        }
        
        /// <summary>
        /// Joins a lobby and returns it asynchronously.
        /// </summary>
        public async Task<Lobby> JoinLobbyAsync(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return LobbyFactory.CreateInvalid();
            }
            
            EnsureProviderSet();
            var room = await Provider.JoinLobbyAsync(roomId);
            if (room.IsValid)
            {
                _dataHolder.SetCurrentLobby(room);
                OnRoomJoined?.Invoke(room);
            }
            return room;
        }
        
        /// <summary>
        /// Leaves the current lobby.
        /// </summary>
        public void LeaveLobby()
        {
            RunTask(async () =>
            {
                EnsureProviderSet();
                await Provider.LeaveLobbyAsync();
                _dataHolder.Clear();
                OnRoomLeft?.Invoke();
            });
        }
        
        /// <summary>
        /// Searches for available lobbies.
        /// </summary>
        public void SearchLobbies(int maxRoomsToFind = 10, Dictionary<string, string> filters = null)
        {
            RunTask(async () =>
            {
                EnsureProviderSet();
                var rooms = await Provider.SearchLobbiesAsync(maxRoomsToFind, filters);
                OnRoomSearchResults?.Invoke(rooms);
            });
        }
        
        #endregion
        
        #region Player State
        
        /// <summary>
        /// Toggles the local player's ready state.
        /// </summary>
        public void ToggleLocalReady()
        {
            if (!CurrentLobby.IsValid)
            {
                Debug.LogError("[LobbyManager] Can't toggle ready state, not in a lobby.");
                return;
            }
            
            RunTask(async () =>
            {
                var localUserId = await Provider.GetLocalUserIdAsync();
                if (string.IsNullOrEmpty(localUserId))
                {
                    Debug.LogError("[LobbyManager] Can't toggle ready state, local user ID is null.");
                    return;
                }
                
                var localUser = CurrentLobby.Members.Find(x => x.Id == localUserId);
                await Provider.SetIsReadyAsync(localUserId, !localUser.IsReady);
            });
        }
        
        /// <summary>
        /// Sets a player's ready state.
        /// </summary>
        public void SetIsReady(string userId, bool isReady)
        {
            RunTask(async () =>
            {
                EnsureProviderSet();
                await Provider.SetIsReadyAsync(userId, isReady);
            });
        }
        
        #endregion
        
        #region Lobby Data
        
        /// <summary>
        /// Sets lobby metadata.
        /// </summary>
        public void SetLobbyData(string key, string value)
        {
            RunTask(async () =>
            {
                EnsureProviderSet();
                await Provider.SetLobbyDataAsync(key, value);
            });
        }
        
        /// <summary>
        /// Gets lobby metadata.
        /// </summary>
        public async Task<string> GetLobbyData(string key)
        {
            EnsureProviderSet();
            return await Provider.GetLobbyDataAsync(key);
        }
        
        /// <summary>
        /// Marks the lobby as started (game in progress).
        /// Call this when transitioning to the game scene.
        /// </summary>
        public void SetLobbyStarted()
        {
            Provider?.SetLobbyStartedAsync();
        }
        
        /// <summary>
        /// Signals that the server is ready to accept connections.
        /// Call this after PurrNet server has started.
        /// </summary>
        public void SetServerReady()
        {
            Provider?.SetServerReadyAsync();
        }
        
        /// <summary>
        /// Waits for the server ready flag to be set.
        /// Used by clients before connecting.
        /// </summary>
        public async Task<bool> WaitForServerReady(float timeoutSeconds = 30f)
        {
            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                var serverReady = await Provider.GetLobbyDataAsync(LobbyConstants.SERVER_READY_KEY);
                if (serverReady == "true")
                {
                    Debug.Log("[LobbyManager] Server is ready!");
                    return true;
                }
                
                await Task.Delay(100);
                elapsed += 0.1f;
            }
            
            Debug.LogError("[LobbyManager] Timeout waiting for server ready");
            return false;
        }
        
        #endregion
        
        #region Helpers
        
        private void InvokeDelayed(Action action)
        {
            try
            {
                _delayedActions.Enqueue(action);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Error in InvokeDelayed: {ex.Message}");
            }
        }
        
        private async void RunTask(Func<Task> taskFunc)
        {
            if (taskFunc == null || Provider == null) return;
            
            _taskLock++;
            try
            {
                await taskFunc();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LobbyManager] Task Error: {ex.Message}");
            }
            finally
            {
                _taskLock--;
                if (_taskLock < 0) _taskLock = 0;
            }
        }
        
        private void EnsureProviderSet()
        {
            if (Provider == null)
                throw new InvalidOperationException("No lobby provider has been set.");
        }
        
        private async void CallOnAllReady()
        {
            await WaitForAllTasksAsync();
            if (CurrentLobby.IsValid && CurrentLobby.Members.TrueForAll(x => x.IsReady))
            {
                await Provider.SetAllReadyAsync();
                OnAllReady?.Invoke();
            }
        }
        
        public async Task WaitForAllTasksAsync()
        {
            while (_taskLock > 0)
            {
                await Task.Yield();
            }
        }
        
        #endregion
    }
}
