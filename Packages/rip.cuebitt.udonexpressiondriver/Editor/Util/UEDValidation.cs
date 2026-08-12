using UdonExpressionDriver;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// Editor-side validation for UED components. Run via the inspector or
    /// Tools > Udon Expression Driver > Validate Selection.
    /// </summary>
    public static class UEDValidation
    {
        [MenuItem("Tools/Udon Expression Driver/Validate Selection")]
        public static void ValidateSelection()
        {
            var errors = 0;
            var warnings = 0;

            foreach (var go in Selection.gameObjects)
            {
                foreach (var behaviour in go.GetComponentsInChildren<UEDBehaviour>(true))
                    Validate(behaviour, ref errors, ref warnings);
            }

            if (errors + warnings == 0)
                Debug.Log("[UED] Validation: no issues found in selection.");
            else
                Debug.Log($"[UED] Validation finished: {errors} error(s), {warnings} warning(s).");
        }

        public static void Validate(UEDBehaviour behaviour, ref int errors, ref int warnings)
        {
            if (behaviour == null) return;

            if (behaviour is UEDFullController controller)
                ValidateController(controller, ref errors, ref warnings);
            else if (behaviour is UEDArmatureLink armatureLink)
                ValidateArmatureLink(armatureLink, ref errors, ref warnings);
        }

        private static void ValidateController(UEDFullController controller, ref int errors, ref int warnings)
        {
            var serialized = new SerializedObject(controller);
            var paramCount = serialized.FindProperty("paramNames")?.arraySize ?? 0;

            foreach (var field in new[] { "paramTypes", "paramDefaults", "paramSynced" })
            {
                var size = serialized.FindProperty(field)?.arraySize ?? 0;
                if (size != paramCount)
                {
                    errors++;
                    Debug.LogError($"[UED] '{controller.name}': '{field}' length ({size}) doesn't match 'paramNames' ({paramCount}).", controller);
                }
            }

            var menuStart = serialized.FindProperty("menuControlStart");
            var controlTypes = serialized.FindProperty("controlTypes");
            var controlCount = controlTypes?.arraySize ?? 0;
            var menuCount = menuStart == null ? 0 : Mathf.Max(0, menuStart.arraySize - 1);

            if (menuStart != null && menuStart.arraySize > 1)
            {
                var previous = -1;
                for (var i = 0; i < menuStart.arraySize; i++)
                {
                    var value = menuStart.GetArrayElementAtIndex(i).intValue;
                    if (value < previous)
                    {
                        errors++;
                        Debug.LogError($"[UED] '{controller.name}': menuControlStart is not monotonically increasing at index {i}.", controller);
                        break;
                    }
                    previous = value;
                }

                if (menuStart.GetArrayElementAtIndex(menuStart.arraySize - 1).intValue != controlCount)
                {
                    errors++;
                    Debug.LogError($"[UED] '{controller.name}': menuControlStart end ({menuStart.GetArrayElementAtIndex(menuStart.arraySize - 1).intValue}) doesn't match control count ({controlCount}).", controller);
                }
            }

            if (controlCount > 0)
            {
                var submenu = serialized.FindProperty("controlSubmenuIndex");
                var paramIndex = serialized.FindProperty("controlParamIndex");
                for (var i = 0; i < controlCount; i++)
                {
                    if (submenu != null)
                    {
                        var sub = submenu.GetArrayElementAtIndex(i).intValue;
                        if (sub != -1 && (sub < 0 || sub >= menuCount))
                        {
                            errors++;
                            Debug.LogError($"[UED] '{controller.name}': control {i} submenu index {sub} out of range (menu count {menuCount}).", controller);
                        }
                    }
                    if (paramIndex != null)
                    {
                        var param = paramIndex.GetArrayElementAtIndex(i).intValue;
                        if (param != -1 && (param < 0 || param >= paramCount))
                        {
                            errors++;
                            Debug.LogError($"[UED] '{controller.name}': control {i} parameter index {param} out of range (param count {paramCount}).", controller);
                        }
                    }
                }
            }

            if (controller.GetComponent<VRCObjectSync>() != null)
            {
                errors++;
                Debug.LogError($"[UED] '{controller.name}': UEDFullController (Manual-synced variables) is on the same GameObject as VRC Object Sync, which VRChat forbids. Move it to a child object.", controller);
            }
        }

        private static void ValidateArmatureLink(UEDArmatureLink armatureLink, ref int errors, ref int warnings)
        {
            if (armatureLink.GetComponent<VRCObjectSync>() == null)
            {
                warnings++;
                Debug.LogWarning($"[UED] '{armatureLink.name}': no VRC Object Sync on this prop, so the worn transform won't sync to other players.", armatureLink);
            }
        }
    }
}
