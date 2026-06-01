using System;
using System.Collections.Generic;
using COMApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Callback class that implements InwSimplePrimitivesCB to extract geometry from Navisworks fragments.
    /// Transforms coordinates to World Space and converts units to Unreal Engine centimeters.
    /// </summary>
    public class DatasmithGeometryCallback : COMApi.InwSimplePrimitivesCB
    {
        private readonly float[] _matrix;
        
        // Lists to store extracted data
        public List<float> Vertices { get; } = new List<float>();
        public List<float> Normals { get; } = new List<float>();
        public List<int> Indices { get; } = new List<int>();

        public DatasmithGeometryCallback(object localToWorldMatrix)
        {
            _matrix = ExtractMatrix(localToWorldMatrix);
        }

        private static float[] ExtractMatrix(object matrixObj)
        {
            var result = new float[16];
            if (matrixObj == null)
            {
                // Identity matrix
                result[0] = 1.0f; result[5] = 1.0f; result[10] = 1.0f; result[15] = 1.0f;
                return result;
            }

            try
            {
                dynamic transform = matrixObj;
                var matrixData = (Array)transform.Matrix;
                int lowerBound = matrixData.GetLowerBound(0);
                for (int i = 0; i < 16; i++)
                {
                    result[i] = Convert.ToSingle(matrixData.GetValue(lowerBound + i));
                }
            }
            catch
            {
                // Fallback to Identity matrix
                result[0] = 1.0f; result[5] = 1.0f; result[10] = 1.0f; result[15] = 1.0f;
            }

            return result;
        }

        public void Triangle(COMApi.InwSimpleVertex v1, COMApi.InwSimpleVertex v2, COMApi.InwSimpleVertex v3)
        {
            // Process Vertex 1
            ProcessVertex(v1, out float x1, out float y1, out float z1, out float nx1, out float ny1, out float nz1);
            // Process Vertex 2
            ProcessVertex(v2, out float x2, out float y2, out float z2, out float nx2, out float ny2, out float nz2);
            // Process Vertex 3
            ProcessVertex(v3, out float x3, out float y3, out float z3, out float nx3, out float ny3, out float nz3);

            // Add vertices to list (swapping Y and Z to convert to Unreal coordinate convention if needed, or keeping standard Z-up)
            // Unreal is Left-Handed, Z-Up, Centimeters.
            // Navisworks is Right-Handed, Z-Up, Meters.
            // Swap: X = x * 100, Y = -y * 100, Z = z * 100 (standard conversion to left-handed Z-up)
            int baseIndex = Vertices.Count / 3;

            Vertices.Add(x1 * 100.0f); Vertices.Add(-y1 * 100.0f); Vertices.Add(z1 * 100.0f);
            Vertices.Add(x2 * 100.0f); Vertices.Add(-y2 * 100.0f); Vertices.Add(z2 * 100.0f);
            Vertices.Add(x3 * 100.0f); Vertices.Add(-y3 * 100.0f); Vertices.Add(z3 * 100.0f);

            // Add normals to list
            Normals.Add(nx1); Normals.Add(-ny1); Normals.Add(nz1);
            Normals.Add(nx2); Normals.Add(-ny2); Normals.Add(nz2);
            Normals.Add(nx3); Normals.Add(-ny3); Normals.Add(nz3);

            // Add indices (triangles)
            Indices.Add(baseIndex);
            Indices.Add(baseIndex + 1);
            Indices.Add(baseIndex + 2);
        }

        private void ProcessVertex(COMApi.InwSimpleVertex v, 
            out float rx, out float ry, out float rz,
            out float rnx, out float rny, out float rnz)
        {
            // Extract coordinates
            var c = (Array)((dynamic)v).coord;
            int lbc = c.GetLowerBound(0);
            float lx = Convert.ToSingle(c.GetValue(lbc));
            float ly = Convert.ToSingle(c.GetValue(lbc + 1));
            float lz = Convert.ToSingle(c.GetValue(lbc + 2));

            // Multiply by transform matrix (Local to World)
            // M = [m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12, m13, m14, m15]
            rx = lx * _matrix[0] + ly * _matrix[4] + lz * _matrix[8] + _matrix[12];
            ry = lx * _matrix[1] + ly * _matrix[5] + lz * _matrix[9] + _matrix[13];
            rz = lx * _matrix[2] + ly * _matrix[6] + lz * _matrix[10] + _matrix[14];

            // Extract normal if present
            rnx = 0.0f;
            rny = 0.0f;
            rnz = 1.0f; // Default normal pointing up

            var normalObj = ((dynamic)v).normal;
            if (normalObj != null)
            {
                var n = (Array)normalObj;
                int lbn = n.GetLowerBound(0);
                float lnx = Convert.ToSingle(n.GetValue(lbn));
                float lny = Convert.ToSingle(n.GetValue(lbn + 1));
                float lnz = Convert.ToSingle(n.GetValue(lbn + 2));

                // Transform normal vector (Rotation/Scale only, no translation)
                rnx = lnx * _matrix[0] + lny * _matrix[4] + lnz * _matrix[8];
                rny = lnx * _matrix[1] + lny * _matrix[5] + lnz * _matrix[9];
                rnz = lnx * _matrix[2] + lny * _matrix[6] + lnz * _matrix[10];

                // Normalize vector
                float len = (float)Math.Sqrt(rnx * rnx + rny * rny + rnz * rnz);
                if (len > 0.0001f)
                {
                    rnx /= len;
                    rny /= len;
                    rnz /= len;
                }
            }
        }

        public void Line(COMApi.InwSimpleVertex v1, COMApi.InwSimpleVertex v2)
        {
            // We ignore lines for Datasmith export
        }

        public void Point(COMApi.InwSimpleVertex v1)
        {
            // We ignore points for Datasmith export
        }

        public void SnapPoint(COMApi.InwSimpleVertex v1)
        {
            // We ignore snap points for Datasmith export
        }
    }
}
