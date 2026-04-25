using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Taiyun.SuckTheWater.Service.LobbyService
{
    /// <summary>
    /// Filter options for friend list queries.
    /// </summary>
    public enum FriendFilter
    {
        InThisGame,
        Online,
        All
    }
    
    /// <summary>
    /// Interface for platform-specific lobby implementations.
    /// Abstracts away Steam, Epic, Unity Services, etc.
    /// 
    /// Implementations handle:
    /// - Lobby creation/joining/leaving
    /// - Player ready states
    /// - Lobby metadata (properties)
    /// - Friend invitations
    /// </summary>
    public interface ILobbyProvider
    {
        #region Lifecycle
        
        /// <summary>
        /// Initializes the provider (e.g., registers Steam callbacks).
        /// </summary>
        Task InitializeAsync();
        
        /// <summary>
        /// Shuts down the provider and cleans up resources.
        /// </summary>
        void Shutdown();
        
        #endregion
        
        #region Lobby Management
        
        /// <summary>
        /// Creates a new lobby.
        /// </summary>
        /// <param name="maxPlayers">Maximum number of players</param>
        /// <param name="lobbyProperties">Initial lobby metadata</param>
        /// <returns>The created lobby, or invalid lobby on failure</returns>
        Task<Lobby> CreateLobbyAsync(int maxPlayers, Dictionary<string, string> lobbyProperties = null);
        
        /// <summary>
        /// Joins an existing lobby by ID.
        /// </summary>
        /// <param name="lobbyId">Platform-specific lobby ID</param>
        /// <returns>The joined lobby, or invalid lobby on failure</returns>
        Task<Lobby> JoinLobbyAsync(string lobbyId);
        
        /// <summary>
        /// Leaves the current lobby.
        /// </summary>
        Task LeaveLobbyAsync();
        
        /// <summary>
        /// Leaves a specific lobby by ID.
        /// </summary>
        Task LeaveLobbyAsync(string lobbyId);
        
        /// <summary>
        /// Searches for available lobbies.
        /// </summary>
        /// <param name="maxRoomsToFind">Maximum results to return</param>
        /// <param name="filters">Optional filters (platform-specific)</param>
        Task<List<Lobby>> SearchLobbiesAsync(int maxRoomsToFind = 10, Dictionary<string, string> filters = null);
        
        #endregion
        
        #region Player State
        
        /// <summary>
        /// Sets a player's ready state.
        /// </summary>
        Task SetIsReadyAsync(string userId, bool isReady);
        
        /// <summary>
        /// Gets all members in the current lobby.
        /// </summary>
        Task<List<LobbyUser>> GetLobbyMembersAsync();
        
        /// <summary>
        /// Gets the local user's platform ID.
        /// </summary>
        Task<string> GetLocalUserIdAsync();
        
        #endregion
        
        #region Lobby Data
        
        /// <summary>
        /// Sets lobby metadata.
        /// </summary>
        Task SetLobbyDataAsync(string key, string value);
        
        /// <summary>
        /// Gets lobby metadata.
        /// </summary>
        Task<string> GetLobbyDataAsync(string key);
        
        /// <summary>
        /// Marks the lobby as "all ready" (pre-game state).
        /// </summary>
        Task SetAllReadyAsync();
        
        /// <summary>
        /// Marks the lobby as "started" (game in progress).
        /// </summary>
        Task SetLobbyStartedAsync();
        
        /// <summary>
        /// Signals that the server is ready to accept connections.
        /// </summary>
        Task SetServerReadyAsync();
        
        #endregion
        
        #region Friends
        
        /// <summary>
        /// Gets the friend list from the platform.
        /// </summary>
        Task<List<FriendUser>> GetFriendsAsync(FriendFilter filter);
        
        /// <summary>
        /// Invites a friend to the current lobby.
        /// </summary>
        Task InviteFriendAsync(FriendUser user);
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when joining a lobby fails.
        /// </summary>
        event Action<string> OnLobbyJoinFailed;
        
        /// <summary>
        /// Fired when leaving a lobby.
        /// </summary>
        event Action OnLobbyLeft;
        
        /// <summary>
        /// Fired when lobby state changes (members, properties, etc.).
        /// </summary>
        event Action<Lobby> OnLobbyUpdated;
        
        /// <summary>
        /// Fired when the player list changes.
        /// </summary>
        event Action<List<LobbyUser>> OnLobbyPlayerListUpdated;
        
        /// <summary>
        /// Fired when friends list is retrieved.
        /// </summary>
        event Action<List<FriendUser>> OnFriendListPulled;
        
        /// <summary>
        /// Fired when a platform error occurs.
        /// </summary>
        event Action<string> OnError;
        
        /// <summary>
        /// Fired when an invite is accepted via platform overlay (Steam Shift+Tab, etc.).
        /// </summary>
        event Action<string> OnJoinLobbyRequested;
        
        #endregion
    }
}
