using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.UI
{
    public class InteractionPromptUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text _promptText;
        [SerializeField] private Image _progressFill;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _startSfx;
        [SerializeField] private AudioClip _progressLoopSfx;
        [SerializeField] private AudioClip _completeSfx;
        [SerializeField] private AudioClip _cancelSfx;

        private bool _isProgressLooping;

        private void Awake()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }

            SetVisible(false);
            SetProgress(0f);
        }

        public void Show(string prompt)
        {
            if (_promptText != null)
            {
                _promptText.text = prompt;
            }

            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
            SetProgress(0f);
            StopProgressLoop();
        }

        public void SetProgress(float normalized)
        {
            if (_progressFill != null)
            {
                _progressFill.fillAmount = Mathf.Clamp01(normalized);
            }
        }

        public void OnInteractionStart()
        {
            PlayOneShot(_startSfx);
            StartProgressLoop();
        }

        public void OnInteractionCancel()
        {
            StopProgressLoop();
            PlayOneShot(_cancelSfx);
        }

        public void OnInteractionComplete()
        {
            StopProgressLoop();
            PlayOneShot(_completeSfx);
        }

        private void SetVisible(bool isVisible)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = isVisible ? 1f : 0f;
                _canvasGroup.blocksRaycasts = isVisible;
                _canvasGroup.interactable = isVisible;
            }
            else
            {
                if (gameObject.activeSelf != isVisible)
                {
                    gameObject.SetActive(isVisible);
                }
            }
        }

        private void StartProgressLoop()
        {
            if (_audioSource == null || _progressLoopSfx == null || _isProgressLooping)
            {
                return;
            }

            _audioSource.loop = true;
            _audioSource.clip = _progressLoopSfx;
            _audioSource.Play();
            _isProgressLooping = true;
        }

        private void StopProgressLoop()
        {
            if (_audioSource == null || !_isProgressLooping)
            {
                return;
            }

            _audioSource.Stop();
            _audioSource.loop = false;
            _audioSource.clip = null;
            _isProgressLooping = false;
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (_audioSource == null || clip == null)
            {
                return;
            }

            _audioSource.PlayOneShot(clip);
        }
    }
}
