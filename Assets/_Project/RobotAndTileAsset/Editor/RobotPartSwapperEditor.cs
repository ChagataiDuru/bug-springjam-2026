using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(RobotPartSwapper))]
public class RobotPartSwapperEditor : Editor
{
    private bool debugMode = false;
    private string searchFolderPath = "Assets/";

    public override void OnInspectorGUI()
    {
        RobotPartSwapper swapper = (RobotPartSwapper)target;

        // Toggle for showing the raw configuration lists
        debugMode = EditorGUILayout.Toggle("Show Setup/Debug Mode", debugMode);
        
        if (debugMode)
        {
            // Draw the default inspector (which contains the bodyParts list)
            DrawDefaultInspector();
            EditorGUILayout.Space(10);
            
            EditorGUILayout.LabelField("Auto-Populate Variants", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Specify a folder path (e.g., 'Assets/Mech_00/Parts'). The tool will search for FBX files and extract all mesh sub-assets from them, adding them to the appropriate body part if their name contains the part's name (e.g., 'Forearm').", MessageType.Info);
            
            GUILayout.BeginHorizontal();
            searchFolderPath = EditorGUILayout.TextField("Search Folder", searchFolderPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Parts Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert absolute path to relative path for Unity AssetDatabase
                    if (path.StartsWith(Application.dataPath))
                    {
                        searchFolderPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                }
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Find and Add Parts from Folder"))
            {
                AutoPopulateParts(swapper, searchFolderPath);
            }
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Auto-Assign Target Renderers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Automatically finds SkinnedMeshRenderers on child objects and assigns them to the matching body part based on the GameObject's name.", MessageType.Info);
            if (GUILayout.Button("Auto-Assign Target Renderers"))
            {
                AutoAssignTargetRenderers(swapper);
            }
            EditorGUILayout.Space(10);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Part Selection", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select which variant to use for each part. Dropdown options are populated from mesh names.", MessageType.Info);

        // Create dropdown for each part part
        foreach (var part in swapper.bodyParts)
        {
            if (part.variants.Count == 0)
            {
                EditorGUILayout.LabelField(part.partName, "No variants assigned");
                continue;
            }

            // Build dropdown options from mesh names
            string[] options = new string[part.variants.Count];
            for (int i = 0; i < part.variants.Count; i++)
            {
                var variant = part.variants[i];
                if (variant.mesh != null)
                {
                    options[i] = variant.mesh.name;
                }
                else
                {
                    options[i] = $"(Empty Slot {i})";
                }
            }

            // Clamp selected index to valid range
            if (part.selectedVariantIndex >= part.variants.Count)
            {
                part.selectedVariantIndex = 0;
            }

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(part.partName, part.selectedVariantIndex, options);
            
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(swapper, "Change Robot Part");
                swapper.SwapPart(part.partName, newIndex);
                EditorUtility.SetDirty(swapper);
            }
        }
    }

    private void AutoPopulateParts(RobotPartSwapper swapper, string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;

        folderPath = folderPath.TrimEnd('/');
        
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"Folder '{folderPath}' does not exist in the project.");
            return;
        }

        // Find all FBX/model files in the folder
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
        
