using TMPro;
using UdonSharp;
using UnityEngine;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
// ReSharper disable MergeIntoPattern
// ReSharper disable MemberCanBePrivate.Global
#endif

namespace UdonExpressionDriver
{
    /// <summary>
    /// Generates a radial menu similar to VRChat's quick menu.
    /// Each segment is a wedge-shaped mesh with gradient fill and an outline.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RadialMenu : UdonSharpBehaviour
    {
        private const float DefaultInnerRadius = 0.3f;
        private const float DefaultOuterRadius = 0.9f;
        private const int DefaultRadialSteps = 48;
        private const float DefaultLabelHeightOffset = 0.01f;
        private const float DefaultLabelScale = 0.25f;
        private const float DefaultLabelZOffset = 0.2f;
        private const int MaxSegmentArraySize = 8;
        private const string MeshHolderName = "Mesh Holder";
        private const string LabelName = "Label";
        private const string TextName = "Text";
        private const string IconName = "Icon";
        private const float IconAboveLabel = 1.5f;
        private const float IconScale = 1.5f;
        private const float IconElevation = -0.01f;

        [Header("Circle Segment Generator Settings")]
        [Range(1, 8)] [SerializeField] private int segmentCount = 8;
        [SerializeField] private float innerRadius = DefaultInnerRadius;
        [SerializeField] private float outerRadius = DefaultOuterRadius;
        [SerializeField] private int radialSteps = DefaultRadialSteps;
        [Tooltip("Number of subdivisions along the arc across the whole circle.")]
        [SerializeField] private float labelOffset = DefaultLabelHeightOffset;
        [SerializeField] private float borderThickness = 0.005f;
        
        [Header("Content")]
        [Tooltip("Text labels for each segment. Leave empty to hide the label.")]
        [SerializeField] private string[] labels;
        [Tooltip("Icon textures for each segment. Leave null to hide the icon.")]
        [SerializeField] private Texture2D[] icons;

        [Header("Internal")]
        [Tooltip("Segment root GameObjects. Should have a 'Mesh Holder' child and 'Label' child.")]
        [SerializeField] private GameObject[] segments = new GameObject[MaxSegmentArraySize];
        [Tooltip("Material with gradient shader for segment fill.")]
        [SerializeField] private Material gradientMaterial;
        [Tooltip("Mesh Holder for the merged border mesh.")]
        [SerializeField] private Transform borderMeshHolder;

        [Header("Controller")]
        [Tooltip("Full controller whose expressions menu this radial displays.")]
        [SerializeField] private UEDFullController fullController;

        [SerializeField, HideInInspector] private bool autoLinked;

        private readonly int _mainTexShaderProperty = Shader.PropertyToID("_MainTex");

        private void Start()
        {
            if (segments == null || segments.Length == 0) return;
            _ApplyWorldScale();
            _SetupSegments();
            _SetupLabelsAndIcons();
        }

        /// <summary>
        /// Cancels the scale of the menu's parent chain so the menu always renders at a fixed world
        /// size (the radius values are world units), no matter how the parent prop/controller is scaled.
        /// </summary>
        private void _ApplyWorldScale()
        {
            var parent = transform.parent;
            if (parent == null) return;

            var parentScale = parent.lossyScale;
            if (parentScale.x <= 0f || parentScale.y <= 0f || parentScale.z <= 0f) return;

            transform.localScale = new Vector3(
                1f / parentScale.x,
                1f / parentScale.y,
                1f / parentScale.z
            );
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            EditorApplication.delayCall += () => { if (this == null) return; _SetupSegments(); };
        }

        private void OnDestroy()
        {
            if (Application.isPlaying) return;

            foreach (var segment in segments)
            {
                if (!segment) continue;
                var mh = segment.transform.Find(MeshHolderName);
                if (!mh) continue;
                var mf = mh.GetComponent<MeshFilter>();

                if (mf != null)
                {
                    var mesh = mf.sharedMesh;
                    if(!mesh) continue;
                    
                    DestroyImmediate(mesh);
                }
            }

            if (borderMeshHolder != null)
            {
                var bmf = borderMeshHolder.GetComponent<MeshFilter>();
                if (bmf != null)
                {
                    var mesh = bmf.sharedMesh;
                    if (mesh) DestroyImmediate(mesh);
                }
            }
        }
#endif

        public void OnButtonPress(int index)
        {
            if (fullController == null) return;
            fullController._OnControlPressed(index);
        }

        /// <summary>
        /// Pushes the current menu level's labels and icons into the wedges and
        /// rebuilds the content. Called by the controller on start and on navigation.
        /// </summary>
        public void SetContent(string[] names, Texture2D[] iconArray)
        {
            if (names == null) names = new string[0];
            if (iconArray == null) iconArray = new Texture2D[0];

            var newCount = Mathf.Max(1, Mathf.Min(names.Length, MaxSegmentArraySize));
            if (segmentCount != newCount) segmentCount = newCount;

            labels = names;
            icons = iconArray;

            _ApplyWorldScale();
            _SetupSegments();
            _SetupLabelsAndIcons();
        }

        public void _SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void _ToggleVisible()
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
        
        /// <summary>
        /// Configures all wedge segments in the radial menu:
        /// - Activates or deactivates each segment based on <see cref="segmentCount" />.
        /// - Positions and rotates each segment correctly around the center.
        /// - Generates the mesh with gradient and outlines using <see cref="CreateWedgeMesh" />.
        /// </summary>
        public void _SetupSegments()
        {
            if (segments == null || gradientMaterial == null) return;

            _SetupSegmentsNoBorders();

            // Borders are optional; without them the wedges are plain gradient fills.
            if (borderMeshHolder == null || borderThickness <= 0f) return;

            var borders = CreateBorderMesh(segmentCount, innerRadius, outerRadius);
            var bmf = borderMeshHolder.GetComponent<MeshFilter>();
            if (borders == null)
            {
                if (bmf != null) bmf.sharedMesh = null;
                return;
            }
            
            if (bmf != null)
            {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                borders.hideFlags = HideFlags.DontSave;
#endif
                bmf.sharedMesh = borders;
            }
        }

        private void _SetupSegmentsNoBorders()
        {
            var angleStep = 360f / segmentCount;
            var startAngle = angleStep / 2f;
            var stepsPerWedge = Mathf.Max(1, radialSteps / segmentCount);

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (!seg) continue;

                var active = i < segmentCount;
                seg.SetActive(active);
                if (!active) continue;

                var meshHolder = seg.transform.Find(MeshHolderName);
                if (meshHolder == null) continue;

                // Rotate the wedge into its slot, centered on the slot's angle.
                meshHolder.localRotation = Quaternion.Euler(0f, angleStep * i - startAngle, 0f);
                var pos = meshHolder.localPosition;
                pos.y = 0f;
                meshHolder.localPosition = pos;

                var mf = meshHolder.GetComponent<MeshFilter>();
                Mesh colliderMesh = null;
                if (mf != null)
                {
                    colliderMesh = CreateWedgeMesh(angleStep, innerRadius, outerRadius, stepsPerWedge);
                    
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                    colliderMesh.hideFlags = HideFlags.DontSave;
#endif
                    
                    mf.sharedMesh = colliderMesh;
                }

                var mc = meshHolder.GetComponent<MeshCollider>();
                if (mc != null && mf != null && colliderMesh != null) mc.sharedMesh = colliderMesh;
            }
        }

