using System;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.Main.SysPopUps
{
    /// <summary>
    /// Simple popup with OK button.
    /// Used for notifications and error messages.
    /// </summary>
    public class PopUpOkay : SysPopUp
    {
        [Header("OK Button")]
        [SerializeField] private Button _buttonOk;
        
        private Action _onClickedOk;
        
        private void Awake()
        {
            if (_buttonOk != null)
                _buttonOk.onClick.AddListener(OnOkClicked);
        }
        
        public void Show(string text, Action onClickedOk = null)
        {
            _onClickedOk = onClickedOk;
            base.Show(text);
        }
        
        private void OnOkClicked()
        {
            _onClickedOk?.Invoke();
            _onClickedOk = null;
            Hide();
        }
        
        private void OnDestroy()
        {
            if (_buttonOk != null)
                _buttonOk.onClick.RemoveListener(OnOkClicked);
        }
    }
}
