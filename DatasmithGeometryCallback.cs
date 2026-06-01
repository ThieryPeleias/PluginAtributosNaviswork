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

                int baseIndex = Vertices.Count / 3;

                // Scale coordinates by 100.0f (meters to centimeters) and invert Y to match Unreal's coordinate convention
                Vertices.Add(x1 * 100.0f); Vertices.Add(-y1 * 100.0f); Vertices.Add(z1 * 100.0f);
                Vertices.Add(x2 * 100.0f); Vertices.Add(-y2 * 100.0f); Vertices.Add(z2 * 100.0f);
                Vertices.Add(x3 * 100.0f); Vertices.Add(-y3 * 100.0f); Vertices.Add(z3 * 100.0f);

                // Add normals
                Normals.Add(nx1); Normals.Add(-ny1); Normals.Add(nz1);
                Normals.Add(nx2); Normals.Add(-ny2); Normals.Add(nz2);
                Normals.Add(nx3); Normals.Add(-ny3); Normals.Add(nz3);

                // Add indices (triangles)
                Indices.Add(baseIndex);
                Indices.Add(baseIndex + 1);
                Indices.Add(baseIndex + 2);
            }
            catch (Exception ex)
            {
                LogStaticError($"Exception in Triangle callback: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ProcessVertex(COMApi.InwSimpleVertex v, 
            out float rx, out float ry, out float rz,
            out float rnx, out float rny, out float rnz)
        {
            rx = 0.0f; ry = 0.0f; rz = 0.0f;
            rnx = 0.0f; rny = 0.0f; rnz = 1.0f; // Default normal pointing up

            try
            {
                // Fetch coord array safely
                object coordObj = v.coord;
                if (coordObj == null) return;

                float lx = 0.0f, ly = 0.0f, lz = 0.0f;
                if (coordObj is Array arr)
                {
                    int lbc = arr.GetLowerBound(0);
                    lx = Convert.ToSingle(arr.GetValue(lbc));
                    ly = Convert.ToSingle(arr.GetValue(lbc + 1));
                    lz = Convert.ToSingle(arr.GetValue(lbc + 2));
                }
                else
                {
                    // Fallback to dynamic indexing if not directly an Array
                    dynamic dyn = coordObj;
                    try
                    {
                        lx = Convert.ToSingle(dyn[0]);
                        ly = Convert.ToSingle(dyn[1]);
                        lz = Convert.ToSingle(dyn[2]);
                    }
                    catch
                    {
                        lx = Convert.ToSingle(dyn[1]);
                        ly = Convert.ToSingle(dyn[2]);
                        lz = Convert.ToSingle(dyn[3]);
                    }
                }

                // Apply Local-to-World transform matrix
                rx = lx * _matrix[0] + ly * _matrix[4] + lz * _matrix[8] + _matrix[12];
                ry = lx * _matrix[1] + ly * _matrix[5] + lz * _matrix[9] + _matrix[13];
                rz = lx * _matrix[2] + ly * _matrix[6] + lz * _matrix[10] + _matrix[14];

                // Fetch normal array safely
                object normalObj = v.normal;
                if (normalObj != null)
                {
                    float lnx = 0.0f, lny = 0.0f, lnz = 0.0f;
                    if (normalObj is Array narr)
                    {
                        int lbn = narr.GetLowerBound(0);
                        lnx = Convert.ToSingle(narr.GetValue(lbn));
                        lny = Convert.ToSingle(narr.GetValue(lbn + 1));
                        lnz = Convert.ToSingle(narr.GetValue(lbn + 2));
                    }
                    else
                    {
                        dynamic dyn = normalObj;
                        try
                        {
                            lnx = Convert.ToSingle(dyn[0]);
                            lny = Convert.ToSingle(dyn[1]);
                            lnz = Convert.ToSingle(dyn[2]);
                        }
                        catch
                        {
                            lnx = Convert.ToSingle(dyn[1]);
                            lny = Convert.ToSingle(dyn[2]);
                            lnz = Convert.ToSingle(dyn[3]);
                        }
                    }

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
            catch (Exception ex)
            {
                LogStaticError($"Exception in ProcessVertex: {ex.Message}\n{ex.StackTrace}");
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
