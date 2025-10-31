using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UdonExpressionDriver.Editor.Util;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VRC.PackageManagement.Core;

namespace UdonExpressionDriver.Editor
{
    public class UEDSetup : EditorWindow
    {
        private float _progressBarProgress = 0f;
        private string _progressBarStatus = "idle";

        private void OnGUI()
        {
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
            };
            var containerStyle = new GUIStyle()
            {
                padding = new RectOffset(5, 5, 10, 0),
            };
            var statusLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 12,
            };

            EditorGUILayout.BeginVertical(containerStyle); // top level

            EditorGUILayout.BeginVertical(); // first section

            EditorGUILayout.LabelField("Udon Expression Driver Setup", headerStyle);

            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "Click the button below to set up Udon Expression Driver. You should only need to do this once per project.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Setup Udon Expression Driver"))
            {
                DownloadAvatarsPackage();
            }

            EditorGUILayout.EndVertical(); // first section

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(); // second section

            EditorGUILayout.LabelField(_progressBarStatus, statusLabelStyle);
            var rect = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(rect, _progressBarProgress, $"{_progressBarProgress * 100}%");

            EditorGUILayout.EndVertical(); // second section

            EditorGUILayout.EndVertical(); // top level
        }

        private void OnEnable()
        {
            _progressBarProgress = 0f;
            _progressBarStatus = "idle";
        }

        [MenuItem("Tools/Udon Expression Driver Setup")]
        public static void ShowWindow()
        {
            GetWindow<UEDSetup>("Udon Expression Driver Setup");
        }

        private void DownloadAvatarsPackage()
        {
            var _ = DoSetup();
        }

        private async Task DoSetup()
        {
            var officialRepo = Repos.Official;
            var avatarsPackageUrl = officialRepo.GetPackage("com.vrchat.avatars").Url;

            var tempFolderPath = Path.Combine(Path.GetTempPath(), "UED_Temp");
            Directory.CreateDirectory(tempFolderPath);

            // Download and extract avatars zip file
            if (!await DownloadAndExtract(avatarsPackageUrl, tempFolderPath))
            {
                _progressBarStatus = "Failed to download/extract avatars package.";
                _progressBarProgress = 0f;
                return;
            }
            
            _progressBarStatus = "idle";
            _progressBarProgress = 0f;

            // Find avatars DLL file
            var avatarsDllPath = Path.Combine(tempFolderPath, $"{Path.GetFileNameWithoutExtension(avatarsPackageUrl)}/Runtime/VRCSDK/Plugins/VRCSDK3A.dll");

            if (!File.Exists(avatarsDllPath))
            {
                Debug.LogError($"Could not find VRCSDK3A.dll at {avatarsDllPath}");
                return;
            }

            var whitelist = new List<string>()
            {
                "VRCExpressionsMenu",
                "VRCExpressionParameters"
            };
            
            _progressBarStatus = "Processing assembly...";
            _progressBarProgress = 0.5f;
            AssemblyStripper.StripExcept(avatarsDllPath, whitelist, Path.GetFullPath("Packages/rip.cuebitt.udonexpressiondriver/Editor/Plugins/VRCSDK3A.dll"));
            
            _progressBarProgress = 1f;
            _progressBarStatus = "Done!";
        }

        private async Task<bool> DownloadAndExtract(string url, string destination)
        {
            var filename = Path.GetFileName(url);
            var tempZip = Path.Combine(destination, filename);

            var extractDirName = Path.GetFileNameWithoutExtension(tempZip);
            var extractPath = Path.Combine(destination, extractDirName);

            _progressBarProgress = 0f;
            _progressBarStatus = $"Downloading {filename}...";
            Repaint();

            // Download the zip file
            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerFile(tempZip);
                var operation = req.SendWebRequest();

                while (!operation.isDone)
                {
                    _progressBarProgress = req.downloadProgress;
                    Repaint();
                    await Task.Delay(100);
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    _progressBarStatus = $"Download failed: {req.error}";
                    _progressBarProgress = 0f;
                    Repaint();
                    return false;
                }

                _progressBarStatus = $"Extracting {filename}...";
                _progressBarProgress = 0f;
                Repaint();

                try
                {
                    string finalExtractPath = extractPath;

                    await Task.Run(() =>
                    {
                        if (Directory.Exists(extractPath))
                        {
                            Directory.Delete(extractPath, true);
                        }

                        using (var archive = ZipFile.OpenRead(tempZip))
                        {
                            var totalEntries = archive.Entries.Count;
                            var currentEntry = 0;

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

                                currentEntry += 1;
                                if (currentEntry % 20 == 0) // update progress every 20 files
                                {
                                    _progressBarProgress = 0.5f + 0.5f * ((float)currentEntry / totalEntries);
                                    RepaintOnMainThread();
                                }
                                
                                
                            }
                        }
                    });
                    
                    Debug.Log($"Extracted {tempZip} to {extractPath}");
                    
                }
                catch (Exception e)
                {
                    _progressBarStatus = $"Extraction failed: {e.Message}";
                }
                finally
                {
                    if (File.Exists(tempZip))
                        File.Delete(tempZip);
                    _progressBarProgress = 1f;
                    Repaint();
                }
            }

            return true;
        }
        
        private void RepaintOnMainThread()
        {
            // Ensure progress update happens on main thread
            EditorApplication.delayCall += Repaint;
        }

        private void StripAssembly(string inputPath, string outputPath, IEnumerable<string> whitelist)
        {
            AssemblyStripper.StripExcept(inputPath, whitelist, outputPath);
        }
    }
    
    
}