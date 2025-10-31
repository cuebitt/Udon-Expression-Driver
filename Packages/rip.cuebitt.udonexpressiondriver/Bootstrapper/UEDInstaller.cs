using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UdonExpressionDriver.Bootstrapper;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VRC.PackageManagement.Core;

namespace UdonExpressionDriver.Editor.Util
{
    [InitializeOnLoad]
    public static class UEDInstaller
    {
        private const string AssemblyGuid = "67cc4cb7839cd3741b63733d5adf0442";
        
        static UEDInstaller()
        {
            UEDInstalled = EnsureDllExists();
        }

        public static Task UEDInstalled { get; private set; }

        [MenuItem("Tools/Udon Expression Driver/Install Udon Expression Driver")]
        private static async Task EnsureDllExists()
        {
            // Check for the assembly in UED's directory or VRChat's directory
            var assemblyPath =
                Path.GetFullPath("Packages/rip.cuebitt.udonexpressiondriver/Editor/Plugins/VRCSDK3A.dll");
            var defaultAssemblyPath = 
                Path.GetFullPath("Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll");
            if (File.Exists(assemblyPath) || File.Exists(defaultAssemblyPath))
            {
                Debug.Log("[UdonExpressionDriver] Udon Expression Driver is already installed.");
                return;
            }

            Debug.Log("[UdonExpressionDriver] Installing Udon Expression Driver...");

            var officialRepo = Repos.Official;
            var avatarsPackageUrl = officialRepo.GetPackage("com.vrchat.avatars").Url;

            var tempFolderPath = Path.Combine(Path.GetTempPath(), "UED_Temp");
            Directory.CreateDirectory(tempFolderPath);

            // Download and extract avatars zip file
            Debug.Log("[UdonExpressionDriver] Downloading VRC Avatars SDK package...");
            var success = await DownloadAndExtract(avatarsPackageUrl, tempFolderPath);
            if (!success)
            {
                Debug.LogError("[UdonExpressionDriver] Failed to download or extract avatars package.");
                return;
            }

            // Find avatars DLL file
            var avatarsDllPath = Path.Combine(tempFolderPath,
                $"{Path.GetFileNameWithoutExtension(avatarsPackageUrl)}/Runtime/VRCSDK/Plugins/VRCSDK3A.dll");

            if (!File.Exists(avatarsDllPath))
            {
                Debug.LogError($"[Udon Expression Driver] Could not find VRCSDK3A.dll at {avatarsDllPath}");
                return;
            }

            var whitelist = new List<string>
            {
                "VRCExpressionsMenu",
                "VRCExpressionParameters"
            };

            Debug.Log("[UdonExpressionDriver] Processing downloaded assembly...");
            AssemblyStripper.StripExcept(avatarsDllPath, whitelist, assemblyPath);
            
            Debug.Log($"[UdonExpressionDriver] Importing downloaded assembly...");
            AssetDatabase.ImportAsset(assemblyPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh();
            GuidChanger.ChangeGuid(assemblyPath, AssemblyGuid);
            

            Debug.Log("[UdonExpressionDriver] Finished installing Udon Expression Driver!");
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
                
                

                while (!resp.isDone)
                {
                    Debug.Log(resp.progress);
                    await Task.Delay(100);
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[UdonExpressionDriver] Failed to download avatars package: status: {req.result}" );
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
    }
}