using CouchGuys.Gameplay.Delivery;
using UnityEngine;

namespace CouchGuys.Gameplay.Depot
{
    /// <summary>Local presentation only. Saved meshes have top pivots, -Y length and +Z travel.</summary>
    [DisallowMultipleComponent]
    public sealed class ConveyorStripCurtain : MonoBehaviour
    {
        [Header("Saved geometry / local input")]
        [SerializeField] private Transform m_frame;
        [SerializeField] private MeshFilter[] m_strips;
        [SerializeField] private DeliveryManager m_dispenser;
        [Tooltip("Optional override. Otherwise uses the enabled, tagged local gameplay camera.")]
        [SerializeField] private Camera m_gameplayCamera;
        [SerializeField] private Bounds m_localBounds = new Bounds(new Vector3(0, -1.1f, .7f), new Vector3(4, 2.6f, 4.5f));
        [Header("Bending")]
        [SerializeField, Range(0, 85)] private float m_maximumBendAngle = 78;
        [SerializeField, Min(0)] private float m_bendStrength = 1.25f;
        [SerializeField, Min(.01f)] private float m_springStiffness = 48;
        [SerializeField, Min(0)] private float m_springDamping = 8;
        [SerializeField, Min(.01f)] private float m_returnSpeed = 1;
        [SerializeField, Min(.05f)] private float m_couchInteractionDepth = 1.8f;
        [SerializeField, Range(0, 1)] private float m_neighbouringStripInfluence = .18f;
        [Header("Visibility")]
        [SerializeField] private bool m_enableVisibilityOptimisation = true;
        [SerializeField, Min(1)] private float m_maximumSimulationDistance = 45;

        private sealed class Strip
        {
            public MeshFilter filter;
            public Mesh source, mesh;
            public Vector3[] rest, normals, vertices, bentNormals;
            public float length, halfWidth, angle, velocity, x, variation;
            public bool atRest = true;
        }
        private Strip[] m_states;
        private readonly Plane[] m_planes = new Plane[6];
        private float m_nextInputSearch;
        private GameObject m_couch;
        private BoxCollider m_couchShape;

        private void Awake()
        {
#if UNITY_SERVER
            enabled = false;
            return;
#else
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            { enabled = false; return; }
            if (m_frame == null || m_strips == null) { enabled = false; return; }
            m_states = new Strip[m_strips.Length];
            for (int i = 0; i < m_states.Length; i++)
            {
                var filter = m_strips[i];
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                var mesh = Instantiate(filter.sharedMesh);
                mesh.name = filter.sharedMesh.name + " (local curtain)";
                mesh.MarkDynamic();
                var s = new Strip { filter = filter, source = filter.sharedMesh, mesh = mesh,
                    rest = mesh.vertices, normals = mesh.normals, vertices = mesh.vertices, bentNormals = mesh.normals,
                    length = -mesh.bounds.min.y, halfWidth = mesh.bounds.extents.x,
                    x = m_frame.InverseTransformPoint(filter.transform.position).x,
                    variation = .94f + Mathf.Repeat(i * .618034f, 1) * .12f };
                // Conservative static bounds cover every flexed pose; no per-frame bounds recalculation.
                mesh.bounds = new Bounds(new Vector3(0, -s.length * .5f, 0),
                    new Vector3(s.halfWidth * 2 + .1f, s.length * 2 + .1f, s.length * 2 + .1f));
                filter.sharedMesh = mesh;
                m_states[i] = s;
            }
#endif
        }

        private void LateUpdate()
        {
            // These searches are throttled, including while the curtain is off screen.
            if (Time.unscaledTime >= m_nextInputSearch)
            {
                m_nextInputSearch = Time.unscaledTime + 1;
                if (m_gameplayCamera == null || !m_gameplayCamera.isActiveAndEnabled) m_gameplayCamera = Camera.main;
                if (m_dispenser == null) m_dispenser = FindFirstObjectByType<DeliveryManager>();
            }
            if (!IsVisible()) { RestoreIdle(); return; }
            var couch = m_dispenser != null && m_dispenser.IsClientInitialized ? m_dispenser.ActiveCouch : null;
            var couchObject = couch != null ? couch.gameObject : null;
            if (m_couch != couchObject)
            {
                m_couch = couchObject;
                m_couchShape = m_couch != null ? m_couch.GetComponent<BoxCollider>() : null;
            }
            Bounds couchBounds = default;
            bool contact = m_couchShape != null && m_couch.activeInHierarchy;
            if (contact) couchBounds = CouchBoundsInFrame(m_couchShape);
            Simulate(couchBounds, contact, Time.deltaTime);
        }

