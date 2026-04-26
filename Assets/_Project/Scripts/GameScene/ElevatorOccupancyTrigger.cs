using System;
using PurrNet;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// Trigger volume placed inside a lower-floor elevator.
    /// Reports occupancy to LevelManager.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorOccupancyTrigger : NetworkBehaviour
    {
        [SerializeField] private string _playerTag = "Player";

        public bool IsOccupied { get; private set; }
        public PlayerRoleSync OccupantRole { get; private set; }

        public event Action<ElevatorOccupancyTrigger> OnOccupancyChanged;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer) return;
            if (!other.CompareTag(_playerTag)) return;
            var roleSync = other.GetComponentInParent<PlayerRoleSync>();
            if (roleSync == null) return;
            IsOccupied = true;
            OccupantRole = roleSync;
            OnOccupancyChanged?.Invoke(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!isServer) return;
            if (!other.CompareTag(_playerTag)) return;
            var roleSync = other.GetComponentInParent<PlayerRoleSync>();
            if (roleSync == null || roleSync != OccupantRole) return;
            IsOccupied = false;
            OccupantRole = null;
            OnOccupancyChanged?.Invoke(this);
        }
    }
}