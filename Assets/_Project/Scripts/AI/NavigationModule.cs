using UnityEngine;

namespace Taiyun.SuckTheWater.AI
{
    /// <summary>
    /// Overrides NavMeshAgent parameters at runtime for enemy movement customization.
    /// </summary>
    public class NavigationModule : MonoBehaviour
    {
        [Header("Parameters")] [Tooltip("The maximum speed at which the enemy is moving (in world units per second).")]
        public float MoveSpeed = 0f;

        [Tooltip("The maximum speed at which the enemy is rotating (degrees per second).")]
        public float AngularSpeed = 0f;

        [Tooltip("The acceleration to reach the maximum speed (in world units per second squared).")]
        public float Acceleration = 0f;
    }
}