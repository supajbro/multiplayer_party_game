using System;
using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private const float DrivewayBedOffset = 0.025f;
        private const float ModuleGroundClearance = 0.018f;
        private const float ModuleJointScale = 0.985f;

        public int GeneratedDrivewaySlabCount { get; private set; }
        public int GeneratedFootpathSlabCount { get; private set; }
        public int GeneratedPathLightCount { get; private set; }

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
            GeneratedDrivewaySurface = null;
            GeneratedDrivewaySlabCount = 0;
            GeneratedFootpathSlabCount = 0;
            GeneratedPathLightCount = 0;
            if (m_drivewaysRoot == null || m_generatedProperties.Count == 0)
            {
                return;
            }

            for (int propertyIndex = 0; propertyIndex < m_generatedProperties.Count; propertyIndex++)
            {
                GeneratedProperty property = m_generatedProperties[propertyIndex];
                if (property == null || property.ReservedDrivewayWidth <= 0.01f)
                {
                    continue;
                }

                HouseExteriorModuleSet modules = property.GetComponent<HouseExteriorModuleSet>();
                if (modules == null || !modules.IsConfigured)
                {
                    Debug.LogWarning(
                        $"House '{property.name}' has no configured exterior module set; " +
                        "its old generated driveway has intentionally not been recreated.",
                        property);
                    continue;
                }

                CreateModularDriveway(property, modules);
                CreateModularFrontPath(property, modules);
            }
        }

        private void CreateModularDriveway(
            GeneratedProperty property,
            HouseExteriorModuleSet modules)
        {
            Vector3 roadEnd = property.DrivewayStart;
            Vector3 houseEnd = modules.DrivewayAnchor.position;
            Vector3 flatSegment = houseEnd - roadEnd;
            flatSegment.y = 0f;
            float length = flatSegment.magnitude;
            if (length <= 0.05f)
            {
                return;
            }

            Vector3 forward = flatSegment / length;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 templateScale = Abs(modules.DrivewaySlabTemplate.lossyScale);
            float nativeLength = Mathf.Max(0.05f, modules.DrivewaySlabSize.z * templateScale.z);
            float authoredSpacing = Mathf.Max(
                nativeLength,
                modules.DrivewaySlabSpacing * templateScale.z);
            int pieceCount = Mathf.Clamp(Mathf.CeilToInt(length / authoredSpacing), 1, 24);
            float pieceSpacing = length / pieceCount;
            float pieceLength = Mathf.Min(pieceSpacing * 0.94f, nativeLength * 1.2f);
            float targetWidth = property.ReservedDrivewayWidth;
            float nativeWidth = Mathf.Max(0.1f, modules.DrivewaySlabSize.x * templateScale.x);

            for (int index = 0; index < pieceCount; index++)
            {
                float t = (index + 0.5f) / pieceCount;
                Vector3 centre = Vector3.Lerp(roadEnd, houseEnd, t);
                Func<Vector3, float> sampler = point => SampleDrivewayHeightAtPoint(property, point);
                CalculateSurfacePose(
                    centre,
                    forward,
                    right,
                    pieceLength * 0.5f,
                    targetWidth * 0.5f,
                    sampler,
                    out Vector3 position,
                    out Quaternion rotation);
                Vector3 scale = templateScale;
                scale.x *= targetWidth / nativeWidth;
                scale.z *= pieceLength * ModuleJointScale / nativeLength;
                CreateModuleInstance(
                    modules.DrivewaySlabTemplate,
                    $"{property.name}_DrivewaySlab_{index:00}",
                    position,
                    rotation,
                    scale,
                    HouseAccessModuleKind.DrivewaySlab);
                GeneratedDrivewaySlabCount++;
            }
        }

        private void CreateModularFrontPath(
            GeneratedProperty property,
            HouseExteriorModuleSet modules)
        {
            Vector3 houseEnd = modules.FootpathAnchor.position;
            Vector3 towardRoad = property.FrontageDirection;
            towardRoad.y = 0f;
            if (towardRoad.sqrMagnitude <= 0.001f)
            {
                return;
            }

            towardRoad.Normalize();
            Vector3 roadCentre = GridToWorld(property.RoadCoordinate);
            float halfRoad = Mathf.Min(m_terrainSettings.RoadHalfWidth, m_roadTileSize * 0.5f);
            float distanceToOuterSidewalk =
                Vector3.Dot(roadCentre - houseEnd, towardRoad) - halfRoad - m_sidewalkWidth;
            if (distanceToOuterSidewalk <= 0.1f)
            {
                return;
            }

            Vector3 sidewalkEnd = houseEnd + towardRoad * distanceToOuterSidewalk;
            Vector3 templateScale = Abs(modules.FootpathSlabTemplate.lossyScale);
            float nativeLength = Mathf.Max(0.05f, modules.FootpathSlabSize.z * templateScale.z);
            float nativeWidth = Mathf.Max(0.1f, modules.FootpathSlabSize.x * templateScale.x);
            float pathWidth = Mathf.Clamp(nativeWidth, 0.85f, 1.6f);
            float authoredSpacing = Mathf.Max(
                nativeLength * 1.1f,
                modules.FootpathSlabSpacing * templateScale.z);
            int pieceCount = Mathf.Clamp(
                Mathf.CeilToInt(distanceToOuterSidewalk / authoredSpacing),
                1,
                32);
            float pieceSpacing = distanceToOuterSidewalk / pieceCount;
            float pieceLength = Mathf.Min(pieceSpacing * 0.72f, nativeLength * 1.25f);
            Vector3 right = Vector3.Cross(Vector3.up, towardRoad).normalized;
            Func<Vector3, float> sampler = SampleGeneratedTerrainHeight;
            for (int index = 0; index < pieceCount; index++)
            {
                float t = (index + 0.5f) / pieceCount;
                Vector3 centre = Vector3.Lerp(houseEnd, sidewalkEnd, t);
                CalculateSurfacePose(
                    centre,
                    towardRoad,
                    right,
                    pieceLength * 0.5f,
                    pathWidth * 0.5f,
                    sampler,
                    out Vector3 position,
                    out Quaternion rotation);
                Vector3 scale = templateScale;
                scale.x *= pathWidth / nativeWidth;
                scale.z *= pieceLength * ModuleJointScale / nativeLength;
                CreateModuleInstance(
                    modules.FootpathSlabTemplate,
                    $"{property.name}_FootpathSlab_{index:00}",
                    position,
                    rotation,
                    scale,
                    HouseAccessModuleKind.FootpathSlab);
                GeneratedFootpathSlabCount++;
            }

            CreatePathLights(
                property,
                modules,
                houseEnd,
                sidewalkEnd,
                towardRoad,
                right,
                pathWidth,
                sampler);
        }

        private void CreatePathLights(
            GeneratedProperty property,
            HouseExteriorModuleSet modules,
            Vector3 pathStart,
            Vector3 pathEnd,
            Vector3 forward,
            Vector3 right,
            float pathWidth,
            Func<Vector3, float> sampler)
        {
            float length = Vector3.Distance(
                new Vector3(pathStart.x, 0f, pathStart.z),
                new Vector3(pathEnd.x, 0f, pathEnd.z));
            int lightCount = Mathf.Clamp(Mathf.FloorToInt(length / 2.8f), 1, 8);
            Vector3 templateScale = Abs(modules.PathLightTemplate.lossyScale);
            float lightWidth = Mathf.Max(0.1f, modules.PathLightSize.x * templateScale.x);
            float sideOffset = pathWidth * 0.5f + lightWidth * 0.5f + 0.18f;
            for (int index = 0; index < lightCount; index++)
            {
                float t = (index + 1f) / (lightCount + 1f);
                float side = (index & 1) == 0 ? 1f : -1f;
                Vector3 centre = Vector3.Lerp(pathStart, pathEnd, t) + right * sideOffset * side;
                CalculateSurfacePose(
                    centre,
                    forward,
                    right,
                    Mathf.Max(0.08f, modules.PathLightSize.z * templateScale.z * 0.5f),
                    lightWidth * 0.5f,
                    sampler,
                    out Vector3 position,
                    out Quaternion rotation);
                CreateModuleInstance(
                    modules.PathLightTemplate,
                    $"{property.name}_PathLight_{index:00}",
                    position,
                    rotation,
                    templateScale,
                    HouseAccessModuleKind.PathLight);
                GeneratedPathLightCount++;
            }
        }

        private GameObject CreateModuleInstance(
            Transform template,
            string instanceName,
            Vector3 position,
            Quaternion rotation,
            Vector3 worldScale,
            HouseAccessModuleKind kind)
        {
            GameObject instance = Instantiate(template.gameObject);
            instance.name = instanceName;
            instance.transform.SetParent(m_drivewaysRoot, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = worldScale;
            instance.SetActive(true);
            GeneratedHouseAccessModule marker = instance.AddComponent<GeneratedHouseAccessModule>();
            marker.Initialise(kind);
            RemoveCollidersFromHierarchy(instance);

            return instance;
        }

        private static void RemoveCollidersFromHierarchy(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                if (Application.isPlaying)
                {
                    Destroy(colliders[index]);
                }
                else
                {
                    DestroyImmediate(colliders[index]);
                }
            }
        }

        private float SampleDrivewayHeightAtPoint(GeneratedProperty property, Vector3 point)
        {
            if (!TryGetDrivewayCoordinates(property, point, out float t, out float across, out _))
            {
                return SampleGeneratedTerrainHeight(point);
            }

            return SampleDrivewaySurfaceHeight(property, point, t, across) - DrivewayBedOffset;
        }

        private float SampleGeneratedTerrainHeight(Vector3 flatWorldPosition)
        {
            GridCoordinate rightOffset = DirectionOffset(TurnRight(m_startDirection));
            GridCoordinate forwardOffset = DirectionOffset(m_startDirection);
            Vector3 gridRight = new Vector3(rightOffset.X, 0f, rightOffset.Y);
            Vector3 gridForward = new Vector3(forwardOffset.X, 0f, forwardOffset.Y);
            Vector3 offset = flatWorldPosition - m_gridOrigin;
            float localX = Vector3.Dot(offset, gridRight) / m_roadTileSize;
            float localY = Vector3.Dot(offset, gridForward) / m_roadTileSize;
            return m_gridOrigin.y + SampleContinuousTerrainHeight(localX, localY, flatWorldPosition);
        }

        private static void CalculateSurfacePose(
            Vector3 centre,
            Vector3 forward,
            Vector3 right,
            float halfLength,
            float halfWidth,
            Func<Vector3, float> heightSampler,
            out Vector3 position,
            out Quaternion rotation)
        {
            halfLength = Mathf.Max(0.02f, halfLength);
            halfWidth = Mathf.Max(0.02f, halfWidth);
            float backHeight = heightSampler(centre - forward * halfLength);
            float frontHeight = heightSampler(centre + forward * halfLength);
            float leftHeight = heightSampler(centre - right * halfWidth);
            float rightHeight = heightSampler(centre + right * halfWidth);
            float forwardGradient = (frontHeight - backHeight) / (halfLength * 2f);
            float rightGradient = (rightHeight - leftHeight) / (halfWidth * 2f);
            Vector3 normal = (Vector3.up - forward * forwardGradient - right * rightGradient).normalized;
            Vector3 surfaceForward = Vector3.ProjectOnPlane(forward, normal).normalized;
            rotation = Quaternion.LookRotation(surfaceForward, normal);

            float centreHeight = (backHeight + frontHeight + leftHeight + rightHeight) * 0.25f;
            for (int forwardSide = -1; forwardSide <= 1; forwardSide += 2)
            {
                for (int rightSide = -1; rightSide <= 1; rightSide += 2)
                {
                    float along = forwardSide * halfLength;
                    float across = rightSide * halfWidth;
                    Vector3 corner = centre + forward * along + right * across;
                    float requiredCentre = heightSampler(corner) -
                                           forwardGradient * along -
                                           rightGradient * across;
                    centreHeight = Mathf.Max(centreHeight, requiredCentre);
                }
            }

            position = new Vector3(centre.x, centreHeight + ModuleGroundClearance, centre.z);
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(
            Mathf.Abs(value.x),
            Mathf.Abs(value.y),
            Mathf.Abs(value.z));

        private float BlendDrivewayGrades(float terrainHeight, Vector3 flatWorldPosition)
        {
            float result = terrainHeight;
            CollectNearbyPropertyIndices(flatWorldPosition);
            for (int candidateIndex = 0; candidateIndex < m_propertyQueryScratch.Count; candidateIndex++)
            {
                int index = m_propertyQueryScratch[candidateIndex];
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

        private bool TrySampleDrivewaySurface(Vector3 worldPosition, out float height)
        {
            CollectNearbyPropertyIndices(worldPosition);
            for (int candidateIndex = 0; candidateIndex < m_propertyQueryScratch.Count; candidateIndex++)
            {
                int index = m_propertyQueryScratch[candidateIndex];
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
            if (direction.sqrMagnitude <= 0.0001f)
            {
                intersection = default;
                return false;
            }

            direction.Normalize();
            float halfRoad = Mathf.Min(
                m_terrainSettings.RoadHalfWidth,
                m_roadTileSize * 0.5f);
            float distanceToRoadCentre = Vector2.Dot(roadCentre - origin, direction);
            float distanceToNearEdge = distanceToRoadCentre - halfRoad;
            if (distanceToNearEdge <= 0.05f)
            {
                intersection = default;
                return false;
            }

            intersection = origin + direction * distanceToNearEdge;
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
