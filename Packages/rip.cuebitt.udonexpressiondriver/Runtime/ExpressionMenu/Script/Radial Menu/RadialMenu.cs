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
        [Header("Circle Segment Generator Settings")]
        [Range(1, 8)] [SerializeField] private int segmentCount = 8;
        [SerializeField] private float innerRadius = 0.1f;
        [SerializeField] private float outerRadius = 0.3f;
        [SerializeField] private int radialSteps = 48;
        [SerializeField] private float outlineThickness = 0.01f;

        [Header("Content")]
        public string[] labels;
        public Texture2D[] icons;

        [Header("Internal")]
        [SerializeField] private GameObject[] segments = new GameObject[8];
        [SerializeField] private Material gradientMaterial;
        
        private readonly int _mainTexShaderProperty = Shader.PropertyToID("_MainTex");

        private void Start()
        {
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
            Debug.Log($"Button pressed: {index}");
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
            var angleStep = 360f / segmentCount;
            var startAngle = angleStep / 2f;

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (!seg) continue;

                var active = i < segmentCount;
                seg.SetActive(active);
                if (!active) continue;

                // Setup segment wedge mesh
                var meshHolder = seg.transform.Find("Mesh Holder");
                if (meshHolder)
                    meshHolder.localRotation = Quaternion.Euler(0f, angleStep * i - startAngle, 0f); // clockwise

                var mf = meshHolder ? meshHolder.GetComponent<MeshFilter>() : null;
                if (mf != null)
                    mf.sharedMesh = CreateWedgeMesh(angleStep, innerRadius, outerRadius, radialSteps, outlineThickness);

                var mr = meshHolder ? meshHolder.GetComponent<MeshRenderer>() : null;
                if (mr != null) mr.sharedMaterials = new[] { gradientMaterial };

                var mc = meshHolder ? meshHolder.GetComponent<MeshCollider>() : null;
                if (mc != null && mf != null) mc.sharedMesh = mf.sharedMesh;
            }
        }

        private void _SetupLabelsAndIcons()
        {
            var angleStep = 360f / segmentCount;

            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (!seg) continue;

                // Setup label and icon
                var label = seg.transform.Find("Label");
                if (label)
                {
                    var text = label.Find("Text");
                    if (text && labels.Length > i && !string.IsNullOrEmpty(labels[i]))
                    {
                        text.gameObject.GetComponent<TMP_Text>().text = labels[i];
                        text.gameObject.SetActive(true);
                    }
                    else
                    {
                        text.gameObject.SetActive(false);
                    }

                    var icon = label.Find("Icon");
                    if (icon && icons.Length > i && icons[i] != null)
                    {
                        var iconMr = icon.GetComponent<MeshRenderer>();
                        if (iconMr)
                        {
                            var block = new MaterialPropertyBlock();
                            iconMr.GetPropertyBlock(block);
                            block.SetTexture(_mainTexShaderProperty, icons[i]);
                            iconMr.SetPropertyBlock(block);
                            
                            icon.gameObject.SetActive(true);
                        }
                    }
                    else
                    {
                        icon.gameObject.SetActive(false);
                    }

                    var midAngle = Mathf.Deg2Rad * (angleStep * i); // middle of this segment in radians
                    var midRadius = (innerRadius + outerRadius) * 0.5f;

                    // Position along XZ plane
                    label.localPosition = new Vector3(
                        Mathf.Sin(midAngle) * midRadius,
                        0.001f,
                        Mathf.Cos(midAngle) * midRadius - 0.2f * midRadius
                    );

                    label.localScale = Vector3.one * 0.25f * midRadius;

                    // Face outward from center
                    label.localRotation = Quaternion.Euler(90f, 0f, 0f); // upright and facing outward
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
        /// <param name="outlineThickness">Width of the outline along the edges in world units.</param>
        /// <returns>A Mesh containing the wedge and its radial outline.</returns>
        private static Mesh CreateWedgeMesh(float angleDeg, float innerR, float outerR, int steps,
            float outlineThickness)
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

            // --- Generate radial outline ---
            var outlineMesh = CreateOutlineMesh(angleDeg, innerR, outerR, outlineThickness);

            // --- Merge wedge and outline ---
            return MergeMeshes(wedgeMesh, outlineMesh);
        }

        /// <summary>
        /// Generates a mesh containing two radial outline quads along the left and right edges of a wedge.
        /// The quads have constant thickness along the wedge edges, are slightly raised along Y, fully inside the wedge,
        /// and all UVs are set to (0,0). Normals point outward from the center.
        /// </summary>
        /// <param name="angleDeg">Angular span of the wedge in degrees.</param>
        /// <param name="innerR">Inner radius of the wedge.</param>
        /// <param name="outerR">Outer radius of the wedge.</param>
        /// <param name="outlineThickness">Width of the outline in world units (radial thickness along the edge).</param>
        /// <returns>A Mesh containing the left and right outline quads.</returns>
        private static Mesh CreateOutlineMesh(float angleDeg, float innerR, float outerR, float outlineThickness)
        {
            var mesh = new Mesh();

            var verts = new Vector3[8]; // 4 vertices per quad
            var uvs = new Vector2[8];
            var tris = new int[12]; // 2 triangles per quad

            const float outlineY = 0.001f; // slight offset to avoid z-fighting
            var angleRad = Mathf.Deg2Rad * angleDeg;

            // Left edge outline (start of wedge)
            SetOutlineStrip(0, 0f, innerR, outerR, outlineThickness, outlineY, verts, uvs, tris);

            // Right edge outline (end of wedge)
            SetOutlineStrip(4, angleRad, innerR, outerR, outlineThickness, outlineY, verts, uvs, tris);

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }


        /// <summary>
        /// Generates a single radial outline quad along a wedge edge with constant thickness.
        /// </summary>
        /// <param name="index">Start index in the vertex array (0 or 4).</param>
        /// <param name="baseAngle">Angle of the wedge edge (radians).</param>
        /// <param name="innerR">Inner radius of the wedge.</param>
        /// <param name="outerR">Outer radius of the wedge.</param>
        /// <param name="outlineThickness">Width of the outline in world units along the edge.</param>
        /// <param name="outlineY">Small Y offset to raise the outline above the wedge.</param>
        /// <param name="verts">Vertex array to populate.</param>
        /// <param name="uvs">UV array (all zero).</param>
        /// <param name="tris">Triangle array to populate (two triangles forming the quad).</param>
        private static void SetOutlineStrip(int index, float baseAngle, float innerR, float outerR,
            float outlineThickness, float outlineY, Vector3[] verts, Vector2[] uvs, int[] tris)
        {
            // Direction along the wedge edge
            var dir = new Vector3(Mathf.Sin(baseAngle), 0f, Mathf.Cos(baseAngle));

            // Perpendicular direction in XZ plane for width of outline
            var perp = new Vector3(dir.z, 0f, dir.x);

            var offset = perp * (outlineThickness * 0.5f);

            // Four vertices forming the quad (radial strip)
            verts[index + 0] = dir * innerR - offset + Vector3.up * outlineY; // inner left
            verts[index + 1] = dir * innerR + offset + Vector3.up * outlineY; // inner right
            verts[index + 2] = dir * outerR - offset + Vector3.up * outlineY; // outer left
            verts[index + 3] = dir * outerR + offset + Vector3.up * outlineY; // outer right

            // All UVs zero
            for (var i = 0; i < 4; i++) uvs[index + i] = Vector2.zero;

            var tBase = (index / 4) * 6;

            // Swap triangles to ensure outward-facing normals (clockwise from above)
            tris[tBase + 0] = index + 0;
            tris[tBase + 1] = index + 2;
            tris[tBase + 2] = index + 1;

            tris[tBase + 3] = index + 1;
            tris[tBase + 4] = index + 2;
            tris[tBase + 5] = index + 3;
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
            var verts = new Vector3[a.vertexCount + b.vertexCount];
            var uvs = new Vector2[a.vertexCount + b.vertexCount];
            var tris = new int[a.triangles.Length + b.triangles.Length];

            // Copy mesh A
            for (var i = 0; i < a.vertexCount; i++) verts[i] = a.vertices[i];
            for (var i = 0; i < a.vertexCount; i++) uvs[i] = a.uv[i];
            for (var i = 0; i < a.triangles.Length; i++) tris[i] = a.triangles[i];

            // Copy mesh B
            for (var i = 0; i < b.vertexCount; i++) verts[a.vertexCount + i] = b.vertices[i];
            for (var i = 0; i < b.vertexCount; i++) uvs[a.vertexCount + i] = b.uv[i];
            for (var i = 0; i < b.triangles.Length; i++) tris[a.triangles.Length + i] = b.triangles[i] + a.vertexCount;

            // ReSharper disable once UseObjectOrCollectionInitializer
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}