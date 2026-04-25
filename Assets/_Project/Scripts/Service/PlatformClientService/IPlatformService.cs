using System;
using System.Threading.Tasks;

namespace Taiyun.SuckTheWater.Service.PlatformClientService
{
    /// <summary>
    /// Abstract interface for platform-specific services (Steam, Epic, etc.).
    /// Handles lobby creation, joining, and player management.
    /// </summary>
    public interface IPlatformService : IService
    {
        /// <summary>
        /// Current user's platform ID (Steam ID, Epic ID, etc.)
        /// </summary>
        ulong UserId { get; }
        
        /// <summary>
        /// Lobby host's platform ID
        /// </summary>
        ulong LobbyHostId { get; }
        
        /// <summary>
        /// Creates a new lobby for multiplayer.
        /// </summary>
        Task<bool> CreateLobby();
        
        /// <summary>
        /// Joins an existing lobby.
        /// </summary>
        /// <param name="lobbyId">Platform-specific lobby identifier</param>
        /// <returns>Tuple: (success, hostId)</returns>
        Task<(bool, string)> JoinLobby(ulong lobbyId);
        
        /// <summary>
        /// Leaves the current lobby.
        /// </summary>
        void LeaveLobby();
        
        /// <summary>
        /// Gets the current lobby's ID.
        /// </summary>
        ulong GetLobbyId();
        
        /// <summary>
        /// Gets all member IDs in the current lobby.
        /// </summary>
        ulong[] GetMemberIds();
        
        /// <summary>
        /// Updates the current scene metadata in the lobby.
        /// Call this when transitioning to a new scene (e.g., from LobbyScene to GameScene).
        /// Only the host should call this.
        /// </summary>
        /// <param name="sceneId">The scene identifier to store in lobby metadata</param>
        void SetCurrentScene(string sceneId);
        
        // Events
        event Action<string> OnCreatedLobby;
        event Action<string> OnEnteredLobby;
        event Action<string> OnJoinLobbyRequested; // When user clicks Steam invite
        event Action<ulong> OnMemberJoinedLobby;
        event Action<ulong> OnMemberDisconnectedLobby;
        event Action<ulong> OnMemberLeaveLobby;
    }
}