using UnityEngine;

namespace Taiyun.SuckTheWater.Game
{
    /// <summary>
    /// This class contains general information describing an actor (player or enemies).
    /// It is mostly used for AI detection logic and determining if an actor is friend or foe.
    /// </summary>
    public class Actor : MonoBehaviour
    {
        [Tooltip("Represents the affiliation (or team) of the actor. Actors of the same affiliation are friendly to each other")]
        public int Affiliation;

        [Tooltip("Represents point where other actors will aim when they attack this actor")]
        public Transform AimPoint;

        private ActorsManager m_ActorsManager;
        private bool _isRegistered = false;

        /// <summary>
        /// Whether this actor is currently registered with an ActorsManager
        /// </summary>
        public bool IsRegistered => _isRegistered;

        void Start()
        {
            // Try to register automatically, but don't error if manager doesn't exist
            TryRegisterWithManager();
        }

        /// <summary>
        /// Attempts to find and register with ActorsManager.
        /// Safe to call in scenes where ActorsManager doesn't exist.
        /// </summary>
        /// <returns>True if successfully registered, false otherwise</returns>
        public bool TryRegisterWithManager()
        {
            if (_isRegistered) return true;
            
            m_ActorsManager = FindFirstObjectByType<ActorsManager>();
            
            if (m_ActorsManager == null)
            {
                // This is expected in some scenes (like Lobby) - not an error
                Debug.Log($"[Actor] ActorsManager not found - {gameObject.name} will register when available");
                return false;
            }
            
            return RegisterWithManager(m_ActorsManager);
        }

        /// <summary>
        /// Registers this actor with a specific ActorsManager.
        /// Called by GameSceneManager or NetworkedPlayerController when manager is available.
        /// </summary>
        public bool RegisterWithManager(ActorsManager actorsManager)
        {
            if (actorsManager == null)
            {
                Debug.LogWarning($"[Actor] Cannot register {gameObject.name} - ActorsManager is null");
                return false;
            }
            
            if (_isRegistered && m_ActorsManager == actorsManager)
            {
                Debug.Log($"[Actor] {gameObject.name} already registered with this ActorsManager");
                return true;
            }
            
            m_ActorsManager = actorsManager;
            
            // Register as an actor
            if (!m_ActorsManager.Actors.Contains(this))
            {
                m_ActorsManager.Actors.Add(this);
                _isRegistered = true;
                Debug.Log($"[Actor] {gameObject.name} registered with ActorsManager");
            }
            
            return true;
        }

        /// <summary>
        /// Unregisters this actor from its ActorsManager.
        /// </summary>
        public void UnregisterFromManager()
        {
            if (m_ActorsManager != null && _isRegistered)
            {
                m_ActorsManager.Actors.Remove(this);
                _isRegistered = false;
                Debug.Log($"[Actor] {gameObject.name} unregistered from ActorsManager");
            }
        }

        void OnDestroy()
        {
            // Unregister as an actor
            UnregisterFromManager();
        }
    }
}