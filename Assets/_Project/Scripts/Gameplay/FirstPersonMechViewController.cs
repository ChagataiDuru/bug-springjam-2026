using PurrNet;
using Taiyun.SuckTheWater.Game;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Keeps the local embodied mech presentation, camera, and weapon hand targets aligned.
    /// Gameplay and shot traces remain owned by the existing player camera / weapon camera flow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkedPlayerController))]
    public sealed class FirstPersonMechViewController : NetworkBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private Camera _weaponCamera;
        [SerializeField] private Transform _headCameraSocket;

        [Tooltip("Higher values snap the camera harder to the animated head socket.")]
        [SerializeField] private float _cameraPositionSharpness = 35f;

        [SerializeField] private bool _stabilizeCameraRoll = true;

        [Header("Weapon IK Targets")]
        [SerializeField] private PlayerWeaponsManager _weaponsManager;
        [SerializeField] private Transform _weaponParentSocket;
        [SerializeField] private Transform _leftHandTarget;
        [SerializeField] private Transform _rightHandTarget;
        [SerializeField] private Vector3 _fallbackLeftGripOffset = new Vector3(-0.28f, -0.08f, 0.42f);
        [SerializeField] private Vector3 _fallbackRightGripOffset = new Vector3(0.18f, -0.1f, 0.2f);

        private NetworkedPlayerController _networkedPlayer;
        private bool _ready;

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);

            if (!asServer && isServer)
            {
                return;
            }

            CacheReferences();
            _ready = true;
            enabled = isOwner;
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void LateUpdate()
        {
            if (!_ready || !isOwner)
            {
                return;
            }

            StabilizeCameraToHeadSocket();
            AlignWeaponCamera();
            UpdateHandTargets();
        }

        private void OnValidate()
        {
            _cameraPositionSharpness = Mathf.Max(0f, _cameraPositionSharpness);
        }

        private void CacheReferences()
        {
            if (_networkedPlayer == null)
            {
                _networkedPlayer = GetComponent<NetworkedPlayerController>();
            }

            if (_weaponsManager == null)
            {
                _weaponsManager = GetComponent<PlayerWeaponsManager>();
            }

            if (_playerCamera == null && _networkedPlayer != null && _networkedPlayer.CharacterController != null)
            {
                _playerCamera = _networkedPlayer.CharacterController.PlayerCamera;
            }

            if (_weaponCamera == null && _weaponsManager != null)
            {
                _weaponCamera = _weaponsManager.WeaponCamera;
            }

            if (_weaponParentSocket == null && _weaponsManager != null)
            {
                _weaponParentSocket = _weaponsManager.WeaponParentSocket;
            }

            _headCameraSocket ??= FindTransformRecursive(transform, "HeadCameraSocket");
            _leftHandTarget ??= FindTransformRecursive(transform, "LeftHandIKTarget");
            _rightHandTarget ??= FindTransformRecursive(transform, "RightHandIKTarget");
        }

        private static Transform FindTransformRecursive(Transform root, string targetName)
        {
            if (root == null)
            {
                return null;
            }

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

        private void StabilizeCameraToHeadSocket()
        {
            if (_playerCamera == null || _headCameraSocket == null)
            {
                return;
            }

            Transform cameraTransform = _playerCamera.transform;
            float t = _cameraPositionSharpness <= 0f
                ? 1f
                : 1f - Mathf.Exp(-_cameraPositionSharpness * Time.deltaTime);

            cameraTransform.position = Vector3.Lerp(cameraTransform.position, _headCameraSocket.position, t);

            if (!_stabilizeCameraRoll)
            {
                return;
            }

            Vector3 localEuler = cameraTransform.localEulerAngles;
            cameraTransform.localEulerAngles = new Vector3(localEuler.x, 0f, 0f);
        }

        private void AlignWeaponCamera()
        {
            if (_playerCamera == null || _weaponCamera == null)
            {
                return;
            }

            Transform weaponCameraTransform = _weaponCamera.transform;
            if (weaponCameraTransform.parent == _playerCamera.transform)
            {
                weaponCameraTransform.localPosition = Vector3.zero;
                weaponCameraTransform.localRotation = Quaternion.identity;
                return;
            }

            weaponCameraTransform.SetPositionAndRotation(
                _playerCamera.transform.position,
                _playerCamera.transform.rotation);
        }

        private void UpdateHandTargets()
        {
            if (_weaponParentSocket == null)
            {
                return;
            }

            WeaponController activeWeapon = _weaponsManager != null ? _weaponsManager.GetActiveWeapon() : null;
            FirstPersonWeaponGripProvider gripProvider = activeWeapon != null
                ? activeWeapon.GetComponentInChildren<FirstPersonWeaponGripProvider>(true)
                : null;

            ApplyGripTarget(_leftHandTarget, gripProvider != null ? gripProvider.LeftGrip : null, _fallbackLeftGripOffset);
            ApplyGripTarget(_rightHandTarget, gripProvider != null ? gripProvider.RightGrip : null, _fallbackRightGripOffset);
        }

        private void ApplyGripTarget(Transform target, Transform grip, Vector3 fallbackOffset)
        {
            if (target == null)
            {
                return;
            }

            if (grip != null)
            {
                target.SetPositionAndRotation(grip.position, grip.rotation);
                return;
            }

            target.SetPositionAndRotation(
                _weaponParentSocket.TransformPoint(fallbackOffset),
                _weaponParentSocket.rotation);
        }
    }
}
