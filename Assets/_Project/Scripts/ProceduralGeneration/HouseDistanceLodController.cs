using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Selects a generated house's mesh LOD using world-space camera distance. Unity's normal
    /// LODGroup screen-height calculation keeps large buildings detailed for too long, so houses
    /// use the region's explicit metre thresholds instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HouseDistanceLodController : MonoBehaviour
    {
        private static Camera[] s_cameraBuffer = new Camera[4];
        private static int s_cameraFrame = -1;
        private static int s_cameraCount;

        private RegionDefinition m_region;
        private LODGroup m_lodGroup;
        private int m_currentLod = -1;
        private float m_nextUpdateTime;

        public int CurrentLod => m_currentLod;

        public void Initialise(RegionDefinition region)
        {
            m_region = region;
            m_lodGroup = GetComponentInChildren<LODGroup>(true);
            m_currentLod = -1;
            ScheduleNextUpdate(true);
            UpdateLod(true);
        }

        private void Awake()
        {
            if (m_lodGroup == null)
            {
                m_lodGroup = GetComponentInChildren<LODGroup>(true);
            }
        }

        private void OnEnable()
        {
            ScheduleNextUpdate(true);
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < m_nextUpdateTime)
            {
                return;
            }

            UpdateLod(false);
            ScheduleNextUpdate(false);
        }

        private void OnDisable()
        {
            if (m_lodGroup != null)
            {
                m_lodGroup.ForceLOD(-1);
            }

            m_currentLod = -1;
        }

        private void UpdateLod(bool immediate)
        {
            if (m_lodGroup == null || m_lodGroup.lodCount == 0)
            {
                return;
            }

            Vector3 referencePoint = m_lodGroup.transform.TransformPoint(
                m_lodGroup.localReferencePoint);
            if (!TryGetNearestCameraDistance(referencePoint, gameObject.layer, out float distance))
            {
                m_lodGroup.ForceLOD(-1);
                m_currentLod = -1;
                return;
            }

            HouseLodDistanceSettings settings = m_region != null
                ? m_region.HouseLod
                : HouseLodDistanceSettings.SuburbsDefault;
            settings.Validate();
            int desiredLod = CalculateLod(distance, settings);
            if (!immediate && m_currentLod >= 0 && desiredLod != m_currentLod)
            {
                float adjustedDistance = desiredLod > m_currentLod
                    ? distance - settings.Hysteresis
                    : distance + settings.Hysteresis;
                desiredLod = CalculateLod(adjustedDistance, settings);
            }

            desiredLod = Mathf.Clamp(desiredLod, 0, m_lodGroup.lodCount - 1);
            if (desiredLod == m_currentLod)
            {
                return;
            }

            m_lodGroup.ForceLOD(desiredLod);
            m_currentLod = desiredLod;
        }

        private static int CalculateLod(float distance, HouseLodDistanceSettings settings)
        {
            if (distance < settings.Lod1Distance) return 0;
            if (distance < settings.Lod2Distance) return 1;
            if (distance < settings.Lod3Distance) return 2;
            return 3;
        }

        private void ScheduleNextUpdate(bool stagger)
        {
            HouseLodDistanceSettings settings = m_region != null
                ? m_region.HouseLod
                : HouseLodDistanceSettings.SuburbsDefault;
            settings.Validate();
            float offset = stagger
                ? Mathf.Abs(GetInstanceID() % 100) * 0.01f * settings.UpdateInterval
                : settings.UpdateInterval;
            m_nextUpdateTime = Time.unscaledTime + offset;
        }

        private static bool TryGetNearestCameraDistance(
            Vector3 position,
            int objectLayer,
            out float distance)
        {
            RefreshCameraCache();
            float nearestSqrDistance = float.PositiveInfinity;
            int layerMask = 1 << objectLayer;
            for (int index = 0; index < s_cameraCount; index++)
            {
                Camera camera = s_cameraBuffer[index];
                if (camera == null || !camera.isActiveAndEnabled ||
                    (camera.cullingMask & layerMask) == 0 ||
                    camera.cameraType == CameraType.Preview ||
                    camera.cameraType == CameraType.Reflection)
                {
                    continue;
                }

                float sqrDistance = (camera.transform.position - position).sqrMagnitude;
                nearestSqrDistance = Mathf.Min(nearestSqrDistance, sqrDistance);
            }

            if (float.IsPositiveInfinity(nearestSqrDistance))
            {
                distance = 0f;
                return false;
            }

            distance = Mathf.Sqrt(nearestSqrDistance);
            return true;
        }

        private static void RefreshCameraCache()
        {
            if (s_cameraFrame == Time.frameCount)
            {
                return;
            }

            int cameraCount = Camera.allCamerasCount;
            if (s_cameraBuffer.Length < cameraCount)
            {
                s_cameraBuffer = new Camera[Mathf.NextPowerOfTwo(cameraCount)];
            }

            s_cameraCount = Camera.GetAllCameras(s_cameraBuffer);
            s_cameraFrame = Time.frameCount;
        }
    }
}
