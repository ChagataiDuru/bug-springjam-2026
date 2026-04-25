using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Steam;
using PurrNet.Transports;
using Taiyun.SuckTheWater.InitScene.Steps;
using Taiyun.SuckTheWater.Main;
using Taiyun.SuckTheWater.Service.LobbyService;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.HubScene
{
    /// <summary>
    /// Consolidated manager for Init → MainMenu → Lobby flow.
    /// Uses Steam Lobby as the source of truth for player management.
    /// 
    /// Key Architecture:
    /// - NO LobbyNetworkState needed
    /// - Player list comes from Steam Lobby (Lobby.Members)
    /// - Ready states stored in Steam Lobby metadata
    /// - Only PurrNet is used for actual gameplay networking
    /// 
    /// This eliminates network spawn race conditions by using Steam's
    /// already-connected lobby system for pre-game coordination.
    /// </summary>
    public class HubSceneManager : MonoBehaviour
    {
        #region State Machine

        public enum HubState
        {
            None,           // Initial state before any transition
            Initializing,   // Steam init, services startup
            MainMenu,       // Host/Join buttons, idle state
            HostingLobby,   // Creating lobby, starting server
            JoiningLobby,   // Connecting as client
            InLobby,        // Player list, ready, start game
            LoadingGame     // Transitioning to GameScene
        }

        [Header("Current State (Debug)")]
        [SerializeField] private HubState _currentState = HubState.None;
        public HubState CurrentState => _currentState;

        public event Action<HubState> OnStateChanged;

        #endregion

        #region UI Panels

        [Header("UI Panels")]
        [SerializeField] private GameObject _initPanel;
        [SerializeField] private GameObject _mainMenuPanel;
        [SerializeField] private GameObject _lobbyPanel;

        #endregion

        #region Init UI

        [Header("Init UI")]
        [SerializeField] private Slider _initProgressSlider;
        [SerializeField] private TMP_Text _initProgressText;
        [SerializeField] private TMP_Text _initStepNameText;

        #endregion

        #region Main Menu UI

        [Header("Main Menu UI")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinFriendButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private TMP_Text _mainMenuStatusText;

        #endregion

        #region Lobby UI

        [Header("Lobby UI")]
        [SerializeField] private TMP_Text _lobbyCodeText;
        [SerializeField] private LobbyPlayerSlot[] _playerSlots;
        [SerializeField] private Button _readyButton;
        [SerializeField] private TMP_Text _readyButtonText;
        [SerializeField] private Button _startGameButton;
        [SerializeField] private Button _leaveLobbyButton;

        #endregion

        #region Dependencies

        private LobbyManager _lobbyManager;
        private LobbyDataHolder _lobbyDataHolder;
        private bool _isBusy;
        private bool _localIsReady;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Debug.Log("[HubSceneManager] Awake");
        }

        private void Start()
        {
            Debug.Log("[HubSceneManager] Start - Beginning initialization");
            Debug.Log($"[HubSceneManager] Current state: {_currentState}");
            
            // Validate panel references
            if (_initPanel == null) Debug.LogError("[HubSceneManager] Init Panel reference is missing!");
            if (_mainMenuPanel == null) Debug.LogError("[HubSceneManager] Main Menu Panel reference is missing!");
            if (_lobbyPanel == null) Debug.LogError("[HubSceneManager] Lobby Panel reference is missing!");
            
            SetupButtonListeners();
            TransitionToState(HubState.Initializing);
        }

        private void OnDestroy()
        {
            CleanupCallbacks();
        }

        #endregion

        #region State Machine Core

        private void TransitionToState(HubState newState)
        {
            if (_currentState == newState) return;

            Debug.Log($"[HubSceneManager] State transition: {_currentState} → {newState}");

            OnExitState(_currentState);
            _currentState = newState;
            OnEnterState(newState);
            OnStateChanged?.Invoke(newState);
        }

        private void OnExitState(HubState state)
        {
            switch (state)
            {
                case HubState.InLobby:
                    UnsubscribeFromLobbyEvents();
                    break;
            }
        }

        private void OnEnterState(HubState state)
        {
            // Hide all panels first
            SetPanelActive(_initPanel, false);
            SetPanelActive(_mainMenuPanel, false);
            SetPanelActive(_lobbyPanel, false);

            switch (state)
            {
                case HubState.None:
                    // Initial state, do nothing - will transition to Initializing
                    break;

                case HubState.Initializing:
                    SetPanelActive(_initPanel, true);
                    RunInitializationAsync().Forget();
                    break;

                case HubState.MainMenu:
                    SetPanelActive(_mainMenuPanel, true);
                    SetupMainMenuCallbacks();
                    SetMainMenuButtonsInteractable(true);
                    _isBusy = false;
                    break;

                case HubState.HostingLobby:
                    SetPanelActive(_mainMenuPanel, true);
                    SetMainMenuButtonsInteractable(false);
                    break;

                case HubState.JoiningLobby:
                    SetPanelActive(_mainMenuPanel, true);
                    SetMainMenuButtonsInteractable(false);
                    break;

                case HubState.InLobby:
                    SetPanelActive(_lobbyPanel, true);
                    _localIsReady = false;
                    SubscribeToLobbyEvents();
                    SetupLobbyUI();
                    UpdateLobbyUI();
                    break;

                case HubState.LoadingGame:
                    // Network scene loading handles this
                    break;
            }
        }

        private void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null)
            {
                panel.SetActive(active);
                Debug.Log($"[HubSceneManager] Panel '{panel.name}' set to {active}");
            }
            else
            {
                Debug.LogWarning("[HubSceneManager] Panel reference is null!");
            }
        }

        #endregion

        #region Initialization State

        private Queue<IInitialStep> _initSteps;

        private async UniTaskVoid RunInitializationAsync()
        {
            Debug.Log("[HubSceneManager] RunInitializationAsync started");
            await UniTask.Yield();

            Debug.Log("[HubSceneManager] Starting initialization sequence...");

            // Check if services are already initialized (by SupremeManager)
            var serviceManager = SupremeManager.Instance?.ServiceManager;
            bool servicesAlreadyInitialized = serviceManager?.LobbyManager != null;

            if (servicesAlreadyInitialized)
            {
                Debug.Log("[HubSceneManager] Services already initialized by SupremeManager, skipping init steps");
            }
            else
            {
                Debug.Log("[HubSceneManager] Services not initialized, running init steps...");
                
                _initSteps = new Queue<IInitialStep>();
                _initSteps.Enqueue(new LinkTestStep());
                _initSteps.Enqueue(new PlatformServiceStep());

                Debug.Log($"[HubSceneManager] Queued {_initSteps.Count} init steps");

                bool success = await ExecuteInitStepsAsync();

                if (!success)
                {
                    Debug.LogError("[HubSceneManager] Initialization failed!");
                    SupremeManager.Instance.ShowPopUpOkay("Initialization failed.\nPlease restart the game.");
                    return;
                }
            }

            Debug.Log("[HubSceneManager] Initialization complete!");
            
            // Brief delay for visual feedback
            UpdateInitUI(1f, "Complete");
            await UniTask.Delay(500);

            Debug.Log("[HubSceneManager] Transitioning to MainMenu...");
            TransitionToState(HubState.MainMenu);
        }

        private async UniTask<bool> ExecuteInitStepsAsync()
        {
            int totalSteps = _initSteps.Count;
            int currentStepIndex = 0;

            while (_initSteps.Count > 0)
            {
                var step = _initSteps.Dequeue();
                currentStepIndex++;

                float progress = (float)currentStepIndex / totalSteps;
                UpdateInitUI(progress, step.Name);

                Debug.Log($"[HubSceneManager] Executing step {currentStepIndex}/{totalSteps}: {step.Name}");

                bool stepSuccess = await step.Execute();
                if (!stepSuccess)
                {
                    Debug.LogError($"[HubSceneManager] Step failed: {step.Name}");
                    return false;
                }

                await UniTask.Yield();
            }

            UpdateInitUI(1f, "Complete");
            return true;
        }

        private void UpdateInitUI(float progress, string stepName)
        {
            if (_initProgressSlider != null) _initProgressSlider.value = progress;
            if (_initProgressText != null) _initProgressText.text = $"{(int)(progress * 100)}%";
            if (_initStepNameText != null) _initStepNameText.text = stepName;
        }

        #endregion

        #region Main Menu State

        private void SetupButtonListeners()
        {
            _hostButton?.onClick.AddListener(OnHostClicked);
            _joinFriendButton?.onClick.AddListener(OnJoinFriendClicked);
            _quitButton?.onClick.AddListener(OnQuitClicked);
            _readyButton?.onClick.AddListener(OnReadyClicked);
            _startGameButton?.onClick.AddListener(OnStartGameClicked);
            _leaveLobbyButton?.onClick.AddListener(OnLeaveClicked);
        }

        private void SetupMainMenuCallbacks()
        {
            _lobbyManager = SupremeManager.Instance?.ServiceManager?.LobbyManager;
            _lobbyDataHolder = LobbyDataHolder.Instance;

            if (_lobbyManager != null)
            {
                // Unsubscribe first to avoid duplicates
                _lobbyManager.OnRoomJoined -= OnLobbyJoined;
                _lobbyManager.OnJoinLobbyRequested -= OnJoinLobbyRequested;
                _lobbyManager.OnRoomJoinFailed -= OnJoinFailed;
                _lobbyManager.OnError -= OnLobbyError;

                // Subscribe
                _lobbyManager.OnRoomJoined += OnLobbyJoined;
                _lobbyManager.OnJoinLobbyRequested += OnJoinLobbyRequested;
                _lobbyManager.OnRoomJoinFailed += OnJoinFailed;
                _lobbyManager.OnError += OnLobbyError;
            }
        }

        private void SubscribeToLobbyEvents()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnPlayerListUpdated += OnPlayerListUpdated;
                _lobbyManager.OnRoomUpdated += OnRoomUpdated;
                _lobbyManager.OnAllReady += OnAllPlayersReady;
            }
        }

        private void UnsubscribeFromLobbyEvents()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnPlayerListUpdated -= OnPlayerListUpdated;
                _lobbyManager.OnRoomUpdated -= OnRoomUpdated;
                _lobbyManager.OnAllReady -= OnAllPlayersReady;
            }
        }

        private void CleanupCallbacks()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnRoomJoined -= OnLobbyJoined;
                _lobbyManager.OnJoinLobbyRequested -= OnJoinLobbyRequested;
                _lobbyManager.OnRoomJoinFailed -= OnJoinFailed;
                _lobbyManager.OnError -= OnLobbyError;
                _lobbyManager.OnPlayerListUpdated -= OnPlayerListUpdated;
                _lobbyManager.OnRoomUpdated -= OnRoomUpdated;
                _lobbyManager.OnAllReady -= OnAllPlayersReady;
            }
        }

        private void SetMainMenuButtonsInteractable(bool interactable)
        {
            if (_hostButton != null) _hostButton.interactable = interactable;
            if (_joinFriendButton != null) _joinFriendButton.interactable = interactable;
            if (_quitButton != null) _quitButton.interactable = interactable;
        }

        #region Main Menu Button Handlers

        private void OnHostClicked()
        {
            if (_isBusy || _currentState != HubState.MainMenu) return;
            _isBusy = true;
            TransitionToState(HubState.HostingLobby);
            HostLobbyAsync().Forget();
        }

        private void OnJoinFriendClicked()
        {
            SupremeManager.Instance.ShowPopUpOkay("Use Steam Overlay (Shift+Tab) to join friends.");
        }

        private void OnQuitClicked()
        {
            Application.Quit();
        }

        #endregion

        #region Lobby Callbacks

        private void OnLobbyJoined(Lobby lobby)
        {
            // Only handle client joins (host flow handled separately)
            if (lobby.IsOwner)
            {
                Debug.Log("[HubSceneManager] OnLobbyJoined: We are host, skipping");
                return;
            }

            if (_currentState == HubState.JoiningLobby)
            {
                Debug.Log("[HubSceneManager] OnLobbyJoined: Already joining, skipping duplicate");
                return;
            }

            Debug.Log($"[HubSceneManager] Joined lobby as CLIENT: {lobby.LobbyId}");
            TransitionToState(HubState.JoiningLobby);
            JoinLobbyAsClientAsync().Forget();
        }

        private void OnJoinLobbyRequested(string lobbyId)
        {
            if (_isBusy || _currentState != HubState.MainMenu) return;
            _isBusy = true;
            JoinLobbyByIdAsync(lobbyId).Forget();
        }

        private void OnJoinFailed(string message)
        {
            HandleError($"Failed to join: {message}");
        }

        private void OnLobbyError(string message)
        {
            HandleError(message);
        }

        private void OnPlayerListUpdated(List<LobbyUser> players)
        {
            Debug.Log($"[HubSceneManager] Player list updated: {players.Count} players");
            UpdateLobbyUI();
        }

        private void OnRoomUpdated(Lobby lobby)
        {
            Debug.Log($"[HubSceneManager] Room updated: {lobby.Members.Count} members");
            UpdateLobbyUI();
        }

        private void OnAllPlayersReady()
        {
            Debug.Log("[HubSceneManager] All players are ready!");
            // Host can now start the game
            UpdateLobbyUI();
        }

        #endregion

        #endregion

        #region Host Flow

        private async UniTaskVoid HostLobbyAsync()
        {
            Debug.Log("[HubSceneManager] Creating lobby...");
            SupremeManager.Instance.ShowPopUpWait("Creating Lobby...");

            try
            {
                // 1. Create Steam lobby
                var lobby = await _lobbyManager.CreateRoomAsync(4);

                if (!lobby.IsValid)
                {
                    HandleError("Failed to create Steam Lobby.");
                    return;
                }

                Debug.Log($"[HubSceneManager] Lobby created: {lobby.LobbyId}");

                // 2. Start PurrNet as host
                SupremeManager.Instance.ShowPopUpWait("Starting Server...");

                bool networkStarted = await StartPurrNetAsHostAsync();
                if (!networkStarted)
                {
                    HandleError("Failed to start network.");
                    return;
                }

                // 3. Transition to lobby UI (same scene!)
                SupremeManager.Instance.HidePopUpWait();
                Debug.Log("[HubSceneManager] HOST entering lobby state");
                TransitionToState(HubState.InLobby);
            }
            catch (Exception ex)
            {
                HandleError($"Host error: {ex.Message}");
            }
        }

        private async UniTask<bool> StartPurrNetAsHostAsync()
        {
            await UniTask.SwitchToMainThread();

            var nm = InstanceHandler.NetworkManager;
            var lobbyData = LobbyDataHolder.Instance;

            if (nm == null || lobbyData == null || !lobbyData.HasValidLobby())
            {
                Debug.LogError("[HubSceneManager] NetworkManager or LobbyData invalid");
                return false;
            }

            // Configure transport
            if (nm.transport is SteamTransport steamTransport)
            {
                steamTransport.peerToPeer = true;
                steamTransport.address = lobbyData.GetHostId();
                Debug.Log($"[HubSceneManager] Transport configured. Address: {lobbyData.GetHostId()}");
            }
            else
            {
                Debug.LogError("[HubSceneManager] Transport is not SteamTransport!");
                return false;
            }

            // 1. Start server
            Debug.Log("[HubSceneManager] Starting server...");
            nm.StartServer();

            float elapsed = 0f;
            while (!nm.isServer && elapsed < 10f)
            {
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            if (!nm.isServer)
            {
                Debug.LogError("[HubSceneManager] Server failed to start!");
                return false;
            }

            Debug.Log("[HubSceneManager] Server started, waiting before client...");
            await UniTask.Delay(500);

            // 2. Start local client
            Debug.Log("[HubSceneManager] Starting host client...");
            nm.StartClient();

            elapsed = 0f;
            while ((!nm.isClient || nm.localPlayer == default) && elapsed < 10f)
            {
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            if (!nm.isClient || nm.localPlayer == default)
            {
                Debug.LogError("[HubSceneManager] Host client failed to connect!");
                return false;
            }

            Debug.Log($"[HubSceneManager] Host fully initialized. LocalPlayer: {nm.localPlayer}");

            // 3. Signal server ready via Steam Lobby metadata
            _lobbyManager?.SetServerReady();

            // 4. Initialize networked scene loader for later
            SupremeManager.Instance?.InitNetworkedSceneLoader();

            return true;
        }

        #endregion

        #region Client Flow

        private async UniTaskVoid JoinLobbyByIdAsync(string lobbyId)
        {
            Debug.Log($"[HubSceneManager] Join requested for lobby: {lobbyId}");
            SupremeManager.Instance.ShowPopUpWait("Joining Lobby...");

            try
            {
                var lobby = await _lobbyManager.JoinLobbyAsync(lobbyId);

                if (!lobby.IsValid)
                {
                    HandleError("Failed to join lobby.");
                    return;
                }

                // OnLobbyJoined callback will trigger JoinLobbyAsClientAsync
            }
            catch (Exception ex)
            {
                HandleError($"Join error: {ex.Message}");
            }
        }

        private async UniTaskVoid JoinLobbyAsClientAsync()
        {
            try
            {
                SupremeManager.Instance.ShowPopUpWait("Connecting to server...");

                bool networkStarted = await StartPurrNetAsClientAsync();
                if (!networkStarted)
                {
                    HandleError("Failed to connect to server.");
                    return;
                }

                // Transition to lobby UI (same scene!)
                SupremeManager.Instance.HidePopUpWait();
                Debug.Log("[HubSceneManager] CLIENT entering lobby state");
                TransitionToState(HubState.InLobby);
            }
            catch (Exception ex)
            {
                HandleError($"Client error: {ex.Message}");
            }
        }

        private async UniTask<bool> StartPurrNetAsClientAsync()
        {
            await UniTask.SwitchToMainThread();

            var nm = InstanceHandler.NetworkManager;
            var lobbyData = LobbyDataHolder.Instance;

            if (nm == null || lobbyData == null || !lobbyData.HasValidLobby())
            {
                Debug.LogError("[HubSceneManager] NetworkManager or LobbyData invalid");
                return false;
            }

            // Force clean state
            if (nm.isClient || nm.isServer)
            {
                Debug.Log("[HubSceneManager] NetworkManager was active, stopping...");
                nm.StopClient();
                nm.StopServer();
                await UniTask.Delay(500);
            }

            // Configure transport
            if (nm.transport is SteamTransport steamTransport)
            {
                steamTransport.peerToPeer = true;
                steamTransport.address = lobbyData.GetHostId();
                Debug.Log($"[HubSceneManager] Transport configured. Target: {steamTransport.address}");
            }
            else
            {
                Debug.LogError("[HubSceneManager] Transport is not SteamTransport!");
                return false;
            }

            // Wait for server ready flag (using LobbyManager's built-in method)
            Debug.Log("[HubSceneManager] Waiting for server to be ready...");
            bool serverReady = await _lobbyManager.WaitForServerReady(30f);
            if (!serverReady)
            {
                Debug.LogError("[HubSceneManager] Server ready timeout!");
                return false;
            }

            Debug.Log("[HubSceneManager] Server ready! Waiting for P2P relay...");
            await UniTask.Delay(1000);

            // Start client
            Debug.Log("[HubSceneManager] Starting PurrNet Client...");
            nm.StartClient();

            // Wait for connection
            float timeout = 30f;
            float elapsed = 0f;

            while (!nm.isClient && elapsed < timeout)
            {
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            if (!nm.isClient)
            {
                Debug.LogError("[HubSceneManager] Client failed to start!");
                return false;
            }

            Debug.Log("[HubSceneManager] Client started. Waiting for player spawn...");

            elapsed = 0f;
            while (nm.localPlayer == default && elapsed < timeout)
            {
                await UniTask.Yield();
                elapsed += Time.deltaTime;
            }

            if (nm.localPlayer == default)
            {
                Debug.LogError("[HubSceneManager] Player spawn timeout!");
                nm.StopClient();
                return false;
            }

            Debug.Log($"[HubSceneManager] Client fully connected! LocalPlayer: {nm.localPlayer}");

            // Initialize networked scene loader
            SupremeManager.Instance?.InitNetworkedSceneLoader();

            return true;
        }

        #endregion

        #region Lobby State (Using Steam Lobby Data)

        private void SetupLobbyUI()
        {
            if (_lobbyCodeText != null && _lobbyDataHolder?.HasValidLobby() == true)
            {
                _lobbyCodeText.text = $"Lobby: {_lobbyDataHolder.CurrentLobby.LobbyId}";
            }

            bool isHost = _lobbyDataHolder?.IsHost ?? false;
            if (_startGameButton != null)
            {
                _startGameButton.gameObject.SetActive(isHost);
            }

            UpdateReadyButtonText();
        }

        /// <summary>
        /// Updates the lobby UI using Steam Lobby data.
        /// No LobbyNetworkState needed!
        /// </summary>
        private void UpdateLobbyUI()
        {
            if (_lobbyDataHolder == null || !_lobbyDataHolder.HasValidLobby()) return;

            var lobby = _lobbyDataHolder.CurrentLobby;
            bool isHost = lobby.IsOwner;

            Debug.Log($"[HubSceneManager] UpdateLobbyUI - IsHost: {isHost}, Members: {lobby.Members?.Count ?? 0}");

            // Update player slots from Steam Lobby members
            if (lobby.Members != null)
            {
                for (int i = 0; i < _playerSlots.Length; i++)
                {
                    if (i < lobby.Members.Count)
                    {
                        var member = lobby.Members[i];
                        bool isMemberHost = (member.Id == lobby.GetHostId());
                        _playerSlots[i].SetPlayer(member.DisplayName, isMemberHost, member.IsReady);

                        // Track local ready state
                        if (member.Id == _lobbyDataHolder.LocalUserId)
                        {
                            _localIsReady = member.IsReady;
                            UpdateReadyButtonText();
                        }
                    }
                    else
                    {
                        _playerSlots[i].SetEmpty();
                    }
                }
            }

            // Update Start button (host only, all must be ready)
            if (_startGameButton != null && isHost)
            {
                bool allReady = lobby.Members != null && 
                               lobby.Members.Count > 0 && 
                               lobby.Members.TrueForAll(x => x.IsReady);
                _startGameButton.interactable = allReady;
            }
        }

        private void UpdateReadyButtonText()
        {
            if (_readyButtonText != null)
            {
                _readyButtonText.text = _localIsReady ? "Not Ready" : "Ready";
            }
            _readyButton?.GetComponent<ButtonHoverGlow>()?.SetState(_localIsReady);
        }

        #region Lobby Button Handlers

        private void OnReadyClicked()
        {
            if (_lobbyManager == null || _currentState != HubState.InLobby) return;

            Debug.Log("[HubSceneManager] Toggling ready state via Steam Lobby...");
            
            // Toggle ready state via Steam Lobby metadata
            _lobbyManager.ToggleLocalReady();
            
            // Optimistic UI update
            _localIsReady = !_localIsReady;
            UpdateReadyButtonText();
        }

        private void OnStartGameClicked()
        {
            if (_currentState != HubState.InLobby) return;
            if (_lobbyDataHolder == null || !_lobbyDataHolder.IsHost) return;

            Debug.Log("[HubSceneManager] Starting game...");
            TransitionToState(HubState.LoadingGame);

            // Mark lobby as started
            _lobbyManager?.SetLobbyStarted();

            // Load GameScene via PurrNet (all connected clients will follow)
            SupremeManager.Instance.LoadSceneNetworked(Scenes.GameScene);
        }

        private void OnLeaveClicked()
        {
            Debug.Log("[HubSceneManager] Leaving lobby...");

            // Leave Steam lobby
            _lobbyManager?.LeaveLobby();

            // Stop networking
            var nm = InstanceHandler.NetworkManager;
            if (nm != null)
            {
                nm.StopClient();
                nm.StopServer();
            }

            // Clear state
            _lobbyDataHolder?.Clear();
            _isBusy = false;
            _localIsReady = false;

            // Return to main menu (same scene, just different state!)
            TransitionToState(HubState.MainMenu);
        }

        #endregion

        #endregion

        #region Error Handling

        private void HandleError(string message)
        {
            _isBusy = false;

            SupremeManager.Instance.HidePopUpWait();
            SupremeManager.Instance.ShowPopUpOkay(message);

            // Clean up network
            var nm = InstanceHandler.NetworkManager;
            if (nm != null)
            {
                if (nm.isClient) nm.StopClient();
                if (nm.isServer) nm.StopServer();
            }

            // Clear lobby state
            LobbyDataHolder.Instance?.Clear();

            Debug.LogError($"[HubSceneManager] Error: {message}");

            // Return to main menu
            TransitionToState(HubState.MainMenu);
        }

        #endregion
    }
}