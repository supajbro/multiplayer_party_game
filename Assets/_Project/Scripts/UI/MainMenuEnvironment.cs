using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuEnvironment : MonoBehaviour
    {
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private Camera m_menuCamera;

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            Subscribe();
        }

        public void Initialise(NeighbourhoodGenerator generator, Camera menuCamera)
        {
            if (m_generator != null)
            {
                m_generator.RegionGenerated -= FrameNeighbourhood;
            }

            m_generator = generator;
            m_menuCamera = menuCamera;
            Subscribe();
        }

        private void OnDestroy()
        {
            if (m_generator != null)
            {
                m_generator.RegionGenerated -= FrameNeighbourhood;
            }
        }

        private void FrameNeighbourhood(NeighbourhoodGenerator generator)
        {
            if (m_menuCamera == null)
            {
                return;
            }

            Bounds bounds = generator.GeneratedWorldBounds;
            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            Vector3 target = bounds.center + Vector3.up * Mathf.Max(4f, bounds.size.y * 0.12f);
            Vector3 offset = new Vector3(-span * 0.5f, span * 0.58f, -span * 0.68f);
            m_menuCamera.transform.SetPositionAndRotation(
                target + offset,
                Quaternion.LookRotation(target - (target + offset), Vector3.up));
            m_menuCamera.fieldOfView = 43f;
            m_menuCamera.nearClipPlane = 0.3f;
            m_menuCamera.farClipPlane = Mathf.Max(1000f, span * 5f);
        }

        private void Subscribe()
        {
            if (m_generator == null)
            {
                return;
            }

            m_generator.RegionGenerated -= FrameNeighbourhood;
            m_generator.RegionGenerated += FrameNeighbourhood;
        }
    }
}
