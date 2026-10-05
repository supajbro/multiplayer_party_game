using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Instantiates an imported model beneath a non-destructive house wrapper.</summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class GeneratedHouseModel : MonoBehaviour
    {
        [SerializeField] private GameObject m_modelPrefab;
        [SerializeField] private Transform m_visualRoot;

        private void OnEnable()
        {
            if (!Application.isPlaying && !gameObject.scene.IsValid())
            {
                return;
            }

            if (m_modelPrefab == null || m_visualRoot == null || m_visualRoot.childCount > 0)
            {
                return;
            }

            GameObject model = Instantiate(m_modelPrefab, m_visualRoot);
            model.name = m_modelPrefab.name;
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            m_visualRoot.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            CentreVisualPivot();
        }

        public void Configure(GameObject modelPrefab, Transform visualRoot)
        {
            m_modelPrefab = modelPrefab;
            m_visualRoot = visualRoot;
        }

        private void CentreVisualPivot()
        {
            Renderer[] renderers = m_visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            m_visualRoot.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) -
                new Vector3(transform.position.x, transform.position.y, transform.position.z);
        }

    }
}
