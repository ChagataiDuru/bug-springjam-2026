using System.Collections.Generic;

namespace Taiyun.SuckTheWater.Service.LobbyService
{
    /// <summary>
    /// Represents a multiplayer lobby with all its state.
    /// Platform-agnostic structure that can work with Steam, Epic, Unity Services, etc.
    /// </summary>
    [System.Serializable]
    public struct Lobby
    {
        public string Name;
        public bool IsValid;
        public string LobbyId;
        public string LobbyCode;
        public int MaxPlayers;
        public Dictionary<string, string> Properties;
        public bool IsOwner;
        public List<LobbyUser> Members;
        
        /// <summary>
        /// Platform-specific server object (e.g., Relay allocation for Unity Services)
        /// </summary>
        public object ServerObject;

        /// <summary>
        /// Checks if lobby state has meaningfully changed.
        /// Used to prevent redundant UI updates.
        /// </summary>
        public bool HasChanged(Lobby other)
        {
            if (!IsValid || Name != other.Name || LobbyId != other.LobbyId || 
                LobbyCode != other.LobbyCode || Members.Count != other.Members.Count || 
                Properties.Count != other.Properties.Count || ServerObject != other.ServerObject)
                return true;

            for (int i = 0; i < other.Members.Count; i++)
            {
                var newMember = other.Members[i];
                var oldMember = Members[i];

                if (newMember.Id != oldMember.Id || 
                    newMember.IsReady != oldMember.IsReady || 
                    newMember.DisplayName != oldMember.DisplayName || 
                    newMember.Avatar != oldMember.Avatar)
                    return true;
            }

            foreach (var oldProp in Properties)
            {
                if (!other.Properties.TryGetValue(oldProp.Key, out var newVal) || oldProp.Value != newVal)
                    return true;
            }

            return false;
        }
        
        /// <summary>
        /// Gets the host's user ID from the lobby.
        /// </summary>
        public string GetHostId()
        {
            if (Properties != null && Properties.TryGetValue(LobbyConstants.HOST_ID_KEY, out var hostId))
                return hostId;
            return string.Empty;
        }
        
        /// <summary>
        /// Checks if the server is ready to accept connections.
        /// </summary>
        public bool IsServerReady()
        {
            if (Properties != null && Properties.TryGetValue(LobbyConstants.SERVER_READY_KEY, out var ready))
                return ready == "true";
            return false;
        }
        
        /// <summary>
        /// Checks if the game has started.
        /// </summary>
        public bool HasStarted()
        {
            if (Properties != null && Properties.TryGetValue(LobbyConstants.STARTED_KEY, out var started))
                return started == "true";
            return false;
        }
    }

    /// <summary>
    /// Factory for creating Lobby instances with proper initialization.
    /// </summary>
    public static class LobbyFactory
    {
        public static Lobby Create(
            string name, 
            string lobbyId, 
            int maxPlayers, 
            bool isOwner, 
            List<LobbyUser> members, 
            Dictionary<string, string> properties)
        {
            return new Lobby
            {
                Name = name,
                IsValid = true,
                LobbyId = lobbyId,
                MaxPlayers = maxPlayers,
                Properties = properties ?? new Dictionary<string, string>(),
                IsOwner = isOwner,
                Members = members ?? new List<LobbyUser>()
            };
        }

        public static Lobby Create(
            string name, 
            string lobbyId, 
            string lobbyCode, 
            int maxPlayers, 
            bool isOwner, 
            List<LobbyUser> members, 
            Dictionary<string, string> properties, 
            object serverObject = null)
        {
            return new Lobby
            {
                Name = name,
                IsValid = true,
                LobbyId = lobbyId,
                LobbyCode = lobbyCode,
                MaxPlayers = maxPlayers,
                Properties = properties ?? new Dictionary<string, string>(),
                IsOwner = isOwner,
                Members = members ?? new List<LobbyUser>(),
                ServerObject = serverObject
            };
        }
        
        public static Lobby CreateInvalid()
        {
            return new Lobby { IsValid = false };
        }
    }
    
    /// <summary>
    /// Constants for lobby metadata keys.
    /// </summary>
    public static class LobbyConstants
    {
        public const string HOST_ID_KEY = "HostId";
        public const string SERVER_READY_KEY = "ServerReady";
        public const string STARTED_KEY = "Started";
        public const string CURRENT_SCENE_KEY = "CurrentScene";
        public const string NAME_KEY = "Name";
    }
}
