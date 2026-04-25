using UnityEngine;

namespace Taiyun.SuckTheWater.Main
{
    /// <summary>
    /// Acts as the bridge between Main Menu and Game Scene.
    /// It persists lobby metadata (like the ID to join) so the next scene knows what to do.
    /// </summary>
    public static class GameSessionContext
    {
        public static bool IsActive { get; private set; }
        public static ulong LobbyId { get; private set; }
        public static ulong HostSteamId { get; private set; }
        public static bool IsHost { get; private set; }

        public static void SetSession(ulong lobbyId, ulong hostSteamId, bool isHost)
        {
            LobbyId = lobbyId;
            HostSteamId = hostSteamId;
            IsHost = isHost;
            IsActive = true;
        }

        public static void Clear()
        {
            IsActive = false;
            LobbyId = 0;
            HostSteamId = 0;
            IsHost = false;
        }
    }
}