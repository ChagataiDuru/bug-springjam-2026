using DG.Tweening;
using PurrNet;
using UnityEngine;
using UnityEngine.Events;

namespace Taiyun.SuckTheWater.Gameplay.Doors
{
    public class SealedDoor : NetworkBehaviour, IInteractable
    {
        public enum DoorMotion
        {
            Rotate,
            Slide
        }

        [Header("Identity")]
        [SerializeField] private string _doorId = "Door_01";
        [SerializeField] private string _roomAId = "";
        [SerializeField] private string _roomBId = "";

        [Header("Interaction")]
        [SerializeField] private float _interactionDuration = 2f;

        [Header("Visuals")]
        [SerializeField] private Transform _doorVisual;
        [SerializeField] private DoorMotion _motion = DoorMotion.Rotate;
        [SerializeField] private Vector3 _openLocalEuler = new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 _openLocalPositionOffset = Vector3.zero;
        [SerializeField] private Light _sealedLight;
        [SerializeField] private Renderer[] _sealedRenderers;
        [SerializeField] private Color _sealedEmissionColor = new Color(0.2f, 0.7f, 1f, 1f);
        [SerializeField] private float _sealedLightIntensity = 2f;

        [Header("Tween")]
        [SerializeField] private bool _useSpeed = false;
        [SerializeField] private float _openDuration = 2f;
        [SerializeField] private float _openSpeed = 90f;
        [SerializeField] private Ease _openEase = Ease.InOutSine;
        [SerializeField] private bool _useCustomCurve = false;
        [SerializeField] private AnimationCurve _openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _openSfx;

        public UnityEvent<string, string> OnDoorOpened;
        public UnityEvent OnDoorReset;

        [SerializeField] private bool _startLocked = false;

        private SyncVar<bool> _isSealed = new SyncVar<bool>(true);
        private SyncVar<bool> _isLocked = new SyncVar<bool>(false);
        private bool _isInitialized;
        private bool _isOpening;
        private bool _isOpen;
        private Quaternion _closedLocalRotation;
        private Quaternion _openLocalRotation;
        private Vector3 _closedLocalPosition;
        private Vector3 _openLocalPosition;
        private MaterialPropertyBlock _materialBlock;
        private Tween _activeTween;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public string InteractionPrompt => "Hold E to Open";
        public float InteractionDuration => _interactionDuration;
        public bool IsLocked => _isLocked.value;
        public bool IsSealed => _isSealed.value;

        public void ConfigureIdentity(string doorId, string roomAId, string roomBId)
        {
            _doorId = doorId;
            _roomAId = roomAId;
            _roomBId = roomBId;
        }

        public bool CanInteract(GameObject interactor)
        {
            return _isSealed.value && !_isOpening && !_isLocked.value;
        }

        public void OnInteractionComplete(GameObject interactor)
        {
            if (!_isSealed.value || _isOpening || _isLocked.value)
            {
                return;
            }

            RequestOpenServerRpc();
        }

        private void Awake()
        {
            if (_doorVisual == null)
            {
                _doorVisual = transform;
            }

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }

