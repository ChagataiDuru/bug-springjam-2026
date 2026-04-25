using System;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.Main.SysPopUps
{
    /// <summary>
    /// Popup with Yes/No buttons for user confirmation.
    /// Used for important decisions (e.g., "Leave lobby?", "Quit game?")
    /// </summary>
    public class PopUpYesNo : SysPopUp
    {
        [Header("Buttons")]
        [SerializeField] private Button _buttonYes;
        [SerializeField] private Button _buttonNo;
        
        private Action<bool> _onClickedYesNo;
        
        private void Awake()
        {
            if (_buttonYes != null)
                _buttonYes.onClick.AddListener(OnYesClicked);
            
            if (_buttonNo != null)
                _buttonNo.onClick.AddListener(OnNoClicked);
        }
        
        public void Show(string text, Action<bool> onClickedYesNo = null)
        {
            _onClickedYesNo = onClickedYesNo;
            base.Show(text);
        }
        
        private void OnYesClicked()
        {
            _onClickedYesNo?.Invoke(true);
            _onClickedYesNo = null;
            Hide();
        }
        
        private void OnNoClicked()
        {
            _onClickedYesNo?.Invoke(false);
            _onClickedYesNo = null;
            Hide();
        }
        
        private void OnDestroy()
        {
            if (_buttonYes != null)
                _buttonYes.onClick.RemoveListener(OnYesClicked);
            
            if (_buttonNo != null)
                _buttonNo.onClick.RemoveListener(OnNoClicked);
        }
    }
}
