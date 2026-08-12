using UdonExpressionDriver;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// FullController inspector: a "Menu View" section (radial menu + interact toggle), an
    /// "Expressions" section (Controller/Menu/Parameter assets, mirroring VRCFury's Full Controller)
    /// that is the single way to populate the data arrays, and a Status summary. When a VRCFury
    /// FullController is present its assets are imported automatically, but nothing is locked.
    /// A "Re-import from VRCFury" button re-pulls the data.
    /// </summary>
    [CustomEditor(typeof(UEDFullController))]
    public class UEDFullControllerInspector : UEDBehaviourInspector
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)) return;

            // The auto-import and animator rewrite touch the prop's components and assets, which can
            // throw when the prop is in a half-configured state (e.g. no Animator yet). Contain it so
            // nothing escapes into UdonSharp's inspector wrapper and floods the console every repaint.
            try
            {
                DrawCore();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UED] Error drawing FullController inspector for '{target.name}': {e}", target);
            }
        }

        private void DrawCore()
        {
            var controller = (UEDFullController)target;
            var vrcFuryPresent = UEDVrcFuryBridge.AutoImportMenu(controller);

            // Swaps the prop's Animator onto a prop-relative copy of the controller (avatar-prop
            // clip paths don't resolve against the prop root's own Animator). Idempotent, so this
            // is cheap on every repaint and keeps the Animation window showing resolved bindings.
            try
            {
                UEDAnimatorRewriter.ApplyForProp(controller);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UED] Failed to apply rewritten animator controller for '{controller.name}': {e}", controller);
            }

            DrawDescription(
                "Drives a prop's Animator from expression parameters and shows the prop's expressions " +
                "menu on a radial menu in the world. All configuration lives on this component. If the " +
                "prop carries a VRCFury Full Controller, its menu, parameters, and animator controller " +
                "are imported automatically.");

            DrawMenuViewSection();
            DrawPuppetsSection();
            DrawExpressionsSection(controller, vrcFuryPresent);
            DrawStatus(controller);
        }

        private void DrawPuppetsSection()
        {
            BeginSection("Puppets");

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("radialPuppet"), new GUIContent("Radial Puppet"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("axisPuppet"), new GUIContent("Axis Puppet"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.HelpBox("World-space puppet controls shown when a puppet menu item is pressed. Created automatically if unset.", MessageType.Info);
            EndSection();
        }

        private void DrawMenuViewSection()
        {
            BeginSection("Menu View");

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("menuView"), new GUIContent("Radial Menu"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("interactTogglesMenu"), new GUIContent("Interact Toggles Menu"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.HelpBox("If no radial menu is assigned, one is created automatically when you enter play mode or build.", MessageType.Info);
            EndSection();
        }

        private void DrawExpressionsSection(UEDFullController controller, bool vrcFuryPresent)
        {
            BeginSection("Expressions");

            var serialized = new SerializedObject(controller);
            EditorGUILayout.PropertyField(serialized.FindProperty("importedAnimatorController"), new GUIContent("Controller"));

            var menu = UEDVrcFuryBridge.GetStoredAsset<VRCExpressionsMenu>(serialized, "importedMenuGuid");
            var parameters = UEDVrcFuryBridge.GetStoredAsset<VRCExpressionParameters>(serialized, "importedParametersGuid");

            var newMenu = (VRCExpressionsMenu)EditorGUILayout.ObjectField("Menu", menu, typeof(VRCExpressionsMenu), false);
            var newParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField("Parameter", parameters, typeof(VRCExpressionParameters), false);

            if (newMenu != menu) UEDVrcFuryBridge.SetStoredAsset(serialized, "importedMenuGuid", newMenu);
            if (newParameters != parameters) UEDVrcFuryBridge.SetStoredAsset(serialized, "importedParametersGuid", newParameters);
            serialized.ApplyModifiedProperties();

            EditorGUILayout.HelpBox("Applied automatically in edit mode, play mode, and builds (as a generated, prop-relative copy).", MessageType.Info);

            if (vrcFuryPresent)
            {
                if (GUILayout.Button("Re-import from VRCFury"))
                    UEDVrcFuryBridge.ReimportFromVrcFury(controller);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No VRCFury component found on this prop. Configure the fields above manually, or " +
                    "install VRCFury and add a Full Controller to the prop for one-click import.",
                    MessageType.Info);
            }

            EndSection();
        }

        private static void DrawStatus(UEDFullController controller)
        {
            var (paramCount, controlCount) = UEDVrcFuryBridge.CountData(controller);

            BeginSection("Status");
            EditorGUILayout.LabelField("Data", $"{paramCount} parameter(s), {controlCount} control(s)");

            var serialized = new SerializedObject(controller);
            var animatorProperty = serialized.FindProperty("animator");
            if (animatorProperty != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(animatorProperty, new GUIContent("Animator"), true);
                EditorGUI.EndDisabledGroup();
            }

            EndSection();
        }
    }
}
