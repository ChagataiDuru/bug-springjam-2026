using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Optional first-person hand grip anchors for a weapon prefab.
    /// When missing, the mech view controller uses fallback offsets from the weapon socket.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonWeaponGripProvider : MonoBehaviour
    {
        [SerializeField] private Transform _leftGrip;
        [SerializeField] private Transform _rightGrip;

        public Transform LeftGrip => _leftGrip;
        public Transform RightGrip => _rightGrip;
    }
}
