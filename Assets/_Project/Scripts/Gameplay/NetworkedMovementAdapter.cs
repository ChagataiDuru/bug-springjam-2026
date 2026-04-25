using UnityEngine;
using PurrNet;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Adapts the existing PlayerCharacterController for network play.
    /// This component wraps the original controller and adds network awareness.
    /// 
    /// Position Sync Strategy:
    /// - Owner sends position to server via ServerRpc
    /// - Server validates and updates SyncVars
    /// - Remote clients interpolate toward synced position
    /// </summary>
    [RequireComponent(typeof(NetworkedPlayerController))]
    public class NetworkedMovementAdapter : NetworkBehaviour
    {
        #region References
        
        private PlayerCharacterController _characterController;
        private NetworkedPlayerController _networkedPlayer;
        private CharacterController _unityCharController;
        
        #endregion
        
        #region Network Variables (SyncVars)
        
        /// <summary>
        /// Synced position for remote player interpolation.
        /// Server Authoritative - owner reports, server writes.
        /// </summary>
        public SyncVar<Vector3> NetworkPosition = new SyncVar<Vector3>(default);
        
        /// <summary>
        /// Synced rotation for remote player interpolation.
        /// Server Authoritative - owner reports, server writes.
        /// </summary>
        public SyncVar<Quaternion> NetworkRotation = new SyncVar<Quaternion>(Quaternion.identity);
        
        /// <summary>
        /// Server-synced position for reconciliation (client prediction correction).
        /// Server Authoritative.
        /// </summary>
        private SyncVar<Vector3> _serverPosition = new SyncVar<Vector3>(default);
        
        /// <summary>
        /// Synced crouching state.
        /// Owner Authoritative (owner writes directly).
        /// </summary>
        public SyncVar<bool> IsCrouching = new SyncVar<bool>(false);
        
        /// <summary>
        /// Synced grounded state (for animations).
        /// Owner Authoritative.
        /// </summary>
        public SyncVar<bool> IsGrounded = new SyncVar<bool>(true);
        
        /// <summary>
        /// Synced velocity for prediction/animation.
        /// Owner Authoritative.
        /// </summary>
        public SyncVar<Vector3> Velocity = new SyncVar<Vector3>(default);
        
        #endregion
        
        #region Settings
        
        [Header("Network Settings")]
        [Tooltip("How often to sync state to server (per second)")]
        [SerializeField] private float _syncRate = 20f;
        
        [Tooltip("Position difference threshold for reconciliation")]
        [SerializeField] private float _reconciliationThreshold = 0.5f;
        
        [Header("Remote Player Interpolation")]
        [Tooltip("How fast remote players interpolate to network position")]
        [SerializeField] private float _interpolationSpeed = 15f;
        
        [Tooltip("Distance threshold to snap instead of interpolate")]
        [SerializeField] private float _snapThreshold = 5f;
        
        #endregion
        
        #region Private State
        
        private float _lastSyncTime;
        private float _syncInterval;
        
        // Local cache to handle "Previous vs Current" logic since PurrNet SyncVar 
        // onChanged only gives the new value.
        private bool _prevCrouching;
        private bool _prevGrounded;
        
        private bool _isInitialized = false;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _networkedPlayer = GetComponent<NetworkedPlayerController>();
            _unityCharController = GetComponent<CharacterController>();
            
            // CharacterController might be optional for remote players
            _characterController = GetComponent<PlayerCharacterController>();
            
            _syncInterval = 1f / _syncRate;
        }
        
        private void Update()
        {
            if (!_isInitialized) return;
            
            if (isOwner)
            {
                UpdateLocalPlayer();
            }
            else
            {
                UpdateRemotePlayer();
            }
        }
        
        #endregion
        
        #region Network Lifecycle
        
        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);
            
            Debug.Log($"[NetworkedMovementAdapter] OnSpawned - asServer: {asServer}, isServer: {isServer}, isOwner: {isOwner}");
            
            // FIX: On host, OnSpawned fires twice (asServer=true, then asServer=false)
            // We only want to initialize once. Skip the client-side call on host.
            if (!asServer && isServer)
            {
                Debug.Log("[NetworkedMovementAdapter] Skipping host's client-side OnSpawned call");
                return;
            }
            
            // Prevent double initialization
            if (_isInitialized)
            {
                Debug.LogWarning("[NetworkedMovementAdapter] Already initialized, skipping");
                return;
            }
            
            // Initialize previous states to match current SyncVar values to avoid startup spikes
            _prevCrouching = IsCrouching.value;
            _prevGrounded = IsGrounded.value;

            // Subscribe to state changes (only once)
            IsCrouching.onChanged += OnCrouchingChanged;
            IsGrounded.onChanged += OnGroundedChanged;
            
            // Initialize network position to current position
            if (isOwner || isServer)
            {
                NetworkPosition.value = transform.position;
                NetworkRotation.value = transform.rotation;
                _serverPosition.value = transform.position;
            }
            
            _isInitialized = true;
            Debug.Log($"[NetworkedMovementAdapter] Initialized - Owner: {isOwner}");
        }
        
        protected override void OnDespawned()
        {
            if (_isInitialized)
            {
                IsCrouching.onChanged -= OnCrouchingChanged;
                IsGrounded.onChanged -= OnGroundedChanged;
            }
            _isInitialized = false;
            
            base.OnDespawned();
        }
        
        #endregion
        
        #region Local Player Update
        
        private void UpdateLocalPlayer()
        {
            // Sync state at regular intervals
            if (Time.time - _lastSyncTime >= _syncInterval)
            {
                SyncState();
                _lastSyncTime = Time.time;
            }
            
            // Server reconciliation check
            // If we are a client (not the host/server), check against server position
            if (isClient && !isServer)
            {
                CheckReconciliation();
            }
        }
        
        private void SyncState()
        {
            // Update owner-authoritative network variables (owner writes directly)
            // Check if character controller exists (might be null for simple cylinder test)
            if (_characterController != null)
            {
                IsCrouching.value = _characterController.IsCrouching;
                IsGrounded.value = _characterController.IsGrounded;
                Velocity.value = _characterController.CharacterVelocity;
            }
            else
            {
                // Fallback for simple test objects without full character controller
                IsGrounded.value = true;
                Velocity.value = Vector3.zero;
            }
            
            // Report position to server for validation and remote sync
            if (!isServer)
            {
                // Client -> Server
                ReportPositionServerRpc(transform.position, transform.rotation);
            }
            else
            {
                // Host: update directly (we are the server)
                _serverPosition.value = transform.position;
                NetworkPosition.value = transform.position;
                NetworkRotation.value = transform.rotation;
            }
        }
        
        private void CheckReconciliation()
        {
            // If server position is significantly different, reconcile
            float distance = Vector3.Distance(transform.position, _serverPosition.value);
            if (distance > _reconciliationThreshold)
            {
                Debug.LogWarning($"[NetworkedMovementAdapter] Reconciliation needed - Distance: {distance}");
                
                // Snap to server position
                if (_unityCharController != null)
                {
                    _unityCharController.enabled = false;
                    transform.position = _serverPosition.value;
                    _unityCharController.enabled = true;
                }
                else
                {
                    transform.position = _serverPosition.value;
                }
            }
        }
        
        [ServerRpc]
        private void ReportPositionServerRpc(Vector3 clientPosition, Quaternion clientRotation)
        {
            // Server validates and stores the authoritative position
            // In a strict server-auth system, you would validate the move here.
            // Since this is client-auth movement with reconciliation, we accept it.
            
            // TODO: Add basic anti-cheat validation here if needed:
            // - Check distance moved since last update
            // - Verify player isn't moving through walls
            // - Cap maximum velocity
            
            // These SyncVars are NOT ownerAuth, so only the Server can write to them.
            _serverPosition.value = clientPosition;
            NetworkPosition.value = clientPosition;
            NetworkRotation.value = clientRotation;
        }
        
        #endregion
        
        #region Remote Player Update
        
        private void UpdateRemotePlayer()
        {
            // Interpolate toward network position for smooth movement
            Vector3 targetPos = NetworkPosition.value;
            Quaternion targetRot = NetworkRotation.value;
            
            // Skip if target is default/uninitialized
            if (targetPos == Vector3.zero && NetworkPosition.value == default)
            {
                return;
            }
            
            // If too far away, snap instead of interpolate (teleport, spawn, etc.)
            float distance = Vector3.Distance(transform.position, targetPos);
            if (distance > _snapThreshold)
            {
                // Disable CharacterController to allow direct position set
                if (_unityCharController != null) _unityCharController.enabled = false;
                
                transform.position = targetPos;
                transform.rotation = targetRot;
                
                if (_unityCharController != null) _unityCharController.enabled = true;
                
                Debug.Log($"[NetworkedMovementAdapter] Remote player snapped - Distance was {distance}");
            }
            else if (distance > 0.01f) // Only interpolate if there's meaningful difference
            {
                // Smooth interpolation
                // Note: CharacterController is disabled for remote players, so we can set position directly
                transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * _interpolationSpeed);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * _interpolationSpeed);
            }
        }
        
        #endregion
        
        #region State Change Handlers
        
        private void OnCrouchingChanged(bool current)
        {
            // Calculate 'previous' state manually
            bool previous = _prevCrouching;
            _prevCrouching = current;

            // Trigger logic if changed
            if (previous != current)
            {
                Debug.Log($"[NetworkedMovementAdapter] Player {owner?.id} crouch state: {current}");
                // TODO: Trigger crouch animation for remote players
            }
        }
        
        private void OnGroundedChanged(bool current)
        {
            bool previous = _prevGrounded;
            _prevGrounded = current;

            // Trigger landing effects
            if (!previous && current)
            {
                // Just landed
                Debug.Log($"[NetworkedMovementAdapter] Player {owner?.id} landed");
                // TODO: Trigger landing animation/sound for remote players
            }
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Force sync state immediately
        /// </summary>
        public void ForceSyncState()
        {
            if (isOwner)
            {
                SyncState();
            }
        }
        
        /// <summary>
        /// Get the velocity (works for local and remote players)
        /// </summary>
        public Vector3 GetVelocity()
        {
            if (isOwner && _characterController != null)
            {
                return _characterController.CharacterVelocity;
            }
            return Velocity.value;
        }
        
        /// <summary>
        /// Check if player is grounded (works for local and remote)
        /// </summary>
        public bool GetIsGrounded()
        {
            if (isOwner && _characterController != null)
            {
                return _characterController.IsGrounded;
            }
            return IsGrounded.value;
        }
        
        /// <summary>
        /// Get network-synced position (useful for remote player logic)
        /// </summary>
        public Vector3 GetNetworkPosition()
        {
            return NetworkPosition.value;
        }
        
        /// <summary>
        /// Get network-synced rotation (useful for remote player logic)
        /// </summary>
        public Quaternion GetNetworkRotation()
        {
            return NetworkRotation.value;
        }
        
        #endregion
    }
}