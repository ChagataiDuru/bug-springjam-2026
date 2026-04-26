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

        [Header("First-Intro Audio")]
        [Tooltip("Played only on the very first level intro. The fade-in duration is driven by this clip's length.")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Unified elevator arrival sound (rumble + door + bell baked into one clip).")]
        [SerializeField] private AudioClip _elevatorArrivalClip;

        [Header("First-Intro Timing")]
        [Tooltip("How long the screen stays pure black with audio playing before the fade begins. The clip's rumble portion should fit in this window.")]
        [SerializeField] private float _firstIntroBlackHold = 3.0f;
        [Tooltip("Duration of the black → clear fade. Tune so the fade resolves around the bell hit.")]
        [SerializeField] private float _firstIntroFadeInDuration = 2.1f;
        [Tooltip("Volume during the first intro (0..1).")]
        [SerializeField, Range(0f, 1f)] private float _firstIntroVolume = 0.7f;
        [Tooltip("If the clip is still playing after the fade completes, fade audio out over this duration.")]
        [SerializeField] private float _firstIntroAudioTailOut = 0.5f;

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

        public float GetIntroDuration(bool firstIntro)
        {
            if (!firstIntro) return TotalIntroDuration;
            return _firstIntroBlackHold
                 + _firstIntroFadeInDuration
                 + _textHoldDuration;
        }

        public async UniTask PlayIntroAsync(PlayerRole localRole, bool firstIntro)
        {
            if (!firstIntro)
            {
                await PlayIntroAsync(localRole);
                return;
            }

            SetRoleText(localRole);

            if (_blackOverlay != null) _blackOverlay.alpha = 1f;
            if (_textOverlay != null) _textOverlay.alpha = 0f;

            PlayElevatorClip();

            // Hold black while the rumble portion of the clip plays
            await UniTask.Delay(System.TimeSpan.FromSeconds(_firstIntroBlackHold),
                ignoreTimeScale: true);

            // Fade in — door+bell portion of the clip should land during this window
            var fadeTextIn = FadeCanvasGroup(_textOverlay, 0f, 1f, _textFadeDuration);
            var fadeBlackOut = FadeCanvasGroup(_blackOverlay, 1f, 0f, _firstIntroFadeInDuration);
            await UniTask.WhenAll(fadeTextIn, fadeBlackOut);

            // Tail out audio if it's still playing past the visual fade
            FadeOutClip(_firstIntroAudioTailOut).Forget();

            await UniTask.Delay(System.TimeSpan.FromSeconds(_textHoldDuration),
                ignoreTimeScale: true);

            await FadeCanvasGroup(_textOverlay, 1f, 0f, _textFadeDuration);
        }

        private void PlayElevatorClip()
        {
            if (_audioSource == null || _elevatorArrivalClip == null) return;
            _audioSource.clip = _elevatorArrivalClip;
            _audioSource.loop = false;
            _audioSource.volume = _firstIntroVolume;
            _audioSource.Play();
        }

        private async UniTaskVoid FadeOutClip(float duration)
        {
            if (_audioSource == null || !_audioSource.isPlaying) return;
            float startVol = _audioSource.volume;
            float t = 0f;
            while (t < duration && _audioSource.isPlaying)
            {
                t += Time.unscaledDeltaTime;
                _audioSource.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(t / duration));
                await UniTask.Yield();
            }
            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.volume = startVol;
            }
        }

    }
}