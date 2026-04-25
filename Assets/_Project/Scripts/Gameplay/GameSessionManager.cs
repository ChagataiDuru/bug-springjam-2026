using System.Collections;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Taiyun.SuckTheWater.Gameplay
{
    public class GameSessionManager : NetworkBehaviour
    {
        public enum SessionState
        {
            WaitingToStart,
            FloorActive,
            FloorComplete,
            SessionWin,
            SessionFail
        }

        [Header("Defaults")]
        [SerializeField] private int _defaultFloorNumber = 1;
        [SerializeField] private int _defaultRoomsRequired = 2;
        [SerializeField] private float _waitingToStartDelay = 0f;
        [SerializeField] private float _floorCompleteDelay = 2f;
        [SerializeField] private bool _autoWinOnFloorComplete = true;

        [Header("Debug")]
        [SerializeField] private bool _enableDebugShortcuts = true;

        public SyncVar<SessionState> CurrentState = new SyncVar<SessionState>(SessionState.WaitingToStart);
        public SyncVar<int> CurrentFloor = new SyncVar<int>(1);
        public SyncVar<int> RoomsCleared = new SyncVar<int>(0);
        public SyncVar<int> RoomsRequired = new SyncVar<int>(0);

        public static event System.Action<SessionState> OnSessionStateChanged;
        public static event System.Action<string> OnRoomCleared;

        private readonly HashSet<string> _registeredRooms = new HashSet<string>();
        private readonly HashSet<string> _clearedRooms = new HashSet<string>();
        private bool _isInitialized;
        private SessionState _localState = (SessionState)(-1);
        private Coroutine _waitingToStartRoutine;
        private Coroutine _floorCompleteRoutine;

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

            _isInitialized = true;
            ApplyState(CurrentState.value);
        }

        protected override void OnDespawned()
        {
            _isInitialized = false;
            base.OnDespawned();
        }

        private void Update()
        {
            if (!isServer || !_enableDebugShortcuts)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (CurrentState.value == SessionState.FloorActive)
            {
                if (keyboard.f1Key.wasPressedThisFrame)
                {
                    DebugClearNextRoom();
                }

                if (keyboard.f2Key.wasPressedThisFrame)
                {
                    ForceFloorComplete();
                }
            }

            if (keyboard.f3Key.wasPressedThisFrame)
            {
                ForceSessionFail();
            }
#endif
        }

        public void StartSession()
        {
            StartSession(_defaultFloorNumber, _defaultRoomsRequired);
        }

        public void StartSession(int floorNumber, int roomsRequired)
        {
            if (isServer)
            {
                ApplyStartSession(floorNumber, roomsRequired);
            }
            else
            {
                StartSessionServerRpc(floorNumber, roomsRequired);
            }
        }

        public void RegisterRoom(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                return;
            }

            if (isServer)
            {
                _registeredRooms.Add(roomId);
            }
            else
            {
                RegisterRoomServerRpc(roomId);
            }
        }

        public void ReportRoomCleared(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                return;
            }

            if (isServer)
            {
                ApplyRoomCleared(roomId);
            }
            else
            {
                ReportRoomClearedServerRpc(roomId);
            }
        }

        public void OnTimerExpired()
        {
            if (isServer)
            {
                SetState(SessionState.SessionFail);
            }
            else
            {
                OnTimerExpiredServerRpc();
            }
        }

        public void OnAllPlayersDead()
        {
            if (isServer)
            {
                SetState(SessionState.SessionFail);
            }
            else
            {
                OnAllPlayersDeadServerRpc();
            }
        }

        public void ForceSessionWin()
        {
            if (isServer)
            {
                SetState(SessionState.SessionWin);
            }
        }

        public void ForceSessionFail()
        {
            if (isServer)
            {
                SetState(SessionState.SessionFail);
            }
        }

        public void ForceFloorComplete()
        {
            if (isServer)
            {
                SetState(SessionState.FloorComplete);
            }
        }

        [ServerRpc(requireOwnership: false)]
        private void StartSessionServerRpc(int floorNumber, int roomsRequired)
        {
            ApplyStartSession(floorNumber, roomsRequired);
        }

        [ServerRpc(requireOwnership: false)]
        private void RegisterRoomServerRpc(string roomId)
        {
            _registeredRooms.Add(roomId);
        }

        [ServerRpc(requireOwnership: false)]
        private void ReportRoomClearedServerRpc(string roomId)
        {
            ApplyRoomCleared(roomId);
        }

        [ServerRpc(requireOwnership: false)]
        private void OnTimerExpiredServerRpc()
        {
            SetState(SessionState.SessionFail);
        }

        [ServerRpc(requireOwnership: false)]
        private void OnAllPlayersDeadServerRpc()
        {
            SetState(SessionState.SessionFail);
        }

        private void ApplyStartSession(int floorNumber, int roomsRequired)
        {
            StopRoutines();

            _registeredRooms.Clear();
            _clearedRooms.Clear();

            CurrentFloor.value = Mathf.Max(1, floorNumber);
            RoomsRequired.value = Mathf.Max(0, roomsRequired);
            RoomsCleared.value = 0;

            SetState(SessionState.WaitingToStart);

            if (_waitingToStartDelay > 0f)
            {
                _waitingToStartRoutine = StartCoroutine(BeginFloorAfterDelay(_waitingToStartDelay));
            }
            else
            {
                SetState(SessionState.FloorActive);
            }
        }

        private void ApplyRoomCleared(string roomId)
        {
            if (CurrentState.value != SessionState.FloorActive)
            {
                return;
            }

            _registeredRooms.Add(roomId);

            if (!_clearedRooms.Add(roomId))
            {
                return;
            }

            RoomsCleared.value = _clearedRooms.Count;
            NotifyRoomClearedObserversRpc(roomId, RoomsCleared.value, RoomsRequired.value);

            if (RoomsRequired.value > 0 && RoomsCleared.value >= RoomsRequired.value)
            {
                SetState(SessionState.FloorComplete);
            }
        }

        private void SetState(SessionState newState)
        {
            if (CurrentState.value == newState)
            {
                return;
            }

            CurrentState.value = newState;
            NotifyStateChangedObserversRpc(newState);
            ApplyState(newState);

            if (isServer && newState == SessionState.FloorComplete)
            {
                HandleFloorComplete();
            }
        }

        private void ApplyState(SessionState state)
        {
            if (_localState == state)
            {
                return;
            }

            _localState = state;
            OnSessionStateChanged?.Invoke(state);
        }

        private void HandleFloorComplete()
        {
            StopFloorCompleteRoutine();

            if (_autoWinOnFloorComplete)
            {
                if (_floorCompleteDelay > 0f)
                {
                    _floorCompleteRoutine = StartCoroutine(CompleteSessionAfterDelay(_floorCompleteDelay));
                }
                else
                {
                    SetState(SessionState.SessionWin);
                }
            }
        }

        private IEnumerator BeginFloorAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetState(SessionState.FloorActive);
        }

        private IEnumerator CompleteSessionAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetState(SessionState.SessionWin);
        }

        private void StopRoutines()
        {
            if (_waitingToStartRoutine != null)
            {
                StopCoroutine(_waitingToStartRoutine);
                _waitingToStartRoutine = null;
            }

            StopFloorCompleteRoutine();
        }

        private void StopFloorCompleteRoutine()
        {
            if (_floorCompleteRoutine != null)
            {
                StopCoroutine(_floorCompleteRoutine);
                _floorCompleteRoutine = null;
            }
        }

        private void DebugClearNextRoom()
        {
            string roomId = $"Room_{RoomsCleared.value + 1}";
            ApplyRoomCleared(roomId);
        }

        [ObserversRpc]
        private void NotifyStateChangedObserversRpc(SessionState newState)
        {
            ApplyState(newState);
        }

        [ObserversRpc]
        private void NotifyRoomClearedObserversRpc(string roomId, int clearedCount, int requiredCount)
        {
            OnRoomCleared?.Invoke(roomId);
        }
    }
}
