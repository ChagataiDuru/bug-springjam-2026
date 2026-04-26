using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using PurrNet;
using Taiyun.SuckTheWater.Game;
using Taiyun.SuckTheWater.Gameplay;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// Main coordinator for the Game Scene.
    /// Handles initialization after networked scene transition from Lobby.
    /// </summary>
    public class GameSceneManager : NetworkBehaviour
    {
        #region Serialized Fields
        
        [Header("Scene References")]
        [Tooltip("Reference to the NetworkedPlayerSpawner in this scene")]
        [SerializeField] private NetworkedPlayerSpawner _playerSpawner;
        [Tooltip("Reference to LevelManager — owns per-level loop after Playing state.")]
        [SerializeField] private LevelManager _levelManager;
        [Header("Game Managers")]
        [Tooltip("Reference to ActorsManager")]
        [SerializeField] private ActorsManager _actorsManager;
        
        [Tooltip("Reference to ObjectiveManager")]
        [SerializeField] private ObjectiveManager _objectiveManager;
        
        [Tooltip("Reference to AudioManager")]
        [SerializeField] private AudioManager _audioManager;
        
        [Header("Initialization Settings")]
        [Tooltip("Delay before starting initialization (allows scene to stabilize)")]
        [SerializeField] private float _initializationDelay = 0.5f;
        
        [Tooltip("Maximum time to wait for all players to be ready")]
        [SerializeField] private float _maxWaitForPlayersTime = 10f;
        
        [Header("UI References")]
        [Tooltip("Loading screen to show during initialization")]
        [SerializeField] private GameObject _loadingScreen;
        
        [Tooltip("Loading progress text")]
        [SerializeField] private TMPro.TMP_Text _loadingText;
        
        #endregion
        
        #region Network Variables
        
        /// <summary>
        /// Server tracks the game state.
        /// Server Authoritative (ownerAuth = false).
        /// </summary>
        public SyncVar<GameState> CurrentGameState = new SyncVar<GameState>(GameState.Initializing);
        
        /// <summary>
        /// Number of players that have finished loading.
        /// Server Authoritative.
        /// </summary>
        private SyncVar<int> _playersReady = new SyncVar<int>(0);
        
        #endregion
        
        #region Public Properties
        
        public List<NetworkedPlayerController> AllPlayers { get; private set; } = new List<NetworkedPlayerController>();
        
        public NetworkedPlayerController LocalPlayer { get; private set; }
        
        public bool IsGameReady => CurrentGameState.value == GameState.Playing;
        
        public static GameSceneManager Instance { get; private set; }

        private NetworkManager NM => InstanceHandler.NetworkManager;
        
        #endregion
        
        #region Enums
        
        public enum GameState
        {
            Initializing,
            SpawningPlayers,
            WaitingForPlayers,
            Starting,
            Playing,
            Paused,
            Ending
        }
        
        #endregion
        
        #region Private Fields
        
        private bool _hasInitialized = false;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            Instance = this;
            FindManagers();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            
            if (Instance == this)
                Instance = null;
            
            NetworkedPlayerController.OnPlayerSpawned -= OnPlayerSpawned;
            NetworkedPlayerController.OnPlayerDespawned -= OnPlayerDespawned;
        }
        
        #endregion
        
        #region Network Lifecycle
        
        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);
            
            Debug.Log($"[GameSceneManager] OnSpawned - asServer: {asServer}, isServer: {isServer}");
            
            // FIX: On host, OnSpawned fires twice (asServer=true, then asServer=false)
            // We only want to initialize once. Skip the client-side call on host.
            if (!asServer && isServer)
            {
                Debug.Log("[GameSceneManager] Skipping host's client-side OnSpawned call");
                return;
            }
            
            // Prevent double initialization
            if (_hasInitialized)
            {
                Debug.LogWarning("[GameSceneManager] Already initialized, skipping");
                return;
            }
            _hasInitialized = true;
            
            // Subscribe to events (only once)
            NetworkedPlayerController.OnPlayerSpawned += OnPlayerSpawned;
            NetworkedPlayerController.OnPlayerDespawned += OnPlayerDespawned;
            
            // SyncVar callbacks
            CurrentGameState.onChanged += OnGameStateChanged;
            
            // Start appropriate initialization
            if (asServer)
            {
                ServerInitialization().Forget();
            }
            else
            {
                // Pure client (not host) - need to clean up old scenes first!
                ClientInitialization().Forget();
            }
        }
        
        protected override void OnDespawned()
        {
            NetworkedPlayerController.OnPlayerSpawned -= OnPlayerSpawned;
            NetworkedPlayerController.OnPlayerDespawned -= OnPlayerDespawned;
            CurrentGameState.onChanged -= OnGameStateChanged;
            _hasInitialized = false;
            
            base.OnDespawned();
        }
        
        #endregion
        
        #region Initialization
        
        private void FindManagers()
        {
            if (_actorsManager == null) _actorsManager = FindFirstObjectByType<ActorsManager>();
            if (_objectiveManager == null) _objectiveManager = FindFirstObjectByType<ObjectiveManager>();
            if (_audioManager == null) _audioManager = FindFirstObjectByType<AudioManager>();
            if (_playerSpawner == null) _playerSpawner = FindFirstObjectByType<NetworkedPlayerSpawner>();
            if (_levelManager == null) _levelManager = FindFirstObjectByType<LevelManager>();
        }
        
        private async UniTask ServerInitialization()
        {
            Debug.Log("[GameSceneManager] Server initialization starting...");
            ShowLoadingScreen("Initializing...");
            
            // Wait for scene to stabilize
            await UniTask.Delay((int)(_initializationDelay * 1000));
            
            // Step 1: Spawn players
            CurrentGameState.value = GameState.SpawningPlayers;
            ShowLoadingScreen("Spawning players...");
            
            await SpawnAllPlayers();
            
            // Step 2: Wait for players
            CurrentGameState.value = GameState.WaitingForPlayers;
            ShowLoadingScreen("Waiting for players...");
            
            float waitStartTime = Time.time;
            while (!AreAllPlayersReady())
            {
                if (Time.time - waitStartTime > _maxWaitForPlayersTime)
                {
                    Debug.LogWarning("[GameSceneManager] Timeout waiting for players, starting anyway");
                    break;
                }
                await UniTask.Delay(500);
            }
            
            Debug.Log($"[GameSceneManager] All {AllPlayers.Count} players ready");
            
            // Step 3: Initialize systems
            CurrentGameState.value = GameState.Starting;
            ShowLoadingScreen("Starting game...");
            
            await InitializeGameSystems();
            
            // Step 4: Start
            CurrentGameState.value = GameState.Playing;
            HideLoadingScreen();
            
            Debug.Log("[GameSceneManager] Game started!");
            if (_levelManager != null)
            {
                _levelManager.ServerBeginLoop();
            }
            else
            {
                Debug.LogError("[GameSceneManager] LevelManager not assigned — game cannot proceed past initial spawn.");
            }
        }
        
        private async UniTask SpawnAllPlayers()
        {
            GameObject playerPrefabObj = null;
            if (_playerSpawner != null && _playerSpawner.PlayerPrefab != null)
            {
                playerPrefabObj = _playerSpawner.PlayerPrefab;
            }
            else
            {
                Debug.LogError("[GameSceneManager] Player prefab not assigned in Spawner!");
                return;
            }

            Debug.Log($"[GameSceneManager] Spawning players for {NM.players.Count} clients");

            foreach (var kvp in NM.players)
            {
                PlayerID playerId = kvp;
                
                if (AllPlayers.Any(p => p.PlayerClientId == playerId.id))
                {
                    Debug.Log($"[GameSceneManager] Client {playerId} already has a player");
                    continue;
                }

                SpawnPlayerForClient(playerId, playerPrefabObj);
                await UniTask.WaitForEndOfFrame();
            }
            
            await UniTask.Delay(500);
        }
        
        private void SpawnPlayerForClient(PlayerID playerId, GameObject playerPrefab)
        {
            Debug.Log($"[GameSceneManager] Spawning for client {playerId}");
            
            GameObject playerInstance = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            
            var netIdentity = playerInstance.GetComponent<NetworkIdentity>();
            if (netIdentity != null)
            {
                netIdentity.GiveOwnership(playerId);
                
                if (_playerSpawner != null)
                {
                    _playerSpawner.PositionNewlySpawnedPlayer(playerInstance, playerId);
                }
            }
            else
            {
                Debug.LogError($"[GameSceneManager] Player prefab missing NetworkIdentity!");
                Destroy(playerInstance);
            }
        }
        
        private async UniTask ClientInitialization()
        {
            Debug.Log("[GameSceneManager] Client initialization starting...");
            ShowLoadingScreen("Connecting...");
            
            // FIX: Clean up old scenes that weren't unloaded (e.g., HubScene)
            // This is critical for clients because PurrNet loads the new scene
            // but the old scene cleanup only runs on the server
            await CleanupOldScenesOnClient();
            
            // Wait for scene to stabilize
            await UniTask.Delay((int)(_initializationDelay * 1000));
            
            NotifyClientReadyServerRpc();
            
            // Wait until Playing
            while (CurrentGameState.value != GameState.Playing)
            {
                UpdateLoadingText(GetStateDisplayText(CurrentGameState.value));
                await UniTask.Delay(100);
            }
            
            HideLoadingScreen();
            Debug.Log("[GameSceneManager] Client ready to play!");
        }
        
        /// <summary>
        /// Cleans up old scenes on the client side.
        /// PurrNet loads the GameScene on clients but doesn't unload the HubScene.
        /// This method handles that cleanup.
        /// </summary>
        private async UniTask CleanupOldScenesOnClient()
        {
            Debug.Log("[GameSceneManager] Client cleaning up old scenes...");
            
            // Get the current scene (GameScene)
            string currentSceneName = gameObject.scene.name;
            
            // Find and unload any scenes that shouldn't be loaded
            List<string> scenesToUnload = new List<string>();
            
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                
                // Keep: Supreme/Managers scene, Current GameScene
                // Unload: HubScene, any other leftover scenes
                if (scene.isLoaded && 
                    scene.name != currentSceneName &&
                    scene.name != "1_Supreme" &&
                    scene.name != "Supreme")
                {
                    scenesToUnload.Add(scene.name);
                }
            }
            
            foreach (string sceneName in scenesToUnload)
            {
                Debug.Log($"[GameSceneManager] Client unloading old scene: {sceneName}");
                
                try
                {
                    AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(sceneName);
                    if (unloadOp != null)
                    {
                        while (!unloadOp.isDone)
                        {
                            await UniTask.Yield();
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[GameSceneManager] Failed to unload scene {sceneName}: {e.Message}");
                }
            }
            
            // Clean up unused assets
            await Resources.UnloadUnusedAssets();
            
            Debug.Log("[GameSceneManager] Client scene cleanup complete");
        }
        
        private async UniTask InitializeGameSystems()
        {
            Debug.Log("[GameSceneManager] Initializing game systems...");
            
            if (_actorsManager != null)
            {
                foreach (var player in AllPlayers)
                {
                    player.RegisterWithGameSystems(_actorsManager);
                }
            }
            
            await UniTask.Yield();
        }
        
        #endregion
        
        #region Player Events
        
        private void OnPlayerSpawned(NetworkedPlayerController player)
        {
            Debug.Log($"[GameSceneManager] Player spawned: ID {player.PlayerClientId}, Owner: {player.isOwner}");
            
            if (!AllPlayers.Contains(player))
            {
                AllPlayers.Add(player);
            }
            
            if (player.isOwner)
            {
                LocalPlayer = player;
                Debug.Log("[GameSceneManager] Local player reference set");
            }
            
            if (_actorsManager != null)
            {
                player.RegisterWithGameSystems(_actorsManager);
            }
        }
        
        private void OnPlayerDespawned(NetworkedPlayerController player)
        {
            Debug.Log($"[GameSceneManager] Player despawned: {player.PlayerClientId}");
            AllPlayers.Remove(player);
            if (LocalPlayer == player) LocalPlayer = null;
        }
        
        #endregion
        
        #region Network RPCs
        
        [ServerRpc(requireOwnership: false)]
        private void NotifyClientReadyServerRpc(RPCInfo info = default)
        {
            Debug.Log($"[GameSceneManager] Client {info.sender} notified ready");
            _playersReady.value++;
        }
        
        [ServerRpc(requireOwnership: false)]
        private void ReturnToLobbyServerRpc()
        {
            ReturnToLobby();
        }
        
        #endregion
        
        #region Helper Methods
        
        private bool AreAllPlayersReady()
        {
            if (NM == null) return false;
            
            int connectedClients = NM.players.Count;
            int spawnedPlayers = AllPlayers.Count;
            
            bool allSpawned = spawnedPlayers >= connectedClients;
            
            if (!allSpawned)
            {
                Debug.Log($"[GameSceneManager] Waiting: {spawnedPlayers}/{connectedClients} spawned");
            }
            
            return allSpawned;
        }
        
        private void OnGameStateChanged(GameState newValue)
        {
            Debug.Log($"[GameSceneManager] Game state changed to: {newValue}");
            UpdateLoadingText(GetStateDisplayText(newValue));
        }
        
        private string GetStateDisplayText(GameState state)
        {
            return state switch
            {
                GameState.Initializing => "Initializing...",
                GameState.SpawningPlayers => "Spawning players...",
                GameState.WaitingForPlayers => "Waiting for players...",
                GameState.Starting => "Starting game...",
                GameState.Playing => "Playing",
                GameState.Paused => "Paused",
                GameState.Ending => "Game ending...",
                _ => "Loading..."
            };
        }
        
        private void ShowLoadingScreen(string message)
        {
            if (_loadingScreen != null) _loadingScreen.SetActive(true);
            UpdateLoadingText(message);
        }
        
        private void HideLoadingScreen()
        {
            if (_loadingScreen != null) _loadingScreen.SetActive(false);
        }
        
        private void UpdateLoadingText(string message)
        {
            if (_loadingText != null) _loadingText.text = message;
        }
        
        #endregion
        
        #region Public API
        
        public void ReturnToLobby()
        {
            if (!isServer)
            {
                ReturnToLobbyServerRpc();
                return;
            }
            
            Debug.Log("[GameSceneManager] Returning to lobby...");
            CurrentGameState.value = GameState.Ending;
            
            foreach (var player in AllPlayers.ToList())
            {
                // Cleanup handled by scene unload
            }
            AllPlayers.Clear();
        }
        
        #endregion
    }
}
