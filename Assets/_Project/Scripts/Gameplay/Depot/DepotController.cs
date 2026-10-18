using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using UnityEngine;

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

        public BoxCollider SafetyVolume => m_safetyVolume;

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
                // Progress is still driven by the existing synchronized door state.
                // Sampling lets the authored Blender curves supply the intermediate
                // rolling motion while preserving immediate state restoration and reversal.
                m_authoredAnimation.SampleAnimation(
                    m_animationRoot, m_authoredAnimation.length * Progress);
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
        [SerializeField] private Transform[] m_vehicleFallbacks = Array.Empty<Transform>();
        [SerializeField] private Transform m_couchHiddenSpawn;
        [SerializeField] private Transform[] m_conveyorPath = Array.Empty<Transform>();

        [Header("Conveyor")]
        [SerializeField, Min(0.1f)] private float m_conveyorSpeed = 2.25f;
        [SerializeField, Min(0f)] private float m_arrivalDelay = 0.75f;

        [Header("Roller Doors")]
        [SerializeField, Min(0.1f)] private float m_doorAnimationSpeed = 0.65f;
        [SerializeField] private DepotDoorRig m_exitDoor = new();
        [SerializeField] private DepotDoorRig m_vehicleGarageDoor = new();
        [SerializeField] private DepotDoorButton[] m_doorButtons = Array.Empty<DepotDoorButton>();

        private readonly Collider[] m_safetyHits = new Collider[32];

        public Transform DeliveryNpcMarker => m_deliveryNpc;
        public Transform VehicleArrival => m_vehicleArrival;
        public IReadOnlyList<Transform> VehicleFallbacks => m_vehicleFallbacks;
        public Transform CouchHiddenSpawn => m_couchHiddenSpawn;
        public IReadOnlyList<Transform> ConveyorPath => m_conveyorPath;
        public float ConveyorSpeed => m_conveyorSpeed;
        public float ArrivalDelay => m_arrivalDelay;
        public bool HasConveyor => m_couchHiddenSpawn != null && m_conveyorPath != null && m_conveyorPath.Length > 0;

        private void Update()
        {
            m_exitDoor?.Tick(m_doorAnimationSpeed, Time.deltaTime);
            m_vehicleGarageDoor?.Tick(m_doorAnimationSpeed, Time.deltaTime);
        }

        public void ApplyDoorStates(bool exitOpen, bool garageOpen, bool immediate = false)
        {
            ApplyDoorState(m_exitDoor, exitOpen, immediate);
            ApplyDoorState(m_vehicleGarageDoor, garageOpen, immediate);
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
                    hit.GetComponentInParent<VanController>() != null)
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
            if (m_vehicleArrival != null) yield return m_vehicleArrival;
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

        private static void ApplyDoorState(DepotDoorRig rig, bool open, bool immediate)
        {
            if (rig == null) return;
            if (immediate) rig.SetImmediate(open);
            else rig.TargetOpen = open;
        }

        private static Vector3 Abs(Vector3 value) =>
            new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        /// <summary>
        /// Runtime-safe fallback for a directly assigned depot model. This also keeps
        /// Game-scene testing functional before/after the editor-authored wrapper prefab
        /// is rebuilt.
        /// </summary>
        public static bool TryConfigureImportedModel(GameObject root,
            out StartingArea startingArea)
        {
            startingArea = null;
            if (root == null || Find(root.transform, "CGD_MARK_Player_InitialSpawn") == null)
                return false;

            Transform roadSource = Find(root.transform, "CGD_MARK_ExitDoor_SuburbsOutside");
            Transform npc = Find(root.transform, "CGD_MARK_DeliveryMan_Frog");
            Transform player = Find(root.transform, "CGD_MARK_Player_InitialSpawn");
            Transform vehicle = Find(root.transform, "CGD_MARK_Truck_RearOffstageArrival");
            Transform couchHidden = Find(root.transform, "CGD_MARK_Couch_OffstageHiddenSpawn");
            if (roadSource == null || npc == null || player == null || vehicle == null || couchHidden == null)
                return false;

            Transform road = CreateRuntimeMarker(root.transform, "RoadConnection", roadSource);
            Transform interiorApproach = Find(root.transform,
                "CGD_MARK_ExitDoor_InteriorApproach");
            Vector3 outward = Vector3.ProjectOnPlane(roadSource.position -
                (interiorApproach != null ? interiorApproach.position : root.transform.position),
                Vector3.up).normalized;
            road.rotation = Quaternion.LookRotation(outward.sqrMagnitude > 0.01f
                ? outward : -root.transform.forward, Vector3.up);
            npc.localScale = Vector3.one;
            Transform npcSpawn = CreateRuntimeMarker(root.transform,
                "DeliveryNpcSpawnPoint", npc);

            Transform spawnRoot = new GameObject("PlayerSpawnPoints").transform;
            spawnRoot.SetParent(root.transform, false);
            Vector3[] offsets =
            {
                new(-1.1f, 0f, -0.8f), new(1.1f, 0f, -0.8f),
                new(-1.1f, 0f, 0.8f), new(1.1f, 0f, 0.8f)
            };
            Transform[] spawns = new Transform[offsets.Length];
            for (int index = 0; index < offsets.Length; index++)
            {
                spawns[index] = CreateRuntimeMarker(spawnRoot,
                    $"PlayerSpawnPoint{index + 1:00}", player);
                spawns[index].position += root.transform.TransformVector(offsets[index]);
                spawns[index].rotation = road.rotation;
            }

            AddRuntimeColliders(root.transform);
            DepotDoorRig garage = CreateRuntimeDoorRig(root.transform, "TruckGarageDoor",
                new Vector3(8.05f, 5.4f, 0.45f));
            DepotDoorRig exit = CreateRuntimeDoorRig(root.transform, "ExitRollerDoor",
                new Vector3(7.85f, 5.4f, 0.45f));
            DepotDoorButton exterior = AddRuntimeButton(root.transform,
                "CGD_INTERACT_ExitDoor_ExteriorButton");
            DepotDoorButton interior = AddRuntimeButton(root.transform,
                "CGD_INTERACT_ExitDoor_InteriorButton");

            Transform[] fallbacks =
            {
                Find(root.transform, "CGD_MARK_Truck_Path_01_InsideRear"),
                Find(root.transform, "CGD_MARK_Truck_Path_02_Centre"),
                Find(root.transform, "CGD_MARK_Truck_DispatchStaging")
            };
            Transform[] conveyor = new Transform[5];
            for (int index = 0; index < conveyor.Length; index++)
                conveyor[index] = Find(root.transform, $"CGD_MARK_Couch_ConveyorPoint_{index}");

            DepotController depot = root.GetComponent<DepotController>() ??
                                    root.AddComponent<DepotController>();
            depot.Configure(npcSpawn, vehicle, fallbacks, couchHidden, conveyor, exit, garage,
                new[] { exterior, interior });
            startingArea = root.GetComponent<StartingArea>() ?? root.AddComponent<StartingArea>();
            startingArea.Initialise(road, spawns, npcSpawn, new Vector2Int(5, 5));
            startingArea.SetDepot(depot);
            return true;
        }

        private static DepotDoorRig CreateRuntimeDoorRig(Transform root, string prefix,
            Vector3 blockerSize)
        {
            Transform[] slats = new Transform[12];
            Vector3[] closedPositions = new Vector3[12];
            Quaternion[] closedRotations = new Quaternion[12];
            Vector3[] openPositions = new Vector3[12];
            Quaternion[] openRotations = new Quaternion[12];
            float top = float.NegativeInfinity;
            for (int index = 0; index < slats.Length; index++)
            {
                slats[index] = Find(root, $"{prefix}_Slat_{index:00}");
                if (slats[index] == null) continue;
                closedPositions[index] = slats[index].localPosition;
                closedRotations[index] = slats[index].localRotation;
                top = Mathf.Max(top, slats[index].position.y);
            }
            Transform animationMarker = Find(root, $"CGD_MARK_{prefix}_AnimationRoot");
            Vector3 inward = animationMarker != null
                ? Vector3.ProjectOnPlane(root.position - animationMarker.position, Vector3.up).normalized
                : root.forward;
            float openHeight = float.IsNegativeInfinity(top) ? root.position.y + 6f : top + 0.84f;
            for (int index = 0; index < slats.Length; index++)
            {
                if (slats[index] == null) continue;
                Vector3 world = slats[index].position;
                world.y = openHeight;
                world += inward * (1.5f + index * 0.445f);
                openPositions[index] = slats[index].parent.InverseTransformPoint(world);
                Quaternion worldRotation = slats[index].rotation * Quaternion.Euler(-90f, 0f, 0f);
                openRotations[index] = Quaternion.Inverse(slats[index].parent.rotation) * worldRotation;
            }

            Vector3 markerPosition = animationMarker != null
                ? animationMarker.position
                : root.position;
            BoxCollider blocker = CreateRuntimeBox(root, $"{prefix}_Collision",
                markerPosition, blockerSize, false);
            string safetyName = prefix == "ExitRollerDoor"
                ? "CGD_MARK_ExitDoor_SafetyVolume"
                : "CGD_MARK_TruckDoor_SafetyVolume";
            Transform safetyMarker = Find(root, safetyName);
            BoxCollider safety = CreateRuntimeBox(root, $"{prefix}_SafetyVolume",
                safetyMarker != null ? safetyMarker.position : markerPosition,
                new Vector3(blockerSize.x, 4.8f, 3.2f), true);
            DepotDoorRig rig = new();
            rig.Configure(slats, closedPositions, closedRotations, openPositions,
                openRotations, blocker, safety);
            return rig;
        }

        private static BoxCollider CreateRuntimeBox(Transform root, string name,
            Vector3 position, Vector3 size, bool trigger)
        {
            GameObject value = new GameObject(name);
            value.transform.SetParent(root, false);
            value.transform.position = position;
            value.transform.rotation = root.rotation;
            BoxCollider collider = value.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = trigger;
            return collider;
        }

        private static void AddRuntimeColliders(Transform root)
        {
            string[] tokens =
            {
                "WarehouseInterior_WalkableFloor", "Foundation_Main", "BrickFront_Solid",
                "PaleWood_Solid", "BelowWindow", "RearWall_", "RealLintel",
                "Backstage_LeftOpaquePartition", "Backstage_FrontWall",
                "Conveyor_VisibilityBaffle", "Conveyor_MainBody", "Conveyor_Guardrail",
                "FrogCounter_Main", "ArrivalServiceTunnel_SideWall", "RearPortal_Bumper",
                "WarehouseCornerSupport", "DispatchSafetyBollard"
            };
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                bool selected = false;
                foreach (string token in tokens)
                    if (filter.name.Contains(token, StringComparison.Ordinal)) { selected = true; break; }
                if (!selected) continue;
                BoxCollider collider = filter.gameObject.AddComponent<BoxCollider>();
                collider.center = filter.sharedMesh.bounds.center;
                collider.size = filter.sharedMesh.bounds.size;
            }
        }

        private static DepotDoorButton AddRuntimeButton(Transform root, string name)
        {
            Transform marker = Find(root, name);
            if (marker == null) return null;
            DepotDoorButton button = marker.GetComponent<DepotDoorButton>() ??
                                     marker.gameObject.AddComponent<DepotDoorButton>();
            button.Configure(DepotDoorKind.Exit);
            return button;
        }

        private static Transform CreateRuntimeMarker(Transform parent, string name,
            Transform source)
        {
            Transform marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.SetPositionAndRotation(source.position, source.rotation);
            return marker;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }

}
