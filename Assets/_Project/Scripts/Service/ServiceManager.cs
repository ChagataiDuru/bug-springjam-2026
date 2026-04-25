using System;
using System.Threading.Tasks;
using Taiyun.SuckTheWater.Service.LinkTestService;
using Taiyun.SuckTheWater.Service.LobbyService;
using UnityEngine;

namespace Taiyun.SuckTheWater.Service
{
    /// <summary>
    /// Manages all game services.
    /// Now includes LobbyManager for unified lobby management.
    /// 
    /// Services:
    /// - LinkTestService: Internet connectivity check
    /// - LobbyManager: Platform-agnostic lobby management
    /// - (Internal) SteamLobbyProvider: Steam-specific implementation
    /// </summary>
    public sealed class ServiceManager
    {
        #region Services

        /// <summary>
        /// Service for checking internet connectivity.
        /// </summary>
        public ILinkTestService LinkTestService { get; private set; }

        /// <summary>
        /// Manager for lobby operations (create, join, leave, etc.).
        /// This is the primary API for lobby interactions.
        /// </summary>
        public LobbyManager LobbyManager { get; private set; }

        #endregion

        #region Internal

        // The platform-specific provider (hidden from game code)
        private ILobbyProvider _lobbyProvider;

        #endregion

        #region Initialization

        private bool _isInitialized;

        public ServiceManager()
        {
            // Create service instances (but don't initialize yet)
            LinkTestService = new UnityBasedLinkTestService();
            LobbyManager = new LobbyManager();

#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            _lobbyProvider = new SteamLobbyProvider();
#else
            Debug.LogError("[ServiceManager] Platform not supported! Only Windows/Mac/Linux with Steam are supported.");
#endif
        }

        /// <summary>
        /// Whether services have been initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initializes all services sequentially.
        /// Safe to call multiple times - will skip if already initialized.
        /// </summary>
        public async Task<bool> Init()
        {
            if (_isInitialized)
            {
                Debug.Log("[ServiceManager] Already initialized, skipping");
                return true;
            }

            await Task.Yield();

            try
            {
                // Initialize Link Test Service
                bool initLinkTestService = await LinkTestService.InitService();
                if (!initLinkTestService)
                {
                    Debug.LogError("[ServiceManager] LinkTestService.InitService() failed");
                    return false;
                }

                // Initialize Lobby Manager with platform provider
                if (_lobbyProvider != null)
                {
                    await LobbyManager.Initialize(_lobbyProvider);
                }
                else
                {
                    Debug.LogError("[ServiceManager] No lobby provider available");
                    return false;
                }

                _isInitialized = true;
                Debug.Log("[ServiceManager] All services initialized successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServiceManager] Initialization error: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Updates services that need per-frame processing.
        /// Should be called from SupremeManager.Update().
        /// </summary>
        public void Update()
        {
            LobbyManager?.Update();
        }

        /// <summary>
        /// Stops all services and cleans up resources.
        /// Called on application quit.
        /// </summary>
        public async void StopAllServices()
        {
            await Task.Yield();

            try
            {
                await LinkTestService.StopService();
                LobbyManager?.Shutdown();

                Debug.Log("[ServiceManager] All services stopped");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServiceManager] Error stopping services: {e.Message}");
            }
        }

        #endregion
    }
}