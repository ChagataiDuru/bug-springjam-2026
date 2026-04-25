using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Taiyun.SuckTheWater.Main.SysPopUps
{
    /// <summary>
    /// Manages all system popups (OK, Yes/No, Wait).
    /// Accessed through SupremeManager.
    /// </summary>
    public class SysPopUpManager : MonoBehaviour
    {
        [Header("Popup References")]
        [SerializeField] private PopUpOkay _popUpOkay;
        [SerializeField] private PopUpYesNo _popUpYesNo;
        [SerializeField] private PopUpWait _popUpWait;
        
        private bool _isInitialized;

        public void Init()
        {
            if (_isInitialized)
            {
                Debug.LogWarning("[SysPopUpManager] Already initialized");
                return;
            }
            
            // Validate references
            if (_popUpOkay == null)
                Debug.LogError("[SysPopUpManager] PopUpOkay reference is missing!");
            
            if (_popUpYesNo == null)
                Debug.LogError("[SysPopUpManager] PopUpYesNo reference is missing!");
            
            if (_popUpWait == null)
                Debug.LogError("[SysPopUpManager] PopUpWait reference is missing!");
            
            // Hide all popups initially
            _popUpOkay?.Hide();
            _popUpYesNo?.Hide();
            _popUpWait?.Hide();
            
            _isInitialized = true;
            Debug.Log("[SysPopUpManager] Initialized successfully");
        }

        #region Show Popup Methods
        
        /// <summary>
        /// Shows OK popup with optional callback.
        /// </summary>
        public void ShowPopUpOkay(string text, Action onClickedOk = null)
        {
            if (_popUpOkay == null)
            {
                Debug.LogError("[SysPopUpManager] Cannot show PopUpOkay - reference is null");
                return;
            }
            
            _popUpOkay.Show(text, onClickedOk);
        }
        
        /// <summary>
        /// Shows Yes/No popup with optional callback.
        /// </summary>
        public void ShowPopUpYesNo(string text, Action<bool> onClickedYesNo = null)
        {
            if (_popUpYesNo == null)
            {
                Debug.LogError("[SysPopUpManager] Cannot show PopUpYesNo - reference is null");
                return;
            }
            
            _popUpYesNo.Show(text, onClickedYesNo);
        }
        
        /// <summary>
        /// Shows Wait popup (non-dismissible).
        /// </summary>
        public void ShowPopUpWait(string text)
        {
            if (_popUpWait == null)
            {
                Debug.LogError("[SysPopUpManager] Cannot show PopUpWait - reference is null");
                return;
            }
            
            _popUpWait.Show(text);
        }
        
        /// <summary>
        /// Hides Wait popup.
        /// </summary>
        public void HidePopUpWait()
        {
            if (_popUpWait == null)
                return;
            
            _popUpWait.Hide();
        }
        
        #endregion

        #region Async Popup Methods
        
        /// <summary>
        /// Shows OK popup and waits for user to click OK.
        /// </summary>
        public async Task WaitPopUpOk(string text, int millisecondsDelay = 100)
        {
            bool clicked = false;
            
            ShowPopUpOkay(text, () => clicked = true);
            
            while (!clicked)
            {
                await Task.Delay(millisecondsDelay);
            }
        }
        
        /// <summary>
        /// Shows Yes/No popup and waits for user response.
        /// </summary>
        /// <returns>True if Yes clicked, False if No clicked</returns>
        public async Task<bool> WaitPopUpYesOrNo(string text, int millisecondsDelay = 100)
        {
            bool? result = null;
            
            ShowPopUpYesNo(text, (answer) => result = answer);
            
            while (!result.HasValue)
            {
                await Task.Delay(millisecondsDelay);
            }
            
            return result.Value;
        }
        
        #endregion
    }
}
