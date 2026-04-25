using Taiyun.SuckTheWater.AI;
using Taiyun.SuckTheWater.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Taiyun.SuckTheWater.UI
{
    public class EnemyCounter : MonoBehaviour
    {
        [Header("Enemies")] [Tooltip("Text component for displaying enemy objective progress")]
        public Text EnemiesText;

        EnemyManager m_EnemyManager;

        void Awake()
        {
            m_EnemyManager = FindFirstObjectByType<EnemyManager>();
            DebugUtility.HandleErrorIfNullFindObject<EnemyManager, EnemyCounter>(m_EnemyManager, this);
        }

        void Update()
        {
            EnemiesText.text = m_EnemyManager.NumberOfEnemiesRemaining + "/" + m_EnemyManager.NumberOfEnemiesTotal;
        }
    }
}