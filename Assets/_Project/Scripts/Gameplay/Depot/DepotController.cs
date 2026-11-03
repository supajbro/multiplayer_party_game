using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CouchGuys.Gameplay.Depot
{
    public enum DepotDoorKind
    {
        Exit,
        VehicleGarage
    }

    [Serializable]
    public sealed class DepotDoorRig
    {
        [SerializeField] private Transform[] m_slats = Array.Empty<Transform>();
        [SerializeField] private Vector3[] m_closedPositions = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] m_closedRotations = Array.Empty<Quaternion>();
        [SerializeField] private Vector3[] m_openPositions = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] m_openRotations = Array.Empty<Quaternion>();
        [SerializeField] private BoxCollider m_blockingCollider;
        [SerializeField] private BoxCollider m_safetyVolume;
        [SerializeField] private GameObject m_animationRoot;
        [SerializeField] private AnimationClip m_authoredAnimation;

        [NonSerialized] public float Progress;
        [NonSerialized] public bool TargetOpen;
        [NonSerialized] private PlayableGraph m_graph;
        [NonSerialized] private AnimationClipPlayable m_clipPlayable;

        public BoxCollider SafetyVolume => m_safetyVolume;

        public void Dispose()
        {
            if (m_graph.IsValid()) m_graph.Destroy();
        }

        public void Configure(Transform[] slats, Vector3[] closedPositions,
            Quaternion[] closedRotations, Vector3[] openPositions,
            Quaternion[] openRotations, BoxCollider blockingCollider,
            BoxCollider safetyVolume)
        {
            m_slats = slats ?? Array.Empty<Transform>();
            m_closedPositions = closedPositions ?? Array.Empty<Vector3>();
            m_closedRotations = closedRotations ?? Array.Empty<Quaternion>();
            m_openPositions = openPositions ?? Array.Empty<Vector3>();
            m_openRotations = openRotations ?? Array.Empty<Quaternion>();
            m_blockingCollider = blockingCollider;
            m_safetyVolume = safetyVolume;
            Progress = 0f;
            TargetOpen = false;
            ApplyPose();
        }

        public void SetImmediate(bool open)
        {
            TargetOpen = open;
            Progress = open ? 1f : 0f;
            ApplyPose();
        }

        public void Tick(float speed, float deltaTime)
        {
            Progress = Mathf.MoveTowards(Progress, TargetOpen ? 1f : 0f,
                Mathf.Max(0.01f, speed) * deltaTime);
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (m_animationRoot != null && m_authoredAnimation != null)
            {
                // Use the compiled Mecanim clip in players, rather than relying on
                // editor sampling data. Each door has its own binding root so evaluating
                // one door cannot restore the other door's transforms to a default pose.
                if (!m_graph.IsValid())
                {
                    Animator animator = m_animationRoot.GetComponent<Animator>();
                    if (animator == null) animator = m_animationRoot.AddComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    m_graph = PlayableGraph.Create(m_animationRoot.name);
                    m_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    m_clipPlayable = AnimationClipPlayable.Create(m_graph, m_authoredAnimation);
                    m_clipPlayable.SetSpeed(0);
                    m_clipPlayable.SetApplyFootIK(false);
                    AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                        m_graph, "Roller door", animator);
                    output.SetSourcePlayable(m_clipPlayable);
                    m_graph.Play();
                }
                m_clipPlayable.SetTime(m_authoredAnimation.length * Progress);
                m_graph.Evaluate(0f);
            }
            else
            {
                float eased = Progress * Progress * (3f - 2f * Progress);
                int count = Mathf.Min(m_slats.Length,
                    Mathf.Min(m_closedPositions.Length, m_openPositions.Length));
                count = Mathf.Min(count,
                    Mathf.Min(m_closedRotations.Length, m_openRotations.Length));
                for (int index = 0; index < count; index++)
                {
                    Transform slat = m_slats[index];
                    if (slat == null) continue;
                    slat.localPosition = Vector3.LerpUnclamped(
                        m_closedPositions[index], m_openPositions[index], eased);
                    slat.localRotation = Quaternion.SlerpUnclamped(
                        m_closedRotations[index], m_openRotations[index], eased);
                }
            }

            // The animated slats are decorative. One cheap blocker supplies reliable
            // character/vehicle collision and is removed only when passage is clear.
            if (m_blockingCollider != null)
                m_blockingCollider.enabled = Progress < 0.92f;
        }
    }

    /// <summary>
    /// Runtime references and local presentation for the authored depot. Authoritative
    /// delivery and door state remains on DeliveryManager's network object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DepotController : MonoBehaviour
    {
        [Header("Dispatch Markers")]
        [SerializeField] private Transform m_deliveryNpc;
        [SerializeField, Tooltip("Depot-local customer area in front of the dispatcher desk.")]
        private Bounds m_counterInteractionArea;
        [SerializeField, Min(0f), Tooltip("Counter interaction is available only while the dispatcher is near his home marker.")]
        private float m_counterDispatcherDistance = 1.5f;
        [SerializeField] private Transform m_vehicleArrival;
        [Tooltip("Purchased trucks use this transform's position and heading. Local +Z faces the exit.")]
        [SerializeField] private Transform m_truckSpawnPoint;
        [Tooltip("Authored floor used to ground purchased trucks inside a roofed depot.")]
        [SerializeField] private Collider m_vehicleSpawnSurface;
        [SerializeField] private Transform[] m_vehicleFallbacks = Array.Empty<Transform>();
        [SerializeField] private Transform m_couchHiddenSpawn;
        [SerializeField] private Transform[] m_conveyorPath = Array.Empty<Transform>();

        [Header("Conveyor")]
        [SerializeField, Min(0.1f)] private float m_conveyorSpeed = 2.25f;
        [SerializeField, Min(0f)] private float m_arrivalDelay = 0.75f;
        [Tooltip("Forward speed retained when leaving the final belt waypoint. Zero preserves a stationary handoff.")]
        [SerializeField, Min(0f)] private float m_conveyorExitSpeed;
        [SerializeField] private Transform m_couchDropOffPoint;

        [Header("Doors")]
        [SerializeField] private Transform m_entranceDetectionCentre;
        [SerializeField, Min(1f)] private float m_entranceDetectionRadius = 4.5f;
        [SerializeField, Min(0.1f)] private float m_entranceOpeningSpeed = 1.4f;
        [SerializeField, Min(0.1f)] private float m_entranceClosingSpeed = 1f;
        [SerializeField, Min(0f)] private float m_entranceClosingDelay = 1.2f;
        private float m_entranceLastOccupied = float.NegativeInfinity;
        [SerializeField, Min(0.1f)] private float m_doorAnimationSpeed = 0.65f;
        [SerializeField] private DepotDoorRig m_exitDoor = new();
        [SerializeField] private DepotDoorRig m_vehicleGarageDoor = new();
        [SerializeField, Min(1f)] private float m_garageDetectionRadius = 4.5f;
        [SerializeField, Min(0f)] private float m_garageClosingDelay = 2f;
        [SerializeField, Tooltip("Depot-local truck presence bounds; a truck inside keeps the roller door open.")]
        private Bounds m_truckInterior;
        [SerializeField] private DepotDoorButton[] m_doorButtons = Array.Empty<DepotDoorButton>();

        [Header("Local Depot Music")]
        [SerializeField] private AudioSource m_musicSource;
        [SerializeField, Range(0f, 1f)] private float m_musicMaximumVolume = 0.15f;
        [SerializeField, Min(0f)] private float m_musicFadeInDuration = 2.5f;
        [SerializeField, Min(0f)] private float m_musicFadeOutDuration = 2f;
        [SerializeField] private NeighbourhoodMusic m_neighbourhoodMusic;
        private NetworkPlayerOwnership m_musicPlayer;
        private float m_nextMusicPlayerSearch;
        private bool m_musicStarted;
        private bool m_musicPaused;
        private float m_musicLevel;
        private static DepotController s_musicOwner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMusicOwner() => s_musicOwner = null;

        private void Awake()
        {
            if (m_musicSource == null) return;
            m_musicSource.playOnAwake = false;
            m_musicSource.loop = true;
            m_musicSource.spatialBlend = 0f;
            m_musicSource.volume = 0f;
        }

        private void UpdateLocalMusic()
        {
            if (m_musicSource == null && m_neighbourhoodMusic == null) return;
            // Resolve after client ownership/spawning is ready, and reacquire on respawn.
            // Remote characters and server-only players never qualify.
            if (m_musicPlayer == null || !m_musicPlayer.IsOwner ||
                !m_musicPlayer.IsClientInitialized || !m_musicPlayer.gameObject.activeInHierarchy)
            {
                m_musicPlayer = null;
                if (Time.unscaledTime >= m_nextMusicPlayerSearch)
                {
                    m_nextMusicPlayerSearch = Time.unscaledTime + 0.5f;
                    foreach (var player in FindObjectsByType<NetworkPlayerOwnership>(FindObjectsSortMode.None))
                    {
                        if (!player.IsClientInitialized || !player.IsOwner || !player.gameObject.activeInHierarchy) continue;
                        if (player.TryGetComponent<PlayerHealth>(out var health) && health.IsAiTeammate) continue;
                        m_musicPlayer = player;
                        break;
                    }
                }
            }

            // Reuse the authored interior bounds; position testing also covers an
            // initial spawn/teleport inside, without relying on trigger enter events.
            bool inside = m_musicPlayer != null && m_truckInterior.Contains(
                transform.InverseTransformPoint(m_musicPlayer.transform.position));
            // Hold one owner for both tracks, including outside and during music breaks,
            // so overlapping scene loads cannot start another neighbourhood source.
            if (m_musicPlayer != null && s_musicOwner == null) s_musicOwner = this;
            m_neighbourhoodMusic?.TickLocalMusic(m_musicPlayer != null &&
                s_musicOwner == this && !inside);
            if (m_musicSource == null || m_musicSource.clip == null)
            {
                if (m_musicPlayer == null && s_musicOwner == this) s_musicOwner = null;
                return;
            }
            bool audible = inside && s_musicOwner == this;
            float maximum = Mathf.Clamp01(m_musicMaximumVolume);
            float target = audible ? maximum : 0f;
            if (target > 0f)
            {
                if (!m_musicStarted)
                {
                    m_musicSource.Play();
                    m_musicStarted = true;
                }
                else if (m_musicPaused) m_musicSource.UnPause();
                m_musicPaused = false;
            }

            float duration = audible ? m_musicFadeInDuration : m_musicFadeOutDuration;
            m_musicLevel = duration <= 0f ? (audible ? 1f : 0f) : Mathf.MoveTowards(
                m_musicLevel, audible ? 1f : 0f, Time.unscaledDeltaTime / duration);
            m_musicSource.volume = maximum * m_musicLevel;
            if (target == 0f && m_musicSource.volume == 0f)
            {
                PauseMusic();
                if (m_musicPlayer == null && s_musicOwner == this) s_musicOwner = null;
            }
        }

        private void PauseMusic()
        {
            if (m_musicSource == null) return;
            m_musicLevel = 0f;
            m_musicSource.volume = 0f;
            if (m_musicStarted && !m_musicPaused) m_musicSource.Pause();
            m_musicPaused = m_musicStarted;
        }

        private readonly Collider[] m_safetyHits = new Collider[32];
        private readonly Collider[] m_proximityHits = new Collider[64];
        private float m_garageLastOccupied = float.NegativeInfinity;
        private float m_nextGarageCheck;
        private bool m_cachedGarageOpen;

        public bool CanUseDispatcherCounter(Vector3 dispatcherPosition, Vector3 playerPosition)
        {
            return DispatcherCounterDistanceSquared(dispatcherPosition, playerPosition) <= .0001f;
        }

        public float DispatcherCounterDistanceSquared(Vector3 dispatcherPosition, Vector3 playerPosition)
        {
            if (m_deliveryNpc == null || m_counterInteractionArea.size.sqrMagnitude <= 0f ||
                (dispatcherPosition - m_deliveryNpc.position).sqrMagnitude > m_counterDispatcherDistance * m_counterDispatcherDistance)
                return float.PositiveInfinity;
            // Measure reach from the counter, rather than requiring the player's root
            // to enter a strict box. Transform back so scaled/rotated depots use world metres.
            Vector3 closest = m_counterInteractionArea.ClosestPoint(transform.InverseTransformPoint(playerPosition));
            return (playerPosition - transform.TransformPoint(closest)).sqrMagnitude;
        }

        // Called on the server. Reuse the existing door SyncVar; proximity needs no network traffic.
        public bool ShouldOpenAutomaticGarage()
        {
            if (Time.time < m_nextGarageCheck) return m_cachedGarageOpen;
            m_nextGarageCheck = Time.time + .2f;
            bool occupied = false;
            foreach (var truck in TruckController.ActiveTrucks)
            {
                if (truck != null && truck.gameObject.activeInHierarchy &&
                    m_truckInterior.Contains(transform.InverseTransformPoint(truck.transform.position)))
                { occupied = true; break; }
            }
            var volume = m_vehicleGarageDoor?.SafetyVolume;
            if (!occupied && volume != null)
            {
                Vector3 centre = volume.transform.TransformPoint(volume.center);
                centre.y = (m_deliveryNpc != null ? m_deliveryNpc.position.y : transform.position.y) + 1f;
                int count = Physics.OverlapSphereNonAlloc(centre, m_garageDetectionRadius,
                    m_proximityHits, ~0, QueryTriggerInteraction.Collide);
                occupied = count == m_proximityHits.Length;
                for (int i = 0; i < count && !occupied; i++)
                    occupied = m_proximityHits[i] != null && m_proximityHits[i].GetComponentInParent<PlayerCouchCarrier>() != null;
            }
            // DeliveryManager also checks doorway safety every frame for immediate reopening.
            if (occupied || IsDoorwayBlocked(DepotDoorKind.VehicleGarage)) m_garageLastOccupied = Time.time;
            m_cachedGarageOpen = Time.time - m_garageLastOccupied <= m_garageClosingDelay;
            return m_cachedGarageOpen;
        }

        public void HoldGarageDoorOpen()
        {
            m_garageLastOccupied = Time.time;
            m_cachedGarageOpen = true;
            m_nextGarageCheck = Time.time + .2f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(m_counterInteractionArea.center, m_counterInteractionArea.size);
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(m_truckInterior.center, m_truckInterior.size);
        }

        public Transform DeliveryNpcMarker => m_deliveryNpc;
        public Collider VehicleSpawnSurface => m_vehicleSpawnSurface;
        public Transform VehicleArrival => m_truckSpawnPoint != null ? m_truckSpawnPoint : m_vehicleArrival;
        public IReadOnlyList<Transform> VehicleFallbacks => m_vehicleFallbacks;
        public Transform CouchHiddenSpawn => m_couchHiddenSpawn;
        public IReadOnlyList<Transform> ConveyorPath => m_conveyorPath;
        public float ConveyorSpeed => m_conveyorSpeed;
        public float ArrivalDelay => m_arrivalDelay;
        public Transform CouchDropOffPoint => m_couchDropOffPoint;
        public Vector3 ConveyorExitVelocity => m_conveyorPath != null && m_conveyorPath.Length > 0 &&
            m_conveyorPath[m_conveyorPath.Length - 1] != null
            ? m_conveyorPath[m_conveyorPath.Length - 1].forward * m_conveyorExitSpeed : Vector3.zero;
        public bool HasConveyor => m_couchHiddenSpawn != null && m_conveyorPath != null && m_conveyorPath.Length > 0;

        private void Update()
        {
            UpdateLocalMusic();
            m_exitDoor?.Tick(m_exitDoor.TargetOpen ? m_entranceOpeningSpeed : m_entranceClosingSpeed, Time.deltaTime);
            m_vehicleGarageDoor?.Tick(m_doorAnimationSpeed, Time.deltaTime);
        }

        private void OnDisable()
        {
            PauseMusic();
            m_neighbourhoodMusic?.Suspend();
            m_musicPlayer = null;
            if (s_musicOwner == this) s_musicOwner = null;
            ReleaseDoorAnimations();
        }

        private void OnDestroy() => ReleaseDoorAnimations();

        public void ReleaseDoorAnimations()
        {
            m_exitDoor?.Dispose();
            m_vehicleGarageDoor?.Dispose();
        }

        public void ApplyDoorStates(bool exitOpen, bool garageOpen, bool immediate = false)
        {
            ApplyDoorState(m_exitDoor, exitOpen, immediate);
            ApplyDoorState(m_vehicleGarageDoor, garageOpen, immediate);
        }

        // Called only by DeliveryManager on the server; its SyncVar replicates the result.
        public bool ShouldOpenAutomaticEntrance()
        {
            if (m_entranceDetectionCentre == null) return IsDoorwayBlocked(DepotDoorKind.Exit);
            int count = Physics.OverlapSphereNonAlloc(m_entranceDetectionCentre.position,
                m_entranceDetectionRadius, m_safetyHits, ~0, QueryTriggerInteraction.Collide);
            bool occupied = count >= m_safetyHits.Length;
            for (int i = 0; i < count; i++)
                if (m_safetyHits[i] != null &&
                    m_safetyHits[i].GetComponentInParent<PlayerCouchCarrier>() != null)
                    occupied = true;
            if (occupied || IsDoorwayBlocked(DepotDoorKind.Exit))
                m_entranceLastOccupied = Time.time;
            return Time.time - m_entranceLastOccupied <= m_entranceClosingDelay;
        }

        public bool IsDoorwayBlocked(DepotDoorKind kind)
        {
            DepotDoorRig rig = kind == DepotDoorKind.Exit ? m_exitDoor : m_vehicleGarageDoor;
            BoxCollider volume = rig?.SafetyVolume;
            if (volume == null) return false;

            Vector3 centre = volume.transform.TransformPoint(volume.center);
            Vector3 halfExtents = Vector3.Scale(volume.size * 0.5f,
                Abs(volume.transform.lossyScale));
            int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, m_safetyHits,
                volume.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_safetyHits[index];
                if (hit == null || hit == volume || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent<PlayerCouchCarrier>() != null ||
                    hit.GetComponentInParent<TruckController>() != null)
                    return true;
            }
            return count >= m_safetyHits.Length;
        }

        public bool HasNearbyButton(Vector3 playerPosition, float maximumDistance,
            out DepotDoorKind kind)
        {
            kind = DepotDoorKind.Exit;
            float best = maximumDistance * maximumDistance;
            bool found = false;
            foreach (DepotDoorButton button in m_doorButtons)
            {
                if (button == null) continue;
                float distance = (button.transform.position - playerPosition).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                kind = button.DoorKind;
                found = true;
            }
            return found;
        }

        public IEnumerable<Transform> EnumerateVehicleSpawnMarkers()
        {
            if (VehicleArrival != null) yield return VehicleArrival;
            if (m_vehicleFallbacks == null) yield break;
            foreach (Transform marker in m_vehicleFallbacks)
                if (marker != null) yield return marker;
        }

        public void Configure(Transform deliveryNpc, Transform vehicleArrival,
            Transform[] vehicleFallbacks, Transform couchHiddenSpawn,
            Transform[] conveyorPath, DepotDoorRig exitDoor,
            DepotDoorRig vehicleGarageDoor, DepotDoorButton[] doorButtons)
        {
            m_deliveryNpc = deliveryNpc;
            m_vehicleArrival = vehicleArrival;
            m_vehicleFallbacks = vehicleFallbacks ?? Array.Empty<Transform>();
            m_couchHiddenSpawn = couchHiddenSpawn;
            m_conveyorPath = conveyorPath ?? Array.Empty<Transform>();
            m_exitDoor = exitDoor ?? new DepotDoorRig();
            m_vehicleGarageDoor = vehicleGarageDoor ?? new DepotDoorRig();
            m_doorButtons = doorButtons ?? Array.Empty<DepotDoorButton>();
        }

        public void SetTruckSpawnPoint(Transform marker) => m_truckSpawnPoint = marker;

        private static void ApplyDoorState(DepotDoorRig rig, bool open, bool immediate)
        {
            if (rig == null) return;
            if (immediate) rig.SetImmediate(open);
            else rig.TargetOpen = open;
        }

        private static Vector3 Abs(Vector3 value) =>
            new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        public static bool TryConfigureImportedModel(GameObject root, out StartingArea startingArea)
        {
            startingArea = root != null ? root.GetComponent<StartingArea>() : null;
            return startingArea != null && root.GetComponent<DepotController>() != null;
        }
    }
}
