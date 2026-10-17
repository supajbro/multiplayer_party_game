using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private const float TuftBaseLift = 0.012f;
        private const float TuftHalfWidth = 0.075f;
        private const float TuftMaximumWindDisplacement = 0.16f;
        private const float GrassChunkSize = 48f;

        private sealed class GrassChunkData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(2048);
            public readonly List<Vector3> Normals = new List<Vector3>(2048);
            public readonly List<Vector2> Uvs = new List<Vector2>(2048);
            public readonly List<Vector2> Phases = new List<Vector2>(2048);
            public readonly List<Color32> Colours = new List<Color32>(2048);
            public readonly List<int> Triangles = new List<int>(3072);
            public int TuftCount;
        }

        private Material CreateLawnMaterial()
        {
            if (m_grassSettings == null)
            {
                return null;
            }

            Shader shader = Shader.Find("Couch Guys/Suburbs Mowed Lawn");
            if (shader == null)
            {
                Debug.LogError("The Suburbs Mowed Lawn shader could not be found.", this);
                return null;
            }

            Material material = new Material(shader) { name = "Generated Mowed Lawn" };
            material.SetColor("_BaseColor", m_grassSettings.BaseGrassColour);
            material.SetColor("_LightStripeColor", m_grassSettings.LightStripeColour);
            material.SetColor("_DarkStripeColor", m_grassSettings.DarkStripeColour);
            material.SetFloat("_StripeWidth", m_grassSettings.StripeWidth);
            material.SetFloat("_StripeContrast", m_grassSettings.StripeContrast);
            material.SetFloat("_DetailStrength", m_grassSettings.GrassDetailStrength);
            material.SetFloat("_WindEnabled", m_grassSettings.WindEnabled ? 1f : 0f);
            material.SetFloat("_WindStrength", m_grassSettings.WindStrength);
            material.SetFloat("_WindSpeed", m_grassSettings.WindSpeed);
            return material;
        }

        private void CreateGrassTufts()
        {
            if (m_grassSettings == null || !m_grassSettings.EnableGrassTufts ||
                m_grassSettings.GrassTuftDensity <= 0f || GeneratedTerrain == null)
            {
                return;
            }

            float spacing = Mathf.Sqrt(1f / m_grassSettings.GrassTuftDensity);
            float clearance = TuftHalfWidth + TuftMaximumWindDisplacement *
                              m_grassSettings.WindStrength + 0.06f;
            Rect bounds = CalculatePlayableLocalBounds();
            int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / spacing));
            int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / spacing));
            System.Random random = new System.Random(DeriveSeed(CurrentSeed, 0x45A17D2B));

            Dictionary<Vector2Int, GrassChunkData> chunks =
                new Dictionary<Vector2Int, GrassChunkData>();
            int accepted = 0;
            int rejected = 0;

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float jitterX = ((float)random.NextDouble() - 0.5f) * spacing * 0.74f;
                    float jitterZ = ((float)random.NextDouble() - 0.5f) * spacing * 0.74f;
                    Vector3 mapLocal = new Vector3(
                        bounds.xMin + (column + 0.5f) * spacing + jitterX,
                        0f,
                        bounds.yMin + (row + 0.5f) * spacing + jitterZ);
                    Vector3 world = MapLocalToWorld(mapLocal);
                    if (!IsValidGrassTuftPosition(world, clearance))
                    {
                        rejected++;
                        continue;
                    }

                    world.y = SampleTerrainHeight(world) + TuftBaseLift;
                    Vector3 normal = SampleGrassSurfaceNormal(world);
                    if (Vector3.Angle(normal, Vector3.up) > 34f)
                    {
                        rejected++;
                        continue;
                    }

                    float height = Mathf.Lerp(0.15f, 0.28f, (float)random.NextDouble());
                    float width = TuftHalfWidth * Mathf.Lerp(0.78f, 1.16f, (float)random.NextDouble());
                    float yaw = (float)random.NextDouble() * 180f;
                    float phase = (float)random.NextDouble();
                    byte tint = (byte)Mathf.RoundToInt(Mathf.Lerp(220f, 255f, (float)random.NextDouble()));
                    Vector2Int chunkCoordinate = new Vector2Int(
                        Mathf.FloorToInt(mapLocal.x / GrassChunkSize),
                        Mathf.FloorToInt(mapLocal.z / GrassChunkSize));
                    if (!chunks.TryGetValue(chunkCoordinate, out GrassChunkData chunk))
                    {
                        chunk = new GrassChunkData();
                        chunks.Add(chunkCoordinate, chunk);
                    }
                    AddTuft(chunk.Vertices, chunk.Normals, chunk.Uvs, chunk.Phases,
                        chunk.Colours, chunk.Triangles,
                        m_residentialGroundRoot, world, normal,
                        width, height, yaw, phase, new Color32(tint, 255, tint, 255));
                    chunk.TuftCount++;
                    accepted++;
                }
            }

            if (accepted == 0)
            {
                return;
            }

            Shader shader = Shader.Find("Couch Guys/Suburbs Grass Tuft");
            if (shader == null)
            {
                Debug.LogError("The Suburbs Grass Tuft shader could not be found.", this);
                return;
            }

            Material material = new Material(shader) { name = "Generated Grass Tufts" };
            material.SetColor("_BaseColor", m_grassSettings.DarkStripeColour);
            material.SetColor("_TipColor", m_grassSettings.LightStripeColour);
            material.SetFloat("_WindEnabled", m_grassSettings.WindEnabled ? 1f : 0f);
            material.SetFloat("_WindStrength", m_grassSettings.WindStrength);
            material.SetFloat("_WindSpeed", m_grassSettings.WindSpeed);
            material.SetFloat("_DrawDistance", m_grassSettings.GrassTuftDrawDistance);

            GameObject tuftRoot = new GameObject(
                "GeneratedGrassTufts",
                typeof(GeneratedTerrainMesh),
                typeof(GeneratedGrassTufts));
            tuftRoot.transform.SetParent(m_residentialGroundRoot, false);
            // The root owns the one shared runtime material. Each child owns only its mesh,
            // avoiding duplicate material destruction during teardown.
            tuftRoot.GetComponent<GeneratedTerrainMesh>().Initialise(null, material);
            tuftRoot.GetComponent<GeneratedGrassTufts>().Initialise(accepted, rejected);

            List<Vector2Int> chunkCoordinates = new List<Vector2Int>(chunks.Keys);
            chunkCoordinates.Sort((first, second) =>
            {
                int yComparison = first.y.CompareTo(second.y);
                return yComparison != 0 ? yComparison : first.x.CompareTo(second.x);
            });
            for (int index = 0; index < chunkCoordinates.Count; index++)
            {
                Vector2Int coordinate = chunkCoordinates[index];
                CreateGrassChunk(tuftRoot.transform, coordinate, chunks[coordinate], material);
            }
            tuftRoot.AddComponent<GeneratedGrassChunkCuller>().Initialise(
                m_grassSettings.GrassTuftDrawDistance);
        }

        private static void CreateGrassChunk(
            Transform parent,
            Vector2Int coordinate,
            GrassChunkData chunk,
            Material material)
        {
            Mesh mesh = new Mesh
            {
                name = $"GeneratedGrassTufts_{coordinate.x}_{coordinate.y}",
                indexFormat = chunk.Vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(chunk.Vertices);
            mesh.SetNormals(chunk.Normals);
            mesh.SetUVs(0, chunk.Uvs);
            mesh.SetUVs(1, chunk.Phases);
            mesh.SetColors(chunk.Colours);
            mesh.SetTriangles(chunk.Triangles, 0);
            mesh.RecalculateBounds();
            Bounds meshBounds = mesh.bounds;
            meshBounds.Expand(TuftMaximumWindDisplacement * 2f);
            mesh.bounds = meshBounds;

            GameObject chunkObject = new GameObject(
                $"GrassChunk_{coordinate.x}_{coordinate.y}",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(GeneratedTerrainMesh));
            chunkObject.transform.SetParent(parent, false);
            chunkObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = chunkObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            chunkObject.GetComponent<GeneratedTerrainMesh>().Initialise(mesh, null);
        }

        private bool IsValidGrassTuftPosition(Vector3 worldPosition, float clearance)
        {
            if (TrySampleRoadFootprint(worldPosition, out _, out float distanceToRoad) &&
                distanceToRoad <= m_sidewalkWidth + clearance)
            {
                return false;
            }

            CollectNearbyPropertyIndices(worldPosition);
            for (int candidateIndex = 0; candidateIndex < m_propertyQueryScratch.Count; candidateIndex++)
            {
                int index = m_propertyQueryScratch[candidateIndex];
                GeneratedProperty property = m_generatedProperties[index];
                if (property != null && TryGetDrivewayCoordinates(
                        property, worldPosition, out _, out float across, out _) &&
                    Mathf.Abs(across) <= property.ReservedDrivewayWidth * 0.5f + clearance)
                {
                    return false;
                }
            }

            Vector2 point = new Vector2(worldPosition.x, worldPosition.z);
            for (int index = 0; index < m_placedHouseFootprints.Count; index++)
            {
                if (IsPointInsideFootprint(point, m_placedHouseFootprints[index], clearance))
                {
                    return false;
                }
            }

            if (GeneratedStartingArea != null)
            {
                Vector3 local = GeneratedStartingArea.transform.InverseTransformPoint(worldPosition);
                float halfWidth = GeneratedStartingArea.GridFootprint.x * m_roadTileSize * 0.5f;
                float halfDepth = GeneratedStartingArea.GridFootprint.y * m_roadTileSize * 0.5f;
                if (Mathf.Abs(local.x) <= halfWidth + clearance &&
                    Mathf.Abs(local.z) <= halfDepth + clearance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPointInsideFootprint(
            Vector2 point,
            OrientedFootprint footprint,
            float clearance)
        {
            Vector2 offset = point - footprint.Centre;
            return Mathf.Abs(Vector2.Dot(offset, footprint.Right)) <= footprint.HalfSize.x + clearance &&
                   Mathf.Abs(Vector2.Dot(offset, footprint.Forward)) <= footprint.HalfSize.y + clearance;
        }

        private Vector3 SampleGrassSurfaceNormal(Vector3 worldPosition)
        {
            const float epsilon = 0.18f;
            float left = SampleTerrainHeight(worldPosition - Vector3.right * epsilon);
            float right = SampleTerrainHeight(worldPosition + Vector3.right * epsilon);
            float back = SampleTerrainHeight(worldPosition - Vector3.forward * epsilon);
            float front = SampleTerrainHeight(worldPosition + Vector3.forward * epsilon);
            return new Vector3(-(right - left), epsilon * 2f, -(front - back)).normalized;
        }

        private static void AddTuft(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Vector2> phases,
            List<Color32> colours,
            List<int> triangles,
            Transform meshSpace,
            Vector3 centre,
            Vector3 surfaceNormal,
            float halfWidth,
            float height,
            float yaw,
            float phase,
            Color32 colour)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal) *
                                  Quaternion.Euler(0f, yaw, 0f);
            AddBlade(0f);
            AddBlade(90f);

            void AddBlade(float angle)
            {
                int first = vertices.Count;
                Vector3 right = rotation * (Quaternion.Euler(0f, angle, 0f) * Vector3.right);
                Vector3 up = rotation * Vector3.up;
                Vector3 normal = Vector3.Cross(up, right).normalized;
                vertices.Add(meshSpace.InverseTransformPoint(centre - right * halfWidth));
                vertices.Add(meshSpace.InverseTransformPoint(centre + right * halfWidth));
                vertices.Add(meshSpace.InverseTransformPoint(
                    centre + up * height + right * halfWidth * 0.28f));
                vertices.Add(meshSpace.InverseTransformPoint(
                    centre + up * height - right * halfWidth * 0.28f));
                Vector3 localNormal = meshSpace.InverseTransformDirection(normal).normalized;
                for (int i = 0; i < 4; i++)
                {
                    normals.Add(localNormal);
                    phases.Add(new Vector2(phase, 0f));
                    colours.Add(colour);
                }
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
                triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
            }
        }

        private void AddGrassExclusionAxisBoundaries(
            List<float> coordinates,
            float minimum,
            float maximum,
            bool xAxis)
        {
            for (int index = 0; index < m_placedHouseFootprints.Count; index++)
            {
                OrientedFootprint footprint = m_placedHouseFootprints[index];
                Vector2 right = footprint.Right * footprint.HalfSize.x;
                Vector2 forward = footprint.Forward * footprint.HalfSize.y;
                Vector2[] corners =
                {
                    footprint.Centre - right - forward,
                    footprint.Centre + right - forward,
                    footprint.Centre + right + forward,
                    footprint.Centre - right + forward
                };
                float axisMinimum = float.PositiveInfinity;
                float axisMaximum = float.NegativeInfinity;
                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    Vector3 mapLocal = WorldToMapLocal(new Vector3(
                        corners[cornerIndex].x,
                        m_gridOrigin.y,
                        corners[cornerIndex].y));
                    float value = (xAxis ? mapLocal.x : mapLocal.z) / m_roadTileSize;
                    axisMinimum = Mathf.Min(axisMinimum, value);
                    axisMaximum = Mathf.Max(axisMaximum, value);
                }

                AddAxisBoundary(coordinates, axisMinimum, minimum, maximum);
                AddAxisBoundary(coordinates, axisMaximum, minimum, maximum);
            }
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
