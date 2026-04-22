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
        private const float DefaultInnerRadius = 0.1f;
        private const float DefaultOuterRadius = 0.3f;
        private const int DefaultRadialSteps = 48;
        private const float DefaultLabelHeightOffset = 0.01f;
        private const float DefaultLabelScale = 0.25f;
        private const float DefaultLabelZOffset = 0.2f;
        private const int MaxSegmentArraySize = 8;

        [Header("Circle Segment Generator Settings")]
        [Range(1, 8)] [SerializeField] private int segmentCount = 8;
        [SerializeField] private float innerRadius = DefaultInnerRadius;
        [SerializeField] private float outerRadius = DefaultOuterRadius;
        [SerializeField] private int radialSteps = DefaultRadialSteps;
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

        private readonly int _mainTexShaderProperty = Shader.PropertyToID("_MainTex");

        private void Start()
        {
            if (segments == null || segments.Length == 0) return;
            _SetupSegments();
            _SetupLabelsAndIcons();
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            EditorApplication.delayCall += _SetupSegments;
        }

        private void OnDestroy()
        {
            if (Application.isPlaying) return;

            foreach (var segment in segments)
            {
                if (!segment) continue;
                var mh = segment.transform.Find("Mesh Holder");
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

            var angleStep = 360f / segmentCount;
            var startAngle = angleStep / 2f;

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (!seg) continue;

                var active = i < segmentCount;
                seg.SetActive(active);
                if (!active) continue;

                var meshHolder = seg.transform.Find("Mesh Holder");
                if (meshHolder == null) continue;

                meshHolder.localRotation = Quaternion.Euler(0f, angleStep * i - startAngle, 0f);
                var pos = meshHolder.localPosition;
                pos.y = 0f;
                meshHolder.localPosition = pos;

                var mf = meshHolder.GetComponent<MeshFilter>();
                Mesh colliderMesh = null;
                if (mf != null)
                {
                    colliderMesh = CreateWedgeMesh(angleStep, innerRadius, outerRadius, radialSteps);
                    
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                    colliderMesh.hideFlags = HideFlags.DontSave;
#endif
                    
                    mf.sharedMesh = colliderMesh;
                }
                    

                var mr = meshHolder.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterials = new[] { gradientMaterial };

                var mc = meshHolder.GetComponent<MeshCollider>();
                if (mc != null && mf != null && colliderMesh != null) mc.sharedMesh = colliderMesh;
            }

            if (borderMeshHolder != null && borderThickness > 0f)
            {
                var borders = CreateBorderMesh(segmentCount, innerRadius, outerRadius);
                if (borders != null)
                {
                    var bmf = borderMeshHolder.GetComponent<MeshFilter>();
                    if (bmf != null)
                    {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                        borders.hideFlags = HideFlags.DontSave;
#endif
                        bmf.sharedMesh = borders;
                    }

                    var bmr = borderMeshHolder.GetComponent<MeshRenderer>();
                    if (bmr != null) bmr.sharedMaterials = new[] { gradientMaterial };
                }
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

                var label = seg.transform.Find("Label");
                if (!label) continue;

                var text = label.Find("Text");
                if (text && hasLabels && i < labels.Length && !string.IsNullOrEmpty(labels[i]))
                {
                    var tmpText = text.gameObject.GetComponent<TMP_Text>();
                    if (tmpText != null)
                    {
                        tmpText.text = labels[i];
                        text.gameObject.SetActive(true);
                    }
                }
                else if (text)
                {
                    text.gameObject.SetActive(false);
                }

                var icon = label.Find("Icon");
                if (icon && hasIcons && i < icons.Length && icons[i] != null)
                {
                    var iconMr = icon.GetComponent<MeshRenderer>();
                    if (iconMr != null)
                    {
                        var block = new MaterialPropertyBlock();
                        iconMr.GetPropertyBlock(block);
                        block.SetTexture(_mainTexShaderProperty, icons[i]);
                        iconMr.SetPropertyBlock(block);

                        icon.gameObject.SetActive(true);
                    }
                }
                else if (icon)
                {
                    icon.gameObject.SetActive(false);
                }

                var midAngle = Mathf.Deg2Rad * (angleStep * i);
                var midRadius = (innerRadius + outerRadius) * 0.5f;

                label.localPosition = new Vector3(
                    Mathf.Sin(midAngle) * midRadius,
                    labelOffset,
                    Mathf.Cos(midAngle) * midRadius - DefaultLabelZOffset * midRadius
                );

                label.localScale = Vector3.one * DefaultLabelScale * midRadius;
                label.localRotation = Quaternion.Euler(90f, 0f, 0f);
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
                var boundaryAngle = Mathf.Deg2Rad * (angleStep * i - startAngle);
                var tangent = new Vector3(Mathf.Cos(boundaryAngle), 0f, -Mathf.Sin(boundaryAngle));

                var vi = i * 4;
                verts[vi] = new Vector3(Mathf.Sin(boundaryAngle), 0f, Mathf.Cos(boundaryAngle)) * innerR + tangent * borderThickness;
                verts[vi + 1] = new Vector3(Mathf.Sin(boundaryAngle), 0f, Mathf.Cos(boundaryAngle)) * outerR + tangent * borderThickness;
                verts[vi + 2] = new Vector3(Mathf.Sin(boundaryAngle), 0f, Mathf.Cos(boundaryAngle)) * innerR - tangent * borderThickness;
                verts[vi + 3] = new Vector3(Mathf.Sin(boundaryAngle), 0f, Mathf.Cos(boundaryAngle)) * outerR - tangent * borderThickness;

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