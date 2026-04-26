using System.Collections;
using PurrNet;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// Handles the visual "catch" pose: snaps the Upper player to a socket on the Lower
    /// player so it reads as a held catch. Plays a camera shake for juice.
    /// Animation Rigging IK targets (CatchGripLeft/Right) are exposed for future polish.
    /// </summary>
    [RequireComponent(typeof(PlayerRoleSync))]
    public class CatchPoseController : NetworkBehaviour
    {
        [Header("Catch Socket (used when this player is the LOWER catcher)")]
        [Tooltip("Where the Upper player will be parented when caught. Place at chest height, slightly in front of the lower player.")]
        [SerializeField] private Transform _heldUpperSocket;

        [Header("IK Grip Points (used when this player is the LOWER catcher) — optional, for future Animation Rigging")]
        [Tooltip("Where the Lower player's left hand should reach toward the Upper player.")]
        [SerializeField] private Transform _catchGripLeft;
        [Tooltip("Where the Lower player's right hand should reach toward the Upper player.")]
        [SerializeField] private Transform _catchGripRight;

        [Header("Camera Shake (Upper, when caught)")]
        [SerializeField] private float _upperShakeDuration = 0.6f;
        [SerializeField] private float _upperShakeAmplitude = 0.25f;
        [SerializeField] private float _upperShakeFrequency = 28f;

        [Header("Camera Shake (Lower, on impact)")]
        [SerializeField] private float _lowerShakeDuration = 0.35f;
        [SerializeField] private float _lowerShakeAmplitude = 0.12f;
        [SerializeField] private float _lowerShakeFrequency = 22f;

        [Header("Refs")]
        [Tooltip("Optional. Falls back to the player camera resolved from NetworkedPlayerController.")]
        [SerializeField] private Camera _localCamera;

        private PlayerRoleSync _roleSync;
        private Gameplay.NetworkedPlayerController _player;

        // Original transform parent / local pose, captured before catch so we can restore on level reset
        private Transform _originalParent;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private bool _heldByOther;

        public Transform HeldUpperSocket => _heldUpperSocket;
        public Transform CatchGripLeft => _catchGripLeft;
        public Transform CatchGripRight => _catchGripRight;

        private void Awake()
        {
            _roleSync = GetComponent<PlayerRoleSync>();
            _player = GetComponent<Taiyun.SuckTheWater.Gameplay.NetworkedPlayerController>();

            if (_localCamera == null && _player != null && _player.CharacterController != null)
                _localCamera = _player.CharacterController.PlayerCamera;
        }

        /// <summary>
        /// Server-only entry point. Call when LevelManager fires a successful catch.
        /// </summary>
        public void ServerApplyCatchPose(CatchPoseController lowerCatcher)
        {
            if (!isServer || lowerCatcher == null) return;

            var upperIdentity = GetComponent<NetworkIdentity>();
            var lowerIdentity = lowerCatcher.GetComponent<NetworkIdentity>();
            if (upperIdentity == null || lowerIdentity == null) return;

            ApplyCatchPoseObserversRpc(upperIdentity, lowerIdentity);
        }

        /// <summary>
        /// Server-only entry point to undo the pose, e.g. on level transition / reset.
        /// </summary>
        public void ServerReleaseCatchPose()
        {
            if (!isServer) return;
            ReleaseCatchPoseObserversRpc();
        }

        [ObserversRpc(bufferLast: false)]
        private void ApplyCatchPoseObserversRpc(NetworkIdentity upperIdentity, NetworkIdentity lowerIdentity)
        {
            if (upperIdentity == null || lowerIdentity == null) return;
            var upperPose = upperIdentity.GetComponent<CatchPoseController>();
            var lowerPose = lowerIdentity.GetComponent<CatchPoseController>();
            if (upperPose == null || lowerPose == null) return;

            upperPose.LocalSnapToSocket(lowerPose);
        }

        [ObserversRpc(bufferLast: false)]
        private void ReleaseCatchPoseObserversRpc()
        {
            LocalReleaseSocket();
        }

        private void LocalSnapToSocket(CatchPoseController lowerPose)
        {
            if (lowerPose == null || lowerPose._heldUpperSocket == null)
            {
                Debug.LogWarning("[CatchPoseController] Lower socket not assigned — skipping snap.");
            }
            else
            {
                // Capture original parent so we can restore on release
                _originalParent = transform.parent;
                _originalLocalPos = transform.localPosition;
                _originalLocalRot = transform.localRotation;

                // Disable CC so parenting + transform set doesn't fight collisions
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                transform.SetParent(lowerPose._heldUpperSocket, worldPositionStays: false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;

                _heldByOther = true;
            }

            // Camera shake — only on the local owner's camera
            if (isOwner && _localCamera != null)
            {
                StartCoroutine(ShakeRoutine(_localCamera.transform,
                    _upperShakeDuration, _upperShakeAmplitude, _upperShakeFrequency));
            }
            // Tell the lower-side local camera to shake too
            if (lowerPose != null && lowerPose.isOwner && lowerPose._localCamera != null)
            {
                lowerPose.StartCoroutine(lowerPose.ShakeRoutine(lowerPose._localCamera.transform,
                    _lowerShakeDuration, _lowerShakeAmplitude, _lowerShakeFrequency));
            }
        }

        private void LocalReleaseSocket()
        {
            if (!_heldByOther) return;

            transform.SetParent(_originalParent, worldPositionStays: false);
            transform.localPosition = _originalLocalPos;
            transform.localRotation = _originalLocalRot;

            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;

            _heldByOther = false;
        }

        private IEnumerator ShakeRoutine(Transform cameraTransform, float duration, float amplitude, float frequency)
        {
            if (cameraTransform == null) yield break;

            float elapsed = 0f;
            Vector3 baseLocalPos = cameraTransform.localPosition;
            float seedX = Random.Range(0f, 100f);
            float seedY = Random.Range(100f, 200f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float falloff = 1f - Mathf.Clamp01(elapsed / duration);
                float t = elapsed * frequency;

                float offsetX = (Mathf.PerlinNoise(seedX, t) - 0.5f) * 2f * amplitude * falloff;
                float offsetY = (Mathf.PerlinNoise(seedY, t) - 0.5f) * 2f * amplitude * falloff;

                cameraTransform.localPosition = baseLocalPos + new Vector3(offsetX, offsetY, 0f);
                yield return null;
            }

            cameraTransform.localPosition = baseLocalPos;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_heldUpperSocket != null)
            {
                Gizmos.color = new Color(1f, 0.8f, 0f, 0.8f);
                Gizmos.DrawWireSphere(_heldUpperSocket.position, 0.15f);
                UnityEditor.Handles.Label(_heldUpperSocket.position + Vector3.up * 0.2f, "HeldUpperSocket");
            }
            if (_catchGripLeft != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_catchGripLeft.position, 0.05f);
            }
            if (_catchGripRight != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_catchGripRight.position, 0.05f);
            }
        }
#endif
    }
}