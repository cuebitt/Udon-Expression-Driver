using System.IO;
using UnityEditor;
using UnityEngine;

namespace UdonExpressionDriver.Bootstrapper
{
    public static class GuidChanger
    {
        // Change the GUID of an asset at path "assetPath"
        public static void ChangeGuid(string assetPath, string newGuid)
        {
            var metaPath = assetPath + ".meta";

            if (!File.Exists(metaPath))
            {
                Debug.LogError("[Udon Expression Driver] Meta file not found: " + metaPath);
                return;
            }

            var lines = File.ReadAllLines(metaPath);
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith("guid: "))
                {
                    lines[i] = "guid: " + newGuid;
                    break;
                }

            File.WriteAllLines(metaPath, lines);

            AssetDatabase.Refresh();
        }
    }
}