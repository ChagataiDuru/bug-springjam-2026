using System;
using System.Threading.Tasks;
using Taiyun.SuckTheWater.Main.SysPopUps;
using Taiyun.SuckTheWater.Service;
using OrangeWolf.Generic;
using UnityEngine;

namespace Taiyun.SuckTheWater.Main
{
    /// <summary>
    /// Supreme orchestrator of the game.
    /// Persistent singleton that manages services, scene loading, and popups.
    /// Lives in the Supreme scene which is never unloaded.
    /// </summary>
    public sealed class SupremeManager : Singleton<SupremeManager>
    {
        [Header("Core Components")]
        [SerializeField] private SceneLoader _sceneLoader;
        [SerializeField] private SysPopUpManager _sysPopUpManager;
        
        public ServiceManager ServiceManager { get; private set; }
        
        /// <summary>
        /// Event fired every Update() - used by services that need regular updates (e.g., Steam callbacks)
        /// </summary>
        public event Action OnUpdate;
        
        protected override void OnAwake()
        {
            base.OnAwake();
            
            // Application settings
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;

            try
            {
                Init();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SupremeManager] Initialization error: {e.Message}\n{e.StackTrace}");
            }
        }
        
        private async void Init()
        {
            await Task.Yield();
            
            Debug.Log("[SupremeManager] Initializing...");
            
            // Initialize SceneLoader
            if (_sceneLoader != null)
                _sceneLoader.Init();
            else
                Debug.LogError("[SupremeManager] SceneLoader reference is missing!");
            
            // Initialize PopUp system
            if (_sysPopUpManager != null)
                _sysPopUpManager.Init();
            else
                Debug.LogWarning("[SupremeManager] SysPopUpManager reference is missing!");
            
            // Initialize Service Manager
            ServiceManager = new ServiceManager();
            bool serviceInitStatus = await ServiceManager.Init();
            if (!serviceInitStatus)
            {
                Debug.LogError("[SupremeManager] ServiceManager initialization failed");
                ShowPopUpOkay("Failed to initialize services. Please restart the game.");
                return;
            }
            
            // Register internet connectivity tracker
            ServiceManager.LinkTestService.OnLinkStatusChanged += OnLinkStatusChanged;
            
            Debug.Log("[SupremeManager] Initialization complete");
            
            Debug.Log("[SupremeManager] Loading Hub scene...");
            LoadScene(Scenes.HubScene);
        }
        
        private void OnLinkStatusChanged(bool hasInternet)
        {
            if (!hasInternet)
            {
                ShowPopUpWait("No internet connection. Please check your network...");
            }
            else
            {
                HidePopUpWait();
            }
        }

        private void Update()
        {
            OnUpdate?.Invoke();
            ServiceManager?.Update();
        }

        private void OnDestroy()
        {
            ServiceManager?.StopAllServices();
        }
        
        #region SceneLoader Wrapper Methods
        
        /// <summary>
        /// Loads a scene (non-networked).
        /// </summary>
        public async void LoadScene(Scenes scene)
        {
            if (_sceneLoader == null)
            {
                Debug.LogError("[SupremeManager] Cannot load scene - SceneLoader is null");
                return;
            }
            
            await _sceneLoader.LoadScene(scene);
        }
        
        /// <summary>
        /// Unloads a scene.
        /// </summary>
        public async void UnloadScene(Scenes scene)
        {
            if (_sceneLoader == null)
            {
                Debug.LogError("[SupremeManager] Cannot unload scene - SceneLoader is null");
                return;
            }
            
            await _sceneLoader.UnloadScene(scene);
        }
        
        /// <summary>
        /// Loads a scene using networked scene management (host only).
        /// </summary>
        public async void LoadSceneNetworked(Scenes scene)
        {
            if (_sceneLoader == null)
            {
                Debug.LogError("[SupremeManager] Cannot load networked scene - SceneLoader is null");
                return;
            }
            
            await _sceneLoader.LoadSceneNetworked(scene);
        }
        
        /// <summary>
        /// Enables networked scene loading mode.
        /// Call this after NetworkManager is initialized.
        /// </summary>
        public void InitNetworkedSceneLoader()
        {
            if (_sceneLoader == null)
            {
                Debug.LogError("[SupremeManager] Cannot init networked mode - SceneLoader is null");
                return;
            }
            
            _sceneLoader.InitNetworked();
        }
        
        #endregion

        #region PopUp Wrapper Methods
        
        /// <summary>
        /// Shows OK popup with optional callback.
        /// </summary>
        public void ShowPopUpOkay(string text, Action onClickedOk = null)
        {
            if (_sysPopUpManager == null)
            {
                Debug.LogError("[SupremeManager] Cannot show popup - SysPopUpManager is null");
                return;
            }
            
            _sysPopUpManager.ShowPopUpOkay(text, onClickedOk);
        }
        
        /// <summary>
        /// Shows Yes/No popup with optional callback.
        /// </summary>
        public void ShowPopUpYesNo(string text, Action<bool> onClickedYesNo = null)
        {
            if (_sysPopUpManager == null)
            {
                Debug.LogError("[SupremeManager] Cannot show popup - SysPopUpManager is null");
                return;
            }
            
            _sysPopUpManager.ShowPopUpYesNo(text, onClickedYesNo);
        }
        
        /// <summary>
        /// Shows Wait popup (non-dismissible loading popup).
        /// </summary>
        public void ShowPopUpWait(string text)
        {
            if (_sysPopUpManager == null)
            {
                Debug.LogError("[SupremeManager] Cannot show popup - SysPopUpManager is null");
                return;
            }
            
            _sysPopUpManager.ShowPopUpWait(text);
        }
        
        /// <summary>
        /// Hides Wait popup.
        /// </summary>
        public void HidePopUpWait()
        {
            if (_sysPopUpManager == null)
                return;
            
            _sysPopUpManager.HidePopUpWait();
        }
        
        /// <summary>
        /// Shows OK popup and waits for user to click OK.
        /// </summary>
        public async Task WaitPopUpOk(string text, int millisecondsDelay = 100)
        {
            if (_sysPopUpManager == null)
            {
                Debug.LogError("[SupremeManager] Cannot show popup - SysPopUpManager is null");
                return;
            }
            
            await _sysPopUpManager.WaitPopUpOk(text, millisecondsDelay);
        }
        
        /// <summary>
        /// Shows Yes/No popup and waits for user response.
        /// </summary>
        public async Task<bool> WaitPopUpYesOrNo(string text, int millisecondsDelay = 100)
        {
            if (_sysPopUpManager == null)
            {
                Debug.LogError("[SupremeManager] Cannot show popup - SysPopUpManager is null");
                return false;
            }
            
            return await _sysPopUpManager.WaitPopUpYesOrNo(text, millisecondsDelay);
        }
        
        #endregion
    }
}
