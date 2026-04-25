using System;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Named visibility groups for the first-person Artillery mech presentation.
    /// Groups are intentionally Artillery-specific for the current MVP rig.
    /// </summary>
    [Flags]
    public enum MechVisibilityPart
    {
        None = 0,
        Chest = 1 << 0,
        BackAttachments = 1 << 1,
        Head = 1 << 2,
        LeftShoulder = 1 << 3,
        LeftArm = 1 << 4,
        LeftHand = 1 << 5,
        RightShoulder = 1 << 6,
        RightArm = 1 << 7,
        RightHand = 1 << 8,
        HandAttachments = 1 << 9,
        LowerBody = 1 << 10,
    }
}
