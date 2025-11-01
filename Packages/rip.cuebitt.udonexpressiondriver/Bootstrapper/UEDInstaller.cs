using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UdonExpressionDriver.Bootstrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VRC.PackageManagement.Core;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UdonExpressionDriver.Editor.Util
{
    [InitializeOnLoad]
    public static class UEDInstaller
    {
        private const string AssemblyGuid = "67cc4cb7839cd3741b63733d5adf0442";

        static UEDInstaller()
        {
            Installed = Install();
        }

        public static Task Installed { get; private set; }

        private static async Task<bool> Install()
        {
            var packageName = GetPackageNameForType(typeof(UEDInstaller));
            // Check for the UED-relevant files from the VRChat Avatars SDK
            if (CheckForExisting(packageName))
            {
                Debug.Log("[UdonExpressionDriver] Udon Expression Driver is installed.");
                return true;
            }

            Debug.Log("[UdonExpressionDriver] Installing Udon Expression Driver...");

            // Download and extract the VRChat Avatars SDK package
            var avatarsPackageUrl = Repos.Official.GetPackage("com.vrchat.avatars").Url;
            var tempFolderPath = Path.Combine(Path.GetTempPath(), "UED_Temp"); // Store in a system temp folder
            Directory.CreateDirectory(tempFolderPath);

            // Start downloading and extracting the avatars sdk zip file
            Debug.Log("[UdonExpressionDriver] Downloading VRC Avatars SDK package...");
            var success = await DownloadAndExtract(avatarsPackageUrl, tempFolderPath);
            if (!success)
            {
                Debug.LogError("[UdonExpressionDriver] Failed to download or extract avatars package.");
                return false;
            }

            // Find downloaded files
            var downloadedPackageDirectory =
                Path.Combine(tempFolderPath, Path.GetFileNameWithoutExtension(avatarsPackageUrl));
            var avatarsDllPath =
                Path.Combine(downloadedPackageDirectory, "Runtime/VRCSDK/Plugins/VRCSDK3A.dll");

            if (!File.Exists(avatarsDllPath))
            {
                Debug.LogError($"[Udon Expression Driver] Could not find VRCSDK3A.dll at {avatarsDllPath}");
                return false;
            }

            // Strip downloaded assembly to only required types
            Debug.Log("[UdonExpressionDriver] Processing downloaded assembly...");
            var outputAssemblyPath =
                Path.GetFullPath($"Packages/{packageName}/Editor/VRCSDK/Plugins/VRCSDK3A.dll");
            StripAssembly(avatarsDllPath, outputAssemblyPath);

            // Import the processed assembly into the project
            Debug.Log("[UdonExpressionDriver] Importing downloaded assets...");
            AssetDatabase.ImportAsset(outputAssemblyPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh();
            GuidChanger.ChangeGuid(outputAssemblyPath, AssemblyGuid);

            Debug.Log("[UdonExpressionDriver] Finished installing Udon Expression Driver!");
            return true;
        }

        private static bool CheckForExisting(string packageName)
        {
            // Check for existing DLL file
            var possibleDllPaths = new[]
            {
                Path.GetFullPath("Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll"),
                Path.GetFullPath($"Packages/{packageName}/Editor/VRCSDK/Plugins/VRCSDK3A.dll")
            };

            return File.Exists(possibleDllPaths[0]) || File.Exists(possibleDllPaths[1]);
        }

        private static async Task<bool> DownloadAndExtract(string url, string destination)
        {
            var filename = Path.GetFileName(url);
            var tempZip = Path.Combine(destination, filename);

            var extractDirName = Path.GetFileNameWithoutExtension(tempZip);
            var extractPath = Path.Combine(destination, extractDirName);

            // Download the zip file
            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerFile(tempZip);
                var resp = req.SendWebRequest();


                while (!resp.isDone) await Task.Delay(100);

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[UdonExpressionDriver] Failed to download avatars package: status: {req.result}");
                    return false;
                }

                Debug.Log("[UdonExpressionDriver] Extracting avatars package...");

                try
                {
                    var finalExtractPath = extractPath;

                    await Task.Run(() =>
                    {
                        if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);

                        using (var archive = ZipFile.OpenRead(tempZip))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                var fullPath = Path.Combine(finalExtractPath, entry.FullName);
                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    Directory.CreateDirectory(fullPath);
                                }
                                else
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                                    entry.ExtractToFile(fullPath, true);
                                }
                            }
                        }
                    });
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UdonExpressionDriver] Extraction failed: {e.Message}");
                    return false;
                }
                finally
                {
                    if (File.Exists(tempZip))
                        File.Delete(tempZip);
                }
            }

            return true;
        }

        private static void StripAssembly(string inputPath, string outputPath)
        {
            var whitelist = new List<string>
            {
                "VRCExpressionsMenu",
                "VRCExpressionParameters"
            };

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            AssemblyStripper.StripExcept(inputPath, whitelist, outputPath);
        }

        private static string GetPackageNameForType(Type type)
        {
            var script = AssetDatabase.FindAssets("t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonoScript>)
                .FirstOrDefault(s => s != null && s.GetClass() == type);

            if (script == null)
            {
                return null;
            }

            var assetPath = AssetDatabase.GetAssetPath(script);

            // If it’s under Packages/, extract package name
            if (assetPath.StartsWith("Packages/"))
            {
                // e.g. "Packages/com.unity.textmeshpro/Scripts/TextMeshPro.cs"
                var parts = assetPath.Split('/');
                if (parts.Length > 1)
                {
                    var packageName = parts[1];
                    return packageName;
                }
            }

            // Otherwise, it's part of your project (Assets/)
            return "Assets";
        }

        
    }
}