using PurrNet;
using System.Collections.Generic;
using Taiyun.SuckTheWater.Game;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Drives the visible mech Animator for either the local first-person overlay
    /// or the remote third-person presentation using already-synchronized gameplay state.
    /// </summary>
    [RequireComponent(typeof(NetworkedPlayerController))]
    [RequireComponent(typeof(NetworkedMovementAdapter))]
    public class NetworkedAnimator : NetworkBehaviour
    {
        private const float MinimumSpeed = 0.01f;

        #region Serialized Fields

        [Header("Animator References")]
        [Tooltip("Animator used for the remote third-person mech presentation.")]
        [SerializeField] private Animator _thirdPersonAnimator;

        [Tooltip("Animator used for the owner's local first-person mech overlay.")]
        [SerializeField] private Animator _firstPersonAnimator;

        [Tooltip("Animator used for the owner's local shadow-only body proxy.")]
        [SerializeField] private Animator _ownerShadowAnimator;

        [Tooltip("Additional owner-only animators driven by local presentation state.")]
        [SerializeField] private Animator[] _ownerPresentationAnimators;

        [Tooltip("Additional remote third-person animators driven by replicated presentation state.")]
        [SerializeField] private Animator[] _remotePresentationAnimators;

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
        private NetworkedPlayerController _networkedPlayer;
        private NetworkedWeaponDriver _weaponDriver;
        private Health _health;
        private PlayerCharacterController _ownerController;

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
        private bool _ready;
        private readonly List<Animator> _animatorVisitBuffer = new List<Animator>(4);

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _adapter = GetComponent<NetworkedMovementAdapter>();
            _networkedPlayer = GetComponent<NetworkedPlayerController>();
            CacheAnimatorReferences();
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            ForEachCurrentPresentationAnimator(animator =>
            {
                if (!IsAnimatorUsable(animator) || animator.GetBool(_hashIsDead))
                {
                    return;
                }

                UpdateLocomotionParameters(animator);
                UpdateStateBools(animator);
            });
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

            CacheAnimatorReferences();
            _adapter ??= GetComponent<NetworkedMovementAdapter>();
            _networkedPlayer ??= GetComponent<NetworkedPlayerController>();

            Animator currentAnimator = GetPrimaryPresentationAnimator();
            if (currentAnimator == null)
            {
                Debug.LogError($"[NetworkedAnimator] Missing {(isOwner ? "owner" : "remote")} presentation Animator for player {owner?.id}.");
                return;
            }

            if (currentAnimator.runtimeAnimatorController == null)
            {
                Debug.LogError($"[NetworkedAnimator] {(isOwner ? "First-person" : "Third-person")} Animator has no controller assigned for player {owner?.id}.");
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

            _weaponDriver = GetComponent<NetworkedWeaponDriver>();
            _health = GetComponent<Health>();

            if (isOwner)
            {
                _ownerController = _networkedPlayer != null ? _networkedPlayer.CharacterController : null;

                if (_ownerController != null)
                {
                    _ownerController.OnJumpStarted += HandleOwnerJumped;
                }
                else
                {
                    Debug.LogWarning("[NetworkedAnimator] Owner has no PlayerCharacterController; jump animation disabled");
                }

                if (_weaponDriver != null)
                {
                    _weaponDriver.OnLocalUseIntentRaised += HandleLocalWeaponUse;
                }
                else
                {
                    Debug.LogWarning("[NetworkedAnimator] Owner has no NetworkedWeaponDriver; first-person firing animation disabled");
                }
            }
            else
            {
                if (!_adapter.GetIsGrounded())
                {
                    ForEachCurrentPresentationAnimator(animator =>
                    {
                        if (IsAnimatorUsable(animator))
                        {
                            animator.Play(_hashJumpMidState, 0, 0f);
                        }
                    });
                }

                if (_weaponDriver != null)
                {
                    _weaponDriver.OnObserverUseReplicatedEvent += HandleObservedWeaponUse;
                }
                else
                {
                    Debug.LogWarning("[NetworkedAnimator] No NetworkedWeaponDriver; third-person firing animation disabled");
                }
            }

            if (_networkedPlayer != null)
            {
                _networkedPlayer.OnDamageReplicatedEvent += HandleDamageReplicated;
            }
            else
            {
                Debug.LogWarning("[NetworkedAnimator] No NetworkedPlayerController; damage reactions disabled");
            }

            if (_health != null)
            {
                _health.OnDie += HandleDeath;
            }
            else
            {
                Debug.LogWarning("[NetworkedAnimator] No Health component; death animation disabled");
            }

            _ready = true;
            Debug.Log($"[NetworkedAnimator] Initialized for player {owner?.id}, isOwner: {isOwner}");
        }

        protected override void OnDespawned()
        {
            if (_ownerController != null)
            {
                _ownerController.OnJumpStarted -= HandleOwnerJumped;
                _ownerController = null;
            }

            if (_weaponDriver != null)
            {
                _weaponDriver.OnLocalUseIntentRaised -= HandleLocalWeaponUse;
                _weaponDriver.OnObserverUseReplicatedEvent -= HandleObservedWeaponUse;
                _weaponDriver = null;
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

        private void UpdateLocomotionParameters(Animator animator)
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

            animator.SetFloat(_hashMoveX, moveX, _locomotionDamp, Time.deltaTime);
            animator.SetFloat(_hashMoveZ, moveZ, _locomotionDamp, Time.deltaTime);
            animator.SetFloat(_hashSpeed, speed, _locomotionDamp, Time.deltaTime);
        }

        private void UpdateStateBools(Animator animator)
        {
            animator.SetBool(_hashIsGrounded, _adapter.GetIsGrounded());
            animator.SetBool(_hashIsCrouching, _adapter.IsCrouching.value);
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

        private void CacheAnimatorReferences()
        {
            if (_thirdPersonAnimator == null)
            {
                _thirdPersonAnimator = FindAnimatorUnderNamedRoot("ThirdPersonVisualRoot");
            }

            if (_firstPersonAnimator == null)
            {
                _firstPersonAnimator = FindAnimatorUnderNamedRoot("FirstPersonVisualRoot");
            }

            if (_ownerShadowAnimator == null)
            {
                _ownerShadowAnimator = FindAnimatorUnderNamedRoot("OwnerShadowVisualRoot");
            }
        }

        private Animator FindAnimatorUnderNamedRoot(string rootName)
        {
            Transform root = FindTransformRecursive(transform, rootName);
            return root != null ? root.GetComponentInChildren<Animator>(true) : null;
        }

        private Transform FindTransformRecursive(Transform root, string targetName)
        {
            if (root.name == targetName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransformRecursive(root.GetChild(i), targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private Animator GetPrimaryPresentationAnimator()
        {
            if (isOwner)
            {
                Animator animator = GetFirstUsableAnimator(_ownerPresentationAnimators);
                return animator != null ? animator : (_firstPersonAnimator != null ? _firstPersonAnimator : _ownerShadowAnimator);
            }

            Animator remoteAnimator = GetFirstUsableAnimator(_remotePresentationAnimators);
            return remoteAnimator != null ? remoteAnimator : _thirdPersonAnimator;
        }

        private static Animator GetFirstUsableAnimator(Animator[] animators)
        {
            if (animators == null)
            {
                return null;
            }

            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null)
                {
                    return animators[i];
                }
            }

            return null;
        }

        private void ForEachCurrentPresentationAnimator(System.Action<Animator> action)
        {
            if (action == null)
            {
                return;
            }

            if (isOwner)
            {
                _animatorVisitBuffer.Clear();
                VisitAnimatorUnique(_firstPersonAnimator, action);
                VisitAnimatorUnique(_ownerShadowAnimator, action);
                VisitAnimatorArray(_ownerPresentationAnimators, action);
                return;
            }

            _animatorVisitBuffer.Clear();
            VisitAnimatorUnique(_thirdPersonAnimator, action);
            VisitAnimatorArray(_remotePresentationAnimators, action);
        }

        private void VisitAnimatorArray(Animator[] animators, System.Action<Animator> action)
        {
            if (animators == null)
            {
                return;
            }

            for (int i = 0; i < animators.Length; i++)
            {
                VisitAnimatorUnique(animators[i], action);
            }
        }

        private void VisitAnimatorUnique(Animator animator, System.Action<Animator> action)
        {
            if (animator == null)
            {
                return;
            }

            if (_animatorVisitBuffer.Contains(animator))
            {
                return;
            }

            _animatorVisitBuffer.Add(animator);
            action(animator);
        }

        private static bool IsAnimatorUsable(Animator animator)
        {
            return animator != null &&
                   animator.gameObject.activeInHierarchy &&
                   animator.runtimeAnimatorController != null;
        }

        #endregion

        #region Discrete Trigger Replication

        private void HandleOwnerJumped()
        {
            if (!_ready)
            {
                return;
            }

            PlayAnimationTrigger(_hashJumpTrigger);
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

        private void PlayAnimationTrigger(int hash)
        {
            if (!_ready)
            {
                return;
            }

            ForEachCurrentPresentationAnimator(animator =>
            {
                if (IsAnimatorUsable(animator))
                {
                    animator.SetTrigger(hash);
                }
            });
        }

        #endregion

        #region Continuous State Replication

        private void HandleLocalWeaponUse(NetworkedWeaponDriver.WeaponUseIntentPayload payload)
        {
            if (!isOwner)
            {
                return;
            }

            HandleWeaponUse(payload);
        }

        private void HandleObservedWeaponUse(NetworkedWeaponDriver.WeaponUseIntentPayload payload)
        {
            if (isOwner)
            {
                return;
            }

            HandleWeaponUse(payload);
        }

        private void HandleWeaponUse(NetworkedWeaponDriver.WeaponUseIntentPayload payload)
        {
            if (!_ready)
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
                    ForEachCurrentPresentationAnimator(animator =>
                    {
                        if (IsAnimatorUsable(animator))
                        {
                            animator.SetBool(_hashIsFiring, true);
                        }
                    });
                    break;
                case NetworkedWeaponDriver.WeaponUsePhase.End:
                    ForEachCurrentPresentationAnimator(animator =>
                    {
                        if (IsAnimatorUsable(animator))
                        {
                            animator.SetBool(_hashIsFiring, false);
                        }
                    });
                    break;
            }
        }

        #endregion

        #region Damage & Death Replication

        private void HandleDamageReplicated(float appliedDamage, bool killed)
        {
            if (!_ready)
            {
                return;
            }

            if (killed || appliedDamage < _hitReactionMinDamage)
            {
                return;
            }

            PlayAnimationTrigger(_hashHitTrigger);
        }

        private void HandleDeath()
        {
            if (!_ready)
            {
                return;
            }

            ForEachCurrentPresentationAnimator(animator =>
            {
                if (animator == null)
                {
                    return;
                }

                animator.SetBool(_hashIsDead, true);
                animator.SetBool(_hashIsFiring, false);
            });
            Debug.Log($"[NetworkedAnimator] Player {owner?.id} died; animation set to Die state");
        }

        #endregion

        #region Debug API

        public void GetDebugSnapshot(out float moveX, out float moveZ, out float speed, out bool isGrounded, out bool isCrouching)
        {
            Animator animator = GetPrimaryPresentationAnimator();
            if (!_ready || animator == null)
            {
                moveX = 0f;
                moveZ = 0f;
                speed = 0f;
                isGrounded = false;
                isCrouching = false;
                return;
            }

            moveX = animator.GetFloat(_hashMoveX);
            moveZ = animator.GetFloat(_hashMoveZ);
            speed = animator.GetFloat(_hashSpeed);
            isGrounded = animator.GetBool(_hashIsGrounded);
            isCrouching = animator.GetBool(_hashIsCrouching);
        }

        #endregion
    }
}
