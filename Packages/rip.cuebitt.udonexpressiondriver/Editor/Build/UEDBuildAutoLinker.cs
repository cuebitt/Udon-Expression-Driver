using System.Collections.Generic;
using UdonExpressionDriver;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// Adds and links Physbone/Contact forwarders on every UED prop automatically, behind the
    /// scenes (VRCFury-style): the forwarders exist only for the play session or the release
    /// build and are removed again afterwards, so the developer's authored scene is never
    /// permanently modified.
    /// Also ensures every UEDArmatureLink prop has a VRC Object Sync + kinematic, gravity-free
    /// Rigidbody (added permanently if missing) before play/build.
    ///   - Play mode: added at ExitingEditMode (before the scene is cloned), removed after
    ///     EnteredEditMode on the next editor update (once the edit scene is restored).
    ///   - Release build: added at IVRCSDKBuildRequestedCallback (edit mode) so the build bakes them,
    ///     then removed on the next build/play trigger. Never saved to the scene.
    /// Revert works by scanning for the autoLinked marker on forwarders (not a static list), so it
    /// survives the domain reload that happens when entering play mode.
    /// Idempotent: forwarders are only added where missing. Failures never abort a build.
    /// </summary>
    [InitializeOnLoad]
    public class UEDBuildAutoLinker : IVRCSDKBuildRequestedCallback
    {
        static UEDBuildAutoLinker()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                try
                {
                    AutoLinkForwarders();
                    EnsurePropComponents();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UED] Auto-link forwarders failed: {e}");
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // When EnteredEditMode fires the scene may not be restored to its pre-play state
                // yet, so reverting here either finds nothing or gets overwritten by the restore
                // that follows (leaving the auto-added Animator/puppets/menu behind in the scene).
                // Defer to the next editor update so the cleanup runs against the restored edit
                // scene; each pass is isolated so one failure can't skip the others.
                EditorApplication.delayCall += RevertAutoLinkedAfterPlay;
            }
        }

        /// <summary>
        /// Runs after leaving play mode once the edit-mode scene has been restored. Skips when the
        /// editor is already heading back into play, since the next ExitingEditMode pass re-adds and
        /// cleans up its own leftovers.
        /// </summary>
        private static void RevertAutoLinkedAfterPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            TryRevert("forwarders", RevertForwarders);
            TryRevert("auto-linked menu/puppet objects", RevertAutoLinked);
            TryRevert("auto-added Animators", RevertAutoAddedAnimators);
            TryRevert("auto-added gesture params", RevertAutoAddedGestureParams);
        }

        private static void TryRevert(string what, System.Action revert)
        {
            try
            {
                revert();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UED] Failed to revert {what} after play mode: {e}");
            }
        }

        public int callbackOrder => 100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            // Release builds only (edit mode). ClientSim fires a build request during the play
            // transition; forwarders there are handled by the play hooks (isPlayingOrWillChangePlaymode).
            if (requestedBuildType == VRCSDKRequestedBuildType.Scene && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                try
                {
                    AutoLinkForwarders();
                    EnsurePropComponents();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UED] Auto-link forwarders failed; continuing build: {e}");
                }
            }

            return true;
        }

        // Every UED behaviour lives somewhere under a scene root; this is the shared walk
        // used by all the add/revert passes below. Scans every loaded scene so props in
        // additively loaded scenes are linked too.
        private static IEnumerable<T> FindInScene<T>() where T : Component
        {
            for (var i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<T>(true))
                        yield return component;
            }
        }

        /// <summary>
        /// Ensures every UEDArmatureLink prop has a VRC Object Sync and a Rigidbody (kinematic,
        /// no gravity) added permanently if missing, and every UEDFullController prop has an
        /// Animator before play/build. The Animator is transient: it is marked auto-added and
        /// removed again by RevertAutoAddedAnimators when leaving play mode, so the authored
        /// scene is never saved with it. VRCFury controller/menu/param data is also wired in
        /// (idempotently).
        /// </summary>
        private static void EnsurePropComponents()
        {
            TryRevert("leftover auto-linked menu/puppet objects", RevertAutoLinked);
            TryRevert("leftover auto-added Animators", RevertAutoAddedAnimators);
            TryRevert("leftover auto-added gesture params", RevertAutoAddedGestureParams);

            var processedLinks = new HashSet<GameObject>();
            var processedControllers = new HashSet<GameObject>();
            var addedCount = 0;

            foreach (var link in FindInScene<UEDArmatureLink>())
            {
                if (!processedLinks.Add(link.gameObject)) continue;

                var go = link.gameObject;
                var changed = false;

                if (go.GetComponent<Rigidbody>() == null)
                {
                    var rigidbody = go.AddComponent<Rigidbody>();
                    rigidbody.isKinematic = true;
                    rigidbody.useGravity = false;
                    changed = true;
                }

                if (go.GetComponent<VRCObjectSync>() == null)
                {
                    go.AddComponent<VRCObjectSync>();
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(go);
                    addedCount++;
                }
            }

            foreach (var controller in FindInScene<UEDFullController>())
            {
                if (!processedControllers.Add(controller.gameObject)) continue;

                var go = controller.gameObject;
                var changed = false;

                if (controller.transform.root.GetComponentInChildren<Animator>(true) == null)
                {
                    controller.transform.root.gameObject.AddComponent<Animator>();
                    changed = true;
                    UEDBehaviourInspector.SetMarker(controller, "autoAddedAnimator", true);
                }

                if (EnsureMenuView(controller)) changed = true;
                if (EnsurePuppets(controller)) changed = true;
                if (IsHandGestureEmulationEnabled(controller) && EnsureHandGestures(controller)) changed = true;

                // Idempotently wires VRCFury controller/menu/param data, then applies whatever
                // assets are stored on the controller (VRCFury's or the Expressions section's).
                UEDVrcFuryBridge.AutoImportMenu(controller);
                UEDVrcFuryBridge.ApplyExpressions(controller);
                UEDVrcFuryBridge.EnsureHandGestureParams(controller);

                // Swaps in a prop-relative copy of the controller (avatar-prop clip paths don't
                // resolve against the prop root's own Animator) as a transient generated asset.
                try
                {
                    UEDAnimatorRewriter.ApplyForProp(controller);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UED] Failed to rewrite animator controller for '{controller.name}': {e}", controller);
                }

                if (changed)
                {
                    EditorUtility.SetDirty(go);
                    addedCount++;
                }
            }

            if (addedCount > 0)
                Debug.Log($"[UED] Added missing Rigidbody/VRC Object Sync/Animator to {addedCount} prop(s).");
        }

        /// <summary>Creates a Radial Menu prefab instance as a child of the controller if menuView is unset.</summary>
        private static bool EnsureMenuView(UEDFullController controller)
        {
            var controllerSerialized = new SerializedObject(controller);
            var menuView = controllerSerialized.FindProperty("menuView");
            if (menuView == null || menuView.objectReferenceValue != null) return false;

            const string prefabPath = "Packages/rip.cuebitt.udonexpressiondriver/Runtime/ExpressionMenu/Radial Menu.prefab";
            var instance = InstantiateAutoLinked(prefabPath, "Expression Menu", controller.transform);
            if (instance == null) return false;

            var radialMenu = instance.GetComponent<RadialMenu>();
            if (radialMenu == null)
            {
                Object.DestroyImmediate(instance);
                return false;
            }

            menuView.objectReferenceValue = radialMenu;
            controllerSerialized.ApplyModifiedProperties();

            var radialSerialized = new SerializedObject(radialMenu);
            radialSerialized.FindProperty("fullController").objectReferenceValue = controller;
            radialSerialized.ApplyModifiedProperties();

            UEDBehaviourInspector.MarkAutoLinked(radialMenu);

            return true;
        }

        /// <summary>
        /// Creates the world-space puppet controls (Radial Puppet + Axis Puppet) as inactive
        /// children of the controller if the refs are unset, and wires each puppet's handler
        /// to the controller. Auto-added objects are marked so they can be removed again
        /// when leaving play mode.
        /// </summary>
        private static bool EnsurePuppets(UEDFullController controller)
        {
            var controllerSerialized = new SerializedObject(controller);
            var radialPuppetProp = controllerSerialized.FindProperty("radialPuppet");
            var axisPuppetProp = controllerSerialized.FindProperty("axisPuppet");
            if (radialPuppetProp == null || axisPuppetProp == null) return false;

            const string radialPrefabPath = "Packages/rip.cuebitt.udonexpressiondriver/Runtime/ExpressionMenu/Menu Controls/Radial Puppet/Radial Puppet.prefab";
            const string axisPrefabPath = "Packages/rip.cuebitt.udonexpressiondriver/Runtime/ExpressionMenu/Menu Controls/Axis Puppet/Axis Puppet.prefab";

            var radial = EnsurePuppet<RadialPuppet>(radialPuppetProp, radialPrefabPath, "Radial Puppet", controller);
            var axis = EnsurePuppet<AxisPuppet>(axisPuppetProp, axisPrefabPath, "Axis Puppet", controller);

            var changed = false;
            if (radial != null && radialPuppetProp.objectReferenceValue != radial)
            {
                radialPuppetProp.objectReferenceValue = radial;
                changed = true;
            }
            if (axis != null && axisPuppetProp.objectReferenceValue != axis)
            {
                axisPuppetProp.objectReferenceValue = axis;
                changed = true;
            }

            if (radial != null)
                if (LinkPuppetHandler(radial, controller)) changed = true;

            if (axis != null)
                if (LinkPuppetHandler(axis, controller)) changed = true;

            if (changed) controllerSerialized.ApplyModifiedProperties();
            return changed;
        }

        /// <summary>Returns the component already assigned to the controller, or spawns one if unset.</summary>
        private static T EnsurePuppet<T>(SerializedProperty prop, string prefabPath, string objectName, UEDFullController controller) where T : UdonSharpBehaviour
        {
            var existing = prop.objectReferenceValue as T;
            if (existing != null) return existing;

            var instance = InstantiateAutoLinked(prefabPath, objectName, controller.transform);
            if (instance == null) return null;

            var component = instance.GetComponent<T>();
            if (component != null) UEDBehaviourInspector.MarkAutoLinked(component);
            return component;
        }

        /// <summary>True when Hand Gesture Emulation is enabled on the controller.</summary>
        private static bool IsHandGestureEmulationEnabled(UEDFullController controller)
        {
            var serialized = new SerializedObject(controller);
            var prop = serialized.FindProperty("enableHandGestureEmulation");
            return prop != null && prop.boolValue;
        }

        /// <summary>
        /// Creates the world-space hand gesture menu as an inactive child of the controller if the
        /// ref is unset, and wires its handler to the controller. Auto-added objects are marked so
        /// they can be removed again when leaving play mode.
        /// </summary>
        private static bool EnsureHandGestures(UEDFullController controller)
        {
            var controllerSerialized = new SerializedObject(controller);
            var handGesturesProp = controllerSerialized.FindProperty("handGestures");
            if (handGesturesProp == null) return false;

            const string prefabPath = "Packages/rip.cuebitt.udonexpressiondriver/Runtime/ExpressionMenu/Menu Controls/Gesture Menu/Gesture Menu.prefab";

            var gesture = EnsurePuppet<HandGestureMenu>(handGesturesProp, prefabPath, "Hand Gestures", controller);

            var changed = false;
            if (gesture != null && handGesturesProp.objectReferenceValue != gesture)
            {
                handGesturesProp.objectReferenceValue = gesture;
                changed = true;
            }

            if (gesture != null)
                if (LinkPuppetHandler(gesture, controller)) changed = true;

            if (changed) controllerSerialized.ApplyModifiedProperties();
            return changed;
        }

        /// <summary>Instantiates one of the package's prefabs as an inactive child, ready to be auto-linked.</summary>
        private static GameObject InstantiateAutoLinked(string prefabPath, string objectName, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[UED] Could not load prefab at '{prefabPath}'.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return null;

            instance.name = objectName;
            instance.transform.SetParent(parent, false);
            instance.SetActive(false);
            return instance;
        }

        /// <summary>Assigns the controller as the puppet's typed handler (idempotent).</summary>
        private static bool LinkPuppetHandler(UdonSharpBehaviour puppet, UEDPuppetHandler controller)
        {
            var serialized = new SerializedObject(puppet);
            var handlerProperty = serialized.FindProperty("handler");
            if (handlerProperty == null) return false;
            if (handlerProperty.objectReferenceValue == controller) return false;
            handlerProperty.objectReferenceValue = controller;
            serialized.ApplyModifiedProperties();
            return true;
        }

        private static void AutoLinkForwarders()
        {
            TryRevert("leftover forwarders", RevertForwarders);

            var processed = new HashSet<GameObject>();
            var totalPhysbone = 0;
            var totalContact = 0;

            foreach (var behaviour in FindInScene<UEDBehaviour>())
            {
                if (!processed.Add(behaviour.gameObject)) continue;

                var (physboneCount, contactCount) =
                    UEDBehaviourInspector.LinkChildForwardersAndCount(behaviour.gameObject);
                totalPhysbone += physboneCount;
                totalContact += contactCount;
            }

            if (totalPhysbone + totalContact > 0)
                Debug.Log($"[UED] Linked {totalPhysbone} Physbone + {totalContact} Contact forwarder(s).");
        }

        private static void RevertForwarders()
        {
            foreach (var forwarder in FindInScene<PhysboneForwarder>())
                if (UEDBehaviourInspector.IsAutoLinked(forwarder)) DestroyForwarder(forwarder);

            foreach (var forwarder in FindInScene<ContactForwarder>())
                if (UEDBehaviourInspector.IsAutoLinked(forwarder)) DestroyForwarder(forwarder);
        }

        private static void DestroyForwarder(UdonSharpBehaviour forwarder)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(forwarder);
            if (backing != null) Object.DestroyImmediate(backing);
            Object.DestroyImmediate(forwarder);
        }

        /// <summary>
        /// Removes the Animator that EnsurePropComponents auto-added to a controller's prop (when the
        /// prop had none) and unsets the controller's animator reference, so the authored scene is
        /// never saved with it. Idempotent: only controllers carrying the hidden autoAddedAnimator
        /// marker are touched; pre-existing Animators are never removed.
        /// </summary>
        private static void RevertAutoAddedAnimators()
        {
            var removed = 0;

            foreach (var controller in FindInScene<UEDFullController>())
            {
                var serialized = new SerializedObject(controller);
                var autoAdded = serialized.FindProperty("autoAddedAnimator");
                if (autoAdded == null || !autoAdded.boolValue) continue;

                var animator = serialized.FindProperty("animator")?.objectReferenceValue as Animator;
                // The marker is only set when the prop had no Animator anywhere, so any Animator the
                // controller's hierarchy holds now is the one UED added to the prop root.
                if (animator == null)
                    animator = controller.transform.root.GetComponentInChildren<Animator>(true);

                // Destroy the Animator before clearing the field: ApplyModifiedProperties below fires
                // OnValidate, which would otherwise re-grab the still-present Animator and leave the
                // field pointing at the just-destroyed component (inspector shows "Missing (Animator)").
                if (animator != null)
                {
                    Object.DestroyImmediate(animator);
                    removed++;
                }

                var animatorProperty = serialized.FindProperty("animator");
                if (animatorProperty != null) animatorProperty.objectReferenceValue = null;
                autoAdded.boolValue = false;
                serialized.ApplyModifiedProperties();
            }

            if (removed > 0)
                Debug.Log($"[UED] Removed {removed} auto-added Animator(s).");
        }

        /// <summary>
        /// Removes the radial menu view and puppet controls that UEDBuildAutoLinker added
        /// before play/build, and clears the controller's references to them, so the authored
        /// scene is never saved with them. Idempotent: only objects carrying the hidden
        /// autoLinked marker (which survives the play-mode domain reload) are removed.
        /// </summary>
        private static void RevertAutoLinked()
        {
            var marked = new HashSet<GameObject>();

            foreach (var menu in FindInScene<RadialMenu>())
                if (UEDBehaviourInspector.IsAutoLinked(menu)) marked.Add(menu.gameObject);

            foreach (var puppet in FindInScene<RadialPuppet>())
                if (UEDBehaviourInspector.IsAutoLinked(puppet)) marked.Add(puppet.gameObject);

            foreach (var puppet in FindInScene<AxisPuppet>())
                if (UEDBehaviourInspector.IsAutoLinked(puppet)) marked.Add(puppet.gameObject);

            foreach (var menu in FindInScene<HandGestureMenu>())
                if (UEDBehaviourInspector.IsAutoLinked(menu)) marked.Add(menu.gameObject);

            if (marked.Count == 0) return;

            foreach (var controller in FindInScene<UEDFullController>())
                ClearAutoAddedRefs(controller, marked);

            foreach (var go in marked)
                if (go != null) Object.DestroyImmediate(go);

            Debug.Log($"[UED] Removed {marked.Count} auto-added menu/puppet object(s).");
        }

        /// <summary>Clears a controller's refs to auto-added menu/puppet objects before they are destroyed.</summary>
        private static void ClearAutoAddedRefs(UEDFullController controller, HashSet<GameObject> marked)
        {
            var serialized = new SerializedObject(controller);
            var changed = false;

            var menuView = serialized.FindProperty("menuView");
            if (menuView != null && menuView.objectReferenceValue is RadialMenu radialMenu &&
                radialMenu != null && radialMenu.gameObject != null && marked.Contains(radialMenu.gameObject))
            {
                menuView.objectReferenceValue = null;
                changed = true;
            }

            foreach (var field in new[] { "radialPuppet", "axisPuppet", "handGestures" })
            {
                var prop = serialized.FindProperty(field);
                if (prop == null) continue;
                var component = prop.objectReferenceValue as Component;
                if (component != null && marked.Contains(component.gameObject))
                {
                    prop.objectReferenceValue = null;
                    changed = true;
                }
            }

            if (changed) serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Removes the GestureLeft/GestureRight params that UEDBuildAutoLinker auto-appended to a
        /// controller's parameter arrays before play/build (via UEDVrcFuryBridge.EnsureHandGestureParams),
        /// and clears the marker, so the authored scene is never saved with them. Idempotent: only the
        /// exact names recorded in autoAddedHandGestureParams are stripped, so a user's own gesture params
        /// are never touched.
        /// </summary>
        private static void RevertAutoAddedGestureParams()
        {
            var strippedCount = 0;

            foreach (var controller in FindInScene<UEDFullController>())
            {
                var serialized = new SerializedObject(controller);
                var markerProp = serialized.FindProperty("autoAddedHandGestureParams");
                if (markerProp == null || string.IsNullOrEmpty(markerProp.stringValue)) continue;

                if (StripGestureParams(serialized, markerProp.stringValue)) strippedCount++;

                markerProp.stringValue = "";
                serialized.ApplyModifiedProperties();
            }

            if (strippedCount > 0)
                Debug.Log($"[UED] Removed {strippedCount} auto-added Hand Gesture parameter set(s).");
        }

        private static bool StripGestureParams(SerializedObject serialized, string namesCsv)
        {
            var names = namesCsv.Split(',');
            var namesProp = serialized.FindProperty("paramNames");
            var typesProp = serialized.FindProperty("paramTypes");
            var defaultsProp = serialized.FindProperty("paramDefaults");
            var syncedProp = serialized.FindProperty("paramSynced");
            if (namesProp == null) return false;

            var keepNames = new List<string>();
            var keepTypes = new List<int>();
            var keepDefaults = new List<float>();
            var keepSynced = new List<bool>();
            var removed = false;

            for (var i = 0; i < namesProp.arraySize; i++)
            {
                var name = namesProp.GetArrayElementAtIndex(i).stringValue;
                if (Contains(name, names))
                {
                    removed = true;
                    continue;
                }
                keepNames.Add(name);
                keepTypes.Add(typesProp != null ? typesProp.GetArrayElementAtIndex(i).intValue : 0);
                keepDefaults.Add(defaultsProp != null ? defaultsProp.GetArrayElementAtIndex(i).floatValue : 0f);
                keepSynced.Add(syncedProp != null ? syncedProp.GetArrayElementAtIndex(i).boolValue : false);
            }

            if (!removed) return false;

            WriteArray(namesProp, keepNames);
            WriteArray(typesProp, keepTypes);
            WriteArray(defaultsProp, keepDefaults);
            WriteArray(syncedProp, keepSynced);
            return true;
        }

        private static bool Contains(string value, string[] candidates)
        {
            foreach (var c in candidates)
            {
                if (c == value) return true;
            }
            return false;
        }

        private static void WriteArray<T>(SerializedProperty prop, List<T> values)
        {
            if (prop == null) return;
            prop.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                prop.GetArrayElementAtIndex(i).boxedValue = values[i];
        }
    }
}
