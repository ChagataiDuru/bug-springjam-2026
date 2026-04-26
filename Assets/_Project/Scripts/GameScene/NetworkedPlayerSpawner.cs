using PurrNet;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// Handles initial player positioning right after spawn.
    /// Role-specific positioning (Upper/Lower) is owned by LevelManager,
    /// which teleports players to their role spawns once the level loop begins.
    /// This spawner just places everyone at a safe pre-loop position.
    /// </summary>
    public class NetworkedPlayerSpawner : NetworkBehaviour
    {
        #region Serialized Fields

        [Header("Prefabs")]
        [Tooltip("The Player Prefab to be spawned. Must have a NetworkIdentity + PlayerRoleSync.")]
        public GameObject PlayerPrefab; // GameSceneManager reads this

        [Header("Initial Spawn")]
        [Tooltip("Where players appear immediately after network spawn, before LevelManager teleports them to their role-specific spawn.")]
        [SerializeField] private Transform _initialSpawn;

        #endregion

        #region Private Fields

        private NetworkManager NM => InstanceHandler.NetworkManager;

        #endregion

        #region Network Lifecycle

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);

            if (!asServer) return;

            Debug.Log("[NetworkedPlayerSpawner] Server initialized.");

            if (NM != null)
                NM.onPlayerJoined += OnPlayerJoined;
        }

        protected override void OnDespawned()
        {
            if (NM != null)
                NM.onPlayerJoined -= OnPlayerJoined;

            base.OnDespawned();
        }

        #endregion

        #region Spawn Handling

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!isServer) return;
            // GameSceneManager handles the main spawning loop;
            // this event remains useful for debugging or late-join logic if needed.
            Debug.Log($"[NetworkedPlayerSpawner] Client {player} joined game scene.");
        }

        /// <summary>
        /// Server-only. Places a freshly spawned player at the initial spawn point.
        /// LevelManager will teleport them to their role spawn shortly after.
        /// </summary>
        private void PositionPlayer(GameObject playerObject, PlayerID clientId)
        {
            if (_initialSpawn == null)
            {
                Debug.LogWarning("[NetworkedPlayerSpawner] _initialSpawn not assigned — leaving player at origin.");
                return;
            }

            var charController = playerObject.GetComponent<CharacterController>();
            if (charController != null) charController.enabled = false;

            playerObject.transform.SetPositionAndRotation(_initialSpawn.position, _initialSpawn.rotation);

            if (charController != null) charController.enabled = true;

            Debug.Log($"[NetworkedPlayerSpawner] Initial-positioned client {clientId} at {_initialSpawn.position}");
        }

        #endregion

        #region Public API

        /// <summary>Called by GameSceneManager after manually spawning a player.</summary>
        public void PositionNewlySpawnedPlayer(GameObject playerObject, PlayerID clientId)
        {
            if (!isServer) return;
            PositionPlayer(playerObject, clientId);
        }

        /// <summary>
        /// Manually respawn a player at the initial spawn (server-only).
        /// Generally LevelManager handles role-based teleporting; this is a fallback.
        /// </summary>
        public void RespawnPlayer(PlayerID clientId)
        {
            if (!isServer)
            {
                RespawnPlayerServerRpc(clientId);
                return;
            }

            var gameManager = GameSceneManager.Instance;
            if (gameManager == null) return;

            var playerController = gameManager.AllPlayers.Find(p => p.PlayerClientId == clientId.id);
            if (playerController != null)
                PositionPlayer(playerController.gameObject, clientId);
        }

        [ServerRpc(requireOwnership: false)]
        private void RespawnPlayerServerRpc(PlayerID clientId)
        {
            RespawnPlayer(clientId);
        }

        public Transform InitialSpawn => _initialSpawn;

        #endregion

        #region Editor Visualization

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (_initialSpawn == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_initialSpawn.position, 0.5f);
            Gizmos.DrawLine(_initialSpawn.position, _initialSpawn.position + _initialSpawn.forward * 2f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(_initialSpawn.position, _initialSpawn.position + Vector3.up * 1.8f);
        }

        private void OnDrawGizmosSelected()
        {
            if (_initialSpawn == null) return;
            UnityEditor.Handles.Label(_initialSpawn.position + Vector3.up * 2f, "InitialSpawn");
        }
#endif

        #endregion
    }
}