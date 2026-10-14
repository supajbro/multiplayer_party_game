using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private const float DrivewayBedOffset = 0.025f;

        private readonly struct DrivewaySidewalkCut
        {
            public readonly float InnerMinimum;
            public readonly float InnerMaximum;
            public readonly float OuterMinimum;
            public readonly float OuterMaximum;

            public DrivewaySidewalkCut(
                float innerMinimum,
                float innerMaximum,
                float outerMinimum,
                float outerMaximum)
            {
                InnerMinimum = innerMinimum;
                InnerMaximum = innerMaximum;
                OuterMinimum = outerMinimum;
                OuterMaximum = outerMaximum;
            }
        }

        private void CreateDriveways()
        {
            if (m_drivewaysRoot == null || m_generatedProperties.Count == 0)
            {
                return;
            }

            List<Vector3> vertices = new List<Vector3>(m_generatedProperties.Count * 64);
            List<Vector2> uvs = new List<Vector2>(m_generatedProperties.Count * 64);
            List<int> triangles = new List<int>(m_generatedProperties.Count * 192);
            float spacing = Mathf.Max(0.5f, Mathf.Min(1f, m_terrainSettings.VertexSpacing));
            for (int propertyIndex = 0; propertyIndex < m_generatedProperties.Count; propertyIndex++)
            {
                GeneratedProperty property = m_generatedProperties[propertyIndex];
                if (property == null || property.ReservedDrivewayWidth <= 0.01f)
                {
                    continue;
                }

                Vector3 start = property.DrivewayStart;
                Vector3 end = property.DrivewayEnd;
                Vector3 flatSegment = end - start;
                flatSegment.y = 0f;
                float length = flatSegment.magnitude;
                if (length <= 0.05f)
                {
                    continue;
                }

                Vector3 forward = flatSegment / length;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                int lengthSegments = Mathf.Max(1, Mathf.CeilToInt(length / spacing));
                int widthSegments = Mathf.Max(
                    1,
                    Mathf.CeilToInt(property.ReservedDrivewayWidth / spacing));
                int firstVertex = vertices.Count;
                for (int row = 0; row <= lengthSegments; row++)
                {
                    float t = row / (float)lengthSegments;
                    Vector3 centre = Vector3.Lerp(start, end, t);
                    for (int column = 0; column <= widthSegments; column++)
                    {
                        float across = Mathf.Lerp(
                            -property.ReservedDrivewayWidth * 0.5f,
                            property.ReservedDrivewayWidth * 0.5f,
                            column / (float)widthSegments);
                        Vector3 point = centre + right * across;
                        point.y = SampleDrivewaySurfaceHeight(property, point, t, across);
                        vertices.Add(m_drivewaysRoot.InverseTransformPoint(point));
                        uvs.Add(new Vector2(
                            across / Mathf.Max(0.1f, property.ReservedDrivewayWidth) + 0.5f,
                            t * length / Mathf.Max(0.1f, property.ReservedDrivewayWidth)));
                    }
                }

                int rowSize = widthSegments + 1;
                for (int row = 0; row < lengthSegments; row++)
                {
                    for (int column = 0; column < widthSegments; column++)
                    {
                        int bottomLeft = firstVertex + row * rowSize + column;
                        int bottomRight = bottomLeft + 1;
                        int topLeft = bottomLeft + rowSize;
                        int topRight = topLeft + 1;
                        triangles.Add(bottomLeft);
                        triangles.Add(topLeft);
                        triangles.Add(topRight);
                        triangles.Add(bottomLeft);
                        triangles.Add(topRight);
                        triangles.Add(bottomRight);
                    }
                }
            }

            if (vertices.Count == 0)
            {
                return;
            }

            Mesh mesh = new Mesh
            {
                name = $"GeneratedDriveways_{CurrentSeed}",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            GameObject drivewayObject = new GameObject(
                "GeneratedDriveways",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(MeshCollider),
                typeof(GeneratedTerrainMesh));
            drivewayObject.transform.SetParent(m_drivewaysRoot, false);
            drivewayObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            drivewayObject.GetComponent<MeshCollider>().sharedMesh = mesh;
            drivewayObject.GetComponent<MeshRenderer>().sharedMaterial = GetDrivewayMaterial();
            GeneratedDrivewaySurface = drivewayObject.GetComponent<GeneratedTerrainMesh>();
            GeneratedDrivewaySurface.Initialise(mesh, null);
        }

        private Material GetDrivewayMaterial()
        {
            if (m_drivewayMaterial != null)
            {
                return m_drivewayMaterial;
            }

            if (m_runtimeDrivewayMaterial != null)
            {
                return m_runtimeDrivewayMaterial;
            }

            Shader shader = Shader.Find("Couch Guys/Toon Lit") ??
                            Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            m_runtimeDrivewayMaterial = new Material(shader)
            {
                name = "Generated Driveway Concrete",
                color = new Color(0.69f, 0.70f, 0.72f)
            };
            return m_runtimeDrivewayMaterial;
        }

        private float BlendDrivewayGrades(float terrainHeight, Vector3 flatWorldPosition)
        {
            float result = terrainHeight;
            for (int index = 0; index < m_generatedProperties.Count; index++)
            {
                GeneratedProperty property = m_generatedProperties[index];
                if (!TryGetDrivewayCoordinates(
                        property,
                        flatWorldPosition,
                        out float t,
                        out float across,
                        out _))
                {
                    continue;
                }

                float halfWidth = property.ReservedDrivewayWidth * 0.5f;
                float blendWidth = Mathf.Max(0f, property.DrivewayClearance);
                float distance = Mathf.Abs(across);
                if (distance > halfWidth + blendWidth)
                {
                    continue;
                }

                float influence = distance <= halfWidth || blendWidth <= 0.001f
                    ? 1f
                    : 1f - Mathf.SmoothStep(0f, 1f, (distance - halfWidth) / blendWidth);
                float surfaceWorldHeight = SampleDrivewaySurfaceHeight(
                    property,
                    flatWorldPosition,
                    t,
                    across);
                float drivewayBedHeight = surfaceWorldHeight - m_gridOrigin.y - DrivewayBedOffset;
                result = Mathf.Lerp(result, drivewayBedHeight, influence);
            }

            return result;
        }

        private bool IsTerrainQuadCoveredByDriveway(
            Vector3 bottomLeft,
            Vector3 bottomRight,
            Vector3 topLeft,
            Vector3 topRight)
        {
            for (int propertyIndex = 0; propertyIndex < m_generatedProperties.Count; propertyIndex++)
            {
                GeneratedProperty property = m_generatedProperties[propertyIndex];
                if (property == null || property.ReservedDrivewayWidth <= 0.05f)
                {
                    continue;
                }

                float halfWidth = Mathf.Max(0f, property.ReservedDrivewayWidth * 0.5f - 0.01f);
                if (IsPointInsideDriveway(property, bottomLeft, halfWidth) &&
                    IsPointInsideDriveway(property, bottomRight, halfWidth) &&
                    IsPointInsideDriveway(property, topLeft, halfWidth) &&
                    IsPointInsideDriveway(property, topRight, halfWidth))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointInsideDriveway(
            GeneratedProperty property,
            Vector3 point,
            float halfWidth) =>
            TryGetDrivewayCoordinates(
                property,
                point,
                out _,
                out float across,
                out _) &&
            Mathf.Abs(across) <= halfWidth;

        private bool TrySampleDrivewaySurface(Vector3 worldPosition, out float height)
        {
            for (int index = 0; index < m_generatedProperties.Count; index++)
            {
                GeneratedProperty property = m_generatedProperties[index];
                if (!TryGetDrivewayCoordinates(
                        property,
                        worldPosition,
                        out float t,
                        out float across,
                        out _))
                {
                    continue;
                }

                if (Mathf.Abs(across) <= property.ReservedDrivewayWidth * 0.5f + 0.001f)
                {
                    height = SampleDrivewaySurfaceHeight(property, worldPosition, t, across);
                    return true;
                }
            }

            height = 0f;
            return false;
        }

        private float SampleDrivewaySurfaceHeight(
            GeneratedProperty property,
            Vector3 worldPosition,
            float t,
            float across)
        {
            Vector3 start = property.DrivewayStart;
            Vector3 end = property.DrivewayEnd;
            if (t <= 0.0001f)
            {
                return SampleRoadSurfaceHeight(worldPosition);
            }

            Vector3 endNormal = property.DrivewayAnchor != null
                ? property.DrivewayAnchor.up
                : property.transform.up;
            Vector3 flatForward = end - start;
            flatForward.y = 0f;
            flatForward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            Vector3 startNormal = SampleRoadSurfaceNormal(start);
            float startCrossSlope = Mathf.Abs(startNormal.y) > 0.001f
                ? -Vector3.Dot(startNormal, right) / startNormal.y
                : 0f;
            float endCrossSlope = Mathf.Abs(endNormal.y) > 0.001f
                ? -Vector3.Dot(endNormal, right) / endNormal.y
                : 0f;
            float smooth = t * t * (3f - 2f * t);
            float centreHeight = Mathf.Lerp(start.y, end.y, smooth);
            float crossSlope = Mathf.Lerp(startCrossSlope, endCrossSlope, smooth);
            return centreHeight + across * crossSlope;
        }

        private static bool TryGetDrivewayCoordinates(
            GeneratedProperty property,
            Vector3 worldPosition,
            out float t,
            out float across,
            out float length)
        {
            t = 0f;
            across = 0f;
            length = 0f;
            if (property == null || property.ReservedDrivewayWidth <= 0.01f)
            {
                return false;
            }

            Vector3 start = property.DrivewayStart;
            Vector3 segment = property.DrivewayEnd - start;
            segment.y = 0f;
            length = segment.magnitude;
            if (length <= 0.01f)
            {
                return false;
            }

            Vector3 forward = segment / length;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 offset = worldPosition - start;
            offset.y = 0f;
            float distanceAlong = Vector3.Dot(offset, forward);
            if (distanceAlong < -0.001f || distanceAlong > length + 0.001f)
            {
                return false;
            }

            t = Mathf.Clamp01(distanceAlong / length);
            across = Vector3.Dot(offset, right);
            return true;
        }

        private bool TryIntersectRoadSurface(
            Vector2 origin,
            Vector2 direction,
            Vector2 roadCentre,
            out Vector2 intersection)
        {
            float halfRoad = Mathf.Min(
                m_terrainSettings.RoadHalfWidth,
                m_roadTileSize * 0.5f);
            float minimumT = 0f;
            float maximumT = float.PositiveInfinity;
            for (int axis = 0; axis < 2; axis++)
            {
                float originValue = axis == 0 ? origin.x : origin.y;
                float directionValue = axis == 0 ? direction.x : direction.y;
                float centreValue = axis == 0 ? roadCentre.x : roadCentre.y;
                float minimum = centreValue - halfRoad;
                float maximum = centreValue + halfRoad;
                if (Mathf.Abs(directionValue) <= 0.0001f)
                {
                    if (originValue < minimum || originValue > maximum)
                    {
                        intersection = default;
                        return false;
                    }

                    continue;
                }

                float first = (minimum - originValue) / directionValue;
                float second = (maximum - originValue) / directionValue;
                if (first > second)
                {
                    (first, second) = (second, first);
                }

                minimumT = Mathf.Max(minimumT, first);
                maximumT = Mathf.Min(maximumT, second);
                if (minimumT > maximumT)
                {
                    intersection = default;
                    return false;
                }
            }

            if (minimumT <= 0.05f || float.IsInfinity(minimumT))
            {
                intersection = default;
                return false;
            }

            intersection = origin + direction * minimumT;
            return true;
        }

        private void AddSidewalkSidePieces(
            GridCoordinate roadCell,
            Vector3 centre,
            Vector3 outward,
            Vector3 tangent,
            float inner,
            float outer,
            float along,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            ref int pieceIndex)
        {
            List<DrivewaySidewalkCut> cuts = new List<DrivewaySidewalkCut>();
            for (int index = 0; index < m_generatedProperties.Count; index++)
            {
                GeneratedProperty property = m_generatedProperties[index];
                if (property == null || property.RoadCoordinate != roadCell ||
                    !TryCalculateSidewalkCut(
                        property,
                        centre,
                        outward,
                        tangent,
                        inner,
                        outer,
                        along,
                        out DrivewaySidewalkCut cut))
                {
                    continue;
                }

                cuts.Add(cut);
            }

            if (cuts.Count == 0)
            {
                AddSidewalkPiece(
                    centre + outward * inner - tangent * along,
                    centre + outward * inner + tangent * along,
                    centre + outward * outer + tangent * along,
                    centre + outward * outer - tangent * along,
                    vertices,
                    triangles,
                    uvs,
                    ref pieceIndex);
                return;
            }

            cuts.Sort((first, second) =>
                (first.InnerMinimum + first.OuterMinimum).CompareTo(
                    second.InnerMinimum + second.OuterMinimum));
            float previousInner = -along;
            float previousOuter = -along;
            for (int index = 0; index < cuts.Count; index++)
            {
                DrivewaySidewalkCut cut = cuts[index];
                if (cut.InnerMinimum > previousInner + 0.001f &&
                    cut.OuterMinimum > previousOuter + 0.001f)
                {
                    AddSidewalkPiece(
                        centre + outward * inner + tangent * previousInner,
                        centre + outward * inner + tangent * cut.InnerMinimum,
                        centre + outward * outer + tangent * cut.OuterMinimum,
                        centre + outward * outer + tangent * previousOuter,
                        vertices,
                        triangles,
                        uvs,
                        ref pieceIndex);
                }

                previousInner = Mathf.Max(previousInner, cut.InnerMaximum);
                previousOuter = Mathf.Max(previousOuter, cut.OuterMaximum);
            }

            if (previousInner < along - 0.001f && previousOuter < along - 0.001f)
            {
                AddSidewalkPiece(
                    centre + outward * inner + tangent * previousInner,
                    centre + outward * inner + tangent * along,
                    centre + outward * outer + tangent * along,
                    centre + outward * outer + tangent * previousOuter,
                    vertices,
                    triangles,
                    uvs,
                    ref pieceIndex);
            }
        }

        private static bool TryCalculateSidewalkCut(
            GeneratedProperty property,
            Vector3 centre,
            Vector3 outward,
            Vector3 tangent,
            float inner,
            float outer,
            float along,
            out DrivewaySidewalkCut cut)
        {
            Vector3 start = property.DrivewayStart;
            Vector3 segment = property.DrivewayEnd - start;
            segment.y = 0f;
            float length = segment.magnitude;
            if (length <= 0.01f || Vector3.Dot(segment, outward) <= 0.01f)
            {
                cut = default;
                return false;
            }

            Vector3 direction = segment / length;
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            float halfWidth = property.ReservedDrivewayWidth * 0.5f;
            if (!TryCutIntervalAtRadius(
                    start, direction, right, halfWidth, length,
                    centre, outward, tangent, inner,
                    out float innerMinimum, out float innerMaximum) ||
                !TryCutIntervalAtRadius(
                    start, direction, right, halfWidth, length,
                    centre, outward, tangent, outer,
                    out float outerMinimum, out float outerMaximum))
            {
                cut = default;
                return false;
            }

            innerMinimum = Mathf.Clamp(innerMinimum, -along, along);
            innerMaximum = Mathf.Clamp(innerMaximum, -along, along);
            outerMinimum = Mathf.Clamp(outerMinimum, -along, along);
            outerMaximum = Mathf.Clamp(outerMaximum, -along, along);
            if (innerMaximum <= innerMinimum + 0.001f ||
                outerMaximum <= outerMinimum + 0.001f)
            {
                cut = default;
                return false;
            }

            cut = new DrivewaySidewalkCut(
                innerMinimum,
                innerMaximum,
                outerMinimum,
                outerMaximum);
            return true;
        }

        private static bool TryCutIntervalAtRadius(
            Vector3 start,
            Vector3 direction,
            Vector3 right,
            float halfWidth,
            float length,
            Vector3 centre,
            Vector3 outward,
            Vector3 tangent,
            float radius,
            out float minimum,
            out float maximum)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            float directionOutward = Vector3.Dot(direction, outward);
            if (Mathf.Abs(directionOutward) <= 0.001f)
            {
                return false;
            }

            for (int sideIndex = -1; sideIndex <= 1; sideIndex += 2)
            {
                float side = sideIndex * halfWidth;
                float distance = (radius -
                                  Vector3.Dot(start - centre, outward) -
                                  Vector3.Dot(right, outward) * side) /
                                 directionOutward;
                if (distance < -0.01f || distance > length + 0.01f)
                {
                    return false;
                }

                Vector3 point = start + direction * distance + right * side;
                float value = Vector3.Dot(point - centre, tangent);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }

            return minimum <= maximum;
        }

        private static Transform FindNamedDescendant(Transform root, string objectName)
        {
            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                if (child.name == objectName)
                {
                    return child;
                }

                Transform descendant = FindNamedDescendant(child, objectName);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
