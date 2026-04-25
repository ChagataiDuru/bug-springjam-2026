using System.Collections.Generic;
using Taiyun.SuckTheWater.Game;
using UnityEngine;

namespace Taiyun.SuckTheWater.AI
{
    /// <summary>
    /// Tracks all active enemies in the scene and broadcasts kill events.
    /// </summary>
    public class EnemyManager : MonoBehaviour
    {
        /// <summary>Active enemy controllers in the scene.</summary>
        public List<EnemyController> Enemies { get; private set; }
        
        /// <summary>Total enemies spawned since scene start.</summary>
        public int NumberOfEnemiesTotal { get; private set; }
        
        /// <summary>Current count of alive enemies.</summary>
        public int NumberOfEnemiesRemaining => Enemies.Count;

        void Awake()
        {
            Enemies = new List<EnemyController>();
        }

        /// <summary>
        /// Registers an enemy to be tracked.
        /// </summary>
        /// <param name="enemy">Enemy controller to track.</param>
        public void RegisterEnemy(EnemyController enemy)
        {
            Enemies.Add(enemy);

            NumberOfEnemiesTotal++;
        }

        /// <summary>
        /// Unregisters a killed enemy and broadcasts the kill event.
        /// </summary>
        /// <param name="enemyKilled">Enemy controller that was killed.</param>
        public void UnregisterEnemy(EnemyController enemyKilled)
        {
            int enemiesRemainingNotification = NumberOfEnemiesRemaining - 1;

            EnemyKillEvent evt = Events.EnemyKillEvent;
            evt.Enemy = enemyKilled.gameObject;
            evt.RemainingEnemyCount = enemiesRemainingNotification;
            EventManager.Broadcast(evt);

            // removes the enemy from the list, so that we can keep track of how many are left on the map
            Enemies.Remove(enemyKilled);
        }
    }
}