            _closedLocalRotation = _doorVisual.localRotation;
            _openLocalRotation = _closedLocalRotation * Quaternion.Euler(_openLocalEuler);
            _closedLocalPosition = _doorVisual.localPosition;
            _openLocalPosition = _closedLocalPosition + _openLocalPositionOffset;
        }

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);

            if (!asServer && isServer)
            {
                return;
            }

            if (_isInitialized)
            {
                return;
            }

            _isSealed.onChanged += OnSealedChanged;
            _isLocked.onChanged += OnLockedChanged;

            if (asServer)
            {
                _isLocked.value = _startLocked;
            }

            ApplyInitialState();

            _isInitialized = true;
        }

        protected override void OnDespawned()
        {
            if (_isInitialized)
            {
                _isSealed.onChanged -= OnSealedChanged;
                _isLocked.onChanged -= OnLockedChanged;
            }

            _isInitialized = false;
            base.OnDespawned();
        }

        [ServerRpc(requireOwnership: false)]
        private void RequestOpenServerRpc()
        {
            if (!_isSealed.value || _isLocked.value)
            {
                return;
            }

            _isSealed.value = false;
            OpenDoorObserversRpc();
        }

        [ObserversRpc]
        private void OpenDoorObserversRpc()
        {
            StartOpenSequence();
        }

        private void OnSealedChanged(bool isSealed)
        {
            if (isSealed)
            {
                ApplySealedState();
            }
            else
            {
                StartOpenSequence();
            }
        }

        private void OnLockedChanged(bool isLocked)
        {
            if (isLocked)
            {
                CancelOpeningIfNeeded();
            }
        }

        private void ApplyInitialState()
        {
            if (_isSealed.value)
            {
                ApplySealedState();
            }
            else
            {
                ApplyOpenImmediate();
            }
        }

        private void ApplySealedState()
        {
            _isOpen = false;
            _isOpening = false;

            CancelOpeningIfNeeded();

            if (_doorVisual != null)
            {
                _doorVisual.localRotation = _closedLocalRotation;
                _doorVisual.localPosition = _closedLocalPosition;
            }

            SetSealedGlow(true);
        }

        private void StartOpenSequence()
        {
            if (_isOpen || _isOpening || _isLocked.value)
            {
                return;
            }

            _isOpening = true;

            if (_audioSource != null && _openSfx != null)
            {
                _audioSource.PlayOneShot(_openSfx);
            }

            if (_doorVisual == null)
            {
                ApplyOpenImmediate();
                return;
            }

            KillActiveTween();
            float duration = GetOpenDuration();

            if (_motion == DoorMotion.Rotate)
            {
                _activeTween = _doorVisual.DOLocalRotateQuaternion(_openLocalRotation, duration);
            }
            else
            {
                _activeTween = _doorVisual.DOLocalMove(_openLocalPosition, duration);
            }

            ApplyTweenEase(_activeTween);
            _activeTween.OnComplete(ApplyOpenImmediate);
        }

        private void ApplyOpenImmediate()
        {
            _isOpening = false;
            _isOpen = true;

            if (_doorVisual != null)
            {
                _doorVisual.localRotation = _motion == DoorMotion.Rotate ? _openLocalRotation : _closedLocalRotation;
                _doorVisual.localPosition = _motion == DoorMotion.Slide ? _openLocalPosition : _closedLocalPosition;
            }

            SetSealedGlow(false);

            if (isServer)
            {
                OnDoorOpened?.Invoke(_roomAId, _roomBId);
            }
        }

        public void ResetDoor(bool keepLocked = false)
        {
            if (isServer)
            {
                ApplyReset(keepLocked);
            }
            else
            {
                ResetDoorServerRpc(keepLocked);
            }
        }

        public void SetLocked(bool isLocked)
        {
            if (isServer)
            {
                ApplyLock(isLocked);
            }
            else
            {
                SetLockedServerRpc(isLocked);
            }
        }

        [ServerRpc(requireOwnership: false)]
        private void ResetDoorServerRpc(bool keepLocked)
        {
            ApplyReset(keepLocked);
        }

        [ServerRpc(requireOwnership: false)]
        private void SetLockedServerRpc(bool isLocked)
        {
            ApplyLock(isLocked);
        }

        private void ApplyReset(bool keepLocked)
        {
            _isSealed.value = true;

            if (!keepLocked)
            {
                _isLocked.value = false;
            }

            ResetDoorObserversRpc();
        }

        private void ApplyLock(bool isLocked)
        {
            _isLocked.value = isLocked;

            if (isLocked)
            {
                CancelOpeningIfNeeded();
            }
        }

        [ObserversRpc]
        private void ResetDoorObserversRpc()
        {
            ApplySealedState();
            OnDoorReset?.Invoke();
        }

        private void OnDisable()
        {
            KillActiveTween();
        }

        private void KillActiveTween()
        {
            if (_activeTween == null)
            {
                return;
            }

            _activeTween.Kill(false);
            _activeTween = null;
        }

        private void CancelOpeningIfNeeded()
        {
            if (!_isOpening)
            {
                return;
            }

            _isOpening = false;
            KillActiveTween();
        }

        private float GetOpenDuration()
        {
            if (!_useSpeed)
            {
                return Mathf.Max(0.01f, _openDuration);
            }

            float speed = Mathf.Max(0.01f, _openSpeed);

            if (_motion == DoorMotion.Rotate)
            {
                float angle = Quaternion.Angle(_closedLocalRotation, _openLocalRotation);
                return Mathf.Max(0.01f, angle / speed);
            }

            float distance = Vector3.Distance(_closedLocalPosition, _openLocalPosition);
            return Mathf.Max(0.01f, distance / speed);
        }

        private void ApplyTweenEase(Tween tween)
        {
            if (tween == null)
            {
                return;
            }

            if (_useCustomCurve && _openCurve != null)
            {
                tween.SetEase(_openCurve);
            }
            else
            {
                tween.SetEase(_openEase);
            }
        }

        private void SetSealedGlow(bool isEnabled)
        {
            if (_sealedLight != null)
            {
                _sealedLight.enabled = isEnabled;
                _sealedLight.color = _sealedEmissionColor;
                _sealedLight.intensity = _sealedLightIntensity;
            }

            if (_sealedRenderers == null || _sealedRenderers.Length == 0)
            {
                return;
            }

            if (_materialBlock == null)
            {
                _materialBlock = new MaterialPropertyBlock();
            }

            Color emission = isEnabled ? _sealedEmissionColor : Color.black;

            foreach (var renderer in _sealedRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(_materialBlock);
                _materialBlock.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(_materialBlock);
            }
        }
    }
}
