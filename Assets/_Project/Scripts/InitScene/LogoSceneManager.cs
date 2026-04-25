using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.InitScene
{
    /// <summary>
    /// Manages the logo splash screen.
    /// Simple fade in/out animation then transitions to Supreme scene.
    /// </summary>
    public sealed class LogoSceneManager : MonoBehaviour
    {
        [Header("Logo Settings")]
        [SerializeField] private Image _logo;
        [SerializeField] private float _fadeInDuration = 1f;
        [SerializeField] private float _displayDuration = 1f;
        [SerializeField] private float _fadeOutDuration = 1f;

        private void Start()
        {
            if (_logo == null)
            {
                Debug.LogError("[LogoSceneManager] Logo Image reference is missing!");
                LoadSupremeScene();
                return;
            }
            
            StartCoroutine(LogoAnimation());
        }

        private IEnumerator LogoAnimation()
        {
            Color logoColor = _logo.color;
            Color transparentColor = new Color(logoColor.r, logoColor.g, logoColor.b, 0f);
            
            // Start transparent
            _logo.color = transparentColor;
            
            // Fade in
            float timer = 0;
            while (timer <= _fadeInDuration)
            {
                timer += Time.deltaTime;
                float t = timer / _fadeInDuration;
                _logo.color = Color.Lerp(transparentColor, logoColor, t);
                yield return null;
            }
            
            _logo.color = logoColor;

            // Display
            yield return new WaitForSeconds(_displayDuration);
            
            // Fade out
            timer = _fadeOutDuration;
            while (timer >= 0)
            {
                timer -= Time.deltaTime;
                float t = timer / _fadeOutDuration;
                _logo.color = Color.Lerp(transparentColor, logoColor, t);
                yield return null;
            }
            
            _logo.color = transparentColor;

            // Load Supreme scene
            LoadSupremeScene();
        }
        
        private void LoadSupremeScene()
        {
            Debug.Log("[LogoSceneManager] Loading Supreme scene...");
            SceneManager.LoadScene((int)Scenes.Supreme);
        }
    }
}
