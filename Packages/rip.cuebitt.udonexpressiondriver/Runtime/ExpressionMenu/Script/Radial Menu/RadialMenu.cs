using TMPro;
using UdonSharp;
using UnityEngine;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
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
#endif

        public void OnButtonPress(int index)
        {
        }

        // ReSharper disable once MemberCanBePrivate.Global
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
                    mf.sharedMesh = colliderMesh;
                }
                    

                var mr = meshHolder.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterials = new[] { gradientMaterial };

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

                var label = seg.transform.Find("Label");
                if (!label) continue;

                var labelActive = false;

                var text = label.Find("Text");
                if (text && hasLabels && i < labels.Length && !string.IsNullOrEmpty(labels[i]))
                {
                    var tmpText = text.gameObject.GetComponent<TMP_Text>();
                    if (tmpText != null)
                    {
                        tmpText.text = labels[i];
                        text.gameObject.SetActive(true);
                        labelActive = true;
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
                        labelActive = true;
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
        /// <param name="outlineThickness">Width of the outline along the edges in world units.</param>
        /// <param name="_outlineHeightOffset">Height of the outline mesh (prevents z-fighting)</param>
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

        /// <summary>
        ///     Merges two meshes into a single mesh. Vertex and triangle indices of the second mesh
        ///     are offset to follow the first mesh. UVs and normals are preserved.
        /// </summary>
        /// <param name="a">The first mesh.</param>
        /// <param name="b">The second mesh to merge on top of the first.</param>
        /// <returns>A new Mesh combining both input meshes.</returns>
        private static Mesh MergeMeshes(Mesh a, Mesh b)
        {
            var combine = new CombineInstance[2];
            
            combine[0].mesh = a;
            combine[1].mesh = b;

            var m = new Mesh();
            m.CombineMeshes(combine);
            
            m.RecalculateNormals();
            m.RecalculateBounds();

            return m;
        }
        
    }
}