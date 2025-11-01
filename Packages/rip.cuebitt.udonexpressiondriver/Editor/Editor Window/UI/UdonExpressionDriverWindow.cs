using System;
using System.Collections.Generic;
using System.IO;
using UdonExpressionDriver.Editor;
using UdonExpressionDriver.Editor.Templates;
using UdonSharp;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

[Serializable]
public class UEDWindowData : ScriptableObject
{
    // Extractor
    public VRCExpressionsMenu extractorMenu;
    public VRCExpressionParameters extractorParameters;
    public string extractorOutputPath;

    // Driver generator
    public string driverGeneratorOutputPath;
    public string driverGeneratorInputPath;
    public string driverGeneratorClassName;

    // Menu Generator
    public string menuGeneratorInputPath;
    public string menuGeneratorOutputPath;

    // Forwarder
    public GameObject forwarderRootGameObject;
    public bool forwarderForwardPhysbone;
    public bool forwarderForwardContact;

    // Footer
    public string packageName;
    public string packageVersion;
    public string packageAuthor;
}

public class UdonExpressionDriverWindow : EditorWindow
{
    [SerializeField] private VisualTreeAsset visualTree;

    [SerializeField] private StyleSheet styleSheet;

    private SerializedObject _serializedObject;
    private UEDWindowData _data;


    [MenuItem("Window/UI Toolkit/UdonExpressionDriverWindow")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<UdonExpressionDriverWindow>();
        wnd.titleContent = new GUIContent("UdonExpressionDriverWindow");
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        var root = rootVisualElement;

        // Instantiate UXML
        visualTree.CloneTree(root);

        // Add USS stylesheet
        root.styleSheets.Add(styleSheet);

        // Create and bind data object
        _data = CreateInstance<UEDWindowData>();

        // Get package info for footer
        var (packageName, version, author) = GetPackageInfo();
        _data.packageName = packageName;
        _data.packageVersion = version;
        _data.packageAuthor = author;

        _serializedObject = new SerializedObject(_data);
        root.Bind(_serializedObject);

        rootVisualElement.Bind(_serializedObject);

        // Add event handlers
        root.Q<Button>("extractor-output-browse-btn").clicked += () =>
        {
            var fileName = string.IsNullOrEmpty(_data.extractorOutputPath)
                ? "VRCExpressionsMenu.json"
                : Path.GetFileName(Path.ChangeExtension(AssetDatabase.GetAssetPath(_data.extractorMenu), "json"));

            var path = EditorUtility.SaveFilePanel("Save JSON File", "Assets", fileName, "json");
            if (!string.IsNullOrEmpty(path))
            {
                _data.extractorOutputPath = path;
                _serializedObject.Update();
            }
        };
        root.Q<Button>("extract-section-btn").clicked += () =>
        {
            if (_data.extractorMenu == null)
            {
                Debug.LogError("[Udon Expression Driver] Specify a VRCExpressionsMenu asset.");
                return;
            }
            else if (_data.extractorParameters == null)
            {
                Debug.LogError("[Udon Expression Driver] Specify a VRCExpressionParameters asset.");
                return;
            }
            else if (string.IsNullOrEmpty(_data.extractorOutputPath))
            {
                Debug.LogError("[Udon Expression Driver] Specify an output path.");
                return;
            }

            ExtractParameters(_data.extractorMenu, _data.extractorParameters, _data.extractorOutputPath);
        };

        root.Q<Button>("driver-generator-input-browse-btn").clicked += () =>
        {
            var path = EditorUtility.OpenFilePanel("Select Expressions JSON File", "Assets", "json");
            if (!string.IsNullOrEmpty(path))
            {
                _data.driverGeneratorInputPath = path;
                _serializedObject.Update();
            }
        };
        root.Q<Button>("driver-generator-output-path").clicked += () =>
        {
            var path = EditorUtility.SaveFolderPanel("Select output directory", "Assets", "output");
            if (!string.IsNullOrEmpty(path))
            {
                _data.driverGeneratorOutputPath = path;
                _serializedObject.Update();
            }
        };
        root.Q<Button>("generate-driver-section-btn").clicked += () =>
        {
            if (string.IsNullOrEmpty(_data.driverGeneratorInputPath) || !File.Exists(_data.driverGeneratorInputPath))
            {
                Debug.LogError("[Udon Expression Driver] Could not locate JSON input file.");
            }
            else if (string.IsNullOrEmpty(_data.driverGeneratorOutputPath))
            {
                Debug.LogError("[Udon Expression Driver] Please specify a class name.");
            }
            else if (string.IsNullOrEmpty(_data.driverGeneratorOutputPath))
            {
                Debug.LogError("[Udon Expression Driver] Please specify a output path.");
            }

            GenerateDriverBehaviour(_data.driverGeneratorInputPath, _data.driverGeneratorOutputPath,
                _data.driverGeneratorOutputPath);
        };

        root.Q<Button>("menu-generator-input-browse-btn").clicked += () =>
        {
            var path = EditorUtility.OpenFilePanel("Select Menu JSON File", "Assets", "json");
            if (!string.IsNullOrEmpty(path))
            {
                _data.menuGeneratorInputPath = path;
                _serializedObject.Update();
            }
        };
        root.Q<Button>("menu-generator-out-browse-btn").clicked += () =>
        {
            var path = EditorUtility.SaveFilePanel("Save JSON File", "Assets", "UEDMenu", "prefab");
            if (!string.IsNullOrEmpty(path))
            {
                _data.menuGeneratorOutputPath = path;
                _serializedObject.Update();
            }
        };
        root.Q<Button>("generate-menu-section-btn").clicked += () =>
        {
            if (string.IsNullOrEmpty(_data.menuGeneratorInputPath) || !File.Exists(_data.menuGeneratorInputPath))
            {
                Debug.LogError("[Udon Expression Driver] Could not locate JSON input file.");
                return;
            }
            else if (string.IsNullOrEmpty(_data.menuGeneratorOutputPath))
            {
                Debug.LogError("[Udon Expression Driver] Specify an output path.");
                return;
            }

            // todo
            EditorUtility.DisplayDialog("[UED] Error", "This feature is unimplemented.", "OK");
        };

        root.Q<Button>("forwarders-section-btn").clicked += () =>
        {
            if (_data.forwarderRootGameObject == null)
            {
                Debug.LogError("[Udon Expression Driver] Specify a root GameObject.");
                return;
            }

            // todo
            EditorUtility.DisplayDialog("[UED] Error", "This feature is unimplemented.", "OK");
        };
    }

