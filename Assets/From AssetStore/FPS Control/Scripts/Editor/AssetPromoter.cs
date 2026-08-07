using UnityEngine;
using UnityEditor;

namespace EasyPeasyFirstPersonController.EditorScripts
{
    [InitializeOnLoad]
    public class AssetPromoter : EditorWindow
    {
        private const string PrefKey = "AssetPromoter_Shown_EP_V1";
        private GUIStyle headerStyle;
        private GUIStyle cardStyle;
        private GUIStyle titleStyle;

        static AssetPromoter()
        {
            EditorApplication.delayCall += CheckShow;
        }

        private static void CheckShow()
        {
            if (!EditorPrefs.GetBool(PrefKey, false))
            {
                ShowWindow();
                EditorPrefs.SetBool(PrefKey, true);
            }
        }

        [MenuItem("Tools/Eys/Other Assets")]
        public static void ShowWindow()
        {
            AssetPromoter window = GetWindow<AssetPromoter>("Other Assets");
            window.minSize = new Vector2(520, 400);
            window.maxSize = new Vector2(520, 400);
            window.Show();
        }

        private void OnGUI()
        {
            InitStyles();

            EditorGUILayout.BeginVertical();

            GUILayout.Space(20);
            EditorGUILayout.LabelField("OTHER ASSETS", headerStyle);
            EditorGUILayout.LabelField("Boost your workflow with our professional tools", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(25);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                DrawAssetCard("Easy Peasy First Person Controller Pro",
                              "https://assetstore.unity.com/packages/tools/physics/easy-peasy-first-person-controller-new-input-system-337370",
                              "Assets/EasyPeasyFirstPersonController/Icons/EasyPeasyFirstPersonControllerNewInputSystem160x160.jpg");
                GUILayout.Space(20);
                DrawAssetCard("Easy Horror Template",
                              "https://assetstore.unity.com/packages/templates/systems/easy-horror-template-306111",
                              "Assets/EasyPeasyFirstPersonController/Icons/EasyHorrorTemplate160x160.png");



                GUILayout.FlexibleSpace();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(15);
            EditorGUILayout.EndVertical();
        }

        private void DrawAssetCard(string title, string url, string imagePath)
        {
            Rect cardRect = EditorGUILayout.BeginVertical(cardStyle, GUILayout.Width(220), GUILayout.Height(230));

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);

            Rect textureRect = GUILayoutUtility.GetRect(200, 150);
            if (texture != null)
            {
                GUI.DrawTexture(textureRect, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                EditorGUI.DrawRect(textureRect, new Color(0.2f, 0.2f, 0.2f));
                GUI.Label(textureRect, "Preview Missing", EditorStyles.centeredGreyMiniLabel);
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField(title, titleStyle, GUILayout.Height(40));

            if (Event.current.type == EventType.MouseDown && cardRect.Contains(Event.current.mousePosition))
            {
                Application.OpenURL(url);
            }

            EditorGUIUtility.AddCursorRect(cardRect, MouseCursor.Link);

            EditorGUILayout.EndVertical();
        }

        private void InitStyles()
        {
            if (headerStyle != null) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel);
            headerStyle.fontSize = 18;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            headerStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            cardStyle = new GUIStyle(GUI.skin.box);
            cardStyle.padding = new RectOffset(10, 10, 10, 10);
            cardStyle.margin = new RectOffset(5, 5, 5, 5);

            titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.alignment = TextAnchor.UpperCenter;
            titleStyle.wordWrap = true;
            titleStyle.fontSize = 12;
        }
    }
}