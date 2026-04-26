using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Taiyun.SuckTheWater.GameScene
{
    /// <summary>
    /// Black-screen fade + role/intro text overlay.
    /// All timings are inspector-configurable.
    /// </summary>
    public class LevelTransitionUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CanvasGroup _blackOverlay;
        [SerializeField] private CanvasGroup _textOverlay;
        [SerializeField] private TMP_Text _primaryText;
        [SerializeField] private TMP_Text _secondaryText;

        [Header("Timings (seconds)")]
        [Tooltip("Time the screen stays fully black before fading in.")]
        [SerializeField] private float _holdBlackBeforeFadeIn = 0.4f;
        [Tooltip("Duration of black → clear fade.")]
        [SerializeField] private float _fadeInDuration = 1.6f;
        [Tooltip("Duration of clear → black fade.")]
        [SerializeField] private float _fadeOutDuration = 1.0f;
        [Tooltip("How long the intro text stays visible during the fade-in.")]
        [SerializeField] private float _textHoldDuration = 2.5f;
        [Tooltip("Fade duration for the text overlay specifically.")]
        [SerializeField] private float _textFadeDuration = 0.5f;

        [Header("Strings")]
        [SerializeField] private string _upperRoleHeadline = "YOU ARE ON THE UPPER FLOOR";
        [SerializeField] private string _lowerRoleHeadline = "YOU ARE ON THE LOWER FLOOR";
        [SerializeField] private string _trustLine = "Trust your buddy.";
        [SerializeField] private string _failHeadline = "...";
        [SerializeField] private string _failLine = "The fall does not end.";

        public float TotalIntroDuration =>
            _holdBlackBeforeFadeIn + _fadeInDuration + _textHoldDuration;

        public float FadeOutDuration => _fadeOutDuration;

        private void Awake()
        {
            if (_blackOverlay != null) _blackOverlay.alpha = 1f;
            if (_textOverlay != null) _textOverlay.alpha = 0f;
        }

        public async UniTask PlayIntroAsync(PlayerRole localRole)
        {
            SetRoleText(localRole);

            if (_blackOverlay != null) _blackOverlay.alpha = 1f;
            if (_textOverlay != null) _textOverlay.alpha = 0f;

            await UniTask.Delay(TimeSpan.FromSeconds(_holdBlackBeforeFadeIn),
                ignoreTimeScale: true);

            // Fade text in & black out simultaneously
            var fadeTextIn = FadeCanvasGroup(_textOverlay, 0f, 1f, _textFadeDuration);
            var fadeBlackOut = FadeCanvasGroup(_blackOverlay, 1f, 0f, _fadeInDuration);
            await UniTask.WhenAll(fadeTextIn, fadeBlackOut);

            await UniTask.Delay(TimeSpan.FromSeconds(_textHoldDuration),
                ignoreTimeScale: true);

            await FadeCanvasGroup(_textOverlay, 1f, 0f, _textFadeDuration);
        }

        public async UniTask PlayFadeOutAsync()
        {
            await FadeCanvasGroup(_blackOverlay, _blackOverlay != null ? _blackOverlay.alpha : 0f, 1f, _fadeOutDuration);
        }

        public async UniTask PlayFailFadeAsync()
        {
            SetFailText();
            var fadeText = FadeCanvasGroup(_textOverlay, 0f, 1f, _textFadeDuration);
            var fadeBlack = FadeCanvasGroup(_blackOverlay, 0f, 1f, _fadeOutDuration);
            await UniTask.WhenAll(fadeText, fadeBlack);
            await UniTask.Delay(TimeSpan.FromSeconds(_textHoldDuration), ignoreTimeScale: true);
            await FadeCanvasGroup(_textOverlay, 1f, 0f, _textFadeDuration);
        }

        public void SnapToBlack()
        {
            if (_blackOverlay != null) _blackOverlay.alpha = 1f;
            if (_textOverlay != null) _textOverlay.alpha = 0f;
        }

        private void SetRoleText(PlayerRole role)
        {
            if (_primaryText == null) return;
            _primaryText.text = role == PlayerRole.Upper ? _upperRoleHeadline : _lowerRoleHeadline;
            if (_secondaryText != null) _secondaryText.text = _trustLine;
        }

        private void SetFailText()
        {
            if (_primaryText != null) _primaryText.text = _failHeadline;
            if (_secondaryText != null) _secondaryText.text = _failLine;
        }

        private async UniTask FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
        {
            if (cg == null || duration <= 0f)
            {
                if (cg != null) cg.alpha = to;
                return;
            }
            float t = 0f;
            cg.alpha = from;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                await UniTask.Yield();
            }
            cg.alpha = to;
        }
    }
}