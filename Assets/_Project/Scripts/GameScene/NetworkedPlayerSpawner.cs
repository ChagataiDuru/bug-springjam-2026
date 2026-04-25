using System.Collections;
using System.Collections.Generic;
using PurrNet; // Core PurrNet
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    public class NetworkedPlayerSpawner : NetworkBehaviour
    {
        #region Serialized Fields
        
        [Header("Prefabs")]
        [Tooltip("The Player Prefab to be spawned. Must have a NetworkIdentity.")]
        public GameObject PlayerPrefab; // GameSceneManager reads this
        
        [Header("Spawn Points")]
        [Tooltip("Array of spawn point transforms")]
        [SerializeField] private Transform[] _spawnPoints;
        
        [Tooltip("Automatically find spawn points with this tag")]
        [SerializeField] private string _spawnPointTag = "SpawnPoint";
        
        [Header("Spawn Settings")]
        [Tooltip("Randomize spawn point selection")]
        [SerializeField] private bool _randomizeSpawnPoints = false;
        
        [Tooltip("Delay after spawn before positioning (allows physics to initialize)")]
        [SerializeField] private float _positioningDelay = 0.1f;
        
        #endregion
        
        #region Private Fields
        
        private int _nextSpawnIndex = 0;
        private NetworkManager NM => InstanceHandler.NetworkManager;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // Auto-find spawn points if not assigned
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                FindSpawnPoints();
            }
        }
        
        private void FindSpawnPoints()
        {
            // Try to find by tag
            GameObject[] taggedSpawns = GameObject.FindGameObjectsWithTag(_spawnPointTag);
            if (taggedSpawns.Length > 0)
            {
                _spawnPoints = new Transform[taggedSpawns.Length];
                for (int i = 0; i < taggedSpawns.Length; i++)
                {
                    _spawnPoints[i] = taggedSpawns[i].transform;
                }
                Debug.Log($"[NetworkedPlayerSpawner] Found {_spawnPoints.Length} spawn points by tag");
                return;
            }
            
            // Try to find by name pattern
            var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var spawnPointsList = new List<Transform>();
            
            foreach (var t in allTransforms)
            {
                if (t.name.Contains("SpawnPoint") || t.name.Contains("Spawn Point") || t.name.Contains("PlayerSpawn"))
                {
                    spawnPointsList.Add(t);
                }
            }
            
            if (spawnPointsList.Count > 0)
            {
                _spawnPoints = spawnPointsList.ToArray();
                Debug.Log($"[NetworkedPlayerSpawner] Found {_spawnPoints.Length} spawn points by name");
            }
            else
            {
                Debug.LogWarning("[NetworkedPlayerSpawner] No spawn points found! Players will spawn at origin.");
            }
        }
        
        #endregion
        
        #region Network Lifecycle
        
        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);
            
            if (!asServer) return;
            
            Debug.Log("[NetworkedPlayerSpawner] Server initialized - ready to position players");
            
            if (NM != null)
            {
                NM.onPlayerJoined += OnPlayerJoined;
            }
        }
        
        protected override void OnDespawned()
        {
            if (NM != null)
            {
                NM.onPlayerJoined -= OnPlayerJoined;
            }
            
            base.OnDespawned();
        }
        
        #endregion
        
        #region Spawn Handling
        
        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!isServer) return;
            
            // GameSceneManager handles the main spawning loop, 
            // but this event is useful for debugging or late-join logic if needed.
            Debug.Log($"[NetworkedPlayerSpawner] Client {player} joined game scene.");
        }
        
        /// <summary>
        /// Position a player at a spawn point.
        /// Accepts standard GameObject.
        /// </summary>
        private void PositionPlayer(GameObject playerObject, PlayerID clientId)
        {
            Transform spawnTransform = GetNextSpawnPoint();
            
            if (spawnTransform == null)
            {
                Debug.LogWarning("[NetworkedPlayerSpawner] No spawn point available, using default position");
                return;
            }
            
            // Disable CharacterController to allow teleportation
            var charController = playerObject.GetComponent<CharacterController>();
            if (charController != null)
            {
                charController.enabled = false;
            }
            
            // Set position and rotation
            playerObject.transform.position = spawnTransform.position;
            playerObject.transform.rotation = spawnTransform.rotation;
            
            // Re-enable CharacterController
            if (charController != null)
            {
                charController.enabled = true;
            }
            
            Debug.Log($"[NetworkedPlayerSpawner] Positioned player {clientId} at {spawnTransform.position}");
        }
        
        /// <summary>
        /// Get the next spawn point
        /// </summary>
        private Transform GetNextSpawnPoint()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                Debug.LogError("[NetworkedPlayerSpawner] No spawn points configured!");
                return null;
            }
            
            Transform spawn;
            
            if (_randomizeSpawnPoints)
            {
                int randomIndex = Random.Range(0, _spawnPoints.Length);
                spawn = _spawnPoints[randomIndex];
            }
            else
            {
                spawn = _spawnPoints[_nextSpawnIndex];
                _nextSpawnIndex = (_nextSpawnIndex + 1) % _spawnPoints.Length;
            }
            
            return spawn;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Called by GameSceneManager after manually spawning a player.
        /// </summary>
        public void PositionNewlySpawnedPlayer(GameObject playerObject, PlayerID clientId)
        {
            if (!isServer) return;
            PositionPlayer(playerObject, clientId);
        }
        
        /// <summary>
        /// Manually respawn a player at a spawn point
        /// </summary>
        public void RespawnPlayer(PlayerID clientId)
        {
            if (!isServer)
            {
                RespawnPlayerServerRpc(clientId);
                return;
            }
            
            // Look up the player in GameSceneManager
            var gameManager = GameSceneManager.Instance;
            if (gameManager != null)
            {
                var playerController = gameManager.AllPlayers.Find(p => p.PlayerClientId == clientId.id);
                if (playerController != null)
                {
                    PositionPlayer(playerController.gameObject, clientId);
                }
            }
        }
        
        [ServerRpc(requireOwnership: false)]
        private void RespawnPlayerServerRpc(PlayerID clientId)
        {
            RespawnPlayer(clientId);
        }
        
        /// <summary>
        /// Get a specific spawn point by index
        /// </summary>
        public Transform GetSpawnPoint(int index)
        {
            if (_spawnPoints == null || index < 0 || index >= _spawnPoints.Length)
                return null;
            return _spawnPoints[index];
        }
        
        public int SpawnPointCount => _spawnPoints?.Length ?? 0;
        
        #endregion
        
        #region Editor Visualization
        
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_spawnPoints == null) return;
            
            Gizmos.color = Color.green;
            foreach (var spawn in _spawnPoints)
            {
                if (spawn != null)
                {
                    Gizmos.DrawWireSphere(spawn.position, 0.5f);
                    Gizmos.DrawLine(spawn.position, spawn.position + spawn.forward * 2f);
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(spawn.position, spawn.position + Vector3.up * 1.8f);
                    Gizmos.color = Color.green;
                }
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            if (_spawnPoints == null) return;
            
            foreach (var spawn in _spawnPoints)
            {
                if (spawn != null)
                {
                    UnityEditor.Handles.Label(spawn.position + Vector3.up * 2f, spawn.name);
                }
            }
        }
#endif
        
        #endregion
    }
}