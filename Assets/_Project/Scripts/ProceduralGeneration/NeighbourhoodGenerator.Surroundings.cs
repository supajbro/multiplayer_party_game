using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        [Header("Decorative Surroundings")]
        [SerializeField] private DistantSuburbSettings m_distantSuburbs;

        // Only mesh data is generated. All rendering components live on the saved prefab.
        private void CreateDistantSuburbs()
        {
            DistantSuburbSettings settings = m_distantSuburbs != null ? m_distantSuburbs :
                Resources.Load<DistantSuburbSettings>("DistantSuburbs");
            if (settings == null || settings.ChunkPrefab == null ||
                settings.ExtensionDistance <= 0f || GeneratedTerrain == null) return;

            Mesh source = GeneratedTerrain.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = source.vertices;
            Vector2[] uv = source.uv;
            // The existing continuous terrain is a rectangular row-major grid, including
            // its road cutouts. Read its actual edge heights instead of resampling noise.
            int width = 1;
            while (width < uv.Length && Mathf.Abs(uv[width].y - uv[0].y) < 0.00001f) width++;
            if (width < 2 || vertices.Length % width != 0) return;
            int height = vertices.Length / width;
            var edges = new List<Vector3>[4];
            for (int side = 0; side < 4; side++) edges[side] = new List<Vector3>();
            for (int x = 0; x < width; x++) edges[0].Add(vertices[x]);
            for (int z = 0; z < height; z++) edges[1].Add(vertices[z * width + width - 1]);
            for (int x = width - 1; x >= 0; x--) edges[2].Add(vertices[(height - 1) * width + x]);
            for (int z = height - 1; z >= 0; z--) edges[3].Add(vertices[z * width]);
            // Coordinates here are in the unchanged terrain object's local frame.
            
            Vector3 right = (edges[0][width - 1] - edges[0][0]); right.y = 0f; right.Normalize();
            Vector3 forward = (edges[1][height - 1] - edges[1][0]); forward.y = 0f; forward.Normalize();
            Vector3[] outward = { -forward, right, forward, -right };
            float extension = Mathf.Clamp(settings.ExtensionDistance, 1f, 2000f);
            var random = new System.Random(DeriveSeed(CurrentSeed, 0x19A37E51));
            Vector2 noiseOffset = new Vector2(random.Next(10000), random.Next(10000));
            Material ground = GeneratedTerrain.GetComponent<Renderer>().sharedMaterial;
            for (int side = 0; side < 4; side++)
            {
                List<Vector3> edge = edges[side];
                Vector3 tangent = Vector3.Cross(outward[side], Vector3.up);
                // Limit both geometry and renderer bounds; Unity frustum-culls each chunk.
                for (int start = 0; start < edge.Count - 1;)
                {
                    int end = start + 1;
                    while (end < edge.Count - 1 && end - start < 96 &&
                        Vector3.Distance(edge[start], edge[end + 1]) < 96f) end++;
                    var terrain = new SceneryMesh();
                    var buildings = new SceneryMesh();
                    const int rings = 16;
                    var grid = new Vector3[end - start + 1, rings + 1];
                    for (int i = start; i <= end; i++)
                    {
                        // Use physical position, not vertex index: terrain axes are irregular.
                        float along = Vector3.Dot(edge[i] - edge[0], tangent) /
                            Vector3.Dot(edge[edge.Count - 1] - edge[0], tangent);
                        for (int ring = 0; ring <= rings; ring++)
                        {
                            float distance = extension * ring / rings;
                            Vector3 point = edge[i] + outward[side] * distance +
                                tangent * ((along * 2f - 1f) * distance);
                            float noise = Mathf.PerlinNoise(point.x * 0.006f + noiseOffset.x,
                                point.z * 0.006f + noiseOffset.y);
                            point.y += Mathf.SmoothStep(0f, 1f, distance / 100f) *
                                ((noise - 0.5f) * Mathf.Max(0f, settings.HillHeight) +
                                 Mathf.SmoothStep(0f, 1f, distance / extension) * Mathf.Max(0f, settings.HillHeight));
                            // Sink the last strip behind the distant ridge, hiding its edge.
                            if (ring == rings) point.y -= 160f;
                            grid[i - start, ring] = point;
                            if (i > start && ring > 0)
                                terrain.Quad(grid[i-start,ring-1], grid[i-start,ring],
                                    grid[i-start-1,ring], grid[i-start-1,ring-1], Color.white);
                        }
                    }
                    // A street-sized spacing, with correlated gaps, suggests neighbourhoods.
                    // Buildings are baked into one mesh per chunk, never individual objects.
                    float span = Vector3.Distance(edge[start], edge[end]);
                    int lots = Mathf.Max(1, Mathf.FloorToInt(span / 24f));
                    for (int lot = 0; lot < lots; lot++)
                    for (int ring = 1; ring < rings - 2; ring++)
                    {
                        float u = (lot + 0.3f + (float)random.NextDouble() * 0.4f) / lots;
                        float target = u * Vector3.Dot(edge[end] - edge[start], tangent);
                        int column = 0;
                        while (column < end - start - 1 &&
                            Vector3.Dot(edge[start+column+1] - edge[start], tangent) < target) column++;
                        float from = Vector3.Dot(edge[start+column] - edge[start], tangent);
                        float to = Vector3.Dot(edge[start+column+1] - edge[start], tangent);
                        float across = Mathf.InverseLerp(from, to, target);
                        Vector3 a = Vector3.Lerp(grid[column,ring], grid[column+1,ring], across);
                        Vector3 b = Vector3.Lerp(grid[column,ring+1], grid[column+1,ring+1], across);
                        Vector3 point = Vector3.Lerp(a, b, 0.5f);
                        if (extension * (ring + 0.5f) / rings < 24f) continue;
                        float cluster = Mathf.PerlinNoise(point.x * 0.018f + noiseOffset.x,
                            point.z * 0.018f + noiseOffset.y);
                        if (cluster < 0.36f || random.NextDouble() > Mathf.Clamp01(settings.BuildingDensity)) continue;
                        float variety = Mathf.Clamp01(settings.BuildingVariety);
                        bool large = random.NextDouble() < 0.09f * variety;
                        float w = 9f + (float)random.NextDouble() * 5f * variety;
                        float d = 8f + (float)random.NextDouble() * 5f * variety;
                        float h = 4f + (float)random.NextDouble() * 4f * variety;
                        if (large) { w *= 1.5f; d *= 1.5f; h *= 1.8f; }
                        Color colour = settings.BuildingColours != null && settings.BuildingColours.Length > 0 ?
                            settings.BuildingColours[random.Next(settings.BuildingColours.Length)] : Color.white;
                        Quaternion rotation = Quaternion.AngleAxis(side * 90f +
                            ((float)random.NextDouble() - 0.5f) * 20f * variety, Vector3.up);
                        // Deep foundations cover the slope under these distant silhouettes.
                        point.y -= 4f;
                        buildings.House(point, rotation, w, d, h + 4f, colour, large);
                    }
                    SaveSceneryChunk(terrain, ground, $"DistantTerrain_{side}_{start}", settings);
                    if (buildings.Vertices.Count > 0)
                        SaveSceneryChunk(buildings, null, $"DistantBuildings_{side}_{start}", settings);
                    start = end;
                }
            }
        }

        private void SaveSceneryChunk(SceneryMesh data, Material material, string chunkName,
            DistantSuburbSettings settings)
        {
            GeneratedTerrainMesh chunk = Instantiate(settings.ChunkPrefab, m_generatedRoot);
            chunk.name = chunkName;
            chunk.transform.SetPositionAndRotation(GeneratedTerrain.transform.position, GeneratedTerrain.transform.rotation);
            chunk.transform.localScale = GeneratedTerrain.transform.localScale;
            Mesh mesh = new Mesh { name = chunkName,
                indexFormat = data.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(data.Vertices);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.SetColors(data.Colours);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (material != null) chunk.GetComponent<Renderer>().sharedMaterial = material;
            chunk.Initialise(mesh, null);
        }

        private sealed class SceneryMesh
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public readonly List<Color> Colours = new List<Color>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color colour)
            {
                int i = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
                Colours.Add(colour); Colours.Add(colour); Colours.Add(colour);
                Triangles.Add(i); Triangles.Add(i+1); Triangles.Add(i+2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
            { Triangle(a,b,c,colour); Triangle(a,c,d,colour); }
            public void House(Vector3 position, Quaternion rotation, float w, float d, float h, Color colour, bool flat)
            {
                Vector3[] p = {
                    new Vector3(-w/2,0,-d/2), new Vector3(w/2,0,-d/2),
                    new Vector3(w/2,0,d/2), new Vector3(-w/2,0,d/2),
                    new Vector3(-w/2,h,-d/2), new Vector3(w/2,h,-d/2),
                    new Vector3(w/2,h,d/2), new Vector3(-w/2,h,d/2),
                    new Vector3(0,h+(flat?0:3f),-d/2), new Vector3(0,h+(flat?0:3f),d/2) };
                for (int i=0;i<p.Length;i++) p[i] = position + rotation*p[i];
                Quad(p[0],p[4],p[5],p[1],colour); Quad(p[1],p[5],p[6],p[2],colour);
                Quad(p[2],p[6],p[7],p[3],colour); Quad(p[3],p[7],p[4],p[0],colour);
                Triangle(p[4],p[8],p[5],colour); Triangle(p[6],p[9],p[7],colour);
                Color roof = colour * 0.52f; roof.a = 1f;
                Quad(p[4],p[7],p[9],p[8],roof); Quad(p[8],p[9],p[6],p[5],roof);
            }
        }
    }
}


