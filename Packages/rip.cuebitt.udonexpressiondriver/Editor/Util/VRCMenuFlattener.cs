using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace UdonExpressionDriver.Editor
{
    public static class VRCMenuFlattener
    {
        /// <summary>
        ///     Flattens a VRCExpressionsMenu hierarchy and merges parameters from the menu hierarchy
        ///     and an optional external VRCExpressionParameters object.
        /// </summary>
        public static JObject FlattenMenu(VRCExpressionsMenu rootMenu, VRCExpressionParameters extraParameters = null)
        {
            if (rootMenu == null) return null;

            var menus = new List<JObject>();
            var menuIndexMap = new Dictionary<VRCExpressionsMenu, int>();
            var visitedMenus = new HashSet<VRCExpressionsMenu>();
            var uniqueParameters = new Dictionary<string, JObject>();

            // Root menu is always index 0
            var rootJMenu = CloneMenu(rootMenu, visitedMenus, uniqueParameters, menuIndexMap, menus);
            menus.Insert(0, rootJMenu);
            menuIndexMap[rootMenu] = 0;

            // Merge parameters from the extra VRCExpressionParameters object
            if (extraParameters != null && extraParameters.parameters != null)
                foreach (var param in extraParameters.parameters)
                    if (!uniqueParameters.ContainsKey(param.name))
                        uniqueParameters[param.name] = JObject.FromObject(param);

            // Compose final flattened JSON
            var result = new JObject
            {
                ["parameters"] = new JArray(uniqueParameters.Values),
                ["menus"] = JArray.FromObject(menus)
            };

            return result;

            
        }

        // Helper: clone a menu recursively
        private static JObject CloneMenu(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> visitedMenus, Dictionary<string, JObject> uniqueParameters, Dictionary<VRCExpressionsMenu, int> menuIndexMap, List<JObject> menus)
        {
            if (!visitedMenus.Add(menu)) return null;

            // Collect parameters from this menu
            if (menu.Parameters != null)
                foreach (var param in menu.Parameters.parameters)
                    if (!uniqueParameters.ContainsKey(param.name))
                        uniqueParameters[param.name] = JObject.FromObject(param);

            var jMenu = new JObject
            {
                ["name"] = menu.name
            };

            var jControls = new JArray();
            foreach (var c in menu.controls)
            {
                var jControl = new JObject
                {
                    ["name"] = c.name,
                    ["icon"] = c.icon != null
                        ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c.icon))
                        : null,
                    ["type"] = (int)c.type,
                    ["parameter"] = c.parameter != null ? JObject.FromObject(c.parameter) : null,
                    ["value"] = c.value,
                    ["subParameters"] = c.subParameters != null ? JArray.FromObject(c.subParameters) : null
                };

                // Only include labels if present
                if (c.labels is { Length: > 0 })
                {
                    var jLabels = new JArray();
                    foreach (var label in c.labels)
                    {
                        var jLabel = new JObject
                        {
                            ["name"] = label.name
                        };
                        if (label.icon != null)
                        {
                            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(label.icon));
                            jLabel["icon"] = guid;
                        }

                        jLabels.Add(jLabel);
                    }

                    jControl["labels"] = jLabels;
                }

                // Handle submenu recursively
                if (c.subMenu != null)
                {
                    if (!menuIndexMap.TryGetValue(c.subMenu, out var subIndex))
                    {
                        var clonedSubMenu = CloneMenu(c.subMenu, visitedMenus, uniqueParameters, menuIndexMap, menus);
                        if (clonedSubMenu != null)
                        {
                            subIndex = menus.Count;
                            menuIndexMap[c.subMenu] = subIndex;
                            menus.Add(clonedSubMenu);
                        }
                    }

                    jControl["subMenu"] = subIndex;
                }
                else
                {
                    jControl["subMenu"] = null;
                }

                jControls.Add(jControl);
            }

            jMenu["controls"] = jControls;
            return jMenu;
        }
    }
}