using UnityEngine;
using UnityEngine.Events;
using PurrNet;
using Taiyun.SuckTheWater.Game;

namespace Taiyun.SuckTheWater.Gameplay
{
    public class NetworkedPlayerController : NetworkBehaviour
    {
        #region Serialized Fields
        
        [Header("Player Components")]
        [Tooltip("The original PlayerCharacterController - disabled for remote players")]
        [SerializeField] private PlayerCharacterController _characterController;
        
        [Tooltip("Input handler - disabled for remote players")]
        [SerializeField] private PlayerInputHandler _inputHandler;

        [Tooltip("Interaction detector - disabled for remote players")]
        [SerializeField] private PlayerInteractionDetector _interactionDetector;
        
        [Tooltip("Weapons manager - disabled for remote players")]
        [SerializeField] private PlayerWeaponsManager _weaponsManager;
        
        [Header("Visual Components")]
        [Tooltip("Player camera - disabled for remote players")]
        [SerializeField] private Camera _playerCamera;
        
        [Tooltip("Weapon camera (if separate) - disabled for remote players")]
        [SerializeField] private Camera _weaponCamera;
        
        [Tooltip("Audio listener - disabled for remote players")]
        [SerializeField] private AudioListener _audioListener;
        
        [Tooltip("Audio source for player sounds")]
        [SerializeField] private AudioSource _audioSource;
        
        [Header("Remote Player Visual")]
        [Tooltip("The visual mesh/model shown to other players")]
        [SerializeField] private GameObject _thirdPersonModel;
        
        [Tooltip("First person arms/weapon visuals - hidden for remote")]
        [SerializeField] private GameObject _firstPersonVisuals;
        
        [Header("Network Settings")]
        [Tooltip("Components to completely disable on remote players")]
        [SerializeField] private MonoBehaviour[] _localOnlyComponents;
        
        [Tooltip("GameObjects to disable on remote players")]
        [SerializeField] private GameObject[] _localOnlyObjects;
        
        #endregion
        
        #region Public Properties
        
        public PlayerCharacterController CharacterController => _characterController;
        public PlayerWeaponsManager WeaponsManager => _weaponsManager;
        
        /// <summary>
        /// Player's network client ID. In PurrNet, this is player.id
        /// </summary>
        public ulong PlayerClientId => owner?.id ?? 0;
        
        /// <summary>
        /// Player display name (synced from lobby).
        /// Using PurrNet's SyncVar.
        /// </summary>
        public SyncVar<string> PlayerName = new SyncVar<string>("Player");
        
        public bool IsRegisteredWithGameSystems { get; private set; }
        
        #endregion
        
        #region Events
        
        public UnityAction OnLocalPlayerReady;

        /// <summary>
        /// Presentation-side notification fired when authoritative damage is mirrored to observers.
        /// Invoked before the host mutation guard so the host can animate damage on remote players.
        /// </summary>
        public UnityAction<float, bool> OnDamageReplicatedEvent;

        public static UnityAction<NetworkedPlayerController> OnPlayerSpawned;
        public static UnityAction<NetworkedPlayerController> OnPlayerDespawned;
        
        #endregion
        
        #region Private Fields
        
        private Health _health;
        private Actor _actor;
        private CharacterController _unityCharController;
        private bool _isInitialized;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _health = GetComponent<Health>();
            _actor = GetComponent<Actor>();
            _unityCharController = GetComponent<CharacterController>();
            
            if (_characterController == null) _characterController = GetComponent<PlayerCharacterController>();
            if (_inputHandler == null) _inputHandler = GetComponent<PlayerInputHandler>();
            if (_interactionDetector == null) _interactionDetector = GetComponent<PlayerInteractionDetector>();
            if (_weaponsManager == null) _weaponsManager = GetComponent<PlayerWeaponsManager>();
            if (_playerCamera == null) _playerCamera = GetComponentInChildren<Camera>();
            if (_audioListener == null) _audioListener = GetComponentInChildren<AudioListener>();
            if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
        }
        
        #endregion
        
