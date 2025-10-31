using UdonExpressionDriver.Runtime;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace UdonExpressionDriver.Editor
{
    [CustomEditor(typeof(UEDBehaviour), true)]
    public class UEDBehaviourInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var boxWithPadding = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(15, 15, 10, 10)
            };

            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(boxWithPadding);
            DrawUtilitiesSection();
            EditorGUILayout.EndVertical();
        }

        private void DrawUtilitiesSection()
        {
            GUILayout.Label("Udon Expression Driver", EditorStyles.boldLabel);
            GUILayout.Space(10);

            if (GUILayout.Button("Link Child Forwarders"))
            {
                var go = ((UEDBehaviour)target).gameObject;
                LinkChildForwarders(go);
            }
        }

        private static void LinkChildForwarders(GameObject go)
        {
            Debug.Log("[Udon Expression Driver] Linking child forwarders...");

            var rootBehaviour = go.GetComponent<UEDBehaviour>();

            Debug.Log("[Udon Expression Driver] Linking Physbone forwarders...");
            // Link Physbone event forwarders

            Debug.Log("[Udon Expression Driver] Linking Contact forwarders...");
            // Link Contacte event forwarders
        }
    }
}