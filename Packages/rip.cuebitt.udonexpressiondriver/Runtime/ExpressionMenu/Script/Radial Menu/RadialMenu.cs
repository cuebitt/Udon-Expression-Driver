using TMPro;
using UdonSharp;
using UnityEngine;

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

        [Header("Content")]
        public string[] labels;
        public Texture2D[] icons;

        [Header("Internal")]
        [SerializeField] private GameObject[] segments = new GameObject[8];
        [SerializeField] private Material gradientMaterial;
        [SerializeField] private Material[] iconMaterials;

        private void Start()
        {
            _SetupSegments();
        }

        private void OnDestroy()
        {
            // Reset all the icon material textures
            foreach (var mat in iconMaterials)
            {
                mat.mainTexture = null;
            }
        }

        public void OnButtonPress(int index)
        {
            Debug.Log($"Button pressed: {index}");
        }


        // ReSharper disable once MemberCanBePrivate.Global
        /// <summary>
        /// Configures all wedge segments in the radial menu:
        /// - Activates or deactivates each segment based on <see cref="segmentCount"/>.
        /// - Positions and rotates each segment correctly around the center.
        /// - Generates the mesh with gradient and outlines using <see cref="CreateWedgeMesh"/>.
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
                    mf.sharedMesh = CreateWedgeMesh(angleStep, innerRadius, outerRadius, radialSteps);

                var mr = meshHolder ? meshHolder.GetComponent<MeshRenderer>() : null;
                if (mr != null) mr.sharedMaterials = new[] { gradientMaterial };

                var mc = meshHolder ? meshHolder.GetComponent<MeshCollider>() : null;
                if (mc != null && mf != null) mc.sharedMesh = mf.sharedMesh;

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
                            var iconMaterial = iconMr.sharedMaterial;
                            if (iconMaterial == null)
                            {
                                iconMaterial = iconMaterials[i];
                            }

                            iconMaterial.mainTexture = icons[i];
                            iconMr.sharedMaterial = iconMaterial;
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
                        (Mathf.Cos(midAngle) * midRadius) - 0.2f * midRadius
                    );

                    label.localScale *= 0.75f * midRadius;

                    // Face outward from center
                    label.localRotation = Quaternion.Euler(90f, 0f, 0f); // upright and facing outward
                }
            }
        }


        /// <summary>
        /// Creates a wedge-shaped mesh for a radial menu segment with gradient fill and outline.
        /// Uses shared vertex rings for seamless outlines between adjacent segments.
        /// </summary>
        /// <param name="angleDeg">Angular span of the wedge in degrees.</param>
        /// <param name="innerR">Inner radius of the wedge.</param>
        /// <param name="outerR">Outer radius of the wedge.</param>
        /// <param name="steps">Number of subdivisions along the arc.</param>
        /// <returns>A Mesh object representing the wedge with outline.</returns>
        private static Mesh CreateWedgeMesh(float angleDeg, float innerR, float outerR, int steps)
        {
            var mesh = new Mesh();

            // Determine the number of radial steps for this wedge
            var endIdx = steps;

            // Vertex arrays
            // Each step has two vertices: inner and outer
            var verts = new Vector3[(endIdx + 1) * 2];
            var uvs = new Vector2[verts.Length];

            // Build vertices and UVs
            for (var i = 0; i <= endIdx; i++)
            {
                var t = (float)i / endIdx; // normalized along the arc [0,1]
                var angle = Mathf.Deg2Rad * angleDeg * t;

                // Unit direction in XZ plane
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                // Vertex positions
                verts[i] = dir * innerR; // inner ring
                verts[i + endIdx + 1] = dir * outerR; // outer ring

                // UVs
                // Map inner radius to v=0, outer radius to v=1
                // Map angular position to u = 0..1 across the wedge
                uvs[i] = new Vector2(t, 0f);
                uvs[i + endIdx + 1] = new Vector2(t, 1f);
            }

            // Triangles
            var tris = new int[endIdx * 6];
            for (int i = 0, t = 0; i < endIdx; i++)
            {
                var iInner1 = i + 1;
                var iOuter0 = i + endIdx + 1;
                var iOuter1 = i + endIdx + 2;

                // Triangle 1
                tris[t++] = i;
                tris[t++] = iOuter0;
                tris[t++] = iInner1;

                // Triangle 2
                tris[t++] = iInner1;
                tris[t++] = iOuter0;
                tris[t++] = iOuter1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}