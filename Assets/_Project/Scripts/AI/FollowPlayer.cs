using Taiyun.SuckTheWater.Game;
using UnityEngine;

namespace Taiyun.SuckTheWater.AI
{
    /// <summary>
    /// Maintains a fixed offset relative to the player position.
    /// </summary>
    /// <remarks>
    /// Caches initial offset on Start. Disables itself if no player found.
    /// </remarks>
    public class FollowPlayer : MonoBehaviour
    {
        Transform m_PlayerTransform;
        Vector3 m_OriginalOffset;

        void Start()
        {
            ActorsManager actorsManager = FindAnyObjectByType<ActorsManager>();
            if (actorsManager != null)
                m_PlayerTransform = actorsManager.Player.transform;
            else
            {
                enabled = false;
                return;
            }

            m_OriginalOffset = transform.position - m_PlayerTransform.position;
        }

        void LateUpdate()
        {
            transform.position = m_PlayerTransform.position + m_OriginalOffset;
        }
    }
}