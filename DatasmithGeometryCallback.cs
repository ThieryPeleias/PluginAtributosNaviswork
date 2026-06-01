using System;
using System.Collections.Generic;
using System.IO;
using COMApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Callback class that implements InwSimplePrimitivesCB to extract geometry from Navisworks fragments.
    /// Incorporates complete try-catch isolation and diagnostics to prevent silent COM aborts.
    /// </summary>
    public class DatasmithGeometryCallback : COMApi.InwSimplePrimitivesCB
    {
        private readonly float[] _matrix;
        
        // Lists to store extracted data
        public List<float> Vertices { get; } = new List<float>();
        public List<float> Normals { get; } = new List<float>();
        public List<int> Indices { get; } = new List<int>();

        private readonly Dictionary<VertexKey, int> _vertexToIndex = new Dictionary<VertexKey, int>();
        private readonly HashSet<TriangleKey> _uniqueTriangles = new HashSet<TriangleKey>();

        public struct VertexKey : IEquatable<VertexKey>
        {
            public float X, Y, Z;
            public float NX, NY, NZ;

            public VertexKey(float x, float y, float z, float nx, float ny, float nz)
            {
                X = x; Y = y; Z = z;
                NX = nx; NY = ny; NZ = nz;
            }

            public bool Equals(VertexKey other)
            {
                return Math.Abs(X - other.X) < 0.0001f &&
                       Math.Abs(Y - other.Y) < 0.0001f &&
                       Math.Abs(Z - other.Z) < 0.0001f &&
                       Math.Abs(NX - other.NX) < 0.01f &&
                       Math.Abs(NY - other.NY) < 0.01f &&
                       Math.Abs(NZ - other.NZ) < 0.01f;
            }

            public override bool Equals(object obj)
            {
                return obj is VertexKey && Equals((VertexKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + ((int)Math.Round(X * 1000.0f)).GetHashCode();
                    hash = hash * 23 + ((int)Math.Round(Y * 1000.0f)).GetHashCode();
                    hash = hash * 23 + ((int)Math.Round(Z * 1000.0f)).GetHashCode();
                    hash = hash * 23 + ((int)Math.Round(NX * 100.0f)).GetHashCode();
                    hash = hash * 23 + ((int)Math.Round(NY * 100.0f)).GetHashCode();
                    hash = hash * 23 + ((int)Math.Round(NZ * 100.0f)).GetHashCode();
                    return hash;
                }
            }
        }

        public struct TriangleKey : IEquatable<TriangleKey>
        {
            public int I1, I2, I3;

            public TriangleKey(int i1, int i2, int i3)
            {
                if (i1 <= i2)
                {
                    if (i2 <= i3) { I1 = i1; I2 = i2; I3 = i3; }
                    else if (i1 <= i3) { I1 = i1; I2 = i3; I3 = i2; }
                    else { I1 = i3; I2 = i1; I3 = i2; }
                }
                else
                {
                    if (i1 <= i3) { I1 = i2; I2 = i1; I3 = i3; }
                    else if (i2 <= i3) { I1 = i2; I2 = i3; I3 = i1; }
                    else { I1 = i3; I2 = i2; I3 = i1; }
                }
            }

            public bool Equals(TriangleKey other)
            {
                return I1 == other.I1 && I2 == other.I2 && I3 == other.I3;
            }

            public override bool Equals(object obj)
            {
                return obj is TriangleKey && Equals((TriangleKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + I1;
                    hash = hash * 23 + I2;
                    hash = hash * 23 + I3;
                    return hash;
                }
            }
        }

        public DatasmithGeometryCallback(object localToWorldMatrix)
        {
            _matrix = ExtractMatrix(localToWorldMatrix);
        }

        public static float[] ExtractMatrix(object matrixObj)
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
                // Use standard reflection instead of dynamic binder to avoid Double[*] -> Double[] cast error
                object matrixProp = matrixObj.GetType().InvokeMember(
                    "Matrix",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    matrixObj,
                    null
                );

                if (matrixProp is Array matrixData)
                {
                    int lowerBound = matrixData.GetLowerBound(0);
                    for (int i = 0; i < 16; i++)
                    {
                        result[i] = Convert.ToSingle(matrixData.GetValue(lowerBound + i));
                    }
                }
                else
                {
                    // Fallback to Identity
                    result[0] = 1.0f; result[5] = 1.0f; result[10] = 1.0f; result[15] = 1.0f;
                }
            }
            catch (Exception ex)
            {
                LogStaticError($"ExtractMatrix Error: {ex.Message}");
                // Fallback to Identity matrix
                result[0] = 1.0f; result[5] = 1.0f; result[10] = 1.0f; result[15] = 1.0f;
            }

            return result;
        }

        /// <summary>
        /// Writes diagnostic messages to a static temp log file.
        /// </summary>
        private static void LogStaticError(string message)
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string logFilePath = Path.Combine(tempDir, "Virtuart4D_Geometry_Error.log");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public void Triangle(COMApi.InwSimpleVertex v1, COMApi.InwSimpleVertex v2, COMApi.InwSimpleVertex v3)
        {
            try
            {
                // Process Vertex 1
                ProcessVertex(v1, out float x1, out float y1, out float z1, out float nx1, out float ny1, out float nz1);
                // Process Vertex 2
                ProcessVertex(v2, out float x2, out float y2, out float z2, out float nx2, out float ny2, out float nz2);
                // Process Vertex 3
                ProcessVertex(v3, out float x3, out float y3, out float z3, out float nx3, out float ny3, out float nz3);

                // Scale coordinates by 100.0f (meters to centimeters) and invert Y to match Unreal's coordinate convention
                float px1 = x1 * 100.0f; float py1 = -y1 * 100.0f; float pz1 = z1 * 100.0f;
                float px2 = x2 * 100.0f; float py2 = -y2 * 100.0f; float pz2 = z2 * 100.0f;
                float px3 = x3 * 100.0f; float py3 = -y3 * 100.0f; float pz3 = z3 * 100.0f;

                float pnx1 = nx1; float pny1 = -ny1; float pnz1 = nz1;
                float pnx2 = nx2; float pny2 = -ny2; float pnz2 = nz2;
                float pnx3 = nx3; float pny3 = -ny3; float pnz3 = nz3;

                int i1 = GetOrCreateVertex(px1, py1, pz1, pnx1, pny1, pnz1);
                int i2 = GetOrCreateVertex(px2, py2, pz2, pnx2, pny2, pnz2);
                int i3 = GetOrCreateVertex(px3, py3, pz3, pnx3, pny3, pnz3);

                var triKey = new TriangleKey(i1, i2, i3);
                if (_uniqueTriangles.Add(triKey))
                {
                    Indices.Add(i1);
                    Indices.Add(i2);
                    Indices.Add(i3);
                }
            }
            catch (Exception ex)
            {
                LogStaticError($"Exception in Triangle callback: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private int GetOrCreateVertex(float x, float y, float z, float nx, float ny, float nz)
        {
            var key = new VertexKey(x, y, z, nx, ny, nz);
            if (_vertexToIndex.TryGetValue(key, out int index))
            {
                return index;
            }

            index = Vertices.Count / 3;
            Vertices.Add(x); Vertices.Add(y); Vertices.Add(z);
            Normals.Add(nx); Normals.Add(ny); Normals.Add(nz);
            _vertexToIndex[key] = index;
            return index;
        }

        private void ProcessVertex(COMApi.InwSimpleVertex v, 
            out float rx, out float ry, out float rz,
            out float rnx, out float rny, out float rnz)
        {
            rx = 0.0f; ry = 0.0f; rz = 0.0f;
            rnx = 0.0f; rny = 0.0f; rnz = 1.0f; // Default normal pointing up

            Array arr = v.coord as Array;
            if (arr != null)
            {
                int lbc = arr.GetLowerBound(0);
                float lx = Convert.ToSingle(arr.GetValue(lbc));
                float ly = Convert.ToSingle(arr.GetValue(lbc + 1));
                float lz = Convert.ToSingle(arr.GetValue(lbc + 2));

                // Apply Local-to-World transform matrix
                rx = lx * _matrix[0] + ly * _matrix[4] + lz * _matrix[8] + _matrix[12];
                ry = lx * _matrix[1] + ly * _matrix[5] + lz * _matrix[9] + _matrix[13];
                rz = lx * _matrix[2] + ly * _matrix[6] + lz * _matrix[10] + _matrix[14];
            }

            Array narr = v.normal as Array;
            if (narr != null)
            {
                int lbn = narr.GetLowerBound(0);
                float lnx = Convert.ToSingle(narr.GetValue(lbn));
                float lny = Convert.ToSingle(narr.GetValue(lbn + 1));
                float lnz = Convert.ToSingle(narr.GetValue(lbn + 2));

                // Apply Local-to-World transform matrix (Rotation/Scale only)
                rnx = lnx * _matrix[0] + lny * _matrix[4] + lnz * _matrix[8];
                rny = lnx * _matrix[1] + lny * _matrix[5] + lnz * _matrix[9];
                rnz = lnx * _matrix[2] + lny * _matrix[6] + lnz * _matrix[10];

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
