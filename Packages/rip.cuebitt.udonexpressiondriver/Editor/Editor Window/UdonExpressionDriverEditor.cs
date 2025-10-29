using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UdonExpressionDriver.Editor.Templates;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using YamlDotNet.RepresentationModel;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UdonExpressionDriver.Editor
{
    public class UdonExpressionDriverJson
    {
        public IList<UEDControl> controls = new List<UEDControl>();
        public IList<UEDParameter> parameters = new List<UEDParameter>();
    }

    public class UdonExpressionDriverEditor : EditorWindow
    {
        // Class generator
        private string _extractedJsonInputPath = "";
        private string _extractedJsonOutputPath = "";
        private bool _forwarderForwardContacts = true;
        private bool _forwarderForwardPhysbones = true;

        // Forwarder
        private GameObject _forwarderGameObject;
        private string _generatedClassName = "";
        private string _generatedClassOutputPath = "";

        // Menu generator
        private string _menuGeneratorInputJsonPath = "";
        private string _menuGeneratorOutputPath = "";

        // Extractor
        private string _menuInputPath = "";
        private string _packageAuthor = "";
        private string _packageDisplayName = "";

        // Footer info
        private string _packageVersion = "";
        private string _parametersInputPath = "";

        // Scroll view
        private Vector2 _scrollPosition = Vector2.zero;

        // Instructions foldout
        private bool _showInstructionsSection;


        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawHeaderSection();
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            _showInstructionsSection = EditorGUILayout.Foldout(_showInstructionsSection, "Instructions", true);
            if (_showInstructionsSection)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                DrawInstructionsSection();
                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(10);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawExtractorSection();
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawGeneratorSection();
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawMenuGeneratorSection();
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawForwarderSection();
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            EditorGUILayout.BeginVertical(GUI.skin.box);
            DrawFooterInfoSection();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        private void OnFocus()
        {
            var packageInfo = PackageInfo.FindForAssetPath("Packages/rip.cuebitt.udonexpressiondriver");

            if (packageInfo == null) return;

            _packageVersion = packageInfo.version;
            _packageAuthor = packageInfo.author.name;
            _packageDisplayName = packageInfo.displayName;
        }

        [MenuItem("Tools/Udon Expression Driver")]
        public static void ShowWindow()
        {
            GetWindow<UdonExpressionDriverEditor>("Udon Expression Driver");
        }

        private static void DrawHeaderSection()
        {
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.LowerCenter
            };
            var subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 12
            };
            var sectionStyle = new GUIStyle
            {
                padding = new RectOffset(15, 15, 15, 15),
                margin = new RectOffset(5, 5, 5, 5)
            };

            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Udon Expression Driver", headerStyle);
            EditorGUILayout.LabelField("...an Avatars-to-Worlds porting tool for VRChat!", subtitleStyle);
            EditorGUILayout.EndVertical();
        }

        private static void DrawInstructionsSection()
        {
            var sectionStyle = new GUIStyle
            {
                padding = new RectOffset(10, 10, 15, 15)
            };

            var bodyStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = true
            };

            var instructionsText = new List<string>
            {
                "Use the <b>\"Extract Menu + Parameters\"</b> section to convert VRCExpressionsMenu and VRCExpressionParameters assets to JSON.",
                "Use the <b>\"Generate Animator Driver Behaviour\"</b> section to generate a driver script for your prop from the JSON file created in <i>step 1.</i>",
                "Use the <b>\"Generate Menu Prefab\"</b> to section to generate a menu UI prefab that controls the driver behaviour from <i>step 2.</i>",
                "Use the <b>\"Forward Physbone + Contact Events\"</b> section to add event forwarders to every child GameObject that has a Physbone or Contact component.",
                "Add the behaviour script from <i>step 2</i> to the <b>prop's root</b> GameObject",
                "Add the menu prefab from <i>step 3</i> to your scene, then drag the <b>prop's root</b> GameObject to the <i>{placeholder}</i> field of the <b>menu prefab root's</b> behaviour."
            };

            EditorGUILayout.BeginVertical(sectionStyle);
            for (var i = 0; i < instructionsText.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i + 1}.", EditorStyles.boldLabel, GUILayout.Width(20));
                EditorGUILayout.LabelField(instructionsText[i], bodyStyle, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGeneratorSection()
        {
            GUILayout.Label("Generate Animator Driver Behaviour", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox("Generates an UdonSharpBehaviour that manages an expressions menu.",
                MessageType.Info);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            _extractedJsonInputPath = EditorGUILayout.TextField("JSON Path", _extractedJsonInputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFilePanel("Select Expressions JSON File", "Assets", "json");
                if (!string.IsNullOrEmpty(path))
                    _extractedJsonInputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _generatedClassOutputPath =
                EditorGUILayout.TextField("Output Path", _generatedClassOutputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.SaveFolderPanel("Select output directory", "Assets", "output");
                if (!string.IsNullOrEmpty(path))
                    _generatedClassOutputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _generatedClassName =
                EditorGUILayout.TextField("Behaviour Class Name", _generatedClassName);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button("Generate"))
            {
                if (string.IsNullOrEmpty(_extractedJsonInputPath) || !File.Exists(_extractedJsonInputPath))
                {
                    Debug.LogError("[Udon Expression Driver] Could not locate JSON input file.");
                }
                else if (string.IsNullOrEmpty(_generatedClassName))
                {
                    Debug.LogError("[Udon Expression Driver] Please specify a class name.");
                }
                else if (string.IsNullOrEmpty(_generatedClassOutputPath))
                {
                    Debug.LogError("[Udon Expression Driver] Please specify a output path.");
                }
                else
                {
                    Debug.Log("[Udon Expression Driver] Generating UdonSharpBehaviour from template...");

                    var json = JsonConvert.DeserializeObject<UdonExpressionDriverJson>(
                        File.ReadAllText(_extractedJsonInputPath));
                    var className = UdonExpressionDriverUtils.ToValidClassName(_generatedClassName);


                    Debug.Log($"{_generatedClassName} -> {className}");

                    var template = new UEDDriverTemplate
                    {
                        ClassName = className,
                        Parameters = json.parameters,
                        Controls = json.controls
                    };

                    var result = template.TransformText();

                    Debug.Log("[Udon Expression Driver] Writing generated behaviour class file...");
                    File.WriteAllText($"{_generatedClassOutputPath}/{className}.cs", result);
                    AssetDatabase.Refresh();

                    Debug.Log("[Udon Expression Driver] Creating UdonSharpProgramAsset...");
                    var udonProgram = CreateInstance<UdonSharpProgramAsset>();

                    // Load generated script
                    var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
                        UdonExpressionDriverUtils.ToRelativePath($"{_generatedClassOutputPath}/{className}.cs"));
                    udonProgram.sourceCsScript = monoScript;

                    Debug.Log("[Udon Expression Driver] Writing UdonSharpProgramAsset...");
                    AssetDatabase.CreateAsset(udonProgram,
                        UdonExpressionDriverUtils.ToRelativePath($"{_generatedClassOutputPath}/{className}.asset"));

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    Selection.objects = new Object[] { udonProgram, monoScript };
                    EditorUtility.FocusProjectWindow();

                    Debug.Log("[Udon Expression Driver] Finished writing behaviour class file.");
                }
            }
        }

        private void DrawExtractorSection()
        {
            GUILayout.Label("Extract Menu + Parameters", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox("Converts an Expressions Menu + Expression Parameter asset into a JSON file.",
                MessageType.Info);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            _menuInputPath = EditorGUILayout.TextField("Menu Path", _menuInputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFilePanel("Select Expressions Menu File", "Assets", "asset");
                if (!string.IsNullOrEmpty(path)) _menuInputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _parametersInputPath = EditorGUILayout.TextField("Parameters Path", _parametersInputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFilePanel("Select Expression Parameters File", "Assets", "asset");
                if (!string.IsNullOrEmpty(path))
                    _parametersInputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _extractedJsonOutputPath = EditorGUILayout.TextField("Output JSON Path", _extractedJsonOutputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var fileName = string.IsNullOrEmpty(_menuInputPath)
                    ? "UdonExpressionsMenu.json"
                    : Path.GetFileName(Path.ChangeExtension(_menuInputPath, "json"));
                Debug.Log(fileName);

                var path = EditorUtility.SaveFilePanel("Save JSON File", "Assets", fileName, "json");
                if (!string.IsNullOrEmpty(path)) _extractedJsonOutputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button("Extract"))
            {
                Debug.Log("[Udon Expression Driver] Extracting controls...");
                var controls = ExtractControls(_menuInputPath);

                Debug.Log("[Udon Expression Driver] Extracting parameters...");
                var parameters = ExtractParameters(_parametersInputPath);

                Debug.Log("[Udon Expression Driver] Creating JSON...");
                var json = SerializeMenu(controls, parameters);
                File.WriteAllText(_extractedJsonOutputPath, json);

                _extractedJsonInputPath = _extractedJsonOutputPath;
            }
        }

        private void DrawForwarderSection()
        {
            GUILayout.Label("Forward Physbone + Contact Events", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox("Adds scripts that forward all Physbone and Contact events to the root behaviour.",
                MessageType.Info);
            GUILayout.Space(10);

            if (_forwarderGameObject != null && PrefabUtility.IsPartOfPrefabAsset(_forwarderGameObject))
                EditorGUILayout.HelpBox("Specify a GameObject from a scene hierarchy, not a prefab asset.",
                    MessageType.Error);

            _forwarderGameObject = (GameObject)EditorGUILayout.ObjectField(
                "Root GameObject",
                _forwarderGameObject,
                typeof(GameObject),
                true
            );

            _forwarderForwardPhysbones = EditorGUILayout.Toggle("Physbones Events", _forwarderForwardPhysbones);
            _forwarderForwardContacts = EditorGUILayout.Toggle("Contacts Events", _forwarderForwardContacts);

            GUILayout.Space(10);
            if (GUILayout.Button("Add Forwarders"))
            {
                if (_forwarderGameObject == null)
                    Debug.LogError("[Udon Expression Driver] Error: Specify a GameObject to add forwarders to.");
                else if (PrefabUtility.IsPartOfPrefabAsset(_forwarderGameObject))
                    Debug.LogError("[Udon Expression Driver] Error: Specify a scene object, not a prefab asset.");
                else
                    Debug.Log("[Udon Expression Driver] Adding forwarders...");
            }
        }

        private void DrawMenuGeneratorSection()
        {
            GUILayout.Label("Generate Menu Prefab", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox("Generates an Udon-powered Expressions Menu.",
                MessageType.Info);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            _menuGeneratorInputJsonPath = EditorGUILayout.TextField("Menu JSON Path", _menuGeneratorInputJsonPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFilePanel("Select Menu JSON File", "Assets", "json");
                if (!string.IsNullOrEmpty(path))
                    _menuGeneratorInputJsonPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _menuGeneratorOutputPath = EditorGUILayout.TextField("Output Prefab Path", _menuGeneratorOutputPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.SaveFilePanel("Save JSON File", "Assets", "UEDMenu", "prefab");
                if (!string.IsNullOrEmpty(path)) _menuGeneratorOutputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button("Generate Menu"))
            {
            }
        }

        private void DrawFooterInfoSection()
        {
            var paddingStyle = new GUIStyle
            {
                padding = new RectOffset(10, 10, 15, 15)
            };
            var titleLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                fontSize = 16
            };
            var subtitleLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 14
            };

            EditorGUILayout.BeginVertical(paddingStyle);

            EditorGUILayout.LabelField($"<b>{_packageDisplayName}</b> <i>v{_packageVersion}</i>", titleLabelStyle);
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"by {_packageAuthor} ❤", subtitleLabelStyle);

            EditorGUILayout.EndVertical();
        }

        private static string SerializeMenu(List<Dictionary<string, object>> controls,
            List<Dictionary<string, object>> parameters)
        {
            var output = new Dictionary<string, object>
            {
                { "controls", controls },
                { "parameters", parameters }
            };

            return JsonConvert.SerializeObject(output, Formatting.Indented);
        }

        private static List<Dictionary<string, object>> ExtractParameters(string yamlFilePath)
        {
            if (!File.Exists(yamlFilePath))
                throw new FileNotFoundException("YAML file not found", yamlFilePath);

            using var reader = new StreamReader(yamlFilePath);
            var yaml = new YamlStream();
            yaml.Load(reader);

            var root = (YamlMappingNode)yaml.Documents[0].RootNode;

            // MonoBehaviour section
            if (!root.Children.TryGetValue("MonoBehaviour", out var monoNode) || !(monoNode is YamlMappingNode mono))
                throw new Exception("No MonoBehaviour section found in YAML.");

            // Parameters section
            if (!mono.Children.TryGetValue("parameters", out var paramNode) ||
                !(paramNode is YamlSequenceNode paramList))
                throw new Exception("No Parameters section found in MonoBehaviour.");

            // Parsed parameters
            var output = new List<Dictionary<string, object>>();

            foreach (var yamlNode in paramList)
            {
                var pNode = (YamlMappingNode)yamlNode;
                var dict = new Dictionary<string, object>();

                // Parameter name string
                dict["name"] = pNode.GetString("name");

                // saved and networkSynced as bool
                dict["saved"] = pNode.GetInt("saved") != 0;
                dict["networkSynced"] = pNode.GetInt("networkSynced") != 0;

                // Include type if exists
                if (pNode.Children.ContainsKey("valueType"))
                {
                    var type = pNode.GetInt("valueType");
                    dict["type"] = type switch
                    {
                        0 => "int",
                        1 => "float",
                        2 => "bool",
                        _ => "float"
                    };
                }

                if (pNode.Children.ContainsKey("defaultValue") && pNode.Children.ContainsKey("valueType"))
                {
                    var value = float.Parse(pNode.GetString("defaultValue"));
                    dict["defaultValue"] = (int)dict["type"] switch
                    {
                        0 => (int)value,
                        1 => value,
                        2 => Convert.ToBoolean(value),
                        _ => 0f
                    };
                }

                output.Add(dict);
            }

            return output;
        }

        private static List<Dictionary<string, object>> ExtractControls(string yamlFilePath)
        {
            if (!File.Exists(yamlFilePath))
            {
                Debug.LogError("[Udon Expression Driver] Input file not found!");
                return new List<Dictionary<string, object>>();
            }

            using var reader = new StreamReader(yamlFilePath);
            var yaml = new YamlStream();
            yaml.Load(reader);

            var root = (YamlMappingNode)yaml.Documents[0].RootNode;

// The MonoBehaviour section
            if (!root.Children.TryGetValue("MonoBehaviour", out var monoNode) || !(monoNode is YamlMappingNode mono))
            {
                Debug.LogError("[Udon Expression Driver] No 'MonoBehaviour' section found.");
                return new List<Dictionary<string, object>>();
            }

// Access controls
            if (!mono.Children.TryGetValue("controls", out var controlsNode) ||
                !(controlsNode is YamlSequenceNode controlsList))
            {
                Debug.LogError("[Udon Expression Driver] No 'controls' found in MonoBehaviour.");
                return new List<Dictionary<string, object>>();
            }

            var outputList = new List<Dictionary<string, object>>();
            foreach (var yamlNode in controlsList)
            {
                var controlNode = (YamlMappingNode)yamlNode;
                outputList.Add(ProcessControl(controlNode));
            }

            return outputList;
        }

        private static Dictionary<string, object> ProcessControl(YamlMappingNode controlNode)
        {
            var result = new Dictionary<string, object>();

            // name
            result["name"] = controlNode.GetString("name");

            // type
            result["type"] = controlNode.GetInt("type");

            // parameter
            if (controlNode.Children.TryGetValue("parameter", out var paramNode) && paramNode is YamlMappingNode p)
                result["parameter"] = p.GetString("name");

            // icon
            if (controlNode.Children.TryGetValue("icon", out var iconNode) && iconNode is YamlMappingNode i)
                result["icon"] = i.GetString("icon");
            else
                result["icon"] = null;


            // subMenu
            if (controlNode.Children.TryGetValue("subMenu", out var submenuNode) && submenuNode is YamlMappingNode s)
            {
                var guid = s.GetString("guid");

                if (!string.IsNullOrEmpty(guid))
                {
                    var submenuPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (File.Exists(submenuPath))
                        result["subMenu"] = ExtractControls(submenuPath);
                    else
                        result["subMenu"] = new List<Dictionary<string, object>>();
                }
                else
                {
                    result["subMenu"] = new List<Dictionary<string, object>>();
                }
            }

            // subParameters
            if (controlNode.Children.TryGetValue("subParameters", out var spNode) && spNode is YamlSequenceNode spList)
            {
                var names = new List<string>();
                foreach (var yamlNode in spList)
                {
                    var sp = (YamlMappingNode)yamlNode;
                    names.Add(sp.GetString("name"));
                }

                result["subParameters"] = names;
            }

            // labels
            if (controlNode.Children.TryGetValue("labels", out var labelNode) && labelNode is YamlSequenceNode labels)
            {
                var labelList = new List<Dictionary<string, string>>();
                foreach (var yamlNode in labels)
                {
                    var l = (YamlMappingNode)yamlNode;
                    var labelDict = new Dictionary<string, string>();
                    labelDict["name"] = l.GetString("name");
                    if (l.Children.TryGetValue("icon", out var liNode) && liNode is YamlMappingNode li)
                        labelDict["icon"] = li.GetString("guid");
                    else
                        labelDict["icon"] = "";
                    labelList.Add(labelDict);
                }

                result["labels"] = labelList;
            }

            return result;
        }
    }

// Extension methods for safely reading YamlMappingNode
    public static class YamlExtensions
    {
        public static string GetString(this YamlMappingNode node, string key, string defaultValue = "")
        {
            if (node.Children.TryGetValue(key, out var valueNode))
                return valueNode.ToString();
            return defaultValue;
        }

        public static int GetInt(this YamlMappingNode node, string key, int defaultValue = 0)
        {
            if (node.Children.TryGetValue(key, out var valueNode) && int.TryParse(valueNode.ToString(), out var result))
                return result;
            return defaultValue;
        }
    }
}