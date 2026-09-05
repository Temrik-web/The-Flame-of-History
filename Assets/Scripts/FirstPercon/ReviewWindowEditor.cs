#if UNITY_EDITOR
using EasyPeasyFirstPersonController;
using UnityEditor;
using UnityEngine;

namespace EasyPeasyFirstPersonController.EditorScripts
{
    [CustomEditor(typeof(FirstPersonController))]
    [InitializeOnLoad]
    public class ReviewWindowEditor : Editor
    {
        private static string assetStoreUrl = "https://assetstore.unity.com/packages/slug/317073"; 

        static ReviewWindowEditor()
        {
            EditorApplication.hierarchyWindowItemOnGUI += DrawReviewIcon;
        }

        private static void DrawReviewIcon(int instanceID, Rect selectionRect)
        {
            GameObject go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            if (go == null) return;

            if (go.GetComponent<FirstPersonController>() != null)
            {
                Rect iconRect = new Rect(selectionRect.xMax - 20, selectionRect.y, 16, 16);
                
                // Newer Unity versions sometimes deprecate specific icon names or change how tooltips are parsed.
                // It's safer to manually construct the GUIContent with the image and the tooltip.
                GUIContent iconContent = EditorGUIUtility.IconContent("Favorite");
                
                // Fallback icon if "Favorite" isn't found
                if (iconContent == null || iconContent.image == null)
                    iconContent = EditorGUIUtility.IconContent("d_Favorite");

                GUIContent finalContent = (iconContent != null && iconContent.image != null) 
                    ? new GUIContent(iconContent.image, "Rate this asset on Asset Store") 
                    : new GUIContent("★", "Rate this asset on Asset Store");

                if (GUI.Button(iconRect, finalContent, GUIStyle.none))
                {
                    Application.OpenURL(assetStoreUrl);
                }
            }
        }
    }
}
#endif