        #region Network Lifecycle
        
        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);
            
            Debug.Log($"[NetworkedPlayerController] OnSpawned - asServer: {asServer}, isServer: {isServer}, isOwner: {isOwner}, ID: {PlayerClientId}");
            
            // FIX: On host, OnSpawned fires twice (asServer=true, then asServer=false)
            // We only want to initialize once. Skip the client-side call on host.
            if (!asServer && isServer)
            {
                Debug.Log("[NetworkedPlayerController] Skipping host's client-side OnSpawned call");
                return;
            }
            
            // Prevent double initialization
            if (_isInitialized)
            {
                Debug.LogWarning("[NetworkedPlayerController] Already initialized, skipping");
                return;
            }
            
            if (isOwner)
            {
                InitializeAsLocalPlayer();
            }
            else
            {
                InitializeAsRemotePlayer();
            }
            
            TryRegisterWithGameSystems();
            OnPlayerSpawned?.Invoke(this);
            _isInitialized = true;
        }

        protected override void OnDespawned()
        {
            Debug.Log($"[NetworkedPlayerController] OnDespawned - ID: {PlayerClientId}");
            UnregisterFromGameSystems();
            OnPlayerDespawned?.Invoke(this);
            _isInitialized = false;
            base.OnDespawned();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeAsLocalPlayer()
        {
            Debug.Log("[NetworkedPlayerController] Initializing as LOCAL player");
            
            SetComponentEnabled(_characterController, true);
            SetComponentEnabled(_inputHandler, true);
            SetComponentEnabled(_interactionDetector, true);
            SetComponentEnabled(_weaponsManager, true);
            
            if (_playerCamera != null)
            {
                _playerCamera.enabled = true;
                _playerCamera.tag = "MainCamera";
            }
            
            if (_weaponCamera != null) _weaponCamera.enabled = true;
            if (_audioListener != null) _audioListener.enabled = true;
            
            if (_firstPersonVisuals != null) _firstPersonVisuals.SetActive(true);
            if (_thirdPersonModel != null) _thirdPersonModel.SetActive(false);
            
            if (_localOnlyComponents != null)
            {
                foreach (var component in _localOnlyComponents)
                {
                    if (component != null) component.enabled = true;
                }
            }
            
            if (_localOnlyObjects != null)
            {
                foreach (var obj in _localOnlyObjects)
                {
                    if (obj != null) obj.SetActive(true);
                }
            }
            
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            OnLocalPlayerReady?.Invoke();
        }
        
        private void InitializeAsRemotePlayer()
        {
            Debug.Log($"[NetworkedPlayerController] Initializing as REMOTE player (ID: {PlayerClientId})");
            
            SetComponentEnabled(_characterController, false);
            SetComponentEnabled(_inputHandler, false);
            SetComponentEnabled(_interactionDetector, false);
            SetComponentEnabled(_weaponsManager, false);
            
            if (_playerCamera != null)
            {
                _playerCamera.enabled = false;
                _playerCamera.tag = "Untagged";
            }
            
            if (_weaponCamera != null) _weaponCamera.enabled = false;
            if (_audioListener != null) _audioListener.enabled = false;
            
            if (_firstPersonVisuals != null) _firstPersonVisuals.SetActive(false);
            if (_thirdPersonModel != null) _thirdPersonModel.SetActive(true);
            
            if (_localOnlyComponents != null)
            {
                foreach (var component in _localOnlyComponents)
                {
                    if (component != null) component.enabled = false;
                }
            }
            
            if (_localOnlyObjects != null)
            {
                foreach (var obj in _localOnlyObjects)
                {
                    if (obj != null) obj.SetActive(false);
                }
            }
        }
        
        private void SetComponentEnabled(MonoBehaviour component, bool isEnabled)
        {
            if (component != null) component.enabled = isEnabled;
        }
        
        #endregion
        
        #region Game System Registration
        
        private void TryRegisterWithGameSystems()
        {
            var actorsManager = FindFirstObjectByType<ActorsManager>();
            
            if (actorsManager == null)
            {
                Debug.Log("[NetworkedPlayerController] ActorsManager not found.");
                IsRegisteredWithGameSystems = false;
                return;
            }
            
            RegisterWithGameSystems(actorsManager);
        }
        
        public void RegisterWithGameSystems(ActorsManager actorsManager)
        {
            if (actorsManager == null) return;
            if (IsRegisteredWithGameSystems) return;
            
            if (isOwner)
            {
                actorsManager.SetPlayer(gameObject);
                Debug.Log("[NetworkedPlayerController] Registered as local player");
            }
            
            if (_actor != null && !actorsManager.Actors.Contains(_actor))
            {
                actorsManager.Actors.Add(_actor);
                Debug.Log($"[NetworkedPlayerController] Actor registered");
            }
            
            IsRegisteredWithGameSystems = true;
        }
        
        private void UnregisterFromGameSystems()
        {
            if (!IsRegisteredWithGameSystems) return;
            
            var actorsManager = FindFirstObjectByType<ActorsManager>();
            if (actorsManager != null && _actor != null)
            {
                actorsManager.Actors.Remove(_actor);
            }
            
            IsRegisteredWithGameSystems = false;
        }
        
        #endregion
        
        #region Network Synchronization
        
        [ServerRpc]
        public void SetPlayerNameServerRpc(string playerName)
        {
            PlayerName.value = playerName;
        }
        
        [ServerRpc]
        public void RequestDamageServerRpc(float damage, PlayerID attackerId)
        {
            _ = attackerId;
            TryApplyAuthoritativeDamage(damage, null, out _, out _);
        }
        
        #endregion
        
        #region Public API
        
        [ServerRpc]
        public void TeleportServerRpc(Vector3 position, Quaternion rotation)
        {
            TeleportObserversRpc(position, rotation);
        }
        
        [ObserversRpc]
        private void TeleportObserversRpc(Vector3 position, Quaternion rotation)
        {
            if (_unityCharController != null) _unityCharController.enabled = false;
            
            transform.position = position;
            transform.rotation = rotation;
            
            if (_unityCharController != null) _unityCharController.enabled = true;
            
            Debug.Log($"[NetworkedPlayerController] Teleported to {position}");
        }
        
        [ServerRpc]
        public void KillPlayerServerRpc()
        {
            if (_health == null || _health.CurrentHealth <= 0f)
            {
                return;
            }

            float healthBefore = _health.CurrentHealth;
            _health.Kill();

            if (healthBefore > 0f)
            {
                MirrorAuthoritativeDamageObserversRpc(healthBefore, true);
            }
        }

        /// <summary>
        /// Host-authoritative player damage entrypoint used by the unified damage resolver.
        /// Applies on server only, then mirrors the final applied amount to remote observers.
        /// </summary>
        public bool TryApplyAuthoritativeDamage(float damage, GameObject damageSource, out float appliedDamage,
            out bool killed)
        {
            appliedDamage = 0f;
            killed = false;

            if (!isServer || _health == null)
            {
                return false;
            }

            if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            {
                return false;
            }

            if (!gameObject.scene.IsValid())
            {
                return false;
            }

            if (_health.CurrentHealth <= 0f || (_characterController != null && _characterController.IsDead))
            {
                killed = true;
                return false;
            }

            float healthBefore = _health.CurrentHealth;
            _health.TakeDamage(damage, damageSource);

            appliedDamage = Mathf.Max(0f, healthBefore - _health.CurrentHealth);
            killed = _health.CurrentHealth <= 0f || (_characterController != null && _characterController.IsDead);

            if (appliedDamage <= 0f)
            {
                return false;
            }

            MirrorAuthoritativeDamageObserversRpc(appliedDamage, killed);
            return true;
        }

        [ObserversRpc]
        private void MirrorAuthoritativeDamageObserversRpc(float appliedDamage, bool killed)
        {
            OnDamageReplicatedEvent?.Invoke(appliedDamage, killed);

            // Host already applied the authoritative mutation.
            if (isServer)
            {
                return;
            }

            if (_health == null || appliedDamage <= 0f || _health.CurrentHealth <= 0f)
            {
                return;
            }

            _health.TakeDamage(appliedDamage, null);
        }
        
        #endregion
    }
}
