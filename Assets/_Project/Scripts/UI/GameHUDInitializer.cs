using UnityEngine;
using Taiyun.SuckTheWater.Gameplay;

namespace Taiyun.SuckTheWater.UI
{
    /// <summary>
    /// Manages HUD initialization timing for networked gameplay.
    /// Keeps HUD disabled until local player spawns, preventing null reference errors
    /// from HUD scripts that depend on player components.
    /// </summary>
    public class GameHUDInitializer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The GameObject containing all HUD elements. Will be disabled until player spawns.")]
        [SerializeField] private GameObject _hudContent;
        [Tooltip("The GameObject containing all InGame Menu elements.")]
        [SerializeField] private GameObject _inGameMenu;
        
        [Header("Settings")]
        [Tooltip("Show debug logs for HUD initialization")]
        [SerializeField] private bool _debugLogs = true;
        
        private bool _isInitialized = false;
        
        void Awake()
        {
            // Ensure HUD is disabled at start
            if (_hudContent != null)
            {
                _hudContent.SetActive(false);
            }
            else
            {
                Debug.LogError("[GameHUDInitializer] HUDContent reference is null!");
            }
            // InGame Menu should also be disabled at start
            if (_inGameMenu != null)
            {
                _inGameMenu.SetActive(false);
            }
            else
            {
                Debug.LogError("[GameHUDInitializer] HUDContent reference is null!");
            }
        }
        
        void OnEnable()
        {
            // Subscribe to player spawn events
            NetworkedPlayerController.OnPlayerSpawned += OnPlayerSpawned;
            NetworkedPlayerController.OnPlayerDespawned += OnPlayerDespawned;
        }
        
        void OnDisable()
        {
            // Unsubscribe from events
            NetworkedPlayerController.OnPlayerSpawned -= OnPlayerSpawned;
            NetworkedPlayerController.OnPlayerDespawned -= OnPlayerDespawned;
        }
        
        private void OnPlayerSpawned(NetworkedPlayerController player)
        {
            // Only initialize HUD for the local player
            if (!player.isOwner)
            {
                if (_debugLogs)
                    Debug.Log($"[GameHUDInitializer] Remote player {player.PlayerClientId} spawned, ignoring for HUD");
                return;
            }
            
            if (_isInitialized)
            {
                if (_debugLogs)
                    Debug.Log("[GameHUDInitializer] HUD already initialized, skipping");
                return;
            }
            
            InitializeHUDMenu(player);
        }
        
        private void OnPlayerDespawned(NetworkedPlayerController player)
        {
            // If local player despawns, hide HUD
            if (player.isOwner && _isInitialized)
            {
                if (_debugLogs)
                    Debug.Log("[GameHUDInitializer] Local player despawned, hiding HUD");
                
                if (_hudContent != null)
                    _hudContent.SetActive(false);
                
                _isInitialized = false;
            }
        }
        
        private void InitializeHUDMenu(NetworkedPlayerController localPlayer)
        {
            if (_hudContent == null || _inGameMenu == null) return;
    
            if (_debugLogs)
                Debug.Log($"[GameHUDInitializer] Local player {localPlayer.PlayerClientId} ready, activating HUD next frame");
    
            StartCoroutine(ActivateHUDNextFrame(localPlayer));
        }

        private System.Collections.IEnumerator ActivateHUDNextFrame(NetworkedPlayerController localPlayer)
        {
            yield return null; // wait one frame
    
            // Debug: verify player components are findable NOW
            Debug.Log($"[GameHUDInitializer] PlayerCharacterController findable: {FindFirstObjectByType<PlayerCharacterController>() != null}");
            Debug.Log($"[GameHUDInitializer] PlayerWeaponsManager findable: {FindFirstObjectByType<PlayerWeaponsManager>() != null}");
    
            _hudContent.SetActive(true);
            _inGameMenu.SetActive(true);
            _isInitialized = true;
    
            if (_debugLogs)
                Debug.Log("[GameHUDInitializer] HUD initialized successfully");
        }
        /// <summary>
        /// Manually trigger HUD initialization if needed (e.g., for late-joining players)
        /// </summary>
        public void ForceInitialize()
        {
            var localPlayer = FindLocalPlayer();
            if (localPlayer != null)
            {
                InitializeHUDMenu(localPlayer);
            }
            else
            {
                Debug.LogWarning("[GameHUDInitializer] ForceInitialize called but no local player found");
            }
        }
        
        private NetworkedPlayerController FindLocalPlayer()
        {
            var players = FindObjectsByType<NetworkedPlayerController>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (player.isOwner)
                    return player;
            }
            return null;
        }
    }
}