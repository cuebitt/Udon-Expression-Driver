using System.Collections.Generic;
using System.IO;
using UdonExpressionDriver.Editor.Templates;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UdonExpressionDriver.Editor
{
    public class UdonExpressionDriverJson
    {
        public IList<VRCExpressionsMenu.Control> Controls = new List<VRCExpressionsMenu.Control>();
        public IList<VRCExpressionParameters.Parameter> Parameters = new List<VRCExpressionParameters.Parameter>();
    }

    public class UdonExpressionDriverEditor : EditorWindow
    {
        // Extractor
        private string _menuInputPath = "";
        private string _parametersInputPath = "";
        private string _extractedJsonOutputPath = "";
        
        // Class generator
        private string _extractedJsonInputPath = "";
        private string _generatedClassName = "";
        private string _generatedClassOutputPath = "";

        // Menu generator
        private string _menuGeneratorInputJsonPath = "";
        private string _menuGeneratorOutputPath = "";

        // Forwarder
        private GameObject _forwarderGameObject;
        private bool _forwarderForwardContacts = true;
        private bool _forwarderForwardPhysbones = true;

        // Footer info
        private string _packageVersion = "";
        private string _packageAuthor = "";
        private string _packageDisplayName = "";

        // Other UI state
        private Vector2 _scrollPosition = Vector2.zero;
        private bool _showInstructionsSection;
        
        // Cached GUIStyles
        private GUIStyle _headerStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _headerSectionStyle;
        private GUIStyle _richTextWrapStyle;
        private GUIStyle _boxPaddingStyle;
        private GUIStyle _footerTitleStyle;
        private GUIStyle _footerSubtitleStyle;

        private void OnEnable()
        {
            _headerStyle ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 20,
                alignment = TextAnchor.LowerCenter
            };
            _subtitleStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 12
            };
            _headerSectionStyle ??= new GUIStyle
            {
                padding = new RectOffset(15, 15, 15, 15),
                margin = new RectOffset(5, 5, 5, 5)
            };
            _richTextWrapStyle ??= new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = true
            };
            _boxPaddingStyle ??= new GUIStyle
            {
                padding = new RectOffset(10, 10, 15, 15)
            };
            _footerTitleStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                fontSize = 16
            };
            _footerSubtitleStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 14
            };
        }

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

        private void DrawHeaderSection()
        {
            EditorGUILayout.BeginVertical(_headerSectionStyle);
            EditorGUILayout.LabelField("Udon Expression Driver", _headerStyle);
            EditorGUILayout.LabelField("...an Avatars-to-Worlds porting tool for VRChat!", _subtitleStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawInstructionsSection()
        {
            var instructionsText = new List<string>
            {
                "Use the <b>\"Extract Menu + Parameters\"</b> section to convert VRCExpressionsMenu and VRCExpressionParameters assets to JSON.",
                "Use the <b>\"Generate Animator Driver Behaviour\"</b> section to generate a driver script for your prop from the JSON file created in <i>step 1.</i>",
                "Use the <b>\"Generate Menu Prefab\"</b> to section to generate a menu UI prefab that controls the driver behaviour from <i>step 2.</i>",
                "Use the <b>\"Forward Physbone + Contact Events\"</b> section to add event forwarders to every child GameObject that has a Physbone or Contact component.",
                "Add the behaviour script from <i>step 2</i> to the <b>prop's root</b> GameObject",
                "Add the menu prefab from <i>step 3</i> to your scene, then drag the <b>prop's root</b> GameObject to the <i>{placeholder}</i> field of the <b>menu prefab root's</b> behaviour."
            };

            EditorGUILayout.BeginVertical(_boxPaddingStyle);
            for (var i = 0; i < instructionsText.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i + 1}.", EditorStyles.boldLabel, GUILayout.Width(20));
                EditorGUILayout.LabelField(instructionsText[i], _richTextWrapStyle, GUILayout.ExpandWidth(true));
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

                    var json = UEDSerializer.Deserialize<UdonExpressionDriverJson>(
                        File.ReadAllText(_extractedJsonInputPath));
                    var className = UdonExpressionDriverUtils.ToValidClassName(_generatedClassName);


                    var template = new UEDDriverTemplate
                    {
                        ClassName = className,
                        Parameters = json.Parameters,
                        Controls = json.Controls
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

                var path = EditorUtility.SaveFilePanel("Save JSON File", "Assets", fileName, "json");
                if (!string.IsNullOrEmpty(path)) _extractedJsonOutputPath = path;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            if (GUILayout.Button("Extract"))
            {
                Debug.Log("[Udon Expression Driver] Extracting controls...");
                //var controls = ExtractControls(_menuInputPath);
                var controlsAsset =
                    AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(
                        UdonExpressionDriverUtils.ToRelativePath(_menuInputPath));

                if (controlsAsset == null)
                {
                    Debug.LogError("[Udon Expression Driver] Could not locate menu asset.");
                    return;
                }

                var controls = controlsAsset.controls;

                Debug.Log("[Udon Expression Driver] Extracting parameters...");
                var parametersAsset =
                    AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(
                        UdonExpressionDriverUtils.ToRelativePath(_parametersInputPath));

                if (parametersAsset == null)
                {
                    Debug.LogError("[Udon Expression Driver] Could not locate parameters asset.");
                    return;
                }

                var parameters = parametersAsset.parameters;

                Debug.Log("[Udon Expression Driver] Creating JSON...");
                var export = new Dictionary<string, object>
                {
                    { "parameters", parameters },
                    { "controls", controls }
                };
                var json = UEDSerializer.Serialize(export);

                Debug.Log("[Udon Expression Driver] Writing JSON...");
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
                // TODO remove
                EditorUtility.DisplayDialog("[UED] Error", "This feature is unimplemented.", "OK");
                return;

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
                // TODO remove
                EditorUtility.DisplayDialog("[UED] Error", "This feature is unimplemented.", "OK");
        }

        private void DrawFooterInfoSection()
        {
            EditorGUILayout.BeginVertical(_boxPaddingStyle);

            EditorGUILayout.LabelField($"<b>{_packageDisplayName}</b> <i>v{_packageVersion}</i>", _footerTitleStyle);
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"by {_packageAuthor} ❤", _footerSubtitleStyle);

            EditorGUILayout.EndVertical();
        }
    }
}