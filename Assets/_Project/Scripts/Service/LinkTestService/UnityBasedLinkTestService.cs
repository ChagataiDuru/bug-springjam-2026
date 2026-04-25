using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Taiyun.SuckTheWater.Service.LinkTestService
{
    /// <summary>
    /// Unity-based implementation of internet connectivity checking.
    /// Uses UnityWebRequest to ping a reliable server.
    /// </summary>
    public class UnityBasedLinkTestService : ILinkTestService
    {
        private const string TEST_URL = "https://www.google.com";
        private const int TIMEOUT_SECONDS = 5;
        
        private bool _isInitialized;
        private bool _lastLinkStatus = true;
        
        public event Action<bool> OnLinkStatusChanged;

        public async Task<bool> InitService(params object[] args)
        {
            await Task.Yield();
            
            if (_isInitialized)
            {
                Debug.LogWarning($"[{nameof(UnityBasedLinkTestService)}] Already initialized");
                return true;
            }
            
            _isInitialized = true;
            Debug.Log($"[{nameof(UnityBasedLinkTestService)}] Initialized successfully");
            return true;
        }

        public async Task<bool> StartService()
        {
            await Task.Yield();
            Debug.Log($"[{nameof(UnityBasedLinkTestService)}] Service started");
            return true;
        }

        public async Task<bool> StopService()
        {
            await Task.Yield();
            _isInitialized = false;
            Debug.Log($"[{nameof(UnityBasedLinkTestService)}] Service stopped");
            return true;
        }

        public async Task<bool> CheckInternet()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning($"[{nameof(UnityBasedLinkTestService)}] Service not initialized");
                return false;
            }

            try
            {
                using (UnityWebRequest webRequest = UnityWebRequest.Get(TEST_URL))
                {
                    webRequest.timeout = TIMEOUT_SECONDS;
                    
                    // Send request
                    var operation = webRequest.SendWebRequest();
                    
                    // Wait for completion
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }
                    
                    bool isConnected = webRequest.result == UnityWebRequest.Result.Success;
                    
                    // Fire event if status changed
                    if (isConnected != _lastLinkStatus)
                    {
                        _lastLinkStatus = isConnected;
                        OnLinkStatusChanged?.Invoke(isConnected);
                    }
                    
                    return isConnected;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(UnityBasedLinkTestService)}] Internet check failed: {e.Message}");
                
                // Fire event if this represents a status change
                if (_lastLinkStatus != false)
                {
                    _lastLinkStatus = false;
                    OnLinkStatusChanged?.Invoke(false);
                }
                
                return false;
            }
        }
    }
}
