using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// Sync component for a player's current role (Upper / Lower).
    /// Lives alongside NetworkedPlayerController on the player prefab.
    /// Server-authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerRoleSync : NetworkBehaviour
    {
        public SyncVar<PlayerRole> Role = new SyncVar<PlayerRole>(PlayerRole.Unassigned);

        // Static registry so other systems can query "who is upper?"
        private static readonly List<PlayerRoleSync> _all = new List<PlayerRoleSync>();
        public static IReadOnlyList<PlayerRoleSync> All => _all;

        public static event Action<PlayerRoleSync, PlayerRole, PlayerRole> OnRoleChanged;

        public PlayerID OwnerPlayerID => owner ?? default;

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);
            if (!_all.Contains(this)) _all.Add(this);
            Role.onChanged += HandleRoleChanged;
        }

        protected override void OnDespawned()
        {
            _all.Remove(this);
            Role.onChanged -= HandleRoleChanged;
            base.OnDespawned();
        }

        private PlayerRole _last = PlayerRole.Unassigned;
        private void HandleRoleChanged(PlayerRole next)
        {
            var prev = _last;
            _last = next;
            OnRoleChanged?.Invoke(this, prev, next);
        }

        /// <summary>Server only. Set this player's role.</summary>
        public void ServerAssignRole(PlayerRole role)
        {
            if (!isServer) return;
            Role.value = role;
        }

        public static PlayerRoleSync GetByRole(PlayerRole role)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Role.value == role) return _all[i];
            return null;
        }
    }
}