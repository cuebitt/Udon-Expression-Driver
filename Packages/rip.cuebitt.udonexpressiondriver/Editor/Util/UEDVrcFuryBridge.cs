using System.Collections.Generic;
using UdonExpressionDriver;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// Reads VRCFury component config off a prop prefab (non-destructively) and fills the
    /// UED equivalents, so a VRCFury-configured prop works in a world with one click.
    /// VRCFury's runtime classes are internal, so this walks their serialized data via
    /// SerializedObject + type-name matching. There is no VRCFury assembly reference,
    /// so this holds across VRCFury versions.
    /// </summary>
    public static class UEDVrcFuryBridge
    {
        private const string VrcFuryComponentTypeName = "VF.Model.VRCFury";

        /// <summary>
        /// Applies a VRCFury ArmatureLink feature's data (bone + attach point) to the UEDArmatureLink.
        /// Only writes when something actually differs (idempotent), so it is safe to call repeatedly.
        /// It is triggered by the inspector's "Re-import from VRCFury" button.
        /// </summary>
        public static bool AutoImportArmatureLink(UEDArmatureLink link)
        {
            if (!TryGetArmatureLinkData(link, out var bone, out var attach, out var useBone)) return false;

            var serialized = new SerializedObject(link);
            var changed = false;

            var targetBone = serialized.FindProperty("targetBone");
            if (useBone && targetBone != null && targetBone.enumValueIndex != (int)bone)
            {
                targetBone.enumValueIndex = (int)bone;
                changed = true;
            }

            var attachPoint = serialized.FindProperty("attachPoint");
            var currentAttach = attachPoint?.objectReferenceValue as Transform;
            if (currentAttach != attach)
            {
                if (attachPoint != null) attachPoint.objectReferenceValue = attach;
                changed = true;
            }

            if (changed)
            {
                serialized.ApplyModifiedProperties();
                Debug.Log($"[UED] Imported VRCFury ArmatureLink into '{link.name}'.", link);
            }

            return true;
        }

        /// <summary>Returns whether the prop carries a VRCFury ArmatureLink feature, without writing anything.</summary>
        public static bool HasArmatureLink(UEDArmatureLink link)
        {
            return TryGetArmatureLinkData(link, out _, out _, out _);
        }

        private static bool TryGetArmatureLinkData(UEDArmatureLink link, out HumanBodyBones bone, out Transform attach, out bool useBone)
        {
            bone = HumanBodyBones.Hips;
            attach = null;
            useBone = false;

            foreach (var vrcFury in FindVrcFuryComponents(link.gameObject))
            {
                var serialized = new SerializedObject(vrcFury);
                var content = serialized.FindProperty("content");
                var linkTo = content?.FindPropertyRelative("linkTo");
                if (content == null || linkTo == null) continue;
                if (linkTo.arraySize == 0) continue;

                var first = linkTo.GetArrayElementAtIndex(0);
                var useBoneProperty = first.FindPropertyRelative("useBone");
                var useObjProperty = first.FindPropertyRelative("useObj");
                var boneProperty = first.FindPropertyRelative("bone");
                var objProperty = first.FindPropertyRelative("obj");
                var propBoneProperty = content.FindPropertyRelative("propBone");

                if (useObjProperty != null && useObjProperty.boolValue &&
                    objProperty != null && objProperty.objectReferenceValue is GameObject objGo)
                    attach = objGo.transform;
                else if (propBoneProperty?.objectReferenceValue is GameObject propBoneGo)
                    attach = propBoneGo.transform;

                if (useBoneProperty != null && useBoneProperty.boolValue && boneProperty != null)
                {
                    bone = (HumanBodyBones)boneProperty.enumValueIndex;
                    useBone = true;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// If a VRCFury FullController feature exists on the prop, imports its menu+params into the
        /// UEDFullController and returns true (so the inspector can show a "Re-import" button).
        /// Only imports when the controller's data doesn't already match, so it is safe to call on
        /// every inspector repaint.
        /// </summary>
        public static bool AutoImportMenu(UEDFullController controller)
        {
            return ImportFromVrcFury(controller, force: false);
        }

        /// <summary>Forces a full re-import from the VRCFury FullController (ignores the data-match check).</summary>
        public static bool ReimportFromVrcFury(UEDFullController controller)
        {
            return ImportFromVrcFury(controller, force: true);
        }

        private static bool ImportFromVrcFury(UEDFullController controller, bool force)
        {
            foreach (var vrcFury in FindVrcFuryComponents(controller.gameObject))
            {
                var serialized = new SerializedObject(vrcFury);
                var content = serialized.FindProperty("content");
                if (content == null) continue;

                var menus = content.FindPropertyRelative("menus");
                var prms = content.FindPropertyRelative("prms");
                if (menus == null || prms == null) continue;

                var menu = ResolveObjRef(menus, "menu") as VRCExpressionsMenu;
                var parameters = ResolveObjRef(prms, "parameters") as VRCExpressionParameters;
                var controllers = content.FindPropertyRelative("controllers");
                var animatorController = ResolveObjRef(controllers, "controller") as RuntimeAnimatorController;

                // Record the referenced assets so the Expressions section can display them.
                var controllerSerialized = new SerializedObject(controller);
                SetStoredAsset(controllerSerialized, "importedMenuGuid", menu);
                SetStoredAsset(controllerSerialized, "importedParametersGuid", parameters);
                controllerSerialized.ApplyModifiedProperties();

                if (force || (menu != null && NeedsImport(controller, menu, parameters)))
                    UEDExpressionImporter.Import(controller, menu, parameters);

                AutoImportAnimator(controller, animatorController);

                return true; // FullController present
            }

            return false;
        }

        /// <summary>
        /// Applies the Expressions section's stored assets to the controller: imports the data
        /// arrays from the stored Menu/Parameters and wires the stored Controller asset into the
        /// prop's Animator. Idempotent (only imports when the data differs), so it is safe to call
        /// from the build/play auto-setup.
        /// </summary>
        public static void ApplyExpressions(UEDFullController controller)
        {
            var serialized = new SerializedObject(controller);
            var animatorController = serialized.FindProperty("importedAnimatorController")?.objectReferenceValue as RuntimeAnimatorController;
            var menu = GetStoredAsset<VRCExpressionsMenu>(serialized, "importedMenuGuid");
            var parameters = GetStoredAsset<VRCExpressionParameters>(serialized, "importedParametersGuid");

            if (menu != null && NeedsImport(controller, menu, parameters))
                UEDExpressionImporter.Import(controller, menu, parameters);
            AutoImportAnimator(controller, animatorController);
        }

        /// <summary>
        /// Auto-registers GestureLeft/GestureRight as synced int params on the controller (appended
        /// to paramNames/paramTypes/paramDefaults/paramSynced) when Enable Hand Gesture Emulation is on,
        /// the prop's Animator uses the param, and it isn't already present. Idempotent. Records exactly
        /// which names it appended in the hidden autoAddedHandGestureParams field (comma-separated) so the
        /// auto-linker can strip only those entries again after play/build.
        /// </summary>
        public static void EnsureHandGestureParams(UEDFullController controller)
        {
            var serialized = new SerializedObject(controller);
            var enableProp = serialized.FindProperty("enableHandGestureEmulation");
            if (enableProp == null || !enableProp.boolValue) return;

            var animator = serialized.FindProperty("animator")?.objectReferenceValue as Animator;
            if (animator == null) return;

            var namesProp = serialized.FindProperty("paramNames");
            var typesProp = serialized.FindProperty("paramTypes");
            var defaultsProp = serialized.FindProperty("paramDefaults");
            var syncedProp = serialized.FindProperty("paramSynced");
            if (namesProp == null) return;

            var addedNames = new List<string>();
            foreach (var name in new[] { "GestureLeft", "GestureRight" })
            {
                if (!AnimatorUsesParameter(animator, name)) continue;
                if (ContainsName(namesProp, name)) continue;

                var idx = namesProp.arraySize;
                namesProp.arraySize = idx + 1;
                if (typesProp != null) typesProp.arraySize = idx + 1;
                if (defaultsProp != null) defaultsProp.arraySize = idx + 1;
                if (syncedProp != null) syncedProp.arraySize = idx + 1;

                namesProp.GetArrayElementAtIndex(idx).stringValue = name;
                if (typesProp != null) typesProp.GetArrayElementAtIndex(idx).intValue = 1; // int
                if (defaultsProp != null) defaultsProp.GetArrayElementAtIndex(idx).floatValue = 0f;
                if (syncedProp != null) syncedProp.GetArrayElementAtIndex(idx).boolValue = true;

                addedNames.Add(name);
            }

            if (addedNames.Count == 0) return;

            var markerProp = serialized.FindProperty("autoAddedHandGestureParams");
            if (markerProp != null) markerProp.stringValue = string.Join(",", addedNames.ToArray());
            serialized.ApplyModifiedProperties();
        }

        private static bool ContainsName(SerializedProperty arrayProp, string name)
        {
            for (var i = 0; i < arrayProp.arraySize; i++)
            {
                if (arrayProp.GetArrayElementAtIndex(i).stringValue == name) return true;
            }
            return false;
        }

        private static bool AnimatorUsesParameter(Animator animator, string name)
        {
            if (animator.runtimeAnimatorController is UnityEditor.Animations.AnimatorController ac)
            {
                foreach (var p in ac.parameters)
                {
                    if (p.name == name) return true;
                }
            }
            return false;
        }

        public static T GetStoredAsset<T>(SerializedObject serialized, string guidField) where T : Object
        {
            var guid = serialized.FindProperty(guidField)?.stringValue;
            if (string.IsNullOrEmpty(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public static void SetStoredAsset(SerializedObject serialized, string guidField, Object asset)
        {
            var prop = serialized.FindProperty(guidField);
            if (prop == null) return;
            prop.stringValue = asset == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        }

        /// <summary>
        /// Points the controller's animator field at the prop's Animator and wires in the
        /// RuntimeAnimatorController referenced by the VRCFury FullController. Also records the
        /// imported controller asset for the inspector. Idempotent: only writes when something is
        /// missing or differs.
        /// This runs from the inspector's repaint, so it never adds a missing Animator here; a prop
        /// without one gets its Animator added by UEDBuildAutoLinker.EnsurePropComponents (which runs
        /// at play/build, outside the GUI pass).
        /// </summary>
        private static void AutoImportAnimator(UEDFullController controller, RuntimeAnimatorController animatorController)
        {
            var serialized = new SerializedObject(controller);
            var changed = false;

            var importedController = serialized.FindProperty("importedAnimatorController");
            if (importedController != null && importedController.objectReferenceValue != animatorController)
            {
                importedController.objectReferenceValue = animatorController;
                changed = true;
            }

            var animator = serialized.FindProperty("animator")?.objectReferenceValue as Animator;
            if (animator == null)
                animator = controller.transform.root.GetComponentInChildren<Animator>(true);

            if (animator != null)
            {
                var animatorProperty = serialized.FindProperty("animator");
                if (animatorProperty != null && animatorProperty.objectReferenceValue != animator)
                {
                    animatorProperty.objectReferenceValue = animator;
                    changed = true;
                }

                if (animator.runtimeAnimatorController == null && animatorController != null)
                    animator.runtimeAnimatorController = animatorController;
            }

            if (changed) serialized.ApplyModifiedProperties();
        }

        /// <summary>Counts the params and controls currently baked into the controller's data arrays.</summary>
        public static (int Params, int Controls) CountData(UEDFullController controller)
        {
            var serialized = new SerializedObject(controller);
            var paramCount = serialized.FindProperty("paramNames")?.arraySize ?? 0;
            var menuStart = serialized.FindProperty("menuControlStart");
            var controlCount = menuStart != null && menuStart.arraySize > 0
                ? menuStart.GetArrayElementAtIndex(menuStart.arraySize - 1).intValue
                : 0;
            return (paramCount, controlCount);
        }

        private static bool NeedsImport(UEDFullController controller, VRCExpressionsMenu menu, VRCExpressionParameters parameters)
        {
            var serialized = new SerializedObject(controller);
            // Ignore any auto-added GestureLeft/GestureRight params so the import stays idempotent and
            // never re-imports (which would drop them) just because hand gesture emulation appended them.
            var paramCount = serialized.FindProperty("paramNames")?.arraySize ?? 0;
            var marker = serialized.FindProperty("autoAddedHandGestureParams")?.stringValue;
            if (!string.IsNullOrEmpty(marker)) paramCount -= marker.Split(',').Length;
            if (paramCount < 0) paramCount = 0;

            var menuStart = serialized.FindProperty("menuControlStart");
            var controlCount = menuStart != null && menuStart.arraySize > 0
                ? menuStart.GetArrayElementAtIndex(menuStart.arraySize - 1).intValue
                : 0;

            var expectedParams = CountParams(menu, parameters);
            var expectedControls = CountControls(menu);

            return paramCount != expectedParams || controlCount != expectedControls;
        }

        private static int CountControls(VRCExpressionsMenu menu)
        {
            return CountControls(menu, new HashSet<VRCExpressionsMenu>());
        }

        private static int CountControls(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu) || menu.controls == null) return 0;

            var count = 0;
            foreach (var c in menu.controls)
            {
                if (c == null) continue;

                count++;
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    count += CountControls(c.subMenu, seen);
            }

            return count;
        }

        private static int CountParams(VRCExpressionsMenu menu, VRCExpressionParameters parameters)
        {
            var names = new HashSet<string>();
            if (parameters != null && parameters.parameters != null)
            {
                foreach (var p in parameters.parameters)
                    if (p != null && !string.IsNullOrEmpty(p.name)) names.Add(p.name);
            }

            CollectParamNames(menu, names, new HashSet<VRCExpressionsMenu>());
            return names.Count;
        }

        private static void CollectParamNames(VRCExpressionsMenu menu, HashSet<string> names, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu)) return;

            if (menu.Parameters != null && menu.Parameters.parameters != null)
            {
                foreach (var p in menu.Parameters.parameters)
                    if (p != null && !string.IsNullOrEmpty(p.name)) names.Add(p.name);
            }

            if (menu.controls == null) return;
            foreach (var c in menu.controls)
            {
                if (c == null) continue;
                if (c.parameter != null && !string.IsNullOrEmpty(c.parameter.name)) names.Add(c.parameter.name);
                if (c.subParameters != null)
                {
                    foreach (var sp in c.subParameters)
                        if (sp != null && !string.IsNullOrEmpty(sp.name)) names.Add(sp.name);
                }
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    CollectParamNames(c.subMenu, names, seen);
            }
        }

        private static Object ResolveObjRef(SerializedProperty array, string wrapperField)
        {
            if (array == null || array.arraySize == 0) return null;

            var element = array.GetArrayElementAtIndex(0);
            var wrapper = element.FindPropertyRelative(wrapperField);
            if (wrapper == null) return null;

            var objRef = wrapper.FindPropertyRelative("objRef");
            if (objRef?.objectReferenceValue != null)
                return objRef.objectReferenceValue;

            // GuidWrapper keeps a GUID in `id` even when objRef hasn't been resolved (e.g. prefab YAML).
            var id = wrapper.FindPropertyRelative("id")?.stringValue;
            if (string.IsNullOrEmpty(id)) return null;

            var guid = id.Split(':')[0];
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
        }

        private static List<Component> FindVrcFuryComponents(GameObject go)
        {
            var result = new List<Component>();
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().FullName == VrcFuryComponentTypeName)
                    result.Add(behaviour);
            }
            return result;
        }

    }
}
