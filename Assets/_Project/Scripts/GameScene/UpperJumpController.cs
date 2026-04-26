using PurrNet;
using System.Collections;
using Taiyun.SuckTheWater.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Taiyun.SuckTheWater.GameScene
{
        /// <summary>
        /// While the local Upper player is inside a JumpZone, shows a prompt and
        /// listens for the jump key. On press, locks input and performs a deterministic
        /// fall through the balcony hole toward the LaunchTarget below.
        /// </summary>
    [RequireComponent(typeof(NetworkedPlayerController))]
    public class UpperJumpController : NetworkBehaviour
    {
        [Header("Input")]
        [Tooltip("Action that triggers the constrained jump. Default: F key / South gamepad button.")]
        [SerializeField]
        private InputAction _jumpAction = new InputAction(
            name: "JumpFromBalcony",
            type: InputActionType.Button);

        [Header("UI")]
        [Tooltip("Optional. Shown while inside a JumpZone.")]
        [SerializeField] private GameObject _promptUI;

        [Header("Launch")]
        [Tooltip("Time to wait between key press and launch (windup).")]
        [SerializeField] private float _windupDuration = 0.4f;
        [Tooltip("Initial downward speed applied at launch.")]
        [SerializeField] private float _launchVerticalSpeed = 2f;
        [Tooltip("Gravity applied during the constrained fall.")]
        [SerializeField] private float _fallGravity = 25f;
        [Tooltip("Max horizontal correction speed pulling player toward target X/Z.")]
        [SerializeField] private float _horizontalCorrection = 6f;

        [Header("Detection")]
        [Tooltip("Radius of the overlap query used to detect JumpZones around the player.")]
        [SerializeField] private float _zoneDetectionRadius = 0.5f;

        private NetworkedPlayerController _player;
        private PlayerRoleSync _roleSync;
        private JumpZone _currentZone;
        private bool _launching;
        private CharacterController _cc;

        private void Awake()
        {
            _player = GetComponent<NetworkedPlayerController>();
            _roleSync = GetComponent<PlayerRoleSync>();
            _cc = GetComponent<CharacterController>();

            if (_promptUI != null) _promptUI.SetActive(false);

            // Ensure the action has at least one binding even if the inspector wasn't configured.
            // The inspector array bindings will take precedence if you've set any there.
            if (_jumpAction.bindings.Count == 0)
            {
                _jumpAction.AddBinding("<Keyboard>/f");
                _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            }
        }

        private void OnEnable()
        {
            _jumpAction.Enable();
        }

        private void OnDisable()
        {
            _jumpAction.Disable();
            if (_promptUI != null) _promptUI.SetActive(false);
        }

        private void Update()
        {
            if (!isOwner) return;
            if (_roleSync == null || _roleSync.Role.value != PlayerRole.Upper) return;
            if (_launching) return;

            DetectJumpZone();

            if (_currentZone != null && _jumpAction.WasPressedThisFrame())
                BeginLaunchServerRpc();
        }

        private void DetectJumpZone()
        {
            var hits = Physics.OverlapSphere(transform.position, _zoneDetectionRadius);
            JumpZone newZone = null;
            for (int i = 0; i < hits.Length; i++)
            {
                var jz = hits[i].GetComponent<JumpZone>();
                if (jz != null) { newZone = jz; break; }
            }

            if (newZone != _currentZone)
            {
                _currentZone = newZone;
                if (_promptUI != null) _promptUI.SetActive(_currentZone != null);
            }
        }

        [ServerRpc(requireOwnership: true)]
        private void BeginLaunchServerRpc()
        {
            var lm = LevelManager.Instance;
            if (lm == null) return;
            if (lm.CurrentState.value != LevelState.Exploring) return;
            if (_roleSync == null || _roleSync.Role.value != PlayerRole.Upper) return;
            if (_currentZone == null) return;

            BeginLaunchObserversRpc(_currentZone.GetComponent<NetworkIdentity>());
        }

        [ObserversRpc(bufferLast: false)]
        private void BeginLaunchObserversRpc(NetworkIdentity zoneIdentity)
        {
            if (zoneIdentity == null) return;
            var zone = zoneIdentity.GetComponent<JumpZone>();
            if (zone == null) return;

            StartCoroutine(LaunchRoutine(zone));
        }

        private IEnumerator LaunchRoutine(JumpZone zone)
        {
            _launching = true;
            if (_promptUI != null) _promptUI.SetActive(false);

            if (_player is IPlayerMovementLock lockable) lockable.SetMovementLocked(true);

            if (zone.LaunchOrigin != null)
            {
                if (_cc != null) _cc.enabled = false;
                transform.SetPositionAndRotation(
                    zone.LaunchOrigin.position,
                    zone.LaunchOrigin.rotation);
                if (_cc != null) _cc.enabled = true;
            }

            yield return new WaitForSeconds(_windupDuration);

            Vector3 target = zone.LaunchTarget != null
                ? zone.LaunchTarget.position
                : transform.position + Vector3.down * 50f;

            float verticalSpeed = _launchVerticalSpeed;
            while (transform.position.y > target.y)
            {
                verticalSpeed += _fallGravity * Time.deltaTime;

                Vector3 pos = transform.position;
                Vector3 toTargetXZ = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
                Vector3 horizontal = Vector3.ClampMagnitude(toTargetXZ, _horizontalCorrection * Time.deltaTime);

                Vector3 motion = horizontal + Vector3.down * verticalSpeed * Time.deltaTime;

                if (_cc != null && _cc.enabled)
                    _cc.Move(motion);
                else
                    transform.position += motion;

                yield return null;
            }

            _launching = false;
        }
    }
}