    private (string, string, string) GetPackageInfo()
    {
        var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        var packageInfo = PackageInfo.FindForAssetPath(path);
        return (packageInfo.displayName, $"v{packageInfo.version}", packageInfo.author.name);
    }

    private static void ExtractParameters(VRCExpressionsMenu menu, VRCExpressionParameters parameters,
        string outputPath)
    {
        Debug.Log("[Udon Expression Driver] Creating JSON...");
        var export = new Dictionary<string, object>
        {
            { "parameters", parameters.parameters },
            { "controls", menu.controls },
        };
        var json = UEDSerializer.Serialize(export);

        Debug.Log("[Udon Expression Driver] Writing JSON...");
        File.WriteAllText(outputPath, json);
    }

    private static void GenerateDriverBehaviour(string inputPath, string outputPath, string className)
    {
        Debug.Log("[Udon Expression Driver] Generating UdonSharpBehaviour from template...");

        var json = UEDSerializer.Deserialize<UdonExpressionDriverJson>(
            File.ReadAllText(inputPath));
        var validClassName = UdonExpressionDriverUtils.ToValidClassName(className);


        var template = new UEDDriverTemplate
        {
            ClassName = className,
            Parameters = json.Parameters,
            Controls = json.Controls
        };

        var result = template.TransformText();

        Debug.Log("[Udon Expression Driver] Writing generated behaviour class file...");
        File.WriteAllText($"{outputPath}/{validClassName}.cs", result);
        AssetDatabase.Refresh();

        Debug.Log("[Udon Expression Driver] Creating UdonSharpProgramAsset...");
        var udonProgram = CreateInstance<UdonSharpProgramAsset>();

        // Load generated script
        var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
            UdonExpressionDriverUtils.ToRelativePath($"{outputPath}/{validClassName}.cs"));
        udonProgram.sourceCsScript = monoScript;

        Debug.Log("[Udon Expression Driver] Writing UdonSharpProgramAsset...");
        AssetDatabase.CreateAsset(udonProgram,
            UdonExpressionDriverUtils.ToRelativePath($"{outputPath}/{validClassName}.asset"));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.objects = new UnityEngine.Object[] { udonProgram, monoScript };
        EditorUtility.FocusProjectWindow();

        Debug.Log("[Udon Expression Driver] Finished writing behaviour class file.");
    }

    public class UdonExpressionDriverJson
    {
        public IList<VRCExpressionsMenu.Control> Controls = new List<VRCExpressionsMenu.Control>();
        public IList<VRCExpressionParameters.Parameter> Parameters = new List<VRCExpressionParameters.Parameter>();
    }
}