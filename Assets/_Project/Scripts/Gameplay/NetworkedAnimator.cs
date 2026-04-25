using PurrNet;
using Taiyun.SuckTheWater.Game;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Drives the third-person mech Animator from state already synchronized by
    /// NetworkedMovementAdapter. This adds no extra network traffic.
    /// </summary>
    [RequireComponent(typeof(NetworkedPlayerController))]
    [RequireComponent(typeof(NetworkedMovementAdapter))]
    public class NetworkedAnimator : NetworkBehaviour
    {
        private const float MinimumSpeed = 0.01f;

        #region Serialized Fields

        [Header("Animator Reference")]
        [Tooltip("Animator on the third-person mech model. Auto-found in children if null.")]
        [SerializeField] private Animator _animator;

        [Header("Parameter Names")]
        [SerializeField] private string _moveXParam = "MoveX";
        [SerializeField] private string _moveZParam = "MoveZ";
        [SerializeField] private string _speedParam = "Speed";
        [SerializeField] private string _isGroundedParam = "IsGrounded";
        [SerializeField] private string _isCrouchingParam = "IsCrouching";

        [Header("Trigger Parameter Names")]
        [SerializeField] private string _jumpTriggerParam = "JumpTrigger";

        [Header("Firing Parameter Names")]
        [SerializeField] private string _isFiringParam = "IsFiring";

        [Header("Hit/Death Parameter Names")]
        [SerializeField] private string _hitTriggerParam = "HitTrigger";
        [SerializeField] private string _isDeadParam = "IsDead";

        [Header("Hit Reaction Tuning")]
        [Tooltip("Damage below this threshold does not play the hit reaction.")]
        [SerializeField] private float _hitReactionMinDamage = 5f;

        [Header("Tuning")]
        [Tooltip("Character's normal grounded movement speed in world units/sec.")]
        [SerializeField] private float _walkSpeed = 10f;

        [Tooltip("Character's max sprint/run speed in world units/sec.")]
        [SerializeField] private float _runSpeed = 20f;

        [Tooltip("Character's max crouch-walk speed in world units/sec.")]
        [SerializeField] private float _crouchWalkSpeed = 5f;

        [Tooltip("Damp time for locomotion floats, in seconds. Smooths sync-rate jitter.")]
        [SerializeField] private float _locomotionDamp = 0.1f;

        #endregion

        #region Private State

        private NetworkedMovementAdapter _adapter;
        private int _hashMoveX;
        private int _hashMoveZ;
        private int _hashSpeed;
        private int _hashIsGrounded;
        private int _hashIsCrouching;
        private int _hashJumpTrigger;
        private int _hashIsFiring;
        private int _hashHitTrigger;
        private int _hashIsDead;
        private int _hashJumpMidState;
        private PlayerCharacterController _ownerController;
        private NetworkedWeaponDriver _weaponDriver;
        private NetworkedPlayerController _networkedPlayer;
        private Health _health;
        private bool _ready;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _adapter = GetComponent<NetworkedMovementAdapter>();

            if (_animator == null)
            {
                _animator = FindBestAnimator();
            }
        }

        private void Update()
        {
            if (!_ready || _animator == null)
            {
                return;
            }

            // Owner third-person visuals are inactive, so there is no useful Animator work to do.
            if (!_animator.gameObject.activeInHierarchy)
            {
                return;
            }

            if (_animator.GetBool(_hashIsDead))
            {
                return;
            }

            UpdateLocomotionParameters();
            UpdateStateBools();
        }

        private void OnValidate()
        {
            _runSpeed = Mathf.Max(_runSpeed, MinimumSpeed);
            _walkSpeed = Mathf.Clamp(_walkSpeed, MinimumSpeed, _runSpeed);
            _crouchWalkSpeed = Mathf.Max(_crouchWalkSpeed, MinimumSpeed);
            _locomotionDamp = Mathf.Max(0f, _locomotionDamp);
            _hitReactionMinDamage = Mathf.Max(0f, _hitReactionMinDamage);
        }

        #endregion

        #region Network Lifecycle

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);

            // Match NetworkedMovementAdapter's host double-fire guard.
            if (!asServer && isServer)
            {
                Debug.Log("[NetworkedAnimator] Skipping host's client-side OnSpawned call");
                return;
            }

            if (_ready)
            {
                Debug.LogWarning("[NetworkedAnimator] Already initialized, skipping");
                return;
            }

            if (_animator == null)
            {
                _animator = FindBestAnimator();
            }

            if (_animator == null)
            {
                Debug.LogError($"[NetworkedAnimator] No Animator found on third-person model for player {owner?.id}.");
                return;
            }

            if (_animator.runtimeAnimatorController == null)
            {
                Debug.LogError($"[NetworkedAnimator] Animator has no controller assigned for player {owner?.id}.");
                return;
            }

            _hashMoveX = Animator.StringToHash(_moveXParam);
            _hashMoveZ = Animator.StringToHash(_moveZParam);
            _hashSpeed = Animator.StringToHash(_speedParam);
            _hashIsGrounded = Animator.StringToHash(_isGroundedParam);
            _hashIsCrouching = Animator.StringToHash(_isCrouchingParam);
            _hashJumpTrigger = Animator.StringToHash(_jumpTriggerParam);
            _hashIsFiring = Animator.StringToHash(_isFiringParam);
            _hashHitTrigger = Animator.StringToHash(_hitTriggerParam);
            _hashIsDead = Animator.StringToHash(_isDeadParam);
            _hashJumpMidState = Animator.StringToHash("Jump_Mid");

            _ready = true;

            _networkedPlayer = GetComponent<NetworkedPlayerController>();

            if (isOwner)
            {
                _ownerController = _networkedPlayer != null ? _networkedPlayer.CharacterController : null;

                if (_ownerController != null)
                {
                    _ownerController.OnJumpStarted += HandleOwnerJumped;
                }
                else
                {
                    Debug.LogWarning("[NetworkedAnimator] Owner has no PlayerCharacterController; jump replication disabled");
                }
            }
            else if (!_adapter.GetIsGrounded())
            {
                _animator.Play(_hashJumpMidState, 0, 0f);
            }

            if (!isOwner)
            {
                _weaponDriver = GetComponent<NetworkedWeaponDriver>();
                if (_weaponDriver != null)
                {
                    _weaponDriver.OnObserverUseReplicatedEvent += HandleObservedWeaponUse;
                }
                else
                {
                    Debug.LogWarning("[NetworkedAnimator] No NetworkedWeaponDriver; third-person firing animation disabled");
                }

                if (_networkedPlayer != null)
                {
                    _networkedPlayer.OnDamageReplicatedEvent += HandleDamageReplicated;
                }
            }

            _health = GetComponent<Health>();
            if (_health != null)
            {
                _health.OnDie += HandleDeath;
            }
            else
            {
                Debug.LogWarning("[NetworkedAnimator] No Health component; death animation disabled");
            }

            Debug.Log($"[NetworkedAnimator] Initialized for player {owner?.id}, isOwner: {isOwner}");
        }

        protected override void OnDespawned()
        {
            if (_weaponDriver != null)
            {
                _weaponDriver.OnObserverUseReplicatedEvent -= HandleObservedWeaponUse;
                _weaponDriver = null;
            }

            if (_ownerController != null)
            {
                _ownerController.OnJumpStarted -= HandleOwnerJumped;
                _ownerController = null;
            }

            if (_networkedPlayer != null)
            {
                _networkedPlayer.OnDamageReplicatedEvent -= HandleDamageReplicated;
                _networkedPlayer = null;
            }

            if (_health != null)
            {
                _health.OnDie -= HandleDeath;
                _health = null;
            }

            _ready = false;
            base.OnDespawned();
        }

        #endregion

        #region Parameter Driving

        private void UpdateLocomotionParameters()
        {
            Vector3 worldVelocity = _adapter.GetVelocity();
            Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);

            bool isCrouching = _adapter.IsCrouching.value;
            float directionSpeed = isCrouching ? _crouchWalkSpeed : _walkSpeed;
            directionSpeed = Mathf.Max(directionSpeed, MinimumSpeed);

            float moveX = Mathf.Clamp(localVelocity.x / directionSpeed, -1f, 1f);
            float moveZ = Mathf.Clamp(localVelocity.z / directionSpeed, -1f, 1f);

            float horizontalSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
            float speed = GetNormalizedSpeed(horizontalSpeed, isCrouching);

            _animator.SetFloat(_hashMoveX, moveX, _locomotionDamp, Time.deltaTime);
            _animator.SetFloat(_hashMoveZ, moveZ, _locomotionDamp, Time.deltaTime);
            _animator.SetFloat(_hashSpeed, speed, _locomotionDamp, Time.deltaTime);
        }

        private void UpdateStateBools()
        {
            _animator.SetBool(_hashIsGrounded, _adapter.GetIsGrounded());
            _animator.SetBool(_hashIsCrouching, _adapter.IsCrouching.value);
        }

        private float GetNormalizedSpeed(float horizontalSpeed, bool isCrouching)
        {
            if (isCrouching)
            {
                return Mathf.Clamp01(horizontalSpeed / Mathf.Max(_crouchWalkSpeed, MinimumSpeed));
            }

            float walkSpeed = Mathf.Max(_walkSpeed, MinimumSpeed);
            if (horizontalSpeed <= walkSpeed)
            {
                return Mathf.Clamp01(horizontalSpeed / walkSpeed) * 0.5f;
            }

            float runRange = Mathf.Max(_runSpeed - walkSpeed, MinimumSpeed);
            float runBlend = Mathf.Clamp01((horizontalSpeed - walkSpeed) / runRange);
            return 0.5f + runBlend * 0.5f;
        }

        private Animator FindBestAnimator()
        {
            Animator[] animators = GetComponentsInChildren<Animator>(true);

            if (animators.Length == 0)
            {
                return null;
            }

            foreach (Animator animator in animators)
            {
                if (animator.enabled && animator.runtimeAnimatorController != null)
                {
                    return animator;
                }
            }

            foreach (Animator animator in animators)
            {
                if (animator.runtimeAnimatorController != null)
                {
                    return animator;
                }
            }

            foreach (Animator animator in animators)
            {
                if (animator.enabled)
                {
                    return animator;
                }
            }

            return animators[0];
        }

        #endregion

        #region Discrete Trigger Replication

        /// <summary>
        /// Owner-only. Invoked when the local character controller reports a jump.
        /// </summary>
        private void HandleOwnerJumped()
        {
            if (!_ready)
            {
                return;
            }

            RequestJumpServerRpc();
        }

        [ServerRpc]
        private void RequestJumpServerRpc()
        {
            // TODO future: validate jump cooldown / anti-cheat server-side.
            PlayJumpObserversRpc();
        }

        [ObserversRpc]
        private void PlayJumpObserversRpc()
        {
            if (isOwner)
            {
                return;
            }

            PlayAnimationTrigger(_hashJumpTrigger);
        }

        /// <summary>
        /// Generic trigger helper for future discrete animations.
        /// </summary>
        private void PlayAnimationTrigger(int hash)
        {
            if (!_ready || _animator == null || !_animator.gameObject.activeInHierarchy)
            {
                return;
            }

            _animator.SetTrigger(hash);
        }

        #endregion

        #region Continuous State Replication

        /// <summary>
        /// Observer-only weapon use replication drives the third-person upper-body firing pose.
        /// </summary>
        private void HandleObservedWeaponUse(NetworkedWeaponDriver.WeaponUseIntentPayload payload)
        {
            if (!_ready || _animator == null || !_animator.gameObject.activeInHierarchy)
            {
                return;
            }

            if (payload.useKind != NetworkedWeaponDriver.WeaponUseKind.Primary)
            {
                return;
            }

            switch (payload.usePhase)
            {
                case NetworkedWeaponDriver.WeaponUsePhase.Begin:
                    _animator.SetBool(_hashIsFiring, true);
                    break;
                case NetworkedWeaponDriver.WeaponUsePhase.End:
                    _animator.SetBool(_hashIsFiring, false);
                    break;
            }
        }

        #endregion

        #region Damage & Death Replication

        /// <summary>
        /// Observer-side non-fatal damage plays a brief upper-body flinch.
        /// Fatal damage is handled by Health.OnDie so direct kill paths work too.
        /// </summary>
        private void HandleDamageReplicated(float appliedDamage, bool killed)
        {
            if (!_ready || _animator == null || !_animator.gameObject.activeInHierarchy)
            {
                return;
            }

            if (killed || appliedDamage < _hitReactionMinDamage)
            {
                return;
            }

            PlayAnimationTrigger(_hashHitTrigger);
        }

        /// <summary>
        /// Local Health reports death once; hold the terminal Die state and stop firing.
        /// </summary>
        private void HandleDeath()
        {
            if (!_ready || _animator == null)
            {
                return;
            }

            _animator.SetBool(_hashIsDead, true);
            _animator.SetBool(_hashIsFiring, false);
            Debug.Log($"[NetworkedAnimator] Player {owner?.id} died; animation set to Die state");
        }

        #endregion

        #region Debug API

        /// <summary>
        /// Reads current Animator parameter values for future debug tooling.
        /// </summary>
        public void GetDebugSnapshot(out float moveX, out float moveZ, out float speed, out bool isGrounded, out bool isCrouching)
        {
            if (!_ready || _animator == null)
            {
                moveX = 0f;
                moveZ = 0f;
                speed = 0f;
                isGrounded = false;
                isCrouching = false;
                return;
            }

            moveX = _animator.GetFloat(_hashMoveX);
            moveZ = _animator.GetFloat(_hashMoveZ);
            speed = _animator.GetFloat(_hashSpeed);
            isGrounded = _animator.GetBool(_hashIsGrounded);
            isCrouching = _animator.GetBool(_hashIsCrouching);
        }

        #endregion
    }
}