        private void _SetupLabelsAndIcons()
        {
            if (segments == null) return;

            var angleStep = 360f / segmentCount;
            var hasLabels = labels != null && labels.Length > 0;
            var hasIcons = icons != null && icons.Length > 0;

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (!seg) continue;

                var label = seg.transform.Find(LabelName);
                if (!label) continue;

                var midAngle = Mathf.Deg2Rad * (angleStep * i);
                var midRadius = (innerRadius + outerRadius) * 0.5f;
                var sinA = Mathf.Sin(midAngle);
                var cosA = Mathf.Cos(midAngle);

                label.localPosition = new Vector3(
                    sinA * midRadius,
                    labelOffset,
                    cosA * midRadius - DefaultLabelZOffset * midRadius
                );

                label.localScale = Vector3.one * DefaultLabelScale * midRadius;
                label.localRotation = Quaternion.Euler(90f, 0f, 0f);

                var hasText = hasLabels && i < labels.Length && !string.IsNullOrEmpty(labels[i]);
                var hasIcon = hasIcons && i < icons.Length && icons[i] != null;

                var text = label.Find(TextName);
                if (text)
                {
                    if (hasText)
                    {
                        var tmpText = text.gameObject.GetComponent<TMP_Text>();
                        if (tmpText != null) tmpText.text = labels[i];

                        text.localPosition = Vector3.zero;
                        text.gameObject.SetActive(true);
                    }
                    else
                    {
                        text.gameObject.SetActive(false);
                    }
                }

                var icon = label.Find(IconName);
                if (icon)
                {
                    var iconMr = icon.GetComponent<MeshRenderer>();
                    if (hasIcon && iconMr != null)
                    {
                        var block = new MaterialPropertyBlock();
                        iconMr.GetPropertyBlock(block);
                        block.SetTexture(_mainTexShaderProperty, icons[i]);
                        iconMr.SetPropertyBlock(block);

                        // Stack the icon above the label in the label's own frame, so it sits above
                        // the text no matter which way the wedge is oriented.
                        icon.localPosition = new Vector3(0f, IconAboveLabel, IconElevation);
                        icon.localScale = Vector3.one * IconScale;

                        icon.gameObject.SetActive(true);
                    }
                    else
                    {
                        icon.gameObject.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Creates a wedge mesh with the main gradient surface and merged radial outline quads on top.
        /// </summary>
        /// <param name="angleDeg">Angular span of the wedge in degrees.</param>
        /// <param name="innerR">Inner radius of the wedge.</param>
        /// <param name="outerR">Outer radius of the wedge.</param>
        /// <param name="steps">Number of subdivisions along the arc.</param>
        /// <returns>A Mesh containing the wedge and its radial outline.</returns>
        private static Mesh CreateWedgeMesh(float angleDeg, float innerR, float outerR, int steps)
        {
            // --- Base wedge ---
            var wedgeMesh = new Mesh();
            var angleRad = Mathf.Deg2Rad * angleDeg;

            var verts = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[steps * 6];

            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                var angle = t * angleRad;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                verts[i] = dir * innerR;
                verts[i + steps + 1] = dir * outerR;

                uvs[i] = new Vector2(t, 0f);
                uvs[i + steps + 1] = new Vector2(t, 1f);
            }

            // Builds two vertex rings (inner + outer arc) and stitches two triangles per step.
            for (int i = 0, t = 0; i < steps; i++)
            {
                var iInner1 = i + 1;
                var iOuter0 = i + steps + 1;
                var iOuter1 = i + steps + 2;

                tris[t++] = i;
                tris[t++] = iOuter0;
                tris[t++] = iInner1;

                tris[t++] = iInner1;
                tris[t++] = iOuter0;
                tris[t++] = iOuter1;
            }

            wedgeMesh.vertices = verts;
            wedgeMesh.uv = uvs;
            wedgeMesh.triangles = tris;
            wedgeMesh.RecalculateNormals();
            wedgeMesh.RecalculateBounds();

            return wedgeMesh;
        }
        
        // One thin quad along each wedge boundary so adjacent fills read as separate buttons.
        private Mesh CreateBorderMesh(int segCount, float innerR, float outerR)
        {
            if (segCount < 2 || borderThickness <= 0f) return null;

            var borderMesh = new Mesh();
            var angleStep = 360f / segCount;
            var startAngle = angleStep / 2f;

            var verts = new Vector3[segCount * 4];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segCount * 6];

            for (var i = 0; i < segCount; i++)
            {
                var boundaryAngleRad = Mathf.Deg2Rad * (angleStep * i - startAngle);
                var dir = new Vector3(Mathf.Sin(boundaryAngleRad), 0f, Mathf.Cos(boundaryAngleRad));
                var tangent = new Vector3(Mathf.Cos(boundaryAngleRad), 0f, -Mathf.Sin(boundaryAngleRad));
                var thicknessOffset = tangent * borderThickness;

                var vi = i * 4;
                verts[vi] = dir * innerR + thicknessOffset;
                verts[vi + 1] = dir * outerR + thicknessOffset;
                verts[vi + 2] = dir * innerR - thicknessOffset;
                verts[vi + 3] = dir * outerR - thicknessOffset;

                uvs[vi] = new Vector2(0f, 0f);
                uvs[vi + 1] = new Vector2(0f, 0f);
                uvs[vi + 2] = new Vector2(0f, 0f);
                uvs[vi + 3] = new Vector2(0f, 0f);

                var ti = i * 6;
                tris[ti] = vi;
                tris[ti + 1] = vi + 2;
                tris[ti + 2] = vi + 1;

                tris[ti + 3] = vi + 2;
                tris[ti + 4] = vi + 3;
                tris[ti + 5] = vi + 1;
            }

            borderMesh.vertices = verts;
            borderMesh.uv = uvs;
            borderMesh.triangles = tris;
            borderMesh.RecalculateNormals();
            borderMesh.RecalculateBounds();

            return borderMesh;
        }
    }
}