        private void Simulate(Bounds couchBounds, bool contact, float deltaTime)
        {
            float dt = Mathf.Min(deltaTime, .05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / .008f));
            float h = dt / steps;
            foreach (var s in m_states)
            {
                if (s == null) continue;
                float target = 0;
                if (contact && couchBounds.max.y > -s.length && couchBounds.min.y < 0)
                {
                    // Leading face pushes the hanging material along +Z, the authored belt direction.
                    float front = couchBounds.max.z;
                    float back = couchBounds.min.z;
                    if (front > 0 && back < m_couchInteractionDepth)
                    {
                        float gap = Mathf.Max(couchBounds.min.x - (s.x + s.halfWidth),
                            (s.x - s.halfWidth) - couchBounds.max.x);
                        float overlap = gap <= 0 ? 1 : m_neighbouringStripInfluence * Mathf.Clamp01(1 - gap / .45f);
                        // The trailing edge releases continuously, then the spring settles independently.
                        float release = 1 - Mathf.SmoothStep(0, m_couchInteractionDepth, Mathf.Max(0, back));
                        float push = Mathf.Clamp01(front / Mathf.Max(.1f, s.length * .65f));
                        target = Mathf.Min(m_maximumBendAngle,
                            m_maximumBendAngle * m_bendStrength * overlap * push * release * s.variation);
                    }
                }
                if (target == 0 && s.atRest) continue;
                s.atRest = false;
                float stiffness = Mathf.Clamp(m_springStiffness * s.variation * (target == 0 ? m_returnSpeed : 1), .01f, 400);
                // Implicit damping keeps unusually large Inspector damping values stable.
                for (int j = 0; j < steps; j++)
                {
                    s.velocity = (s.velocity + (target - s.angle) * stiffness * h) / (1 + m_springDamping * h);
                    s.angle += s.velocity * h;
                    s.angle = Mathf.Clamp(s.angle, -m_maximumBendAngle, m_maximumBendAngle);
                }
                if (target == 0 && Mathf.Abs(s.angle) < .025f && Mathf.Abs(s.velocity) < .05f)
                { Reset(s); continue; }
                Deform(s);
            }
        }

        private bool IsVisible()
        {
            if (m_gameplayCamera == null || !m_gameplayCamera.isActiveAndEnabled) return false;
            if (!m_enableVisibilityOptimisation) return true;
            Bounds bounds = TransformBounds(m_frame.localToWorldMatrix, m_localBounds);
            if (bounds.SqrDistance(m_gameplayCamera.transform.position) > m_maximumSimulationDistance * m_maximumSimulationDistance) return false;
            GeometryUtility.CalculateFrustumPlanes(m_gameplayCamera, m_planes);
            return GeometryUtility.TestPlanesAABB(m_planes, bounds);
        }

        private Bounds CouchBoundsInFrame(BoxCollider shape)
        {
            // Read the existing collider's dimensions only. No physics queries or simulation.
            return TransformBounds(m_frame.worldToLocalMatrix * shape.transform.localToWorldMatrix,
                new Bounds(shape.center, shape.size));
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            Vector3 e = local.extents;
            Vector3 x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            return new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }

        private static void Deform(Strip s)
        {
            // Three upper arc sections plus a straight tail. Most flex occurs above the
            // couch's top, so the bottom folds over it instead of curling into its face.
            float bend = s.angle * Mathf.Deg2Rad;
            float section = s.length * .6f / 3;
            for (int i = 0; i < s.rest.Length; i++)
            {
                Vector3 p = s.rest[i];
                float remaining = Mathf.Clamp(-p.y, 0, s.length);
                float y = 0, z = 0, theta = 0;
                for (int part = 0; part < 3 && remaining > 0; part++)
                {
                    float distance = Mathf.Min(section, remaining);
                    float curvature = bend * (3 - part) / (6 * section);
                    float end = theta + curvature * distance;
                    if (Mathf.Abs(curvature) < .00001f) { y -= distance * Mathf.Cos(theta); z += distance * Mathf.Sin(theta); }
                    else { y -= (Mathf.Sin(end) - Mathf.Sin(theta)) / curvature; z += (Mathf.Cos(theta) - Mathf.Cos(end)) / curvature; }
                    theta = end; remaining -= distance;
                }
                y -= remaining * Mathf.Cos(theta);
                z += remaining * Mathf.Sin(theta);
                float sin = Mathf.Sin(theta), cos = Mathf.Cos(theta);
                s.vertices[i] = new Vector3(p.x, y + p.z * sin, z + p.z * cos);
                Vector3 n = s.normals[i];
                s.bentNormals[i] = new Vector3(n.x, n.y * cos + n.z * sin, -n.y * sin + n.z * cos);
            }
            s.mesh.vertices = s.vertices;
            s.mesh.normals = s.bentNormals;
        }

        private static void Reset(Strip s)
        {
            s.angle = s.velocity = 0;
            s.atRest = true;
            s.mesh.vertices = s.rest;
            s.mesh.normals = s.normals;
        }
        private void RestoreIdle()
        {
            if (m_states == null) return;
            foreach (var s in m_states) if (s != null && !s.atRest) Reset(s);
        }
        private void OnDisable() => RestoreIdle();
        private void OnDestroy()
        {
            if (m_states == null) return;
            foreach (var s in m_states)
            {
                if (s == null) continue;
                if (s.filter != null) s.filter.sharedMesh = s.source;
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(s.mesh);
                else Destroy(s.mesh);
#else
                Destroy(s.mesh);
#endif
            }
        }
    }
}
