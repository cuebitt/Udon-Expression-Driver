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
    /// "Class Exposure Tree" / binder checks, to a plain-text file for fast offline
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

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("// Udon node definitions (the UdonSharp class-exposure whitelist)");
                sb.AppendLine("// Each line is the Udon signature checked by UdonSharp's binder:");
                sb.AppendLine("//   <SanitizedTypeFullName>.__<MemberName>__<ArgType1>_<ArgType2>...__<ReturnType>");
                sb.AppendLine("// e.g. UnityEngineAnimator.__StringToHash__SystemString__SystemInt32");
                sb.AppendLine($"// Count: {fullNames.Count}");
                sb.AppendLine("// Generated: " + System.DateTime.Now.ToString("s"));
                sb.AppendLine();
                foreach (var name in fullNames)
                {
                    sb.AppendLine(name);
                }

                var path = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "_scrubbed", "reference", "udon-class-exposure.txt"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString());

                Debug.Log($"[UED] Wrote {fullNames.Count} Udon node definitions to {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UED] Failed to dump Udon exposure: {e}");
            }
        }
    }
}
