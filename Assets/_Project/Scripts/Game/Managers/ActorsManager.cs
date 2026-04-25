using System.Collections.Generic;
using UnityEngine;

namespace Taiyun.SuckTheWater.Game
{
    /// <summary>
    /// Tracks all actors in the scene and provides player reference.
    /// </summary>
    public class ActorsManager : MonoBehaviour
    {
        /// <summary>All registered actors in the scene.</summary>
        public List<Actor> Actors { get; private set; }
        
        /// <summary>The player GameObject.</summary>
        public GameObject Player { get; private set; }

        /// <summary>Sets the player reference.</summary>
        public void SetPlayer(GameObject player) => Player = player;

        void Awake()
        {
            Actors = new List<Actor>();
        }
    }
}
