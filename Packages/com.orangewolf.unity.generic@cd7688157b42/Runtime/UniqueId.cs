using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OrangeWolf.Generic
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public class UniqueId : PropertyAttribute { }
    
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(UniqueId))]
    public class UniqueIdDrawer : PropertyDrawer 
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            bool wasEnabled = GUI.enabled;
            GUI.enabled = false;
            if (string.IsNullOrEmpty(property.stringValue)) 
            {
                property.stringValue = Guid.NewGuid().ToString("N");
            }
            EditorGUI.PropertyField(rect, property, label, true);
            GUI.enabled = wasEnabled;
        }
    }
#endif
}
