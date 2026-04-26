using System;
using PurrNet;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// One of 4 jump-off zones on the upper floor balcony, one per side.
    /// SideIndex must match the paired CatchZone on the lower floor.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class JumpZone : NetworkBehaviour
    {
        [Header("Pairing")]
        [Tooltip("Must match the CatchZone on the lower floor for this same side (0..3).")]
        [SerializeField] private int _sideIndex;

        [Header("Detection")]
        [SerializeField] private string _playerTag = "Player";

        public int SideIndex => _sideIndex;

        /// <summary>Server-only. True while the Upper player is standing inside this zone.</summary>
        public bool UpperPlayerInside { get; private set; }

        public event Action<JumpZone, PlayerRoleSync> OnPlayerEntered;
        public event Action<JumpZone, PlayerRoleSync> OnPlayerExited;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer) return;
            if (!other.CompareTag(_playerTag)) return;
            var roleSync = other.GetComponentInParent<PlayerRoleSync>();
            if (roleSync == null || roleSync.Role.value != PlayerRole.Upper) return;

            UpperPlayerInside = true;
            OnPlayerEntered?.Invoke(this, roleSync);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!isServer) return;
            if (!other.CompareTag(_playerTag)) return;
            var roleSync = other.GetComponentInParent<PlayerRoleSync>();
            if (roleSync == null || roleSync.Role.value != PlayerRole.Upper) return;

            UpperPlayerInside = false;
            OnPlayerExited?.Invoke(this, roleSync);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col is BoxCollider box)
            {
                Gizmos.color = new Color(1f, 0.4f, 0f, 0.20f);
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(1f, 0.4f, 0f, 1f);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            UnityEditor.Handles.Label(col.bounds.center + Vector3.up * 1.5f, $"Jump Side {_sideIndex}");
        }
#endif
    }
}