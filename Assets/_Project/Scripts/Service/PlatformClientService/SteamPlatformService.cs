#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
using System;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;
using PurrNet;
using PurrNet.Steam;
using Taiyun.SuckTheWater.Main;
using Object = UnityEngine.Object;

namespace Taiyun.SuckTheWater.Service.PlatformClientService
{
    public class SteamPlatformService : IPlatformService
    {
        // --- Interface Properties ---
        public ulong UserId { get; private set; }
        public ulong LobbyHostId { get; private set; }

        // --- Events ---
        public event Action<string> OnCreatedLobby;
        public event Action<string> OnEnteredLobby;
        public event Action<string> OnJoinLobbyRequested;
        public event Action<ulong> OnMemberJoinedLobby;
        public event Action<ulong> OnMemberDisconnectedLobby;
        public event Action<ulong> OnMemberLeaveLobby;

        // --- Internal State ---
        private bool _isInitialized;
        private CSteamID _currentLobbyId;
        private const string HOST_ADDRESS_KEY = "HostAddress";

        // --- Steam Callbacks ---
        // We keep references to prevent Garbage Collection
        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequested;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdate;

        // --- Async Task Helpers ---
        private TaskCompletionSource<bool> _createLobbyTcs;
        private TaskCompletionSource<(bool, string)> _joinLobbyTcs;

