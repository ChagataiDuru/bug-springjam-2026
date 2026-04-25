using Taiyun.SuckTheWater.Game;
using Taiyun.SuckTheWater.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.UI
{
    public class JetpackCounter : MonoBehaviour
    {
        [Tooltip("Image component representing jetpack fuel")]
        public Image JetpackFillImage;

        [Tooltip("Canvas group that contains the whole UI for the jetack")]
        public CanvasGroup MainCanvasGroup;

        [Tooltip("Component to animate the color when empty or full")]
        public FillBarColorChange FillBarColorChange;

        Jetpack m_Jetpack;

        void Awake()
        {
            m_Jetpack = FindFirstObjectByType<Jetpack>();
            DebugUtility.HandleErrorIfNullFindObject<Jetpack, JetpackCounter>(m_Jetpack, this);

            FillBarColorChange.Initialize(1f, 0f);
        }

        void Update()
        {
            MainCanvasGroup.gameObject.SetActive(m_Jetpack.IsJetpackUnlocked);

            if (m_Jetpack.IsJetpackUnlocked)
            {
                JetpackFillImage.fillAmount = m_Jetpack.CurrentFillRatio;
                FillBarColorChange.UpdateVisual(m_Jetpack.CurrentFillRatio);
            }
        }
    }
}