using System.Collections.Generic;
using UnityEngine;

namespace Taiyun.SuckTheWater.AI
{
    /// <summary>
    /// Defines a patrol route for enemies using a list of waypoint nodes.
    /// </summary>
    /// <remarks>
    /// Automatically assigns itself to configured enemies on Start.
    /// Provides gizmo visualization of the patrol path in editor.
    /// </remarks>
    public class PatrolPath : MonoBehaviour
    {
        [Tooltip("Enemies that will be assigned to this path on Start")]
        public List<EnemyController> EnemiesToAssign = new List<EnemyController>();

        [Tooltip("The Nodes making up the path")]
        public List<Transform> PathNodes = new List<Transform>();

        void Start()
        {
            foreach (var enemy in EnemiesToAssign)
            {
                enemy.PatrolPath = this;
            }
        }

        /// <summary>
        /// Calculates distance from origin to a specific path node.
        /// </summary>
        /// <param name="origin">Starting position.</param>
        /// <param name="destinationNodeIndex">Index of target node.</param>
        /// <returns>Distance to node, or -1 if index is invalid.</returns>
        public float GetDistanceToNode(Vector3 origin, int destinationNodeIndex)
        {
            if (destinationNodeIndex < 0 || destinationNodeIndex >= PathNodes.Count ||
                PathNodes[destinationNodeIndex] == null)
            {
                return -1f;
            }

            return (PathNodes[destinationNodeIndex].position - origin).magnitude;
        }

        /// <summary>
        /// Gets world position of a path node by index.
        /// </summary>
        /// <param name="nodeIndex">Index of the node.</param>
        /// <returns>World position, or Vector3.zero if index is invalid.</returns>
        public Vector3 GetPositionOfPathNode(int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= PathNodes.Count || PathNodes[nodeIndex] == null)
            {
                return Vector3.zero;
            }

            return PathNodes[nodeIndex].position;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < PathNodes.Count; i++)
            {
                int nextIndex = i + 1;
                if (nextIndex >= PathNodes.Count)
                {
                    nextIndex -= PathNodes.Count;
                }

                Gizmos.DrawLine(PathNodes[i].position, PathNodes[nextIndex].position);
                Gizmos.DrawSphere(PathNodes[i].position, 0.1f);
            }
        }
    }
}