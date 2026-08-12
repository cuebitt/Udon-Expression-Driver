using System.Collections.Generic;
using UdonExpressionDriver;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// Rewrites an AnimatorController's animation bindings so they resolve against a prop in a
    /// world. Avatar-prop clips are authored with paths relative to the avatar root (e.g.
    /// "Glass Bottle/Bottle_Stuff/..."), but UED puts the Animator on the prop root itself, so
    /// those paths point at a nonexistent "Glass Bottle" child. This strips the leading prop-root
    /// segment (or any leading segment that doesn't resolve in the prop hierarchy) from every
    /// binding, copying the controller (via AssetDatabase.CopyAsset) and rewriting its clips into a
    /// generated asset under Assets/UEDGenerated so the authored assets are never modified. Applied
    /// (idempotently) whenever the prop's inspector repaints, and again at play-mode entry and
    /// release build, so the edit-mode Animation window, runtime, and builds all use prop-relative
    /// bindings.
    /// </summary>
    public static class UEDAnimatorRewriter
    {
        private const string GeneratedFolder = "Assets/UEDGenerated";

        /// <summary>
        /// Applies a rewritten, prop-relative copy of the controller stored on the prop to its
        /// Animator, creating a generated asset under Assets/UEDGenerated (so it survives scene
        /// baking and is visible in the edit-mode Animation window). Non-destructive: the source
        /// controller and clips are never modified. Idempotent: an up-to-date generated controller
        /// is reused (no regeneration), and nothing is generated when no binding needs rewriting.
        /// </summary>
        public static bool ApplyForProp(UEDFullController controller)
        {
            if (EditorApplication.isPlaying) return false;

            var serialized = new SerializedObject(controller);
            var original = serialized.FindProperty("importedAnimatorController")?.objectReferenceValue as RuntimeAnimatorController;
            var guidProperty = serialized.FindProperty("generatedControllerGuid");
            var sourceGuidProperty = serialized.FindProperty("generatedSourceGuid");
            var animator = FindAnimator(serialized, controller);

            // No source controller: drop any stale generated controller left behind.
            if (original == null)
            {
                if (guidProperty != null && !string.IsNullOrEmpty(guidProperty.stringValue))
                {
                    if (animator != null) animator.runtimeAnimatorController = null;
                    DeleteGeneratedAsset(guidProperty.stringValue);
                    guidProperty.stringValue = "";
                    if (sourceGuidProperty != null) sourceGuidProperty.stringValue = "";
                    serialized.ApplyModifiedProperties();
                }
                return false;
            }
            if (animator == null) return false;

            var propRoot = controller.transform.root.gameObject;
            var sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original));

            var existingGuid = guidProperty != null ? guidProperty.stringValue : "";
            var existingSource = sourceGuidProperty != null ? sourceGuidProperty.stringValue : "";
            var existingAsset = string.IsNullOrEmpty(existingGuid)
                ? null
                : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(existingGuid));

            // Already generated for this exact source -> just make sure the Animator uses it.
            if (existingAsset != null && existingSource == sourceGuid)
            {
                if (animator.runtimeAnimatorController != existingAsset)
                    animator.runtimeAnimatorController = existingAsset;
                return true;
            }

            // Stale or missing generated controller -> remove it before regenerating.
            if (existingAsset != null)
                DeleteGeneratedAsset(existingGuid);

            if (!NeedsRewrite(original, propRoot))
            {
                if (animator.runtimeAnimatorController != original)
                    animator.runtimeAnimatorController = original;
                return false;
            }

            if (!(original is AnimatorController source))
            {
                Debug.LogWarning($"[UED] Cannot rewrite animation paths for '{controller.name}': unsupported controller type '{original.GetType().Name}'.", controller);
                return false;
            }

            var asset = GenerateRewrittenAsset(source, propRoot, controller);
            if (asset == null) return false;

            if (guidProperty != null) guidProperty.stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            if (sourceGuidProperty != null) sourceGuidProperty.stringValue = sourceGuid;
            serialized.ApplyModifiedProperties();

            animator.runtimeAnimatorController = asset;
            Debug.Log($"[UED] Rewrote animation bindings for '{controller.name}' relative to the prop root; generated '{asset.name}'.", controller);
            return true;
        }

        private static Animator FindAnimator(SerializedObject serialized, UEDFullController controller)
        {
            var animator = serialized.FindProperty("animator")?.objectReferenceValue as Animator;
            if (animator == null)
                animator = controller.transform.root.GetComponentInChildren<Animator>(true);
            return animator;
        }

        /// <summary>True if any binding in any clip of the controller needs a path rewrite for this prop.</summary>
        private static bool NeedsRewrite(RuntimeAnimatorController controller, GameObject propRoot)
        {
            if (controller == null || propRoot == null) return false;
            foreach (var clip in controller.animationClips)
                if (clip != null && ClipNeedsRewrite(clip, propRoot)) return true;
            return false;
        }

        private static bool ClipNeedsRewrite(AnimationClip clip, GameObject propRoot)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (RewriteBindingPath(binding.path, propRoot) != binding.path) return true;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (RewriteBindingPath(binding.path, propRoot) != binding.path) return true;
            return false;
        }

        private static AnimatorController GenerateRewrittenAsset(AnimatorController source, GameObject propRoot, UEDFullController controller)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"[UED] Cannot rewrite animation paths for '{controller.name}': the controller is not a project asset.", controller);
                return null;
            }

            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.CreateFolder("Assets", "UEDGenerated");

            var fileName = SanitizeFilename($"{source.name}_{controller.gameObject.GetInstanceID()}.controller");
            var path = GeneratedFolder + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);

            // Object.Instantiate on an AnimatorController shares the state graph with the source, so
            // instead copy the controller asset file: the copy's layers/states are truly independent.
            if (!AssetDatabase.CopyAsset(sourcePath, path))
            {
                Debug.LogWarning($"[UED] Failed to copy animator controller '{source.name}' for '{controller.name}'; skipping path rewrite.", controller);
                return null;
            }

            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (copy == null) return null;
            copy.name = "UED_" + source.name;

            // Rewrite every clip binding to be prop-relative. Clips that change are cloned in-memory
            // (Object.Instantiate deep-copies an AnimationClip); the source clips are never touched.
            var cache = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var layer in copy.layers)
                if (layer.stateMachine != null)
                    RewriteStateMachine(layer.stateMachine, propRoot, cache);

            // Persist rewritten clips as sub-assets of the copied controller so builds bake them.
            var seen = new HashSet<AnimationClip>();
            foreach (var layer in copy.layers)
                if (layer.stateMachine != null)
                    CollectClips(layer.stateMachine, seen);

            var usedNames = new HashSet<string>();
            foreach (var clip in seen)
            {
                if (AssetDatabase.Contains(clip)) continue;
                var name = clip.name;
                var suffix = 1;
                while (!usedNames.Add(name))
                    name = $"{clip.name} {suffix++}";
                clip.name = name;
                AssetDatabase.AddObjectToAsset(clip, copy);
            }

            AssetDatabase.SaveAssets();
            return copy;
        }

        private static void RewriteStateMachine(AnimatorStateMachine stateMachine, GameObject propRoot, Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (stateMachine == null) return;
            foreach (var childState in stateMachine.states)
                if (childState.state != null)
                    childState.state.motion = RewriteMotion(childState.state.motion, propRoot, cache);
            foreach (var childMachine in stateMachine.stateMachines)
                RewriteStateMachine(childMachine.stateMachine, propRoot, cache);
        }

        private static Motion RewriteMotion(Motion motion, GameObject propRoot, Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (motion is AnimationClip clip)
                return RewriteClip(clip, propRoot, cache);

            if (motion is BlendTree tree)
            {
                var children = tree.children;
                var changed = false;
                for (var i = 0; i < children.Length; i++)
                {
                    var newMotion = RewriteMotion(children[i].motion, propRoot, cache);
                    if (!ReferenceEquals(newMotion, children[i].motion))
                    {
                        children[i].motion = newMotion;
                        changed = true;
                    }
                }
                if (changed) tree.children = children;
            }
            return motion;
        }

        private static AnimationClip RewriteClip(AnimationClip clip, GameObject propRoot, Dictionary<AnimationClip, AnimationClip> cache)
        {
            if (cache.TryGetValue(clip, out var existing)) return existing;
            if (!ClipNeedsRewrite(clip, propRoot))
            {
                cache[clip] = clip;
                return clip;
            }

            var clone = Object.Instantiate(clip);
            clone.name = clip.name;
            cache[clip] = clone;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                RewriteFloatBinding(clone, clip, binding, propRoot);
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                RewriteObjectBinding(clone, clip, binding, propRoot);

            return clone;
        }

        private static void RewriteFloatBinding(AnimationClip clone, AnimationClip source, EditorCurveBinding binding, GameObject propRoot)
        {
            var newPath = RewriteBindingPath(binding.path, propRoot);
            if (newPath == binding.path) return;
            if (ShouldDropRootActive(binding, newPath))
            {
                AnimationUtility.SetEditorCurve(clone, binding, null);
                return;
            }
            var newBinding = binding;
            newBinding.path = newPath;
            AnimationUtility.SetEditorCurve(clone, binding, null);
            AnimationUtility.SetEditorCurve(clone, newBinding, AnimationUtility.GetEditorCurve(source, binding));
        }

        private static void RewriteObjectBinding(AnimationClip clone, AnimationClip source, EditorCurveBinding binding, GameObject propRoot)
        {
            var newPath = RewriteBindingPath(binding.path, propRoot);
            if (newPath == binding.path) return;
            if (ShouldDropRootActive(binding, newPath))
            {
                AnimationUtility.SetObjectReferenceCurve(clone, binding, null);
                return;
            }
            var newBinding = binding;
            newBinding.path = newPath;
            AnimationUtility.SetObjectReferenceCurve(clone, binding, null);
            AnimationUtility.SetObjectReferenceCurve(clone, newBinding, AnimationUtility.GetObjectReferenceCurve(source, binding));
        }

        /// <summary>
        /// The Animator sits on the prop root, and a root GameObject can't be deactivated by its
        /// own Animator (it would stop the Animator, soft-locking the prop). Root-active toggles
        /// (e.g. an OFF clip disabling the whole bottle) are therefore dropped rather than bound
        /// to the empty root path; the child bindings still drive the visual off state.
        /// </summary>
        private static bool ShouldDropRootActive(EditorCurveBinding binding, string newPath)
        {
            return newPath == "" && binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive";
        }

        /// <summary>
        /// Computes a prop-relative binding path. Avatar-prop clips prefix every prop-internal path
        /// with the prop root's name; drop that leading segment. If the current root is named
        /// differently (the prop was renamed), fall back to stripping whatever leading segment
        /// actually resolves inside the prop hierarchy. Paths that still don't resolve (avatar
        /// bones etc.) are left untouched.
        /// </summary>
        private static string RewriteBindingPath(string path, GameObject propRoot)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var rootName = propRoot.name;
            if (!string.IsNullOrEmpty(rootName))
            {
                if (path == rootName) return "";
                if (path.StartsWith(rootName + "/")) return path.Substring(rootName.Length + 1);
            }

            if (propRoot.transform.Find(path) != null) return path;
            var segments = path.Split('/');
            if (segments.Length > 1)
            {
                var stripped = string.Join("/", segments, 1, segments.Length - 1);
                if (propRoot.transform.Find(stripped) != null) return stripped;
            }
            return path;
        }

        private static void CollectClips(AnimatorStateMachine stateMachine, HashSet<AnimationClip> seen)
        {
            if (stateMachine == null) return;
            foreach (var childState in stateMachine.states)
                if (childState.state != null)
                    CollectMotionClips(childState.state.motion, seen);
            foreach (var childMachine in stateMachine.stateMachines)
                CollectClips(childMachine.stateMachine, seen);
        }

        private static void CollectMotionClips(Motion motion, HashSet<AnimationClip> seen)
        {
            if (motion is AnimationClip clip)
            {
                seen.Add(clip);
            }
            else if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                    CollectMotionClips(child.motion, seen);
            }
        }

        private static string SanitizeFilename(string name)
        {
            var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (invalid.Contains(chars[i]) || chars[i] == ' ')
                    chars[i] = '_';
            var result = new string(chars);
            return string.IsNullOrEmpty(result) ? "UEDGenerated" : result;
        }

        private static void DeleteGeneratedAsset(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }
    }
}
