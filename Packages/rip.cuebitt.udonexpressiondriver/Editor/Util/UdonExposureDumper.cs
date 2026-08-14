using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Udon.Editor;
using VRC.Udon.Graph;

namespace UdonExpressionDriver.Editor
{
    /// <summary>
    /// Dumps the full Udon node-definition whitelist, the exact set UdonSharp's
    /// "Class Exposure Tree" / binder checks, to a JSON file for fast programmatic
    /// lookup. Run once after UdonSharp/SDK updates: Tools > Udon Expression Driver >
    /// Dump Udon Exposure.
    /// </summary>
    public static class UdonExposureDumper
    {
        private const string MenuPath = "Tools/Udon Expression Driver/Dump Udon Exposure";

        [MenuItem(MenuPath)]
        public static void Dump()
        {
            try
            {
                // Uses the same UdonEditorInterface setup as CompilerUdonInterface.CacheInit
                // (UdonBehaviourTypeResolver included), so the set matches what the
                // UdonSharp binder accepts.
                var fullNames = UdonEditorManager.Instance.GetNodeDefinitions()
                    .Select(d => d.fullName)
                    .Distinct()
                    .OrderBy(s => s, System.StringComparer.Ordinal)
                    .ToList();

                // Emit a JSON document: metadata plus the flat list of Udon signatures.
                // Each signature is: <SanitizedTypeFullName>.__<MemberName>__<Arg1>_<Arg2>...__<ReturnType>
                // e.g. "UnityEngineAnimator.__StringToHash__SystemString__SystemInt32".
                var payload = new
                {
                    format = "udon-class-exposure",
                    generated = System.DateTime.Now.ToString("s"),
                    count = fullNames.Count,
                    definitions = fullNames
                };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload, Newtonsoft.Json.Formatting.Indented);

                var path = EditorUtility.SaveFilePanel(
                    "Save Udon Exposure Dump", "", "udon-class-exposure.json", "json");
                if (string.IsNullOrEmpty(path)) return;

                File.WriteAllText(path, json);
                Debug.Log($"[UED] Wrote {fullNames.Count} Udon node definitions to {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UED] Failed to dump Udon exposure: {e}");
            }
        }
    }
}
