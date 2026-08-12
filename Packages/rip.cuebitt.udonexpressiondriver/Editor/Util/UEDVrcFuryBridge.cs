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

        private static bool NeedsImport(UEDFullController controller, VRCExpressionsMenu menu, VRCExpressionParameters parameters)
        {
            var serialized = new SerializedObject(controller);
            var paramCount = serialized.FindProperty("paramNames")?.arraySize ?? 0;
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

        /// <summary>
        /// Test helper: fabricates a VF.Model.VRCFury component with an ArmatureLink feature on the
        /// selected GameObject, since avatar-side VRCFury components can't be added via the menu in a
        /// world project. Use it to exercise the bridge without an avatar project.
        /// </summary>
        [MenuItem("Tools/Udon Expression Driver/Add Test VRCFury ArmatureLink")]
        public static void AddTestVrcFuryArmatureLink()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogError("[UED] Select a prop first.");
                return;
            }

            var vfType = System.Type.GetType("VF.Model.VRCFury, VRCFury");
            var featureType = System.Type.GetType("VF.Model.Feature.ArmatureLink, VRCFury");
            if (vfType == null || featureType == null)
            {
                Debug.LogError("[UED] VRCFury types not found (is com.vrcfury.vrcfury installed?).");
                return;
            }

            var component = go.AddComponent(vfType);
            var serialized = new SerializedObject(component);
            var content = serialized.FindProperty("content");
            content.managedReferenceValue = System.Activator.CreateInstance(featureType);
            serialized.ApplyModifiedProperties();

            var linkTo = serialized.FindProperty("content").FindPropertyRelative("linkTo");
            if (linkTo.arraySize == 0) linkTo.arraySize = 1;
            var first = linkTo.GetArrayElementAtIndex(0);
            first.FindPropertyRelative("useBone").boolValue = true;
            first.FindPropertyRelative("bone").enumValueIndex = (int)UnityEngine.HumanBodyBones.Head;
            var propBone = serialized.FindProperty("content").FindPropertyRelative("propBone");
            if (propBone != null) propBone.objectReferenceValue = go;
            serialized.ApplyModifiedProperties();

            Debug.Log($"[UED] Added a test VRCFury ArmatureLink (bone = Head) to '{go.name}'. Now click 'Re-import from VRCFury' on its UEDArmatureLink.");
        }
    }
}
