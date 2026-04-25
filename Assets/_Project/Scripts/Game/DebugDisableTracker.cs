using UnityEngine;

namespace SuckTheWater
{
    public class DebugDisableTracker : MonoBehaviour
    {
        void OnDisable()
        {
            Debug.LogError($"[DEBUG] Player GameObject DISABLED!\n{System.Environment.StackTrace}");
        }
    }
}
