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
        private const int ControlSubMenu = 2;
        private const int ControlTwoAxis = 3;
        private const int ControlFourAxis = 4;
        private const int ControlBack = 5;
        private const int ControlRadialPuppet = 6;
        private const int MaxPuppetSubParams = 4;
        private const int MaxMenuControls = 8;

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
            var truncatedControls = 0;
            seenMenus.Clear();
            FlattenMenu(menu, menuList, menuIndexMap, seenMenus, paramByName, ref truncatedControls);
            if (truncatedControls > 0)
                Debug.LogWarning($"[UED] Dropped {truncatedControls} control(s) that did not fit the radial menu's {MaxMenuControls} wedges (submenus hold one fewer control because of their Back wedge).", controller);

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
            SetArray(serialized, "paramNames", paramNames);
            SetArray(serialized, "paramTypes", paramTypes);
            SetArray(serialized, "paramDefaults", paramDefaults);
            SetArray(serialized, "paramSynced", paramSynced);
            SetArray(serialized, "menuControlStart", menuControlStart);
            SetArray(serialized, "controlTypes", controlTypes);
            SetArray(serialized, "controlNames", controlNames);
            SetTextureArray(serialized, "controlIcons", controlIcons);
            SetArray(serialized, "controlParamIndex", controlParamIndex);
            SetArray(serialized, "controlValues", controlValues);
            SetArray(serialized, "controlSubmenuIndex", controlSubmenuIndex);
            SetArray(serialized, "controlSubParamStart", controlSubParamStart);
            SetArray(serialized, "controlSubParams", controlSubParams);
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
            Dictionary<string, int> paramByName, ref int truncatedControls)
        {
            if (menu == null || !seen.Add(menu)) return;

            var index = menuList.Count;
            menuIndexMap[menu] = index;
            var controls = new List<ControlDef>();
            menuList.Add(controls);

            // The radial menu has MaxMenuControls wedges. VRChat adds a Back button implicitly to
            // every submenu, but UED's radial menu needs it as an explicit wedge, so a non-root
            // level gets the Back wedge (even when empty, so an empty submenu is never a dead end)
            // and one fewer slot for real controls. The runtime clamp would otherwise hide the
            // overflow controls silently; drop them here so the data matches what is shown.
            var maxControls = index == 0 ? MaxMenuControls : MaxMenuControls - 1;
            if (index > 0)
            {
                controls.Add(new ControlDef
                {
                    type = ControlBack,
                    name = "Back",
                    icon = null,
                    paramIndex = -1,
                    value = 0f,
                    subMenuIndex = -1,
                    subParamIndices = null,
                });
            }

            if (menu.controls == null) return;

            foreach (var c in menu.controls)
            {
                if (c == null) continue;

                var type = ToControlType(c.type);
                if (type < 0) continue;
                if (controls.Count >= maxControls)
                {
                    truncatedControls++;
                    continue;
                }

                var control = new ControlDef
                {
                    type = type,
                    name = c.name,
                    icon = c.icon,
                    paramIndex = -1,
                    value = c.value,
                    subMenuIndex = -1,
                };

                if (type == ControlSubMenu)
                {
                    if (c.subMenu != null)
                    {
                        FlattenMenu(c.subMenu, menuList, menuIndexMap, seen, paramByName, ref truncatedControls);
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

        /// <summary>
        /// Counts the controls <see cref="Import"/> will flatten for a menu, Back wedges and
        /// per-menu caps included. UEDVrcFuryBridge.NeedsImport compares against this so the
        /// count check stays in sync with what the importer actually writes.
        /// </summary>
        internal static int CountFlattenedControls(VRCExpressionsMenu menu)
        {
            return CountFlattenedControls(menu, new HashSet<VRCExpressionsMenu>());
        }

        private static int CountFlattenedControls(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu)) return 0;

            var isRoot = seen.Count == 1;
            var maxControls = isRoot ? MaxMenuControls : MaxMenuControls - 1;
            var count = isRoot ? 0 : 1; // Back wedge on every non-root submenu

            if (menu.controls == null) return count;

            foreach (var c in menu.controls)
            {
                if (c == null) continue;
                if (ToControlType(c.type) < 0) continue;
                if (count >= maxControls) break;
                count++;
                if (c.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    count += CountFlattenedControls(c.subMenu, seen);
            }

            return count;
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

        private static void SetArray<T>(SerializedObject so, string field, List<T> values)
        {
            var property = so.FindProperty(field);
            if (property == null) return;
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).boxedValue = values[i];
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
