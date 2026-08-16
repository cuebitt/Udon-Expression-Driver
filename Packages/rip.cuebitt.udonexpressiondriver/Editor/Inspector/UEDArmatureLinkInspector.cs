using System.Collections.Generic;
using UdonExpressionDriver;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// ArmatureLink inspector: grouped Attach Target / Behavior / Events / Status sections.
    /// If the prop carries a VRCFury ArmatureLink feature, a "Re-import from VRCFury" button pulls
    /// its bone and attach point back in; nothing is locked. targetBone is drawn as a grouped
    /// body-region dropdown.
    /// </summary>
    [CustomEditor(typeof(UEDArmatureLink))]
    public class UEDArmatureLinkInspector : UEDBehaviourInspector
    {
        private static readonly Dictionary<int, bool> EventsExpanded = new Dictionary<int, bool>();

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)) return;

            var link = (UEDArmatureLink)target;
            var hasVrcFury = UEDVrcFuryBridge.HasArmatureLink(link);

            DrawDescription(
                "Lets a world prop be worn on a player's avatar. While worn, the prop follows the " +
                "chosen bone and VRC Object Sync keeps its position in sync for everyone. Nothing is " +
                "reparented into the avatar, so removing the component leaves the prop as it was.");

            DrawAttachTargetSection(link, hasVrcFury);
            DrawBehaviorSection();
            DrawEventsSection();
            DrawStatus(link);
        }

        private void DrawAttachTargetSection(UEDArmatureLink link, bool hasVrcFury)
        {
            BeginSection("Attach Target");

            serializedObject.Update();
            DrawBonePopup(serializedObject.FindProperty("targetBone"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("attachPoint"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("positionOffset"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationOffset"));
            serializedObject.ApplyModifiedProperties();

            if (hasVrcFury)
            {
                if (GUILayout.Button("Re-import from VRCFury"))
                    UEDVrcFuryBridge.AutoImportArmatureLink(link);

                EditorGUILayout.HelpBox(
                    "A VRCFury ArmatureLink on this prop supplied the bone and attach point above. " +
                    "Click 'Re-import from VRCFury' to pull them back in. Any edits you make here stay until you do.",
                    MessageType.Info);
            }

            EndSection();
        }

        private void DrawBehaviorSection()
        {
            BeginSection("Behavior");

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoWearOnPickup"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wearOnUse"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("releaseUnwears"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("interactTogglesWear"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("disableCollidersWhileWorn"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ownedObjects"));
            serializedObject.ApplyModifiedProperties();

            EndSection();
        }

        private void DrawEventsSection()
        {
            var instanceId = target.GetInstanceID();
            var expanded = EventsExpanded.TryGetValue(instanceId, out var value) && value;

            EditorGUILayout.Space(8);
            expanded = EditorGUILayout.Foldout(expanded, "Events", true);
            EventsExpanded[instanceId] = expanded;
            if (!expanded) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wearEventHandler"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wornEventName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("unwornEventName"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.EndVertical();
        }

        /// <summary>Draws targetBone as a body-region-grouped dropdown instead of the flat enum list.</summary>
        private static void DrawBonePopup(SerializedProperty boneProperty)
        {
            const string tooltip = "Humanoid bone on the wearer's avatar the prop sticks to.";

            var options = new List<string>();
            var values = new List<HumanBodyBones>();
            var headers = new HashSet<int>();

            var bones = (HumanBodyBones[])System.Enum.GetValues(typeof(HumanBodyBones));
            var current = (HumanBodyBones)boneProperty.enumValueIndex;
            var currentIndex = 0;

            string previousRegion = null;
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == HumanBodyBones.LastBone) continue; // enum sentinel, not a real bone

                var region = GetBoneRegion(bone);
                if (region != previousRegion)
                {
                    options.Add(region);
                    headers.Add(options.Count - 1);
                    values.Add(bone);
                    previousRegion = region;
                }

                options.Add("    " + bone);
                values.Add(bone);
                if (bone == current) currentIndex = options.Count - 1;
            }

            var selected = EditorGUILayout.Popup(new GUIContent("Target Bone", tooltip), currentIndex, options.ToArray());
            if (selected != currentIndex && !headers.Contains(selected) && selected >= 0 && selected < values.Count)
                boneProperty.enumValueIndex = (int)values[selected];
        }

        private static string GetBoneRegion(HumanBodyBones bone)
        {
            var name = bone.ToString();
            if (name.Contains("Toes")) return "Toes";
            if (name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") ||
                name.Contains("Ring") || name.Contains("Little")) return "Fingers";
            if (name.Contains("Hand") || name.Contains("Shoulder") || name.Contains("Arm")) return "Arms";
            if (name.Contains("UpperLeg") || name.Contains("LowerLeg") || name.Contains("Foot")) return "Legs";
            if (name.Contains("Head") || name.Contains("Neck")) return "Head & Neck";
            return "Torso";
        }

        private static void DrawStatus(UEDArmatureLink link)
        {
            BeginSection("Status");

            if (link.GetComponent<VRCObjectSync>() == null)
                EditorGUILayout.HelpBox(
                    "VRC Object Sync will be added automatically, along with a kinematic, gravity-free " +
                    "Rigidbody, when you enter play mode or build.",
                    MessageType.Warning);
            else
                EditorGUILayout.HelpBox(
                    "VRC Object Sync is present, so the worn transform syncs and interpolates for other players.",
                    MessageType.Info);

            EndSection();
        }
    }
}
