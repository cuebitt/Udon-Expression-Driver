using System.Collections.Generic;
using UdonExpressionDriver;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// Imports a VRCExpressionsMenu + VRCExpressionParameters into a UEDFullController's
    /// serialized arrays (the editor-side convenience; runtime data is embedded on the
    /// component). Puppet controls (two/four-axis, radial) import their sub-parameters
    /// into the flat controlSubParams array.
    /// </summary>
    public static class UEDExpressionImporter
    {
        private const int ControlTwoAxis = 3;
        private const int ControlFourAxis = 4;
        private const int ControlRadialPuppet = 6;
        private const int MaxPuppetSubParams = 4;

        private struct ControlDef
        {
            public int type;
            public string name;
            public Texture2D icon;
            public int paramIndex;
            public float value;
            public int subMenuIndex;
            public int[] subParamIndices;
        }

        public static void Import(UEDFullController controller, VRCExpressionsMenu menu, VRCExpressionParameters parameters)
        {
            if (controller == null) { Debug.LogError("[UED] No controller to import into."); return; }
            if (menu == null) { Debug.LogError("[UED] Specify an Expressions Menu."); return; }

            // Gather parameters (asset first, then menu-embedded + referenced).
            var paramNames = new List<string>();
            var paramTypes = new List<int>();
            var paramDefaults = new List<float>();
            var paramSynced = new List<bool>();
            var paramByName = new Dictionary<string, int>();

            void AddParam(string name, int type, float defaultValue, bool synced)
            {
                paramByName[name] = paramNames.Count;
                paramNames.Add(name);
                paramTypes.Add(type);
                paramDefaults.Add(defaultValue);
                paramSynced.Add(synced);
            }

            if (parameters != null && parameters.parameters != null)
            {
                foreach (var p in parameters.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || paramByName.ContainsKey(p.name)) continue;
                    AddParam(p.name, ToParamType(p.valueType), p.defaultValue, p.networkSynced);
                }
            }

            var seenMenus = new HashSet<VRCExpressionsMenu>();
            CollectReferencedParams(menu, seenMenus, paramByName, AddParam);

            // Flatten menus into a flat control list per menu level.
            var menuList = new List<List<ControlDef>>();
            var menuIndexMap = new Dictionary<VRCExpressionsMenu, int>();
            seenMenus.Clear();
            FlattenMenu(menu, menuList, menuIndexMap, seenMenus, paramByName);

            var controlTypes = new List<int>();
            var controlNames = new List<string>();
            var controlIcons = new List<Texture2D>();
            var controlParamIndex = new List<int>();
            var controlValues = new List<float>();
            var controlSubmenuIndex = new List<int>();
            var controlSubParamStart = new List<int>();
            var controlSubParams = new List<int>();
            var menuControlStart = new List<int>();

            var controlCount = 0;
            foreach (var controls in menuList)
            {
                menuControlStart.Add(controlCount);
                foreach (var c in controls)
                {
                    controlTypes.Add(c.type);
                    controlNames.Add(c.name ?? "");
                    controlIcons.Add(c.icon);
                    controlParamIndex.Add(c.paramIndex);
                    controlValues.Add(c.value);
                    controlSubmenuIndex.Add(c.subMenuIndex);

                    if (c.subParamIndices != null && c.subParamIndices.Length > 0)
                    {
                        controlSubParamStart.Add(controlSubParams.Count);
                        foreach (var subParam in c.subParamIndices)
                            controlSubParams.Add(subParam);
                    }
                    else
                    {
                        controlSubParamStart.Add(-1);
                    }

                    controlCount++;
                }
            }
            menuControlStart.Add(controlCount);

            // Write the flattened data into the controller's serialized fields.
            var serialized = new SerializedObject(controller);
            SetStringArray(serialized, "paramNames", paramNames);
            SetIntArray(serialized, "paramTypes", paramTypes);
            SetFloatArray(serialized, "paramDefaults", paramDefaults);
            SetBoolArray(serialized, "paramSynced", paramSynced);
            SetIntArray(serialized, "menuControlStart", menuControlStart);
            SetIntArray(serialized, "controlTypes", controlTypes);
            SetStringArray(serialized, "controlNames", controlNames);
            SetTextureArray(serialized, "controlIcons", controlIcons);
            SetIntArray(serialized, "controlParamIndex", controlParamIndex);
            SetFloatArray(serialized, "controlValues", controlValues);
            SetIntArray(serialized, "controlSubmenuIndex", controlSubmenuIndex);
            SetIntArray(serialized, "controlSubParamStart", controlSubParamStart);
            SetIntArray(serialized, "controlSubParams", controlSubParams);
            serialized.ApplyModifiedProperties();

            Debug.Log($"[UED] Imported {paramNames.Count} parameter(s), {menuList.Count} menu(s), {controlCount} control(s) into '{controller.name}'.");
        }

        private static void CollectReferencedParams(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> seen,
            Dictionary<string, int> paramByName, System.Action<string, int, float, bool> addParam)
        {
            if (menu == null || !seen.Add(menu)) return;

            if (menu.Parameters != null && menu.Parameters.parameters != null)
            {
                foreach (var p in menu.Parameters.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name) || paramByName.ContainsKey(p.name)) continue;
                    addParam(p.name, ToParamType(p.valueType), p.defaultValue, p.networkSynced);
                }
            }

            if (menu.controls == null) return;
            foreach (var c in menu.controls)
            {
                if (c == null) continue;
                if (c.parameter != null && !string.IsNullOrEmpty(c.parameter.name) && !paramByName.ContainsKey(c.parameter.name))
                    addParam(c.parameter.name, 0, 0f, true);

                if (c.subParameters != null)
                {
                    foreach (var sp in c.subParameters)
                    {
                        if (sp == null || string.IsNullOrEmpty(sp.name) || paramByName.ContainsKey(sp.name)) continue;
                        addParam(sp.name, 0, 0f, true);
                    }
                }

                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    CollectReferencedParams(c.subMenu, seen, paramByName, addParam);
            }
        }

        private static void FlattenMenu(VRCExpressionsMenu menu, List<List<ControlDef>> menuList,
            Dictionary<VRCExpressionsMenu, int> menuIndexMap, HashSet<VRCExpressionsMenu> seen,
            Dictionary<string, int> paramByName)
        {
            if (menu == null || !seen.Add(menu)) return;

            var index = menuList.Count;
            menuIndexMap[menu] = index;
            var controls = new List<ControlDef>();
            menuList.Add(controls);

            if (menu.controls == null) return;

            foreach (var c in menu.controls)
            {
                if (c == null) continue;

                var type = ToControlType(c.type);
                if (type < 0) continue;

                var control = new ControlDef
                {
                    type = type,
                    name = c.name,
                    icon = c.icon,
                    paramIndex = -1,
                    value = c.value,
                    subMenuIndex = -1,
                };

                if (type == 2) // SubMenu
                {
                    if (c.subMenu != null)
                    {
                        FlattenMenu(c.subMenu, menuList, menuIndexMap, seen, paramByName);
                        if (menuIndexMap.TryGetValue(c.subMenu, out var subIndex))
                            control.subMenuIndex = subIndex;
                    }
                }
                else if (type == ControlTwoAxis || type == ControlFourAxis || type == ControlRadialPuppet)
                {
                    if (c.subParameters != null)
                    {
                        var subParams = new List<int>();
                        for (var i = 0; i < System.Math.Min(c.subParameters.Length, MaxPuppetSubParams); i++)
                        {
                            var sp = c.subParameters[i];
                            if (sp == null || string.IsNullOrEmpty(sp.name)) continue;
                            if (paramByName.TryGetValue(sp.name, out var subIndex))
                                subParams.Add(subIndex);
                        }
                        if (subParams.Count > 0) control.subParamIndices = subParams.ToArray();
                    }
                }
                else if (c.parameter != null && paramByName.TryGetValue(c.parameter.name, out var paramIndex))
                {
                    control.paramIndex = paramIndex;
                }

                controls.Add(control);
            }
        }

        private static int ToParamType(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Int: return 1;
                case VRCExpressionParameters.ValueType.Bool: return 2;
                default: return 0; // Float
            }
        }

        private static int ToControlType(VRCExpressionsMenu.Control.ControlType type)
        {
            switch (type)
            {
                case VRCExpressionsMenu.Control.ControlType.Button: return 0;
                case VRCExpressionsMenu.Control.ControlType.Toggle: return 1;
                case VRCExpressionsMenu.Control.ControlType.SubMenu: return 2;
                case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet: return ControlTwoAxis;
                case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet: return ControlFourAxis;
                case VRCExpressionsMenu.Control.ControlType.RadialPuppet: return ControlRadialPuppet;
                default: return -1;
            }
        }

        private static void SetStringArray(SerializedObject so, string field, List<string> values)
        {
            var property = so.FindProperty(field);
            if (property == null) return;
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).stringValue = values[i] ?? "";
        }

        private static void SetIntArray(SerializedObject so, string field, List<int> values)
        {
            var property = so.FindProperty(field);
            if (property == null) return;
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).intValue = values[i];
        }

        private static void SetFloatArray(SerializedObject so, string field, List<float> values)
        {
            var property = so.FindProperty(field);
            if (property == null) return;
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).floatValue = values[i];
        }

        private static void SetBoolArray(SerializedObject so, string field, List<bool> values)
        {
            var property = so.FindProperty(field);
            if (property == null) return;
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).boolValue = values[i];
        }

        private static void SetTextureArray(SerializedObject so, string field, List<Texture2D> values)
        {
            var property = so.FindProperty(field);
            if (property == null) return;
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

    }
}
