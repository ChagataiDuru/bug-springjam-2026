using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks; // UniTask
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace Taiyun.SuckTheWater.Main
{
    /// <summary>
    /// Handles all scene loading operations (standard and networked).
    /// Displays loading UI with progress bar.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        [Header("Loading UI")]
        [Tooltip("The root GameObject of the Loading UI (e.g., LoadingUI_Panel).")]
        [SerializeField] private GameObject _uiPanel;
        
        private Image _sliderLoading;
        private TMP_Text _textSlider;
        
        // Track indices to handle unloading correctly
        private int _currentStandardSceneIndex = -1;
        private int _networkLoadedSceneIndex = -1;
        private bool _isNetworkedMode = false;

        // Helper for PurrNet Manager
        private NetworkManager NM => InstanceHandler.NetworkManager;
        
        public void Init()
        {
            if (_uiPanel != null)
            {
                _sliderLoading = _uiPanel.GetComponentInChildren<Image>(true);
                _textSlider = _uiPanel.GetComponentInChildren<TMP_Text>(true);
                _uiPanel.SetActive(false);
            }
            
            Debug.Log("[SceneLoader] Initialized");
        }
        
        /// <summary>
        /// Enables networked scene loading mode.
        /// </summary>
        public void InitNetworked()
        {
            var nm = InstanceHandler.NetworkManager;
            if (nm == null)
            {
                Debug.LogError("[SceneLoader] NetworkManager not found");
                return;
            }
    
            // Subscribe to PurrNet's scene events if needed
            _isNetworkedMode = true;
            Debug.Log("[SceneLoader] Networked mode initialized");
        }

        #region Standard Scene Loading (Local)
        
        /// <summary>
        /// Loads a scene asynchronously (non-networked).
        /// Shows loading UI with progress.
        /// </summary>
        public async UniTask LoadScene(Scenes scene)
        {
            if (_uiPanel != null) _uiPanel.SetActive(true);
            
            UpdateProgressUI(0);
            
            Debug.Log($"[SceneLoader] Loading scene: {scene}");
            
            // Standard Unity Load
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync((int)scene, LoadSceneMode.Single);
            
            while (!asyncLoad.isDone)
            {
                UpdateProgressUI(asyncLoad.progress);
                await UniTask.Yield();
            }
            
            UpdateProgressUI(1);
            await UniTask.Delay(100);
            
            if (_uiPanel != null) _uiPanel.SetActive(false);
            
            _currentStandardSceneIndex = (int)scene; 
            Debug.Log($"[SceneLoader] Scene loaded: {scene}");
        }
        
        /// <summary>
        /// Unloads a scene asynchronously.
        /// </summary>
        public async UniTask UnloadScene(Scenes scene)
        {
            Debug.Log($"[SceneLoader] Unloading scene: {scene}");
            
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync((int)scene);
            
            if (asyncUnload != null)
            {
                while (!asyncUnload.isDone)
                {
                    await UniTask.Yield();
                }
            }
            
            await Resources.UnloadUnusedAssets();
            Debug.Log($"[SceneLoader] Scene unloaded: {scene}");
        }
        
        #endregion

        #region Networked Scene Loading
        
        /// <summary>
        /// Only the host/server should call this - all clients will load automatically.
        /// </summary>
        public async UniTask LoadSceneNetworked(Scenes scene)
        {
            if (!_isNetworkedMode)
            {
                Debug.LogError("[SceneLoader] Cannot load networked scene - networked mode not initialized");
                return;
            }
            
            if (NM == null)
            {
                Debug.LogError("[SceneLoader] NetworkManager not found");
                return;
            }
            
            if (!NM.isServer)
            {
                Debug.LogWarning("[SceneLoader] client should not call LoadSceneNetworked.");
                //return;
            }
            
            
            if (_uiPanel != null) _uiPanel.SetActive(true);
            UpdateProgressUI(0);
            
            // Store previous index for unloading
            int previousNetworkSceneIndex = _networkLoadedSceneIndex;
            
            // Get Scene Name (PurrNet loads by name usually or ID)
            string scenePath = SceneUtility.GetScenePathByBuildIndex((int)scene);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            
            Debug.Log($"[SceneLoader] Loading networked scene: {sceneName}");
            
            try
            {
                // 1. Configure Settings
                var settings = new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None // Or Physics3D if separate physics scene needed
                };
                // 2. Start Loading
                // PurrNet's LoadSceneAsync fires the logic. It doesn't strictly return an 
                // "AsyncOperation" we can await progress on easily, so we "Fake" the UI progress
                // while waiting for the scene to actually appear in the scene list.
                await NM.sceneModule.LoadSceneAsync(sceneName, settings);

                // 3. Wait for load to finish
                // We fake a progress bar duration (e.g. 1 second) or wait until SceneManager reports it loaded.
                float fakeProgress = 0f;
                bool isLoaded = false;
                
                while (!isLoaded)
                {
                    // Check if Unity has finished loading it
                    Scene loadedScene = SceneManager.GetSceneByName(sceneName);
                    if (loadedScene.IsValid() && loadedScene.isLoaded)
                    {
                        isLoaded = true;
                        fakeProgress = 1f;
                    }
                    else
                    {
                        fakeProgress += Time.deltaTime * 2f; // Fill over ~0.5s
                        if (fakeProgress > 0.9f) fakeProgress = 0.9f; // Hold at 90%
                    }

                    UpdateProgressUI(fakeProgress);
                    await UniTask.Yield();
                }
                
                // 4. Activate Scene
                Scene finalScene = SceneManager.GetSceneByName(sceneName);
                if (finalScene.IsValid())
                {
                    SceneManager.SetActiveScene(finalScene);
                    _networkLoadedSceneIndex = finalScene.buildIndex;
                }
                
                UpdateProgressUI(1f);
                
                // 5. Unload Previous Networked Scene
                if (previousNetworkSceneIndex != -1)
                {
                    string prevScenePath = SceneUtility.GetScenePathByBuildIndex(previousNetworkSceneIndex);
                    string prevSceneName = Path.GetFileNameWithoutExtension(prevScenePath);
                    
                    Debug.Log($"[SceneLoader] Unloading previous network scene: {prevSceneName}");
                    await NM.sceneModule.UnloadSceneAsync(prevSceneName);
                }

                // 6. Cleanup Local Scenes (Standard Unity Unload)
                // This cleans up any leftovers that weren't managed by PurrNet
                await UnloadUnwantedLocalScenes((int)scene, previousNetworkSceneIndex);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SceneLoader] Network load failed: {e.Message}");
            }

            await UniTask.Delay(100);
            if (_uiPanel != null) _uiPanel.SetActive(false);
            
            await Resources.UnloadUnusedAssets();
        }

        private async UniTask UnloadUnwantedLocalScenes(int activeSceneIndex, int previousNetworkIndex)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                
                // Keep Supreme/Managers, Keep Current, Keep what we just asked PurrNet to unload (PurrNet handles that)
                if (s.name != "1_Supreme" && 
                    s.name != "Supreme" && 
                    s.buildIndex != activeSceneIndex &&
                    s.buildIndex != previousNetworkIndex) 
                {
                    if (s.isLoaded)
                    {
                        Debug.Log($"[SceneLoader] Cleaning up local scene: {s.name}");
                        await SceneManager.UnloadSceneAsync(s);
                    }
                }
            }
        }
                
        #endregion
        
        private void UpdateProgressUI(float progress)
        {
            if (_sliderLoading != null)
                _sliderLoading.fillAmount = progress;
            
            if (_textSlider != null)
                _textSlider.text = (int)(progress * 100f) + "%";
        }
    }
}