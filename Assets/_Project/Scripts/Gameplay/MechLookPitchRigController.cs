using PurrNet;
using System.Collections.Generic;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Moves aim targets for local and remote mech presentation rigs from replicated look pitch.
    /// Animation Rigging constraints on the presentation prefab consume these targets.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkedMovementAdapter))]
    public sealed class MechLookPitchRigController : NetworkBehaviour
    {
        [SerializeField] private NetworkedMovementAdapter _movementAdapter;
        [SerializeField] private Transform _aimOrigin;
        [SerializeField] private Transform[] _aimTargets;
        [SerializeField] private float _targetDistance = 10f;
        [SerializeField] private float _pitchMultiplier = 1f;
        [SerializeField] private float _targetSharpness = 40f;

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
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void LateUpdate()
        {
            if (!_ready || _movementAdapter == null || _aimTargets == null || _aimTargets.Length == 0)
            {
                return;
            }

            Transform origin = _aimOrigin != null ? _aimOrigin : transform;
            float pitch = _movementAdapter.GetLookPitchDegrees() * _pitchMultiplier;
            Vector3 localDirection = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
            Vector3 targetPosition = origin.position + transform.TransformDirection(localDirection) * _targetDistance;

            float t = _targetSharpness <= 0f
                ? 1f
                : 1f - Mathf.Exp(-_targetSharpness * Time.deltaTime);

            for (int i = 0; i < _aimTargets.Length; i++)
            {
                Transform aimTarget = _aimTargets[i];
                if (aimTarget == null)
                {
                    continue;
                }

                aimTarget.position = Vector3.Lerp(aimTarget.position, targetPosition, t);
            }
        }

        private void OnValidate()
        {
            _targetDistance = Mathf.Max(0.1f, _targetDistance);
            _targetSharpness = Mathf.Max(0f, _targetSharpness);
        }

        private void CacheReferences()
        {
            if (_movementAdapter == null)
            {
                _movementAdapter = GetComponent<NetworkedMovementAdapter>();
            }

            if (_aimOrigin == null)
            {
                _aimOrigin = FindTransformRecursive(transform, "HeadCameraSocket");
            }

            if (!HasAnyAssignedTarget(_aimTargets))
            {
                List<Transform> targets = new List<Transform>();
                CollectTransformsRecursive(transform, "LookAimTarget", targets);
                _aimTargets = targets.ToArray();
            }
        }

        private static bool HasAnyAssignedTarget(Transform[] targets)
        {
            if (targets == null)
            {
                return false;
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    return true;
                }
            }

            return false;
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

        private static void CollectTransformsRecursive(Transform root, string targetName, List<Transform> results)
        {
            if (root == null)
            {
                return;
            }

            if (root.name == targetName)
            {
                results.Add(root);
            }

            for (int i = 0; i < root.childCount; i++)
            {
                CollectTransformsRecursive(root.GetChild(i), targetName, results);
            }
        }
    }
}
