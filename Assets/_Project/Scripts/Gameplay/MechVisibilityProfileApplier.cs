using UnityEngine;
using UnityEngine.Rendering;

namespace Taiyun.SuckTheWater.Gameplay
{
    /// <summary>
    /// Applies a named visibility profile to the Artillery first-person mech prefab.
    /// Mapping is explicit to the current rig so authors can toggle semantic parts
    /// without hand-editing renderer overrides.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MechVisibilityProfileApplier : MonoBehaviour
    {
        private const string UpperBodyRootPath = "Char_Mech/c_traj/root.x/spine_01.x";
        private const string LeftShoulderRootPath = UpperBodyRootPath + "/shoulder.l";
        private const string LeftArmRootPath = LeftShoulderRootPath + "/arm_stretch.l";
        private const string LeftHandRootPath = LeftArmRootPath + "/forearm_stretch.l/hand.l";
        private const string RightShoulderRootPath = UpperBodyRootPath + "/shoulder.r";
        private const string RightArmRootPath = RightShoulderRootPath + "/arm_stretch.r";
        private const string RightHandRootPath = RightArmRootPath + "/forearm_stretch.r/hand.r";

        [Header("Profile")]
        [SerializeField] private MechVisibilityProfile _profile;

        [SerializeField] private bool _applyOnEnable = true;

        private static readonly GroupRoot[] GroupRoots =
        {
            new(MechVisibilityPart.Chest, UpperBodyRootPath),
            new(MechVisibilityPart.BackAttachments, UpperBodyRootPath + "/Back_L_Attachment"),
            new(MechVisibilityPart.BackAttachments, UpperBodyRootPath + "/Back_M_Attachment"),
            new(MechVisibilityPart.BackAttachments, UpperBodyRootPath + "/Back_S_Attachment"),
            new(MechVisibilityPart.Head, UpperBodyRootPath + "/neck.x"),
            new(MechVisibilityPart.Head, UpperBodyRootPath + "/neck.x/head.x"),
            new(MechVisibilityPart.LeftShoulder, LeftShoulderRootPath),
            new(MechVisibilityPart.LeftShoulder, LeftArmRootPath + "/arm_twist.l/Shoulder_L_Attachment.L"),
            new(MechVisibilityPart.LeftShoulder, LeftArmRootPath + "/arm_twist.l/Shoulder_M_Attachment.L"),
            new(MechVisibilityPart.LeftShoulder, LeftArmRootPath + "/arm_twist.l/Shoulder_S_Attachment.L"),
            new(MechVisibilityPart.LeftArm, LeftArmRootPath),
            new(MechVisibilityPart.LeftHand, LeftHandRootPath),
            new(MechVisibilityPart.HandAttachments, LeftHandRootPath + "/Hand_Attachment.L"),
            new(MechVisibilityPart.RightShoulder, RightShoulderRootPath),
            new(MechVisibilityPart.RightShoulder, RightArmRootPath + "/arm_twist.r/Shoulder_L_Attachment.R"),
            new(MechVisibilityPart.RightShoulder, RightArmRootPath + "/arm_twist.r/Shoulder_M_Attachment.R"),
            new(MechVisibilityPart.RightShoulder, RightArmRootPath + "/arm_twist.r/Shoulder_S_Attachment.R"),
            new(MechVisibilityPart.RightArm, RightArmRootPath),
            new(MechVisibilityPart.RightHand, RightHandRootPath),
            new(MechVisibilityPart.HandAttachments, RightHandRootPath + "/Hand_Attachment.R"),
        };

        private readonly struct GroupRoot
        {
            public GroupRoot(MechVisibilityPart part, string path)
            {
                Part = part;
                Path = path;
            }

            public MechVisibilityPart Part { get; }
            public string Path { get; }
        }

        public MechVisibilityProfile Profile => _profile;

        private void OnEnable()
        {
#if UNITY_EDITOR
            MechVisibilityProfile.Validated -= HandleProfileValidated;
            MechVisibilityProfile.Validated += HandleProfileValidated;
#endif

            if (_applyOnEnable)
            {
                ApplyProfile(_profile);
            }
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            MechVisibilityProfile.Validated -= HandleProfileValidated;
#endif
        }

        private void OnValidate()
        {
            ApplyProfile(_profile);
        }

        [ContextMenu("Apply Current Profile")]
        public void ApplyCurrentProfile()
        {
            ApplyProfile(_profile);
        }

        public void ApplyProfile(MechVisibilityProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            _profile = profile;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                string rendererPath = GetRelativePath(renderer.transform);
                MechVisibilityPart resolvedPart = ResolvePart(rendererPath);
                bool visible = resolvedPart != MechVisibilityPart.None
                    ? profile.IsVisible(resolvedPart)
                    : profile.IsVisible(MechVisibilityPart.LowerBody);

                renderer.enabled = visible;
                renderer.shadowCastingMode = profile.DisableShadows ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = !profile.DisableShadows;
            }

            Projector[] projectors = GetComponentsInChildren<Projector>(true);
            for (int i = 0; i < projectors.Length; i++)
            {
                projectors[i].enabled = !profile.DisableDecals;
            }

            Behaviour[] behaviours = GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                if (behaviour.GetType().Name.Contains("DecalProjector"))
                {
                    behaviour.enabled = !profile.DisableDecals;
                }
            }
        }

#if UNITY_EDITOR
        private void HandleProfileValidated(MechVisibilityProfile profile)
        {
            if (profile == _profile)
            {
                ApplyProfile(profile);
            }
        }
#endif

        private static bool IsPathMatch(string targetPath, string candidateRootPath)
        {
            return targetPath == candidateRootPath || targetPath.StartsWith(candidateRootPath + "/");
        }

        private static MechVisibilityPart ResolvePart(string rendererPath)
        {
            int bestPathLength = -1;
            MechVisibilityPart resolvedPart = MechVisibilityPart.None;

            for (int i = 0; i < GroupRoots.Length; i++)
            {
                GroupRoot groupRoot = GroupRoots[i];
                if (!IsPathMatch(rendererPath, groupRoot.Path))
                {
                    continue;
                }

                if (groupRoot.Path.Length <= bestPathLength)
                {
                    continue;
                }

                bestPathLength = groupRoot.Path.Length;
                resolvedPart = groupRoot.Part;
            }

            if (bestPathLength >= 0)
            {
                return resolvedPart;
            }

            return IsPathMatch(rendererPath, UpperBodyRootPath)
                ? MechVisibilityPart.Chest
                : MechVisibilityPart.None;
        }

        private string GetRelativePath(Transform target)
        {
            if (target == transform)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(target.name);
            Transform current = target.parent;

            while (current != null && current != transform)
            {
                builder.Insert(0, '/');
                builder.Insert(0, current.name);
                current = current.parent;
            }

            return builder.ToString();
        }
    }
}
