using Taiyun.SuckTheWater.UI;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class PlayerInteractionDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private PlayerInputHandler _inputHandler;

        [Header("Interaction Settings")]
        [SerializeField] private float _interactionRange = 2f;
        [SerializeField] private LayerMask _interactionMask = ~0;

        [Header("Debug")]
        [SerializeField] private bool _enableDebug = false;
        
        private IInteractable _currentInteractable;
        private float _currentHoldTime;
        private bool _isHolding;
        private bool _interactionLocked;
        private InteractionPromptUI _promptUI;


        private void Awake()
        {
            if (_inputHandler == null)
            {
                _inputHandler = GetComponent<PlayerInputHandler>();
            }

            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInChildren<Camera>();
            }
        }

        void Start()
        {
            _promptUI = FindFirstObjectByType<InteractionPromptUI>();
        }
        
        private void Update()
        {
            if (_inputHandler == null || _playerCamera == null)
            {
                return;
            }

            if (!_inputHandler.CanProcessInput())
            {
                ClearInteraction();
                return;
            }
            
            if (!TryGetInteractable(out IInteractable interactable))
            {
                ClearInteraction();
                return;
            }

            if (!interactable.CanInteract(gameObject))
            {
                if (_enableDebug) Debug.Log("[Interaction Debug] Found the door, but CanInteract() returned FALSE! (Is it locked? Is it already open?)");
                ClearInteraction();
                return;
            }

            if (_enableDebug) Debug.Log("[Interaction Debug] Door is ready! Waiting for you to hold E...");

            if (!ReferenceEquals(_currentInteractable, interactable))
            {
                SetCurrentInteractable(interactable);
            }

            bool isHeld = _inputHandler.GetInteractInputHeld();

            // AND THIS FINAL DEBUG CHECK:
            if (_enableDebug && isHeld) Debug.Log("[Interaction Debug] 'E' key is currently being held down!");

            if (!interactable.CanInteract(gameObject))
            {
                ClearInteraction();
                return;
            }

            if (!ReferenceEquals(_currentInteractable, interactable))
            {
                SetCurrentInteractable(interactable);
            }
            
            if (_interactionLocked)
            {
                if (!isHeld || _currentInteractable == null || !_currentInteractable.CanInteract(gameObject))
                {
                    _interactionLocked = false;
                }
                else
                {
                    return;
                }
            }

            if (isHeld)
            {
                HandleHoldProgress();
            }
            else
            {
                CancelHoldIfNeeded();
            }
        }

        private bool TryGetInteractable(out IInteractable interactable)
        {
            interactable = null;

            Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

            if (_enableDebug)
            {
                Debug.DrawRay(ray.origin, ray.direction * _interactionRange, Color.red);
            }

            if (!Physics.Raycast(ray, out RaycastHit hit, _interactionRange, _interactionMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (_enableDebug)
            {
                Debug.Log($"[Interaction Debug] Raycast hit: {hit.collider.gameObject.name} on Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
            }

            var component = hit.collider.GetComponentInParent(typeof(IInteractable));
            interactable = component as IInteractable;

            if (_enableDebug && interactable == null)
            {
                Debug.LogWarning($"[Interaction Debug] Hit {hit.collider.gameObject.name}, but it (or its parents) doesn't have an IInteractable script!");
            }

            return interactable != null;
        }

        private void SetCurrentInteractable(IInteractable interactable)
        {
            CancelHoldIfNeeded();
            _currentInteractable = interactable;
            _currentHoldTime = 0f;
            _isHolding = false;
            _interactionLocked = false;

            if (_promptUI != null)
            {
                _promptUI.Show(_currentInteractable.InteractionPrompt);
                _promptUI.SetProgress(0f);
            }
        }

        private void HandleHoldProgress()
        {
            if (_currentInteractable == null)
            {
                return;
            }

            if (!_isHolding)
            {
                _isHolding = true;
                _currentHoldTime = 0f;
                _promptUI?.OnInteractionStart();
            }

            float duration = Mathf.Max(0.01f, _currentInteractable.InteractionDuration);
            _currentHoldTime += Time.deltaTime;

            float progress = Mathf.Clamp01(_currentHoldTime / duration);
            _promptUI?.SetProgress(progress);

            if (progress >= 1f)
            {
                _currentInteractable.OnInteractionComplete(gameObject);
                _promptUI?.OnInteractionComplete();
                _isHolding = false;
                _currentHoldTime = 0f;
                _interactionLocked = true;
            }
        }

        private void CancelHoldIfNeeded()
        {
            if (!_isHolding)
            {
                return;
            }

            _isHolding = false;
            _currentHoldTime = 0f;
            _promptUI?.SetProgress(0f);
            _promptUI?.OnInteractionCancel();
        }

        private void ClearInteraction()
        {
            CancelHoldIfNeeded();
            _interactionLocked = false;
            _currentInteractable = null;
            _currentHoldTime = 0f;

            if (_promptUI != null)
            {
                _promptUI.Hide();
            }
        }
    }
}
