using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using Taiyun.SuckTheWater.Gameplay;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    public enum LevelState
    {
        Idle,
        Intro,
        Exploring,
        JumpArmed,
        Caught,
        Missed,
        Transitioning
    }

    public class LevelManager : NetworkBehaviour
    {
        public static LevelManager Instance { get; private set; }

        #region Inspector

        [Header("Spawn Refs")]
        [SerializeField] private Transform _upperSpawn;
        [SerializeField] private Transform _lowerSpawn;

        [Header("Side Pairs (index must match across the two arrays)")]
        [Tooltip("4 jump zones on the upper-floor balcony, one per side.")]
        [SerializeField] private JumpZone[] _jumpZones;
        [Tooltip("4 catch zones on the lower floor, one per side. Index must match _jumpZones.")]
        [SerializeField] private CatchZone[] _catchZones;

        [Header("Lower-floor Elevators")]
        [SerializeField] private ElevatorOccupancyTrigger[] _lowerFloorElevators;

        [Header("UI")]
        [SerializeField] private LevelTransitionUI _transitionUI;

        [Header("Failure Detection")]
        [SerializeField] private float _failureYThreshold = -10f;
        [SerializeField] private float _jumpArmGracePeriod = 0.2f;
        [Tooltip("Y delta below upper spawn that arms the jump phase.")]
        [SerializeField] private float _jumpArmYDelta = 1.5f;

        [Header("Pacing")]
        [SerializeField] private float _postCatchHold = 1.5f;
        [SerializeField] private float _postMissHold = 2.5f;

        [Header("Debug")]
        [SerializeField] private bool _logState = true;

        #endregion

        public SyncVar<LevelState> CurrentState = new SyncVar<LevelState>(LevelState.Idle);
        public SyncVar<int> LevelIndex = new SyncVar<int>(0);

        private bool _running;
        private float _jumpArmTime;

        // Server-only side tracking
        private int _upperLastJumpSide = -1; // last JumpZone the upper entered while Exploring
        private int _lowerCurrentCatchSide = -1; // catch zone the lower is currently inside
        public SyncVar<bool> IsFirstIntro = new SyncVar<bool>(true);

        private void Awake() { Instance = this; }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        protected override void OnSpawned(bool asServer)
        {
            base.OnSpawned(asServer);
            CurrentState.onChanged += HandleStateChanged;
            CurrentState.onChanged += LocalUiReactToState;
            HookZoneEvents(true);
        }

        protected override void OnDespawned()
        {
            CurrentState.onChanged -= HandleStateChanged;
            CurrentState.onChanged -= LocalUiReactToState;
            HookZoneEvents(false);
            base.OnDespawned();
        }

        private void HandleStateChanged(LevelState s)
        {
            if (_logState) Debug.Log($"[LevelManager] State → {s}");
        }

        private void HookZoneEvents(bool subscribe)
        {
            if (_jumpZones != null)
            {
                for (int i = 0; i < _jumpZones.Length; i++)
                {
                    if (_jumpZones[i] == null) continue;
                    if (subscribe) _jumpZones[i].OnPlayerEntered += HandleJumpZoneEntered;
                    else _jumpZones[i].OnPlayerEntered -= HandleJumpZoneEntered;
                }
            }
            if (_catchZones != null)
            {
                for (int i = 0; i < _catchZones.Length; i++)
                {
                    if (_catchZones[i] == null) continue;
                    if (subscribe)
                    {
                        _catchZones[i].OnPlayerEntered += HandleCatchZoneEntered;
                        _catchZones[i].OnPlayerExited += HandleCatchZoneExited;
                    }
                    else
                    {
                        _catchZones[i].OnPlayerEntered -= HandleCatchZoneEntered;
                        _catchZones[i].OnPlayerExited -= HandleCatchZoneExited;
                    }
                }
            }
        }

        public void ServerBeginLoop()
        {
            if (!isServer || _running) return;
            _running = true;
            RunLoopAsync().Forget();
        }

        private async UniTask RunLoopAsync()
        {
            while (_running)
            {
                await RunSingleLevelAsync();
                LevelIndex.value++;
            }
        }

        private async UniTask RunSingleLevelAsync()
        {
            // Reset per-level state
            ResetZones();
            _upperLastJumpSide = -1;
            _lowerCurrentCatchSide = -1;

            // Randomize roles every level
            AssignRandomRoles();
            ResetAllPlayerAnimators();

            TeleportPlayersToSpawns();
            BroadcastSetMovementLocked(true);

            // Intro
            bool isFirstIntro = IsFirstIntro.value;
            CurrentState.value = LevelState.Intro;
            await UniTask.Delay(System.TimeSpan.FromSeconds(
                _transitionUI != null ? _transitionUI.GetIntroDuration(isFirstIntro) : 3f));

            if (isFirstIntro) IsFirstIntro.value = false;

            // Exploring
            BroadcastSetMovementLocked(false);
            CurrentState.value = LevelState.Exploring;

            // Wait for upper to drop past arm threshold
            await WaitForJumpArmAsync();
            CurrentState.value = LevelState.JumpArmed;
            _jumpArmTime = Time.time;

            // The chosen jump side is locked at this moment (no longer updates)
            int chosenJumpSide = _upperLastJumpSide;
            if (_logState) Debug.Log($"[LevelManager] Upper jumped from side {chosenJumpSide}");

            // Resolve outcome
            bool caught = await WaitForOutcomeAsync(chosenJumpSide);
            CurrentState.value = caught ? LevelState.Caught : LevelState.Missed;

            if (caught) await HandleSuccessAsync();
            else await HandleMissAsync();
        }

        private void ResetAllPlayerAnimators()
        {
            foreach (var roleSync in PlayerRoleSync.All)
            {
                if (roleSync == null) continue;
                var animator = roleSync.GetComponent<Taiyun.SuckTheWater.Gameplay.NetworkedAnimator>();
                if (animator != null) animator.ServerResetAnimationState();
            }
        }

        #region Role assignment / teleport / lock (unchanged)

        private void AssignRandomRoles()
        {
            var players = new List<PlayerRoleSync>(PlayerRoleSync.All);
            if (players.Count < 2)
            {
                Debug.LogWarning("[LevelManager] Fewer than 2 players — single-player debug mode.");
                if (players.Count == 1) players[0].ServerAssignRole(PlayerRole.Upper);
                return;
            }
            for (int i = players.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (players[i], players[j]) = (players[j], players[i]);
            }
            players[0].ServerAssignRole(PlayerRole.Upper);
            players[1].ServerAssignRole(PlayerRole.Lower);
        }

        private void TeleportPlayersToSpawns()
        {
            var upper = PlayerRoleSync.GetByRole(PlayerRole.Upper);
            var lower = PlayerRoleSync.GetByRole(PlayerRole.Lower);
            if (upper != null && _upperSpawn != null)
                TeleportPlayerOnAllClients(upper, _upperSpawn.position, _upperSpawn.rotation);
            if (lower != null && _lowerSpawn != null)
                TeleportPlayerOnAllClients(lower, _lowerSpawn.position, _lowerSpawn.rotation);
        }

        private void TeleportPlayerOnAllClients(PlayerRoleSync player, Vector3 pos, Quaternion rot)
        {
            ApplyTeleport(player.gameObject, pos, rot);
            TeleportPlayerObserversRpc(player.GetComponent<NetworkIdentity>(), pos, rot);
        }

        [ObserversRpc(bufferLast: false)]
        private void TeleportPlayerObserversRpc(NetworkIdentity identity, Vector3 pos, Quaternion rot)
        {
            if (identity == null) return;
            ApplyTeleport(identity.gameObject, pos, rot);
        }

        private void ApplyTeleport(GameObject go, Vector3 pos, Quaternion rot)
        {
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            go.transform.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = true;
        }

        private void BroadcastSetMovementLocked(bool locked)
        {
            SetMovementLockedObserversRpc(locked);
        }

        [ObserversRpc(bufferLast: true)]
        private void SetMovementLockedObserversRpc(bool locked)
        {
            var roleSync = FindLocalPlayerRoleSync();
            if (roleSync == null) return;
            var lockTarget = roleSync.GetComponent<IPlayerMovementLock>();
            lockTarget?.SetMovementLocked(locked);
        }

        private PlayerRoleSync FindLocalPlayerRoleSync()
        {
            foreach (var rs in PlayerRoleSync.All)
                if (rs.isOwner) return rs;
            return null;
        }

        #endregion

        #region Outcome logic (the meaningful change)

        private void HandleJumpZoneEntered(JumpZone zone, PlayerRoleSync who)
        {
            if (!isServer) return;
            // Only update during Exploring — once jump is armed, the side is locked
            if (CurrentState.value != LevelState.Exploring) return;
            _upperLastJumpSide = zone.SideIndex;
            if (_logState) Debug.Log($"[LevelManager] Upper now on side {zone.SideIndex}");
        }

        private void HandleCatchZoneEntered(CatchZone zone, PlayerRoleSync who)
        {
            if (!isServer) return;
            if (who.Role.value == PlayerRole.Lower)
            {
                _lowerCurrentCatchSide = zone.SideIndex;
                return;
            }
            // Upper entering a catch zone after jumping — check for match
            if (who.Role.value == PlayerRole.Upper && CurrentState.value == LevelState.JumpArmed)
            {
                if (_upperLastJumpSide >= 0 &&
                    zone.SideIndex == _upperLastJumpSide &&
                    zone.LowerPlayerInside)
                {
                    // Catch! Signal via state — the awaiter polls this.
                    _pendingCatchOnSide = zone.SideIndex;
                }
            }
        }

        private void HandleCatchZoneExited(CatchZone zone, PlayerRoleSync who)
        {
            if (!isServer) return;
            if (who.Role.value == PlayerRole.Lower &&
                _lowerCurrentCatchSide == zone.SideIndex)
            {
                _lowerCurrentCatchSide = -1;
            }
        }

        private int _pendingCatchOnSide = -1;

        private async UniTask WaitForJumpArmAsync()
        {
            if (_upperSpawn == null) return;
            float armBelowY = _upperSpawn.position.y - _jumpArmYDelta;
            while (true)
            {
                var upper = PlayerRoleSync.GetByRole(PlayerRole.Upper);
                if (upper != null && upper.transform.position.y < armBelowY) return;
                await UniTask.Yield();
            }
        }

        private async UniTask<bool> WaitForOutcomeAsync(int chosenJumpSide)
        {
            _pendingCatchOnSide = -1;
            var upper = PlayerRoleSync.GetByRole(PlayerRole.Upper);
            var lower = PlayerRoleSync.GetByRole(PlayerRole.Lower);
            if (upper == null) return false;

            while (true)
            {
                if (_pendingCatchOnSide >= 0) return true;

                bool graceElapsed = Time.time - _jumpArmTime > _jumpArmGracePeriod;

                // Upper falls past threshold without catching → miss
                if (graceElapsed && upper.transform.position.y < _failureYThreshold)
                    return false;

                // Lower falls past threshold (walked/jumped off edge) → also miss
                if (lower != null && lower.transform.position.y < _failureYThreshold)
                    return false;

                await UniTask.Yield();
            }
        }

        #endregion

        #region Resolution / flow (unchanged)

        private async UniTask HandleSuccessAsync()
        {
            // Snap upper into lower's socket and shake cameras
            var upper = PlayerRoleSync.GetByRole(PlayerRole.Upper);
            var lower = PlayerRoleSync.GetByRole(PlayerRole.Lower);
            if (upper != null && lower != null)
            {
                var upperPose = upper.GetComponent<CatchPoseController>();
                var lowerPose = lower.GetComponent<CatchPoseController>();
                if (upperPose != null && lowerPose != null)
                    upperPose.ServerApplyCatchPose(lowerPose);
            }

            await UniTask.Delay(System.TimeSpan.FromSeconds(_postCatchHold));

            // Release the held pose before elevators / next level
            if (upper != null)
            {
                var upperPose = upper.GetComponent<CatchPoseController>();
                if (upperPose != null) upperPose.ServerReleaseCatchPose();
            }

            CurrentState.value = LevelState.Transitioning;
            await WaitForBothElevatorsOccupiedAsync();
            FadeOutOnAllClientsRpc();
            await UniTask.Delay(System.TimeSpan.FromSeconds(
                _transitionUI != null ? _transitionUI.FadeOutDuration : 1f));
        }

        private async UniTask HandleMissAsync()
        {
            // Release any lingering held pose (defensive)
            var upper = PlayerRoleSync.GetByRole(PlayerRole.Upper);
            if (upper != null)
            {
                var upperPose = upper.GetComponent<CatchPoseController>();
                if (upperPose != null) upperPose.ServerReleaseCatchPose();
            }

            FadeFailOnAllClientsRpc();
            await UniTask.Delay(System.TimeSpan.FromSeconds(_postMissHold));
        }

        [ObserversRpc(bufferLast: false)]
        private void FadeOutOnAllClientsRpc()
        {
            if (_transitionUI != null) _transitionUI.PlayFadeOutAsync().Forget();
        }

        [ObserversRpc(bufferLast: false)]
        private void FadeFailOnAllClientsRpc()
        {
            if (_transitionUI != null) _transitionUI.PlayFailFadeAsync().Forget();
        }

        private async UniTask WaitForBothElevatorsOccupiedAsync()
        {
            while (true)
            {
                bool allOccupied = true;
                for (int i = 0; i < _lowerFloorElevators.Length; i++)
                {
                    if (!_lowerFloorElevators[i].IsOccupied) { allOccupied = false; break; }
                }
                if (allOccupied) return;
                await UniTask.Delay(150);
            }
        }

        private void ResetZones()
        {
            if (_catchZones != null)
                for (int i = 0; i < _catchZones.Length; i++) _catchZones[i].ServerReset();
        }

        #endregion

        private void LocalUiReactToState(LevelState s)
        {
            if (_transitionUI == null) return;
            if (s == LevelState.Intro)
            {
                var local = FindLocalPlayerRoleSync();
                var role = local != null ? local.Role.value : PlayerRole.Unassigned;
                _transitionUI.PlayIntroAsync(role, IsFirstIntro.value).Forget();
            }
        }
    }
}