        int addedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Load ALL sub-assets from the FBX — this includes every mesh inside it
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);

            foreach (Object asset in allAssets)
            {
                // We only care about Mesh sub-assets
                Mesh mesh = asset as Mesh;
                if (mesh == null) continue;

                string meshName = mesh.name;

                // Check if this is a LimbGun mesh and remap to Forearm.L or Forearm.R
                string remappedPartName = null;
                if (meshName.IndexOf("_LimbGun", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (meshName.EndsWith(".L", System.StringComparison.OrdinalIgnoreCase))
                        remappedPartName = "Forearm.L";
                    else if (meshName.EndsWith(".R", System.StringComparison.OrdinalIgnoreCase))
                        remappedPartName = "Forearm.R";
                }

                string[] nameSegments = meshName.Split('_');

                foreach (var part in swapper.bodyParts)
                {
                    bool isMatch = false;

                    if (remappedPartName != null)
                    {
                        isMatch = part.partName.Equals(remappedPartName, System.StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        foreach (string segment in nameSegments)
                        {
                            if (segment.Equals(part.partName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = true;
                                break;
                            }
                        }
                    }

                    if (isMatch)
                    {
                        // Check for duplicates by mesh reference
                        bool alreadyExists = false;
                        foreach (var variant in part.variants)
                        {
                            if (variant.mesh == mesh)
                            {
                                alreadyExists = true;
                                break;
                            }
                        }

                        if (!alreadyExists)
                        {
                            // Try to get materials from the corresponding renderer in the FBX
                            Material[] mats = null;
                            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (go != null)
                            {
                                SkinnedMeshRenderer[] renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                                foreach (var smr in renderers)
                                {
                                    if (smr.sharedMesh != null && smr.sharedMesh.name == meshName)
                                    {
                                        mats = smr.sharedMaterials;
                                        break;
                                    }
                                }
                            }

                            Undo.RecordObject(swapper, "Auto-Populate Parts");
                            part.variants.Add(new RobotPartVariant
                            {
                                mesh = mesh,
                                materials = mats
                            });
                            addedCount++;
                        }
                        break;
                    }
                }
            }
        }
        
        if (addedCount > 0)
        {
            SyncDropdowns(swapper);
            EditorUtility.SetDirty(swapper);
            Debug.Log($"Auto-populated {addedCount} new part variants from FBX sub-assets!");
        }
        else
        {
            Debug.Log("No new matching meshes found. Make sure mesh names inside the FBX contain the body part names (e.g. 'Forearm', 'Torso', 'Arm').");
        }
    }

    private void AutoAssignTargetRenderers(RobotPartSwapper swapper)
    {
        // Find all SkinnedMeshRenderers in the children of the GameObject this script is attached to, including inactive ones
        SkinnedMeshRenderer[] renderers = swapper.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int assignedCount = 0;

        foreach (var part in swapper.bodyParts)
        {
            // Only assign if it's currently empty
            if (part.targetRenderer != null) continue;

            foreach (var renderer in renderers)
            {
                // Check if the GameObject's name matches the part name (case-insensitive)
                if (renderer.gameObject.name.Equals(part.partName, System.StringComparison.OrdinalIgnoreCase))
                {
                    Undo.RecordObject(swapper, "Auto-Assign Target Renderers");
                    part.targetRenderer = renderer;
                    assignedCount++;
                    break;
                }
            }
        }

        SyncDropdowns(swapper);

        if (assignedCount > 0)
        {
            EditorUtility.SetDirty(swapper);
            Debug.Log($"Auto-assigned {assignedCount} target renderers!");
        }
        else
        {
            Debug.Log("No new matching child objects found for unassigned parts.");
        }
    }

    private void SyncDropdowns(RobotPartSwapper swapper)
    {
        bool changed = false;
        foreach (var part in swapper.bodyParts)
        {
            if (part.targetRenderer != null && part.targetRenderer.sharedMesh != null)
            {
                Mesh currentMesh = part.targetRenderer.sharedMesh;

                for (int i = 0; i < part.variants.Count; i++)
                {
                    if (part.variants[i].mesh == currentMesh)
                    {
                        if (part.selectedVariantIndex != i)
                        {
                            Undo.RecordObject(swapper, "Sync Dropdown");
                            part.selectedVariantIndex = i;
                            changed = true;
                        }
                        break;
                    }
                }
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(swapper);
        }
    }
}

[CustomPropertyDrawer(typeof(RobotPartVariant))]
public class RobotPartVariantDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight; // for the foldout
        
        if (property.isExpanded)
        {
            height += EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("mesh"));
            height += EditorGUIUtility.standardVerticalSpacing;
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("materials"), true);
            height += EditorGUIUtility.standardVerticalSpacing;
            
            SerializedProperty mutuallyExclusive = property.FindPropertyRelative("mutuallyExclusive");
            height += EditorGUI.GetPropertyHeight(mutuallyExclusive);
            
            if (mutuallyExclusive.boolValue)
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("objectsToHide"), true);
            }
        }
        
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        
        Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label);
        
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            
            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty meshProp = property.FindPropertyRelative("mesh");
            float meshHeight = EditorGUI.GetPropertyHeight(meshProp);
            rect.height = meshHeight;
            EditorGUI.PropertyField(rect, meshProp);
            
            rect.y += meshHeight + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty materialsProp = property.FindPropertyRelative("materials");
            float matHeight = EditorGUI.GetPropertyHeight(materialsProp, true);
            rect.height = matHeight;
            EditorGUI.PropertyField(rect, materialsProp, true);
            
            rect.y += matHeight + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty mutuallyExclusiveProp = property.FindPropertyRelative("mutuallyExclusive");
            rect.height = EditorGUI.GetPropertyHeight(mutuallyExclusiveProp);
            EditorGUI.PropertyField(rect, mutuallyExclusiveProp);
            
            if (mutuallyExclusiveProp.boolValue)
            {
                rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                SerializedProperty objectsToHideProp = property.FindPropertyRelative("objectsToHide");
                rect.height = EditorGUI.GetPropertyHeight(objectsToHideProp, true);
                EditorGUI.PropertyField(rect, objectsToHideProp, true);
            }
            
            EditorGUI.indentLevel--;
        }
        
        EditorGUI.EndProperty();
    }
}
