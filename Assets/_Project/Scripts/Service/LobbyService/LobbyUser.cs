using UnityEngine;

namespace Taiyun.SuckTheWater.Service.LobbyService
{
    /// <summary>
    /// Represents a user in a lobby.
    /// Contains display info and ready state.
    /// </summary>
    [System.Serializable]
    public struct LobbyUser
    {
        /// <summary>
        /// Platform-specific user ID (Steam ID, Epic ID, etc.)
        /// </summary>
        public string Id;
        
        /// <summary>
        /// User's display name from the platform
        /// </summary>
        public string DisplayName;
        
        /// <summary>
        /// Whether the user has marked themselves as ready
        /// </summary>
        public bool IsReady;
        
        /// <summary>
        /// User's avatar image (optional, may be null)
        /// </summary>
        public Texture2D Avatar;

        public override string ToString()
        {
            return $"LobbyUser({DisplayName}, Ready={IsReady})";
        }
    }
    
    /// <summary>
    /// Represents a friend from the platform's friend list.
    /// Used for invite functionality.
    /// </summary>
    [System.Serializable]
    public struct FriendUser
    {
        public string Id;
        public string DisplayName;
        public Texture2D Avatar;
        
        public override string ToString()
        {
            return $"Friend({DisplayName})";
        }
    }
}
