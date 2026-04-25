using UnityEngine;
using TMPro;

namespace Taiyun.SuckTheWater.Main.SysPopUps
{
    /// <summary>
    /// Base class for system popups.
    /// Provides common functionality for all popup types.
    /// </summary>
    public abstract class SysPopUp : MonoBehaviour
    {
        [Header("Common UI Elements")]
        [SerializeField] protected GameObject _panel;
        [SerializeField] protected TMP_Text _textMessage;
        
        /// <summary>
        /// Shows the popup with given text.
        /// </summary>
        public virtual void Show(string text)
        {
            if (_textMessage != null)
                _textMessage.text = text;
            
            if (_panel != null)
                _panel.SetActive(true);
        }
        
        /// <summary>
        /// Hides the popup.
        /// </summary>
        public virtual void Hide()
        {
            if (_panel != null)
                _panel.SetActive(false);
        }
        
        /// <summary>
        /// Checks if popup is currently visible.
        /// </summary>
        public bool IsVisible()
        {
            return _panel != null && _panel.activeSelf;
        }
    }
}
