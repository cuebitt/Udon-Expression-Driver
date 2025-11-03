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
        [Range(1, 8)] public int segmentCount = 8;
        public float innerRadius = 0.1f;
        public float outerRadius = 0.3f;
        public float outlineThickness = 0.01f;
        public int radialSteps = 48;
        public float outlineOffsetY = 0.0005f;

        [Header("Internal")]
        public GameObject[] segments = new GameObject[8];
        public Material gradientMaterial;
        public Material outlineMaterial;

        private void Start()
        {
            _SetupSegments();
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

                var meshHolder = seg.transform.Find("Mesh Holder");
                if (meshHolder)
                    meshHolder.localRotation = Quaternion.Euler(0f, (angleStep * i) - startAngle, 0f); // clockwise

                var mf = meshHolder ? meshHolder.GetComponent<MeshFilter>() : null;
                if (mf)
                    mf.sharedMesh = CreateWedgeMesh(angleStep, innerRadius, outerRadius, radialSteps, outlineThickness,
                        outlineOffsetY);

                var mr = meshHolder ? meshHolder.GetComponent<MeshRenderer>() : null;
                if (mr) mr.sharedMaterials = new[] { gradientMaterial, outlineMaterial };

                var mc = meshHolder ? meshHolder.GetComponent<MeshCollider>() : null;
                if (mc && mf)
                {
                    mc.sharedMesh = mf.sharedMesh;
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
        /// <param name="outlineT">Thickness of the outline quads.</param>
        /// <param name="offsetY">Vertical offset to prevent z-fighting.</param>
        /// <returns>A Mesh object representing the wedge with outline.</returns>
        private static Mesh CreateWedgeMesh(float angleDeg, float innerR, float outerR, int steps, float outlineT,
            float offsetY)
        {
            var mesh = new Mesh();

            // Precompute full circle rings
            var innerRing = new Vector3[steps + 1];
            var outerRing = new Vector3[steps + 1];
            for (var i = 0; i <= steps; i++)
            {
                var a = Mathf.Deg2Rad * 360f / steps * i;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                innerRing[i] = dir * innerR;
                outerRing[i] = dir * outerR;
            }

            // Determine which portion of the rings belongs to this wedge
            var endIdx = Mathf.RoundToInt(steps * angleDeg / 360f);

            // Gradient vertices
            var vertCount = (endIdx + 1) * 2;
            var verts = new Vector3[vertCount + endIdx * 8 + 4]; // +outline
            var uvs = new Vector2[verts.Length];

            for (var i = 0; i <= endIdx; i++)
            {
                var vi = i - 0;
                verts[vi] = innerRing[i];
                verts[vi + (endIdx - 0) + 1] = outerRing[i];
                uvs[vi] = new Vector2(0.5f, 0f);
                uvs[vi + (endIdx - 0) + 1] = new Vector2(0.5f, 1f);
            }

            // Gradient triangles
            var trisGradient = new int[endIdx * 6];
            for (int i = 0, t = 0; i < endIdx - 0; i++)
            {
                var i1 = i + 1;
                var i2 = i + endIdx + 1;
                var i3 = i + endIdx + 2;
                trisGradient[t++] = i;
                trisGradient[t++] = i2;
                trisGradient[t++] = i1;
                trisGradient[t++] = i1;
                trisGradient[t++] = i2;
                trisGradient[t++] = i3;
            }

            // Outline vertices and triangles
            int v = vertCount, tIdx = 0;

            var trisOutline = new int[endIdx * 12 + 6];

            for (var i = 0; i < endIdx; i++)
            {
                var d0 = innerRing[i];
                var d1 = innerRing[i + 1];
                var edgeDir = (outerRing[i + 1] - outerRing[i]).normalized;
                var radialOffset = Vector3.Cross(Vector3.up, edgeDir).normalized * outlineT;

                // Inner rim quad (outer->inner)
                AddQuad(verts, trisOutline, v, d0, d1, d0 - radialOffset, d1 - radialOffset, offsetY, tIdx);
                v += 4;
                tIdx += 6;

                // Outer rim quad
                var od0 = outerRing[i];
                var od1 = outerRing[i + 1];
                AddQuad(verts, trisOutline, v, od0 + radialOffset, od1 + radialOffset, od0, od1, offsetY, tIdx);
                v += 4;
                tIdx += 6;
            }

            // Radial start edge
            var startDir = innerRing[0].normalized;
            var startOffset = Vector3.Cross(Vector3.up, startDir).normalized * outlineT;
            AddQuadFlipped(verts, trisOutline, v, innerRing[0], outerRing[0],
                innerRing[0] + startOffset, outerRing[0] + startOffset, offsetY, tIdx);

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(trisGradient, 0);
            mesh.SetTriangles(trisOutline, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }


        /// <summary>
        /// Adds a quad to the vertex and triangle arrays with standard counter-clockwise winding.
        /// </summary>
        /// <param name="v">Array of vertices to write into.</param>
        /// <param name="t">Array of triangle indices to write into.</param>
        /// <param name="s">Starting index in the vertex array for this quad.</param>
        /// <param name="v0">Top-left vertex of the quad (outer corner).</param>
        /// <param name="v1">Top-right vertex of the quad (outer corner).</param>
        /// <param name="v2">Bottom-left vertex of the quad (inner corner).</param>
        /// <param name="v3">Bottom-right vertex of the quad (inner corner).</param>
        /// <param name="y">Vertical offset to apply to all vertices (usually small to prevent z-fighting).</param>
        /// <param name="ti">Starting index in the triangle array for this quad.</param>
        private static void AddQuad(Vector3[] v, int[] t, int s, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            float y, int ti)
        {
            v[s] = v0 + Vector3.up * y;
            v[s + 1] = v1 + Vector3.up * y;
            v[s + 2] = v2 + Vector3.up * y;
            v[s + 3] = v3 + Vector3.up * y;
            t[ti] = s;
            t[ti + 1] = s + 2;
            t[ti + 2] = s + 1;
            t[ti + 3] = s + 1;
            t[ti + 4] = s + 2;
            t[ti + 5] = s + 3;
        }

        /// <summary>
        /// Adds a quad to the vertex and triangle arrays with flipped clockwise winding.
        /// Useful for edges that need reversed normals (e.g., radial start edges).
        /// </summary>
        /// <param name="v">Array of vertices to write into.</param>
        /// <param name="t">Array of triangle indices to write into.</param>
        /// <param name="s">Starting index in the vertex array for this quad.</param>
        /// <param name="v0">Top-left vertex of the quad.</param>
        /// <param name="v1">Top-right vertex of the quad.</param>
        /// <param name="v2">Bottom-left vertex of the quad.</param>
        /// <param name="v3">Bottom-right vertex of the quad.</param>
        /// <param name="y">Vertical offset applied to all vertices.</param>
        /// <param name="ti">Starting index in the triangle array for this quad.</param>
        private static void AddQuadFlipped(Vector3[] v, int[] t, int s, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
            float y, int ti)
        {
            v[s] = v0 + Vector3.up * y;
            v[s + 1] = v1 + Vector3.up * y;
            v[s + 2] = v2 + Vector3.up * y;
            v[s + 3] = v3 + Vector3.up * y;
            t[ti] = s + 1;
            t[ti + 1] = s + 2;
            t[ti + 2] = s;
            t[ti + 3] = s + 3;
            t[ti + 4] = s + 2;
            t[ti + 5] = s + 1;
        }
    }
}