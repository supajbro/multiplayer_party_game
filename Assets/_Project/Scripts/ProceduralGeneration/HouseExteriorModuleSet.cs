using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public enum HouseAccessModuleKind
    {
        DrivewaySlab,
        FootpathSlab,
        PathLight
    }

    /// <summary>Identifies a generated module for validation and runtime inspection.</summary>
    public sealed class GeneratedHouseAccessModule : MonoBehaviour
    {
        [SerializeField] private HouseAccessModuleKind m_kind;

        public HouseAccessModuleKind Kind => m_kind;

        public void Initialise(HouseAccessModuleKind kind) => m_kind = kind;
    }

    /// <summary>
    /// Reusable exterior pieces authored as part of a house model. The template objects stay
    /// disabled beneath the house and are cloned by the neighbourhood generator after the lot
    /// and terrain have been resolved.
    /// </summary>
    public sealed class HouseExteriorModuleSet : MonoBehaviour
    {
        [SerializeField] private Transform m_drivewaySlabTemplate;
        [SerializeField] private Transform m_footpathSlabTemplate;
        [SerializeField] private Transform m_pathLightTemplate;
        [SerializeField] private Transform m_drivewayAnchor;
        [SerializeField] private Transform m_footpathAnchor;
        [SerializeField] private Vector3 m_drivewaySlabSize = new Vector3(3.5f, 0.16f, 1.4f);
        [SerializeField] private Vector3 m_footpathSlabSize = new Vector3(1.25f, 0.16f, 0.7f);
        [SerializeField] private Vector3 m_pathLightSize = new Vector3(0.3f, 1f, 0.3f);
        [SerializeField] private float m_drivewaySlabSpacing = 1.45f;
        [SerializeField] private float m_footpathSlabSpacing = 0.72f;
        [SerializeField] private int m_configurationVersion;

        public Transform DrivewaySlabTemplate => m_drivewaySlabTemplate;
        public Transform FootpathSlabTemplate => m_footpathSlabTemplate;
        public Transform PathLightTemplate => m_pathLightTemplate;
        public Transform DrivewayAnchor => m_drivewayAnchor;
        public Transform FootpathAnchor => m_footpathAnchor;
        public Vector3 DrivewaySlabSize => m_drivewaySlabSize;
        public Vector3 FootpathSlabSize => m_footpathSlabSize;
        public Vector3 PathLightSize => m_pathLightSize;
        public float DrivewaySlabSpacing => m_drivewaySlabSpacing;
        public float FootpathSlabSpacing => m_footpathSlabSpacing;
        public int ConfigurationVersion => m_configurationVersion;

        public bool IsConfigured =>
            m_drivewaySlabTemplate != null &&
            m_footpathSlabTemplate != null &&
            m_pathLightTemplate != null &&
            m_drivewayAnchor != null &&
            m_footpathAnchor != null;

        public void Configure(
            Transform drivewaySlabTemplate,
            Transform footpathSlabTemplate,
            Transform pathLightTemplate,
            Transform drivewayAnchor,
            Transform footpathAnchor,
            Vector3 drivewaySlabSize,
            Vector3 footpathSlabSize,
            Vector3 pathLightSize,
            float drivewaySlabSpacing,
            float footpathSlabSpacing)
        {
            m_drivewaySlabTemplate = drivewaySlabTemplate;
            m_footpathSlabTemplate = footpathSlabTemplate;
            m_pathLightTemplate = pathLightTemplate;
            m_drivewayAnchor = drivewayAnchor;
            m_footpathAnchor = footpathAnchor;
            m_drivewaySlabSize = drivewaySlabSize;
            m_footpathSlabSize = footpathSlabSize;
            m_pathLightSize = pathLightSize;
            m_drivewaySlabSpacing = Mathf.Max(0.05f, drivewaySlabSpacing);
            m_footpathSlabSpacing = Mathf.Max(0.05f, footpathSlabSpacing);
            m_configurationVersion = 2;
        }
    }
}
