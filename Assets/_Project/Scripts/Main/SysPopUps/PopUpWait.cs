using UnityEngine;

namespace Taiyun.SuckTheWater.Main.SysPopUps
{
    /// <summary>
    /// Non-dismissible popup shown during waiting/loading states.
    /// No buttons - must be hidden programmatically.
    /// Example: "Connecting to server...", "Checking internet connection..."
    /// </summary>
    public class PopUpWait : SysPopUp
    {
        [Header("Optional Loading Spinner")]
        [SerializeField] private GameObject _loadingSpinner;
        
        public override void Show(string text)
        {
            base.Show(text);
            
            if (_loadingSpinner != null)
                _loadingSpinner.SetActive(true);
        }
        
        public override void Hide()
        {
            base.Hide();
            
            if (_loadingSpinner != null)
                _loadingSpinner.SetActive(false);
        }
    }
}
