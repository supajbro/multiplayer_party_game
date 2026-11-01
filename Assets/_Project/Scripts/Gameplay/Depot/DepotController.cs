using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Vehicles;
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
        [SerializeField] private DepotDoorButton[] m_doorButtons = Array.Empty<DepotDoorButton>();

        private readonly Collider[] m_safetyHits = new Collider[32];

        public Transform DeliveryNpcMarker => m_deliveryNpc;
        public Collider VehicleSpawnSurface => m_vehicleSpawnSurface;
        public Transform VehicleArrival => m_truckSpawnPoint != null ? m_truckSpawnPoint : m_vehicleArrival;
        public IReadOnlyList<Transform> VehicleFallbacks => m_vehicleFallbacks;
        public Transform CouchHiddenSpawn => m_couchHiddenSpawn;
        public IReadOnlyList<Transform> ConveyorPath => m_conveyorPath;
        public float ConveyorSpeed => m_conveyorSpeed;
        public float ArrivalDelay => m_arrivalDelay;
        public bool HasConveyor => m_couchHiddenSpawn != null && m_conveyorPath != null && m_conveyorPath.Length > 0;

        private void Update()
        {
            m_exitDoor?.Tick(m_exitDoor.TargetOpen ? m_entranceOpeningSpeed : m_entranceClosingSpeed, Time.deltaTime);
            m_vehicleGarageDoor?.Tick(m_doorAnimationSpeed, Time.deltaTime);
        }

        private void OnDisable() => ReleaseDoorAnimations();

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
