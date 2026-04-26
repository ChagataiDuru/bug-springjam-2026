using System;
using PurrNet;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// One of 4 catch zones on the lower floor, one per side of the building.
    /// SideIndex must match the paired JumpZone on the upper floor.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CatchZone : NetworkBehaviour
    {
        [Header("Pairing")]
        [Tooltip("Must match the JumpZone on the upper floor for this same side (0..3).")]
        [SerializeField] private int _sideIndex;

        [Header("Detection")]
        [SerializeField] private string _playerTag = "Player";

        public int SideIndex => _sideIndex;

        /// <summary>Server-only. True while the Lower player is standing inside this zone.</summary>
        public bool LowerPlayerInside { get; private set; }

        /// <summary>Fires for any role entering. Use to detect Upper landing into the zone.</summary>
        public event Action<CatchZone, PlayerRoleSync> OnPlayerEntered;
        public event Action<CatchZone, PlayerRoleSync> OnPlayerExited;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        public void ServerReset()
        {
            if (!isServer) return;
            LowerPlayerInside = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer) return;
            if (!other.CompareTag(_playerTag)) return;
            var roleSync = other.GetComponentInParent<PlayerRoleSync>();
            if (roleSync == null) return;

            if (roleSync.Role.value == PlayerRole.Lower)
                LowerPlayerInside = true;

            OnPlayerEntered?.Invoke(this, roleSync);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!isServer) return;
            if (!other.CompareTag(_playerTag)) return;
            var roleSync = other.GetComponentInParent<PlayerRoleSync>();
            if (roleSync == null) return;

            if (roleSync.Role.value == PlayerRole.Lower)
                LowerPlayerInside = false;

            OnPlayerExited?.Invoke(this, roleSync);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col is BoxCollider box)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.20f);
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            UnityEditor.Handles.Label(col.bounds.center + Vector3.up * 1.5f, $"Catch Side {_sideIndex}");
        }
#endif
    }
}