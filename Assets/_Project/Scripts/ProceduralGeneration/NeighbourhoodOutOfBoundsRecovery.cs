using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Player;
using FishNet;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Server-only safety net behind the physical perimeter. It remembers grounded,
    /// in-bounds poses and recovers players and the networked delivery couch together.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodOutOfBoundsRecovery : MonoBehaviour
    {
        private readonly Dictionary<int, Pose> m_playerSafePoses = new Dictionary<int, Pose>();
        private readonly Dictionary<int, Pose> m_couchSafePoses = new Dictionary<int, Pose>();
        private readonly Dictionary<int, Pose> m_sledSafePoses = new Dictionary<int, Pose>();
        private NeighbourhoodGenerator m_generator;
        private Rect m_localBounds;
        private float m_fallHeight;
        private float m_nextScanTime;
        private float m_nextObjectRefreshTime;
        private PlayerCouchCarrier[] m_players = System.Array.Empty<PlayerCouchCarrier>();
        private CouchCarryController[] m_couches = System.Array.Empty<CouchCarryController>();
        private SledController[] m_sleds = System.Array.Empty<SledController>();

        public void Initialise(
            NeighbourhoodGenerator generator,
            Rect localBounds,
            float fallRecoveryDepth)
        {
            m_generator = generator;
            m_localBounds = localBounds;
            Renderer terrainRenderer = generator.GeneratedTerrain != null
                ? generator.GeneratedTerrain.GetComponent<Renderer>()
                : null;
            m_fallHeight = (terrainRenderer != null
                ? terrainRenderer.bounds.min.y
                : generator.transform.position.y) - fallRecoveryDepth;
        }

        private void FixedUpdate()
        {
            if (m_generator == null || !m_generator.IsGenerationReady ||
                !InstanceFinder.IsServerStarted || Time.unscaledTime < m_nextScanTime)
            {
                return;
            }

            m_nextScanTime = Time.unscaledTime + 0.1f;
            RefreshTrackedObjects();
            RecoverSleds();
            RecoverCouches();
            RecoverPlayers();
        }

        private void RefreshTrackedObjects()
        {
            if (Time.unscaledTime < m_nextObjectRefreshTime) return;
            m_nextObjectRefreshTime = Time.unscaledTime + 1f;
            m_couches = FindObjectsByType<CouchCarryController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            m_players = FindObjectsByType<PlayerCouchCarrier>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            m_sleds = FindObjectsByType<SledController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        }

        private void RecoverSleds()
        {
            for (int index = 0; index < m_sleds.Length; index++)
            {
                SledController sled = m_sleds[index];
                if (sled == null || !sled.IsSpawned) continue;
                int id = sled.GetInstanceID();
                bool inBounds = IsInBounds(sled.transform.position);
                if (inBounds && IsGrounded(sled.transform.position, 2.5f))
                    m_sledSafePoses[id] = new Pose(sled.transform.position, sled.transform.rotation);
                if (inBounds && sled.transform.position.y >= m_fallHeight) continue;

                Pose safePose = m_sledSafePoses.TryGetValue(id, out Pose saved)
                    ? saved
                    : GetFallbackPose(0.45f);
                PlayerCouchCarrier[] carriers = new PlayerCouchCarrier[SledController.MaximumGrabPoints];
                Vector3[] offsets = new Vector3[SledController.MaximumGrabPoints];
                for (int point = 0; point < carriers.Length; point++)
                {
                    carriers[point] = sled.GetServerCarrier(point);
                    if (carriers[point] != null)
                        offsets[point] = carriers[point].transform.position - sled.transform.position;
                }
                sled.TeleportServer(safePose.position, safePose.rotation);
                for (int point = 0; point < carriers.Length; point++)
                {
                    if (carriers[point] == null) continue;
                    Vector3 offset = Vector3.ClampMagnitude(
                        Vector3.ProjectOnPlane(offsets[point], Vector3.up), 2.5f);
                    carriers[point].TeleportWithCouchServer(
                        safePose.position + offset + Vector3.up * 0.1f,
                        carriers[point].transform.rotation);
                }
            }
        }

        private void RecoverCouches()
        {
            for (int index = 0; index < m_couches.Length; index++)
            {
                CouchCarryController couch = m_couches[index];
                if (couch == null || !couch.IsSpawned) continue;
                int id = couch.GetInstanceID();
                bool inBounds = IsInBounds(couch.transform.position);
                if (inBounds && IsGrounded(couch.transform.position, 2.5f))
                {
                    m_couchSafePoses[id] = new Pose(couch.transform.position, couch.transform.rotation);
                }

                if (inBounds && couch.transform.position.y >= m_fallHeight) continue;
                Pose safePose = m_couchSafePoses.TryGetValue(id, out Pose saved)
                    ? saved
                    : GetFallbackPose(1.25f);
                RecoverCouchAndCarriers(couch, safePose);
            }
        }

        private void RecoverPlayers()
        {
            for (int index = 0; index < m_players.Length; index++)
            {
                PlayerCouchCarrier player = m_players[index];
                if (player == null || !player.IsSpawned) continue;
                int id = player.GetInstanceID();
                bool inBounds = IsInBounds(player.transform.position);
                ThirdPersonPlayerController controller =
                    player.GetComponent<ThirdPersonPlayerController>();
                if (inBounds && controller != null && controller.IsGrounded)
                {
                    m_playerSafePoses[id] = new Pose(player.transform.position, player.transform.rotation);
                }

                if (inBounds && player.transform.position.y >= m_fallHeight) continue;
                Pose safePose = m_playerSafePoses.TryGetValue(id, out Pose saved)
                    ? saved
                    : GetFallbackPose(0.25f);
                player.TeleportWithCouchServer(safePose.position, safePose.rotation);
            }
        }

        private void RecoverCouchAndCarriers(CouchCarryController couch, Pose safePose)
        {
            PlayerCouchCarrier[] carriers = new PlayerCouchCarrier[CouchCarryController.MaximumCarryPoints];
            Vector3[] offsets = new Vector3[CouchCarryController.MaximumCarryPoints];
            for (int index = 0; index < carriers.Length; index++)
            {
                carriers[index] = couch.GetServerCarrier(index);
                if (carriers[index] != null)
                    offsets[index] = carriers[index].transform.position - couch.transform.position;
            }

            couch.TeleportServer(safePose.position, safePose.rotation);
            for (int index = 0; index < carriers.Length; index++)
            {
                PlayerCouchCarrier carrier = carriers[index];
                if (carrier == null) continue;
                Vector3 offset = Vector3.ClampMagnitude(
                    Vector3.ProjectOnPlane(offsets[index], Vector3.up),
                    2.5f);
                carrier.TeleportWithCouchServer(
                    safePose.position + offset + Vector3.up * 0.1f,
                    carrier.transform.rotation);
            }
        }

        private bool IsInBounds(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return m_localBounds.Contains(new Vector2(local.x, local.z));
        }

        private static bool IsGrounded(Vector3 position, float distance) =>
            Physics.Raycast(
                position + Vector3.up * 0.25f,
                Vector3.down,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

        private Pose GetFallbackPose(float heightOffset)
        {
            StartingArea startingArea = m_generator.GeneratedStartingArea;
            if (startingArea != null && startingArea.PlayerSpawnPoints != null)
            {
                for (int index = 0; index < startingArea.PlayerSpawnPoints.Length; index++)
                {
                    Transform spawn = startingArea.PlayerSpawnPoints[index];
                    if (spawn != null)
                        return new Pose(spawn.position + Vector3.up * heightOffset, spawn.rotation);
                }
            }

            Vector3 centre = m_generator.PlayableWorldBounds.center;
            centre.y = m_generator.SampleTerrainHeight(centre) + heightOffset;
            return new Pose(centre, Quaternion.identity);
        }
    }
}
