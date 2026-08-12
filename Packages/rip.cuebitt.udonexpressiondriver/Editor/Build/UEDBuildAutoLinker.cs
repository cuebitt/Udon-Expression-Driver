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
        /// no gravity), and every UEDFullController prop has an Animator, before play/build.
        /// VRCFury controller/menu/param data is also wired in (idempotently). Added permanently
        /// if missing. These are required components, not transient wiring.
        /// </summary>
        private static void EnsurePropComponents()
        {
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
                    }

                    if (EnsureMenuView(controller)) changed = true;

                    // Idempotently wires VRCFury controller/menu/param data, then applies whatever
                    // assets are stored on the controller (VRCFury's or the Expressions section's).
                    UEDVrcFuryBridge.AutoImportMenu(controller);
                    UEDVrcFuryBridge.ApplyExpressions(controller);

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

            var radialMenu = instance.GetComponent<RadialMenu>();
            if (radialMenu == null) return false;

            menuView.objectReferenceValue = radialMenu;
            controllerSerialized.ApplyModifiedProperties();

            var radialSerialized = new SerializedObject(radialMenu);
            radialSerialized.FindProperty("fullController").objectReferenceValue = controller;
            radialSerialized.ApplyModifiedProperties();

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
    }
}