        public async Task<bool> InitService(params object[] args)
        {
            if (_isInitialized) return true;

            try
            {
                // Ensure SteamAPI is initialized. 
                // Ideally, you have a SteamManager script in the scene ensuring SteamAPI.RunCallbacks() is called in Update.
                if (!SteamAPI.Init())
                {
                    Debug.LogError($"[{nameof(SteamPlatformService)}] SteamAPI.Init() failed.");
                    return false;
                }

                UserId = SteamUser.GetSteamID().m_SteamID;
                
                // Register Callbacks
                _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreatedCallback);
                _gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequestedCallback);
                _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEnteredCallback);
                _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdateCallback);

                if (SupremeManager.Instance != null)
                {
                    SupremeManager.Instance.OnUpdate += OnUpdate;
                }
                
                _isInitialized = true;
                Debug.Log($"[{nameof(SteamPlatformService)}] Initialized. UserID: {UserId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(SteamPlatformService)}] Error initializing: {e.Message}");
                return false;
            }

            await Task.Yield();
            return true;
        }

        public async Task<bool> StartService()
        {
            // Usually nothing specific needed here for Steam itself if Init passed
            await Task.Yield();
            return _isInitialized;
        }

        public async Task<bool> StopService()
        {
            if (SupremeManager.Instance != null)
            {
                SupremeManager.Instance.OnUpdate -= OnUpdate;
            }
            LeaveLobby();
            _isInitialized = false;
            await Task.Yield();
            return true;
        }
        
        private void OnUpdate()
        {
            if (_isInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        // ----------------------- LOBBY LOGIC -----------------------

        public Task<bool> CreateLobby()
        {
            _createLobbyTcs = new TaskCompletionSource<bool>();
            
            // Create a Public lobby with max 4 players
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 4);

            return _createLobbyTcs.Task;
        }

        public Task<(bool, string)> JoinLobby(ulong lobbyId)
        {
            _joinLobbyTcs = new TaskCompletionSource<(bool, string)>();
            
            CSteamID steamLobbyId = new CSteamID(lobbyId);
            SteamMatchmaking.JoinLobby(steamLobbyId);

            return _joinLobbyTcs.Task;
        }

        public void LeaveLobby()
        {
            if (_currentLobbyId.IsValid())
            {
                SteamMatchmaking.LeaveLobby(_currentLobbyId);
                _currentLobbyId = CSteamID.Nil;
                
                // Also stop PurrNet if we leave
                if (InstanceHandler.NetworkManager != null)
                {
                    InstanceHandler.NetworkManager.StopClient();
                    InstanceHandler.NetworkManager.StopServer();
                }
            }
        }

        // ----------------------- CALLBACKS -----------------------

        private void OnLobbyCreatedCallback(LobbyCreated_t callback)
        {
            if (callback.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError($"[{nameof(SteamPlatformService)}] Failed to create lobby.");
                _createLobbyTcs?.TrySetResult(false);
                return;
            }

            _currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_currentLobbyId, HOST_ADDRESS_KEY, UserId.ToString());

            StartPurrNetAndWait(isHost: true, hostId: UserId);
        }

        private void OnLobbyEnteredCallback(LobbyEnter_t callback)
        {
            _currentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            string hostIdStr = SteamMatchmaking.GetLobbyData(_currentLobbyId, HOST_ADDRESS_KEY);

            if (ulong.TryParse(hostIdStr, out ulong hostId))
            {
                LobbyHostId = hostId;
                Debug.Log($"[{nameof(SteamPlatformService)}] Entered Lobby: {_currentLobbyId}, HostID: {hostId}");
                Debug.Log($"[{nameof(SteamPlatformService)}] The UserID is : {UserId}");
                Debug.Log($"[{nameof(SteamPlatformService)}] The UserID is equal to HostID? {UserId == hostId}");
                
                if (hostId != UserId)
                {
                    StartPurrNetAndWaitClient(hostId);
                }
                else
                {
                    OnEnteredLobby?.Invoke(_currentLobbyId.ToString());
                }
            }
            else
            {
                Debug.LogError($"[{nameof(SteamPlatformService)}] Could not parse Host ID.");
                _joinLobbyTcs?.TrySetResult((false, "Invalid Host ID"));
            }
        }

        private void OnGameLobbyJoinRequestedCallback(GameLobbyJoinRequested_t callback)
        {
            // Triggered when accepting an invite via Steam Overlay
            OnJoinLobbyRequested?.Invoke(callback.m_steamIDLobby.ToString());
        }

        private void OnLobbyChatUpdateCallback(LobbyChatUpdate_t callback)
        {
            // Steam triggers this for Join/Leave/Disconnect
            if ((callback.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
            {
                OnMemberJoinedLobby?.Invoke(callback.m_ulSteamIDUserChanged);
            }
            else if ((callback.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft) != 0)
            {
                OnMemberLeaveLobby?.Invoke(callback.m_ulSteamIDUserChanged);
            }
            else if ((callback.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected) != 0)
            {
                OnMemberDisconnectedLobby?.Invoke(callback.m_ulSteamIDUserChanged);
            }
        }

        // ----------------------- PURRNET INTEGRATION -----------------------

        private async void StartPurrNetAndWait(bool isHost, ulong hostId)
        {
            var networkManager = InstanceHandler.NetworkManager;

            if (networkManager == null)
            {
                Debug.LogError($"[{nameof(SteamPlatformService)}] NetworkManager not found!");
                _createLobbyTcs?.TrySetResult(false);
                return;
            }

            if (networkManager.transport is not SteamTransport steamTransport)
            {
                Debug.LogError($"[{nameof(SteamPlatformService)}] Transport is not SteamTransport!");
                _createLobbyTcs?.TrySetResult(false);
                return;
            }

            steamTransport.peerToPeer = true;
            steamTransport.address = hostId.ToString();

            Debug.Log($"[{nameof(SteamPlatformService)}] Starting PurrNet as {(isHost ? "HOST" : "CLIENT")}");

            if (isHost)
            {
                networkManager.StartHost();
                SupremeManager.Instance?.InitNetworkedSceneLoader();
        
                while (!networkManager.isServer || networkManager.localPlayer == default)
                {
                    await Task.Yield();
                }
        
                Debug.Log($"[{nameof(SteamPlatformService)}] Host fully initialized. LocalPlayer: {networkManager.localPlayer}");
        
                OnCreatedLobby?.Invoke(_currentLobbyId.ToString());
                _createLobbyTcs?.TrySetResult(true);
            }
            else
            {
                networkManager.StartClient();

                while (!networkManager.isClient || networkManager.localPlayer == default)
                {
                    await Task.Yield();
                }
        
                Debug.Log($"[{nameof(SteamPlatformService)}] Client fully connected. LocalPlayer: {networkManager.localPlayer}");
            }
        }
        
        private async void StartPurrNetAndWaitClient(ulong hostId)
        {
            var networkManager = InstanceHandler.NetworkManager;

            if (networkManager == null || networkManager.transport is not SteamTransport steamTransport)
            {
                _joinLobbyTcs?.TrySetResult((false, "NetworkManager or transport not found"));
                return;
            }

            steamTransport.peerToPeer = true;
            steamTransport.address = hostId.ToString();

            Debug.Log($"[{nameof(SteamPlatformService)}] Starting PurrNet as CLIENT");

            networkManager.StartClient();

            // ✅ Wait until client is fully connected
            float timeout = 90f;
            float elapsed = 0f;
            Debug.Log($"[{nameof(SteamPlatformService)}] Timeoutthing Started");
            while ((!networkManager.isClient || networkManager.localPlayer == default) && elapsed < timeout)
            {
                await Task.Yield();
                elapsed += Time.deltaTime;
            }

            if (networkManager.isClient && networkManager.localPlayer != default)
            {
                Debug.Log($"[{nameof(SteamPlatformService)}] Client connected. LocalPlayer: {networkManager.localPlayer}");
                _joinLobbyTcs?.TrySetResult((true, "Success"));
                OnEnteredLobby?.Invoke(_currentLobbyId.ToString());
            }
            else
            {
                Debug.LogError($"[{nameof(SteamPlatformService)}] Client connection timeout");
                _joinLobbyTcs?.TrySetResult((false, "Connection timeout"));
            }
        }

        // --- Helpers ---
        public ulong GetLobbyId() => _currentLobbyId.m_SteamID;
        public ulong[] GetMemberIds() => new ulong[0]; // Implement via SteamMatchmaking.GetLobbyMemberByIndex if needed
        public void SetCurrentScene(string sceneId) { /* Implement via SteamMatchmaking.SetLobbyData if needed */ }
    }
}
#endif