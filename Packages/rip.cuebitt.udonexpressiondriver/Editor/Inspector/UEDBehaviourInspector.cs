using UdonExpressionDriver;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace UdonExpressionDriver.Editor
{
    [CustomEditor(typeof(UEDBehaviour), true)]
    public class UEDBehaviourInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)) return;

            DrawDescription(
                "Base class shared by UED prop behaviours. It does nothing on its own. " +
                "Add a UEDArmatureLink for a wearable prop or a UEDFullController to drive an expressions menu.");

            // Draw the behaviour's serialized fields (param/menu arrays, etc.).
            base.OnInspectorGUI();
        }

        /// <summary>Draws a VRCFury-style info box at the top of the inspector explaining what the component does.</summary>
        protected static void DrawDescription(string text)
        {
            EditorGUILayout.HelpBox(text, MessageType.Info);
        }

        private static readonly GUIStyle SectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            margin = new RectOffset(0, 0, 2, 6),
        };

        /// <summary>Starts a titled help-box section grouping inspector fields.</summary>
        protected static void BeginSection(string title)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, SectionTitleStyle);
        }

        /// <summary>Ends a section started with <see cref="BeginSection"/>.</summary>
        protected static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Adds a PhysboneForwarder to every child with a VRCPhysBone and a ContactForwarder
        /// to every child with a contact sender/receiver, wiring them to the root UEDBehaviour.
        /// Added forwarders are flagged with the hidden autoLinked marker so UEDBuildAutoLinker
        /// can remove them again after play/build (survives domain reloads). Idempotent: skips
        /// children that already have one.
        /// </summary>
        public static (int PhysboneCount, int ContactCount) LinkChildForwardersAndCount(GameObject go)
        {
            var rootBehaviour = go.GetComponent<UEDBehaviour>();
            if (rootBehaviour == null) return (0, 0);

            var physboneCount = 0;
            var contactCount = 0;

            foreach (var child in go.GetComponentsInChildren<Transform>(true))
            {
                var childGo = child.gameObject;
                if (childGo == go) continue;

                if (childGo.GetComponent<VRCPhysBone>() != null && childGo.GetComponent<PhysboneForwarder>() == null)
                {
                    var forwarder = UdonSharpUndo.AddComponent<PhysboneForwarder>(childGo);
                    ConfigureForwarder(forwarder, rootBehaviour);
                    physboneCount++;
                }

                var hasContact = childGo.GetComponent<VRCContactReceiver>() != null ||
                                 childGo.GetComponent<VRCContactSender>() != null;
                if (hasContact && childGo.GetComponent<ContactForwarder>() == null)
                {
                    var forwarder = UdonSharpUndo.AddComponent<ContactForwarder>(childGo);
                    ConfigureForwarder(forwarder, rootBehaviour);
                    contactCount++;
                }
            }

            return (physboneCount, contactCount);
        }

        /// <summary>Wires the target and marks the forwarder as auto-linked (tool-managed).</summary>
        private static void ConfigureForwarder(UdonSharpBehaviour forwarder, UdonSharpBehaviour target)
        {
            var serialized = new SerializedObject(forwarder);
            var targetProperty = serialized.FindProperty("target");
            if (targetProperty != null) targetProperty.objectReferenceValue = target;
            var autoProperty = serialized.FindProperty("autoLinked");
            if (autoProperty != null) autoProperty.boolValue = true;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>Reads a hidden marker field off a tool-managed behaviour (survives domain reloads).</summary>
        internal static bool GetMarker(UdonSharpBehaviour behaviour, string field)
        {
            var serialized = new SerializedObject(behaviour);
            var property = serialized.FindProperty(field);
            return property != null && property.boolValue;
        }

        /// <summary>Sets a hidden marker field on a tool-managed behaviour (survives domain reloads).</summary>
        internal static void SetMarker(UdonSharpBehaviour behaviour, string field, bool value)
        {
            var serialized = new SerializedObject(behaviour);
            var property = serialized.FindProperty(field);
            if (property == null) return;
            property.boolValue = value;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>True if the behaviour was added by the auto-linker and can be removed again.</summary>
        internal static bool IsAutoLinked(UdonSharpBehaviour behaviour)
        {
            return GetMarker(behaviour, "autoLinked");
        }

        /// <summary>Flags a behaviour as auto-linked so it can be reverted after play/build.</summary>
        internal static void MarkAutoLinked(UdonSharpBehaviour behaviour)
        {
            SetMarker(behaviour, "autoLinked", true);
        }
    }
}