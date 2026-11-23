using System.Collections.Generic;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class JumpingPillow
    {
        [Header("Local Visual Deformation")]
        [SerializeField] private MeshFilter m_jumpingSurface;
        [SerializeField] private bool m_deformationEnabled = true;
        [SerializeField, Min(.1f)] private float m_playerDeformationRadius = 1.35f;
        [SerializeField, Min(0f)] private float m_playerDepression = .16f;
        [SerializeField, Min(0f)] private float m_landingDepressionPerSpeed = .025f;
        [SerializeField, Min(.01f)] private float m_maximumDepression = .65f;
        [SerializeField, Min(0f)] private float m_maximumRebound = .08f;
        [SerializeField, Min(.1f)] private float m_springFrequency = 3f;
        [SerializeField, Range(.1f, .99f)] private float m_springDamping = .65f;
        [SerializeField, Min(.05f)] private float m_impactLifetime = .65f;
        [SerializeField, Min(0f)] private float m_impactSpreadSpeed = 1.5f;
        [SerializeField, Min(.01f)] private float m_playerContactDistance = .25f;

        private struct Load { public Vector3 Point; public float Radius, Depth; }
        private struct Impact { public Vector3 Point; public float Radius, Depth, Start; }
        private sealed class PlayerSample
        {
            public ThirdPersonPlayerController Player;
            public CharacterController Controller;
            public Vector3 PreviousFeet;
            public bool HasPrevious, Contact;
        }
        private readonly List<PlayerSample> m_players = new();
        private readonly List<Load> m_loads = new(32);
        private readonly List<Impact> m_impacts = new(24);
        private float m_nextPlayerScan;
        private Mesh m_deformingMesh, m_standaloneSurface;
        private Vector3[] m_restVertices, m_displayVertices;
        private float[] m_vertexMask, m_vertexClearance, m_offsets, m_springVelocities;
        private bool m_visuallyDisplaced;

        private void Start() => InitialiseDeformation();

        private void InitialiseDeformation()
        {
            if (m_restVertices != null || m_jumpingSurface == null) return;
            CaptureSources();
            m_deformingMesh = m_jumpingSurface.sharedMesh;
            if (System.Array.IndexOf(m_fittedVisuals, m_deformingMesh) < 0)
            {
                m_standaloneSurface = Instantiate(m_deformingMesh);
                m_standaloneSurface.name += " (local deformation)";
                m_deformingMesh = m_standaloneSurface;
                m_jumpingSurface.sharedMesh = m_deformingMesh;
            }
            m_deformingMesh.MarkDynamic();
            m_restVertices = m_deformingMesh.vertices;
            m_displayVertices = (Vector3[])m_restVertices.Clone();
            m_vertexMask = new float[m_restVertices.Length];
            m_vertexClearance = new float[m_restVertices.Length];
            m_offsets = new float[m_restVertices.Length];
            m_springVelocities = new float[m_restVertices.Length];
            var colors = m_deformingMesh.colors;
            var sourceVertices = m_sources[System.Array.IndexOf(m_visualMeshes, m_jumpingSurface)].vertices;
            for (int i = 0; i < m_restVertices.Length; i++)
            {
                m_vertexMask[i] = colors.Length == m_restVertices.Length ? Mathf.Clamp01(colors[i].r) : 0f;
                m_vertexClearance[i] = Mathf.Max(0f,
                    m_jumpingSurface.transform.TransformPoint(sourceVertices[i]).y - transform.position.y - .06f);
            }
        }

        // Includes disabled remote motors: their avatar transforms are already replicated.
        // No owner-only callbacks, RPCs, SyncVars or replicated mesh/impact data.
        private void GatherPlayerLoads()
        {
            if (Time.unscaledTime >= m_nextPlayerScan)
            {
                m_nextPlayerScan = Time.unscaledTime + .25f;
                var players = FindObjectsByType<ThirdPersonPlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                m_players.RemoveAll(p => p.Player == null || !p.Player.gameObject.activeInHierarchy);
                foreach (var player in players)
                {
                    bool known = false;
                    foreach (var sample in m_players) if (sample.Player == player) { known = true; break; }
                    if (!known && player.TryGetComponent(out CharacterController controller))
                        m_players.Add(new PlayerSample { Player = player, Controller = controller });
                }
            }
            foreach (var sample in m_players)
            {
                if (sample.Player == null || !sample.Player.gameObject.activeInHierarchy) continue;
                Vector3 feet = sample.Player.transform.TransformPoint(sample.Controller.center - Vector3.up * sample.Controller.height * .5f);
                float landingSpeed = sample.HasPrevious ? Mathf.Max(0f, (sample.PreviousFeet.y - feet.y) / Mathf.Max(.001f, Time.deltaTime)) : 0f;
                bool contact = TrySurfaceContact(feet, m_playerContactDistance, out var hit) && hit.normal.y >= m_minimumUpNormal;
                if (contact)
                {
                    m_loads.Add(new Load { Point = hit.point, Radius = m_playerDeformationRadius, Depth = m_playerDepression });
                    if (!sample.Contact) AddVisualImpact(hit.point, landingSpeed, m_playerDeformationRadius);
                }
                sample.Contact = contact; sample.PreviousFeet = feet; sample.HasPrevious = true;
            }
        }

        private bool TrySurfaceContact(Vector3 feet, float reach, out RaycastHit hit, float maximumPenetration = .3f)
        {
            hit = default;
            if (m_surfaceCollider == null || !m_surfaceCollider.enabled) return false;
            Bounds bounds = m_surfaceCollider.bounds;
            if (feet.x < bounds.min.x || feet.x > bounds.max.x || feet.z < bounds.min.z || feet.z > bounds.max.z) return false;
            Vector3 origin = new Vector3(feet.x, bounds.max.y + 1f, feet.z);
            return m_surfaceCollider.Raycast(new Ray(origin, Vector3.down), out hit, bounds.size.y + 2f) &&
                feet.y - hit.point.y <= reach && feet.y - hit.point.y >= -maximumPenetration;
        }

        /// <summary>A local landing cue. Observers also infer impacts from replicated avatar motion.</summary>
        public void NotifyPlayerLanding(Vector3 point, float speed) => AddVisualImpact(point, speed, m_playerDeformationRadius);

        private void AddVisualImpact(Vector3 point, float speed, float radius)
        {
            if (!m_deformationEnabled || speed <= .1f) return;
            // Coalesce a locally observed landing and its motor callback in the same frame.
            for (int i = m_impacts.Count - 1; i >= 0; i--)
                if (Time.time - m_impacts[i].Start < .04f && (m_impacts[i].Point - point).sqrMagnitude < .09f) return;
            if (m_impacts.Count >= 24) m_impacts.RemoveAt(0);
            m_impacts.Add(new Impact { Point = point, Radius = radius,
                Depth = Mathf.Min(m_maximumDepression, speed * m_landingDepressionPerSpeed), Start = Time.time });
        }

        private void LateUpdate()
        {
            InitialiseDeformation();
            if (m_restVertices == null) return;
            if (!m_deformationEnabled) { RestoreSurface(); return; }
            m_loads.Clear();
            GatherPlayerLoads();
            foreach (var load in m_vehicleLoads) m_loads.Add(load);
            for (int i = m_impacts.Count - 1; i >= 0; i--)
            {
                var impact = m_impacts[i];
                float age = Time.time - impact.Start;
                if (age >= m_impactLifetime) { m_impacts.RemoveAt(i); continue; }
                float envelope = 1f - age / Mathf.Max(.05f, m_impactLifetime);
                m_loads.Add(new Load { Point = impact.Point, Radius = impact.Radius + age * m_impactSpreadSpeed,
                    Depth = impact.Depth * envelope * envelope });
            }
            if (m_loads.Count == 0 && !m_visuallyDisplaced) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (dt <= 0f) return;
            float w = 2f * Mathf.PI * Mathf.Max(.1f, m_springFrequency);
            float damping = Mathf.Clamp(m_springDamping, .1f, .99f);
            float a = damping * w, wd = w * Mathf.Sqrt(1f - damping * damping);
            float decay = Mathf.Exp(-a * dt), c = Mathf.Cos(wd * dt), t = Mathf.Sin(wd * dt) / wd;
            Vector3 localUp = m_jumpingSurface.transform.InverseTransformVector(Vector3.up);
            bool moving = false;
            for (int i = 0; i < m_restVertices.Length; i++)
            {
                if (m_vertexMask[i] <= 0f) continue;
                Vector3 world = m_jumpingSurface.transform.TransformPoint(m_restVertices[i]);
                float depth = 0f;
                foreach (var load in m_loads)
                {
                    float dx = world.x - load.Point.x, dz = world.z - load.Point.z;
                    float q = (dx * dx + dz * dz) / Mathf.Max(.01f, load.Radius * load.Radius);
                    if (q < 1f) depth += load.Depth * (1f - q) * (1f - q);
                }
                float limit = Mathf.Min(m_maximumDepression * m_vertexMask[i], m_vertexClearance[i]);
                float target = -Mathf.Min(limit, depth * m_vertexMask[i]);
                float y = m_offsets[i] - target, v = m_springVelocities[i];
                float next = target + decay * (y * c + (v + a * y) * t);
                float velocity = decay * (v * c - (a * v + w * w * y) * t);
                float clamped = Mathf.Clamp(next, -limit, m_maximumRebound * m_vertexMask[i]);
                if (clamped != next) velocity = 0f;
                if (depth == 0f && Mathf.Abs(clamped) < .0001f && Mathf.Abs(velocity) < .001f) { clamped = 0f; velocity = 0f; }
                m_offsets[i] = clamped; m_springVelocities[i] = velocity;
                moving |= clamped != 0f || velocity != 0f;
                m_displayVertices[i] = m_restVertices[i] + localUp * clamped;
            }
            m_deformingMesh.vertices = m_displayVertices;
            m_deformingMesh.RecalculateNormals();
            m_deformingMesh.RecalculateBounds();
            m_visuallyDisplaced = moving;
        }

        private void RestoreSurface()
        {
            if (m_restVertices == null || !m_visuallyDisplaced) return;
            m_deformingMesh.vertices = m_restVertices;
            m_deformingMesh.RecalculateNormals(); m_deformingMesh.RecalculateBounds();
            System.Array.Clear(m_offsets, 0, m_offsets.Length);
            System.Array.Clear(m_springVelocities, 0, m_springVelocities.Length);
            m_visuallyDisplaced = false;
        }

        private void OnDisable()
        {
            RestoreSurface(); m_impacts.Clear(); m_vehicleLoads.Clear();
            foreach (var sample in m_players) { sample.Contact = false; sample.HasPrevious = false; }
        }

        private void ReleaseDeformation()
        {
            RestoreSurface();
            if (m_standaloneSurface != null)
            {
                int index = System.Array.IndexOf(m_visualMeshes, m_jumpingSurface);
                if (index >= 0) m_jumpingSurface.sharedMesh = m_sources[index];
                Release(m_standaloneSurface);
            }
            m_standaloneSurface = null; m_deformingMesh = null;
            m_restVertices = null; m_impacts.Clear(); m_vehicleLoads.Clear();
        }
    }
}
