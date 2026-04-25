using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.HubScene
{
    /// <summary>
    /// UI component for displaying a single player slot in the lobby.
    /// Shows player name, host indicator, and ready status.
    /// 
    /// Used by HubSceneManager to display Steam Lobby members.
    /// </summary>
    public class LobbyPlayerSlot : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _slotPanel;
        [SerializeField] private TMP_Text _playerNameText;
        [SerializeField] private Image _readyIcon;
        [SerializeField] private GameObject _hostCrown;
        [SerializeField] private Image _avatarImage;

        [Header("Ready State Colors")]
        [SerializeField] private Color _readyColor = new Color(0.2f, 0.8f, 0.2f); // Green
        [SerializeField] private Color _notReadyColor = new Color(0.8f, 0.2f, 0.2f); // Red

        [Header("Empty State")]
        [SerializeField] private string _emptySlotText = "Waiting...";

        /// <summary>
        /// Configures the slot to display a player.
        /// </summary>
        /// <param name="playerName">Display name from Steam</param>
        /// <param name="isHost">Whether this player is the lobby host</param>
        /// <param name="isReady">Whether this player is ready</param>
        /// <param name="avatar">Optional avatar texture from Steam</param>
        public void SetPlayer(string playerName, bool isHost, bool isReady, Texture2D avatar = null)
        {
            if (_slotPanel != null) 
                _slotPanel.SetActive(true);
            
            if (_playerNameText != null) 
                _playerNameText.text = playerName;
            
            if (_hostCrown != null) 
                _hostCrown.SetActive(isHost);
            
            if (_readyIcon != null)
                _readyIcon.color = isReady ? _readyColor : _notReadyColor;

            if (_avatarImage != null && avatar != null)
            {
                _avatarImage.sprite = Sprite.Create(
                    avatar,
                    new Rect(0, 0, avatar.width, avatar.height),
                    new Vector2(0.5f, 0.5f)
                );
                _avatarImage.gameObject.SetActive(true);
            }
            else if (_avatarImage != null)
            {
                _avatarImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Configures the slot to show as empty/available.
        /// </summary>
        public void SetEmpty()
        {
            if (_slotPanel != null) 
                _slotPanel.SetActive(false);
        }

        /// <summary>
        /// Shows the slot as "waiting for player" state.
        /// </summary>
        public void SetWaiting()
        {
            if (_slotPanel != null) 
                _slotPanel.SetActive(true);
            
            if (_playerNameText != null) 
                _playerNameText.text = _emptySlotText;
            
            if (_hostCrown != null) 
                _hostCrown.SetActive(false);
            
            if (_readyIcon != null)
                _readyIcon.color = Color.gray;

            if (_avatarImage != null)
                _avatarImage.gameObject.SetActive(false);
        }
    }
}