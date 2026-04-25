using System;
using UnityEngine;

namespace Taiyun.SuckTheWater.Gameplay
{
    [CreateAssetMenu(
        fileName = "MechVisibilityProfile",
        menuName = "Drain Gang/Gameplay/Mech Visibility Profile")]
    public sealed class MechVisibilityProfile : ScriptableObject
    {
        [SerializeField] private MechVisibilityPart _visibleParts =
            MechVisibilityPart.Chest |
            MechVisibilityPart.LeftShoulder |
            MechVisibilityPart.LeftArm |
            MechVisibilityPart.LeftHand |
            MechVisibilityPart.RightShoulder |
            MechVisibilityPart.RightArm |
            MechVisibilityPart.RightHand |
            MechVisibilityPart.HandAttachments |
            MechVisibilityPart.LowerBody;

        [SerializeField] private bool _disableShadows = true;
        [SerializeField] private bool _disableDecals = true;

        public MechVisibilityPart VisibleParts => _visibleParts;
        public bool DisableShadows => _disableShadows;
        public bool DisableDecals => _disableDecals;

        public bool IsVisible(MechVisibilityPart part)
        {
            return (_visibleParts & part) != 0;
        }

#if UNITY_EDITOR
        public static event Action<MechVisibilityProfile> Validated;

        private void OnValidate()
        {
            Validated?.Invoke(this);
        }
#endif
    }
}
