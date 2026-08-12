using System.Collections.Generic;
using UdonExpressionDriver;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    ///   - Play mode: added at ExitingEditMode (before the scene is cloned), removed at EnteredEditMode.
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
                RevertForwarders();
                RevertAutoLinked();
                RevertAutoAddedAnimators();
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
            RevertAutoLinked();
            RevertAutoAddedAnimators();

            var scene = EditorSceneManager.GetActiveScene();
            var processedLinks = new HashSet<GameObject>();
            var processedControllers = new HashSet<GameObject>();
            var addedCount = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var link in root.GetComponentsInChildren<UEDArmatureLink>(true))
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

                foreach (var controller in root.GetComponentsInChildren<UEDFullController>(true))
                {
                    if (!processedControllers.Add(controller.gameObject)) continue;

                    var go = controller.gameObject;
                    var changed = false;

                    if (controller.transform.root.GetComponentInChildren<Animator>(true) == null)
                    {
                        controller.transform.root.gameObject.AddComponent<Animator>();
                        changed = true;
                        MarkAutoAddedAnimator(controller);
                    }

                    if (EnsureMenuView(controller)) changed = true;
                    if (EnsurePuppets(controller)) changed = true;

                    // Idempotently wires VRCFury controller/menu/param data, then applies whatever
                    // assets are stored on the controller (VRCFury's or the Expressions section's).
                    UEDVrcFuryBridge.AutoImportMenu(controller);
                    UEDVrcFuryBridge.ApplyExpressions(controller);

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
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[UED] Could not load Radial Menu prefab at '{prefabPath}'.");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return false;

            instance.name = "Expression Menu";
            instance.transform.SetParent(controller.transform, false);
            instance.SetActive(false);

            var radialMenu = instance.GetComponent<RadialMenu>();
            if (radialMenu == null) return false;

            menuView.objectReferenceValue = radialMenu;
            controllerSerialized.ApplyModifiedProperties();

            var radialSerialized = new SerializedObject(radialMenu);
            radialSerialized.FindProperty("fullController").objectReferenceValue = controller;
            radialSerialized.ApplyModifiedProperties();

            MarkAutoLinked(radialMenu);

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

            var radial = radialPuppetProp.objectReferenceValue as GameObject;
            var axis = axisPuppetProp.objectReferenceValue as GameObject;

            const string radialPrefabPath = "Packages/rip.cuebitt.udonexpressiondriver/Runtime/ExpressionMenu/Menu Controls/Radial Puppet/Radial Puppet.prefab";
            const string axisPrefabPath = "Packages/rip.cuebitt.udonexpressiondriver/Runtime/ExpressionMenu/Menu Controls/Axis Puppet/Axis Puppet.prefab";

            if (radial == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(radialPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[UED] Could not load Radial Puppet prefab at '{radialPrefabPath}'.");
                }
                else
                {
                    radial = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    if (radial != null)
                    {
                        radial.name = "Radial Puppet";
                        radial.transform.SetParent(controller.transform, false);
                        radial.SetActive(false);
                        if (radial.GetComponent<RadialPuppet>() is RadialPuppet puppetBehaviour)
                            MarkAutoLinked(puppetBehaviour);
                    }
                }
            }

            if (axis == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(axisPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[UED] Could not load Axis Puppet prefab at '{axisPrefabPath}'.");
                }
                else
                {
                    axis = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    if (axis != null)
                    {
                        axis.name = "Axis Puppet";
                        axis.transform.SetParent(controller.transform, false);
                        axis.SetActive(false);
                        if (axis.GetComponent<AxisPuppet>() is AxisPuppet puppetBehaviour)
                            MarkAutoLinked(puppetBehaviour);
                    }
                }
            }

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

            if (radial != null && radial.GetComponent<RadialPuppet>() is RadialPuppet radialPuppet)
                if (LinkPuppetHandler(radialPuppet, controller)) changed = true;

            if (axis != null && axis.GetComponent<AxisPuppet>() is AxisPuppet axisPuppet)
                if (LinkPuppetHandler(axisPuppet, controller)) changed = true;

            if (changed) controllerSerialized.ApplyModifiedProperties();
            return changed;
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
            RevertForwarders();

            var scene = EditorSceneManager.GetActiveScene();
            var processed = new HashSet<GameObject>();
            var totalPhysbone = 0;
            var totalContact = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<UEDBehaviour>(true))
                {
                    if (!processed.Add(behaviour.gameObject)) continue;

                    var (physboneCount, contactCount) =
                        UEDBehaviourInspector.LinkChildForwardersAndCount(behaviour.gameObject);
                    totalPhysbone += physboneCount;
                    totalContact += contactCount;
                }
            }

            if (totalPhysbone + totalContact > 0)
                Debug.Log($"[UED] Linked {totalPhysbone} Physbone + {totalContact} Contact forwarder(s).");
        }

        private static void RevertForwarders()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var forwarder in root.GetComponentsInChildren<PhysboneForwarder>(true))
                    if (IsAutoLinked(forwarder)) DestroyForwarder(forwarder);

                foreach (var forwarder in root.GetComponentsInChildren<ContactForwarder>(true))
                    if (IsAutoLinked(forwarder)) DestroyForwarder(forwarder);
            }
        }

        private static bool IsAutoLinked(UdonSharpBehaviour forwarder)
        {
            var serialized = new SerializedObject(forwarder);
            var property = serialized.FindProperty("autoLinked");
            return property != null && property.boolValue;
        }

        private static void DestroyForwarder(UdonSharpBehaviour forwarder)
        {
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(forwarder);
            if (backing != null) Object.DestroyImmediate(backing);
            Object.DestroyImmediate(forwarder);
        }

        /// <summary>Flags a behaviour as auto-added so it can be removed again later (survives domain reloads).</summary>
        private static void MarkAutoLinked(UdonSharpBehaviour behaviour)
        {
            var serialized = new SerializedObject(behaviour);
            var property = serialized.FindProperty("autoLinked");
            if (property == null) return;
            property.boolValue = true;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Marks the controller as having had its missing Animator auto-added by EnsurePropComponents,
        /// so RevertAutoAddedAnimators can remove it again when leaving play mode. The marker is a
        /// serialized field on the controller (survives the play-mode domain reload).
        /// </summary>
        private static void MarkAutoAddedAnimator(UEDFullController controller)
        {
            var serialized = new SerializedObject(controller);
            var property = serialized.FindProperty("autoAddedAnimator");
            if (property == null) return;
            property.boolValue = true;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Removes the Animator that EnsurePropComponents auto-added to a controller's prop (when the
        /// prop had none) and unsets the controller's animator reference, so the authored scene is
        /// never saved with it. Idempotent: only controllers carrying the hidden autoAddedAnimator
        /// marker are touched; pre-existing Animators are never removed.
        /// </summary>
        private static void RevertAutoAddedAnimators()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var removed = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var controller in root.GetComponentsInChildren<UEDFullController>(true))
                {
                    var serialized = new SerializedObject(controller);
                    var autoAdded = serialized.FindProperty("autoAddedAnimator");
                    if (autoAdded == null || !autoAdded.boolValue) continue;

                    var animator = serialized.FindProperty("animator")?.objectReferenceValue as Animator;
                    if (animator == null)
                        animator = controller.transform.root.gameObject.GetComponent<Animator>();

                    var animatorProperty = serialized.FindProperty("animator");
                    if (animatorProperty != null) animatorProperty.objectReferenceValue = null;
                    autoAdded.boolValue = false;
                    serialized.ApplyModifiedProperties();

                    if (animator != null)
                    {
                        Object.DestroyImmediate(animator);
                        removed++;
                    }
                }
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
            var scene = EditorSceneManager.GetActiveScene();
            var marked = new HashSet<GameObject>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var menu in root.GetComponentsInChildren<RadialMenu>(true))
                    if (menu != null && IsAutoLinked(menu)) marked.Add(menu.gameObject);

                foreach (var puppet in root.GetComponentsInChildren<RadialPuppet>(true))
                    if (puppet != null && IsAutoLinked(puppet)) marked.Add(puppet.gameObject);

                foreach (var puppet in root.GetComponentsInChildren<AxisPuppet>(true))
                    if (puppet != null && IsAutoLinked(puppet)) marked.Add(puppet.gameObject);
            }

            if (marked.Count == 0) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var controller in root.GetComponentsInChildren<UEDFullController>(true))
                    ClearAutoAddedRefs(controller, marked);
            }

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

            foreach (var field in new[] { "radialPuppet", "axisPuppet" })
            {
                var prop = serialized.FindProperty(field);
                if (prop == null) continue;
                var go = prop.objectReferenceValue as GameObject;
                if (go != null && marked.Contains(go))
                {
                    prop.objectReferenceValue = null;
                    changed = true;
                }
            }

            if (changed) serialized.ApplyModifiedProperties();
        }
    }
}
