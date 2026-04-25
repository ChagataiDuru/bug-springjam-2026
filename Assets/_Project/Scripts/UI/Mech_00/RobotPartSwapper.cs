using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RobotPartVariant
{
    [Tooltip("Drag a Mesh from the Project window here")]
    public Mesh mesh;
    
    [Tooltip("Assign the materials for this part here")]
    public Material[] materials;

    [Tooltip("Tick this to specify objects to hide when this variant is active")]
    public bool mutuallyExclusive;

    [Tooltip("Objects to disable when this variant is active")]
    public List<GameObject> objectsToHide = new List<GameObject>();
}

[Serializable]
public class RobotPart
{
    public string partName;
    
    public SkinnedMeshRenderer targetRenderer;
    
    public List<RobotPartVariant> variants = new List<RobotPartVariant>();
    
    [HideInInspector]
    public int selectedVariantIndex = 0;

    public RobotPart(string name)
    {
        partName = name;
    }
}

public class RobotPartSwapper : MonoBehaviour
{
    public List<RobotPart> bodyParts = new List<RobotPart>
    {
        new RobotPart("Arm"),
        new RobotPart("Forearm.L"),
        new RobotPart("Forearm.R"),
        new RobotPart("Leg"),
        new RobotPart("Pelvis"),
        new RobotPart("Shoulder"),
        new RobotPart("Thigh"),
        new RobotPart("Torso")
    };

    public void SwapPart(string partName, int variantIndex)
    {
        RobotPart part = bodyParts.Find(p => p.partName == partName);
        if (part == null)
        {
            Debug.LogWarning($"Part part '{partName}' not found.");
            return;
        }

        if (variantIndex < 0 || variantIndex >= part.variants.Count)
        {
            Debug.LogWarning($"Variant index {variantIndex} out of range for '{partName}'.");
            return;
        }

        StartCoroutine(SwapNextFrame(part, variantIndex));
    }

    private IEnumerator SwapNextFrame(RobotPart part, int variantIndex)
    {
        yield return null;
        ApplyVariant(part, variantIndex);
    }

    private void ApplyVariant(RobotPart part, int variantIndex)
    {
        // Re-enable previously hidden objects
        if (part.selectedVariantIndex >= 0 && part.selectedVariantIndex < part.variants.Count)
        {
            RobotPartVariant oldVariant = part.variants[part.selectedVariantIndex];
            if (oldVariant.mutuallyExclusive && oldVariant.objectsToHide != null)
            {
                foreach (GameObject obj in oldVariant.objectsToHide)
                {
                    if (obj != null)
                    {
#if UNITY_EDITOR
                        UnityEditor.Undo.RecordObject(obj, "Re-enable Object");
#endif
                        obj.SetActive(true);
                    }
                }
            }
        }

        RobotPartVariant variant = part.variants[variantIndex];
        
        if (part.targetRenderer == null)
        {
            Debug.LogWarning($"No target renderer assigned for '{part.partName}'.");
            return;
        }

        if (variant.mesh == null)
        {
            Debug.LogWarning($"Variant at index {variantIndex} has no mesh assigned.");
            return;
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(part.targetRenderer, "Swapped Robot Mesh");
#endif
        // Store bones before swapping to preserve skeleton binding
        var bones = part.targetRenderer.bones;
        var rootBone = part.targetRenderer.rootBone;

        part.targetRenderer.sharedMesh = variant.mesh;

        // Reapply bones after swapping
        part.targetRenderer.bones = bones;
        part.targetRenderer.rootBone = rootBone;
        
        if (variant.materials != null && variant.materials.Length > 0)
        {
            part.targetRenderer.sharedMaterials = variant.materials;
        }

        // Hide new objects
        if (variant.mutuallyExclusive && variant.objectsToHide != null)
        {
            foreach (GameObject obj in variant.objectsToHide)
            {
                if (obj != null)
                {
#if UNITY_EDITOR
                    UnityEditor.Undo.RecordObject(obj, "Disable Object");
#endif
                    obj.SetActive(false);
                }
            }
        }

        part.selectedVariantIndex = variantIndex;
    }

    public enum Variant
    {
        Var1,
        Var2,
        Var3
    }
    public class PartVariantTupple
    {
        public GameObject partPrefab;
        public Variant var;
    }
}