using UnityEngine;

namespace Taiyun.SuckTheWater.Service.LobbyService
{
    /// <summary>
    /// Persists lobby data across scene transitions.
    /// This is crucial for the ConnectionStarter pattern - the game scene
    /// needs to know which lobby to connect to after loading.
    /// 
    /// Uses DontDestroyOnLoad to survive scene changes.
    /// </summary>
    public class LobbyDataHolder : MonoBehaviour
    {
        private static LobbyDataHolder _instance;
        public static LobbyDataHolder Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<LobbyDataHolder>();
                    if (_instance == null)
                    {
                        var go = new GameObject("LobbyDataHolder");
                        _instance = go.AddComponent<LobbyDataHolder>();
                    }
                }
                return _instance;
            }
        }
        
        [SerializeField] private Lobby _serializedLobby;
        
        /// <summary>
        /// The current lobby we're in (or attempting to join).
        /// </summary>
        public Lobby CurrentLobby { get; private set; }
        
        /// <summary>
        /// Local user's platform ID (Steam ID, etc.)
        /// </summary>
        public string LocalUserId { get; private set; }
        
        /// <summary>
        /// Whether we are the host of the current lobby.
        /// </summary>
        public bool IsHost => CurrentLobby.IsValid && CurrentLobby.IsOwner;

        // Track last lobby ID to prevent duplicate logs
        private string _lastLoggedLobbyId = "";
        private bool _lastLoggedIsOwner = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Updates the stored lobby data.
        /// Called by LobbyManager when lobby state changes.
        /// </summary>
        public void SetCurrentLobby(Lobby newLobby)
        {
            CurrentLobby = newLobby;
            _serializedLobby = newLobby; // For inspector visibility
            
            // FIX: Only log if lobby ID or ownership changed (prevents spam)
            bool shouldLog = newLobby.LobbyId != _lastLoggedLobbyId || 
                             newLobby.IsOwner != _lastLoggedIsOwner;
            
            if (shouldLog)
            {
                Debug.Log($"[LobbyDataHolder] Lobby set: {newLobby.LobbyId}, IsOwner: {newLobby.IsOwner}, IsValid: {newLobby.IsValid}");
                _lastLoggedLobbyId = newLobby.LobbyId;
                _lastLoggedIsOwner = newLobby.IsOwner;
            }
        }
        
        /// <summary>
        /// Sets the local user ID.
        /// </summary>
        public void SetLocalUserId(string userId)
        {
            LocalUserId = userId;
        }

        /// <summary>
        /// Clears the stored lobby data.
        /// Called when leaving a lobby.
        /// </summary>
        public void Clear()
        {
            CurrentLobby = default;
            _serializedLobby = default;
            _lastLoggedLobbyId = "";
            _lastLoggedIsOwner = false;
            Debug.Log("[LobbyDataHolder] Cleared");
        }
        
        /// <summary>
        /// Checks if we have valid lobby data to connect with.
        /// </summary>
        public bool HasValidLobby()
        {
            return CurrentLobby.IsValid && !string.IsNullOrEmpty(CurrentLobby.LobbyId);
        }
        
        /// <summary>
        /// Gets the host ID from the current lobby.
        /// </summary>
        public string GetHostId()
        {
            return CurrentLobby.GetHostId();
        }
    }
}