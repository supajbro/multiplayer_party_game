using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Server-authoritative state for the single active couch delivery.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NeighbourhoodGenerator))]
    [DisallowMultipleComponent]
    public sealed class DeliveryManager : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private NetworkObject m_couchPrefab;
        [SerializeField] private GameObject m_collectionMarkerPrefab;

        [Header("Delivery Completion")]
        [SerializeField, Min(0.25f)] private float m_collectionRadius = 2.5f;
        [SerializeField, Min(0)] private int m_rewardPerDelivery = 100;
        [SerializeField, Min(0f)] private float m_markerGroundOffset = 0.035f;

        [Header("Debug/Test Teleport")]
        [SerializeField, Min(0.25f)] private float m_testTeleportOutsideRadius = 1.5f;
        [SerializeField, Min(1f)] private float m_groundProbeHeight = 40f;
        [SerializeField, Min(1f)] private float m_groundProbeDistance = 100f;

        private readonly SyncVar<NetworkObject> m_activeCouch = new();
        private readonly SyncVar<int> m_destinationIndex = new(-1);
        private readonly SyncVar<int> m_currency = new();

        private GameObject m_collectionMarker;
        private Material m_runtimeMarkerMaterial;
        private bool m_isCompletingDelivery;

        public event Action<int> CurrencyChanged;

        public NetworkObject ActiveCouch => m_activeCouch.Value;
        public int ActiveDestinationIndex => m_destinationIndex.Value;
        public int CurrentCurrency => m_currency.Value;
        public bool HasActiveDelivery => m_destinationIndex.Value >= 0 && m_activeCouch.Value != null;

        public DeliveryDestination ActiveDestination
        {
            get
            {
                m_generator ??= GetComponent<NeighbourhoodGenerator>();
                IReadOnlyList<DeliveryDestination> destinations = m_generator.DeliveryDestinations;
                int index = m_destinationIndex.Value;
                return index >= 0 && index < destinations.Count ? destinations[index] : null;
            }
        }

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_destinationIndex.OnChange += OnDestinationIndexChanged;
            m_currency.OnChange += OnCurrencyChanged;
        }

        private void OnDestroy()
        {
            m_destinationIndex.OnChange -= OnDestinationIndexChanged;
            m_currency.OnChange -= OnCurrencyChanged;
            DestroyCollectionMarker();
            if (m_runtimeMarkerMaterial != null)
            {
                Destroy(m_runtimeMarkerMaterial);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshCollectionMarker();
            CurrencyChanged?.Invoke(m_currency.Value);
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_isCompletingDelivery || !HasActiveDelivery ||
                !m_activeCouch.Value.TryGetComponent(out CouchCarryController couch) ||
                couch.ServerCarrierCount <= 0)
            {
                return;
            }

            DeliveryDestination destination = ActiveDestination;
            if (destination == null || !destination.CanReceiveDelivery)
            {
                return;
            }

            Vector3 offset = couch.transform.position - destination.DropPosition.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= m_collectionRadius * m_collectionRadius)
            {
                CompleteDeliveryServer(couch);
            }
        }

        public void SetCouchPrefab(NetworkObject couchPrefab)
        {
            m_couchPrefab = couchPrefab;
        }

        public bool TryStartDeliveryServer(DeliveryNPC npc, PlayerCouchCarrier player)
        {
            if (!IsServerInitialized || npc == null || player == null || HasActiveDelivery ||
                m_destinationIndex.Value >= 0 || m_activeCouch.Value != null)
            {
                return false;
            }

            if (m_couchPrefab == null || m_couchPrefab.GetComponent<CouchCarryController>() == null)
            {
                Debug.LogError("Delivery Manager needs a registered Couch prefab with CouchCarryController.", this);
                return false;
            }

            int destinationIndex = SelectDestinationIndex();
            if (destinationIndex < 0)
            {
                Debug.LogWarning("A delivery could not start because the neighbourhood has no valid delivery points.", this);
                return false;
            }

            m_destinationIndex.Value = destinationIndex;
            NetworkObject couch = Instantiate(m_couchPrefab, npc.CouchSpawnPosition, npc.CouchSpawnRotation);
            Spawn(couch);
            m_activeCouch.Value = couch;
            return true;
        }

        /// <summary>Server-validated temporary test shortcut used by the T input.</summary>
        public bool TryTeleportActiveDeliveryServer(PlayerCouchCarrier requestingPlayer)
        {
            CouchCarryController requestedCouch = requestingPlayer != null
                ? requestingPlayer.GetCarriedCouchServer()
                : null;
            if (!IsServerInitialized || m_isCompletingDelivery || requestedCouch == null ||
                !HasActiveDelivery || requestedCouch.NetworkObject != m_activeCouch.Value ||
                !m_activeCouch.Value.TryGetComponent(out CouchCarryController couch) ||
                couch.ServerCarrierCount <= 0)
            {
                return false;
            }

            DeliveryDestination destination = ActiveDestination;
            if (destination == null || !destination.CanReceiveDelivery)
            {
                return false;
            }

            Vector3 awayFromHouse = Vector3.ProjectOnPlane(
                destination.DropPosition.position - destination.transform.position,
                Vector3.up);
            if (awayFromHouse.sqrMagnitude < 0.01f)
            {
                awayFromHouse = Vector3.ProjectOnPlane(destination.DropPosition.forward, Vector3.up);
            }

            awayFromHouse = awayFromHouse.sqrMagnitude > 0.01f
                ? awayFromHouse.normalized
                : Vector3.forward;
            Vector3 couchPosition = destination.DropPosition.position +
                awayFromHouse * (m_collectionRadius + m_testTeleportOutsideRadius);
            float pivotToBottom = CalculatePivotToBottom(couch);
            couchPosition.y = FindGroundHeight(couchPosition, destination.DropPosition.position.y) + pivotToBottom;
            Quaternion couchRotation = Quaternion.LookRotation(-awayFromHouse, Vector3.up);

            couch.TeleportServer(couchPosition, couchRotation);
            for (int index = 0; index < CouchCarryController.MaximumCarryPoints; index++)
            {
                PlayerCouchCarrier carrier = couch.GetServerCarrier(index);
                CouchCarryPoint point = couch.GetPoint(index);
                if (carrier == null || point == null)
                {
                    continue;
                }

                Vector3 outward = Vector3.ProjectOnPlane(point.transform.position - couchPosition, Vector3.up);
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : -awayFromHouse;
                Vector3 playerPosition = point.transform.position + outward * 0.75f;
                playerPosition.y = FindGroundHeight(
                    playerPosition,
                    destination.DropPosition.position.y,
                    couch.transform);
                Vector3 facing = Vector3.ProjectOnPlane(couchPosition - playerPosition, Vector3.up);
                Quaternion playerRotation = facing.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                    : couchRotation;
                carrier.TeleportWithCouchServer(playerPosition, playerRotation);
            }

            return true;
        }

        private void CompleteDeliveryServer(CouchCarryController couch)
        {
            if (!IsServerInitialized || m_isCompletingDelivery || couch == null ||
                couch.NetworkObject != m_activeCouch.Value)
            {
                return;
            }

            m_isCompletingDelivery = true;
            NetworkObject deliveredCouch = m_activeCouch.Value;
            DeliveryDestination destination = ActiveDestination;
            m_activeCouch.Value = null;
            m_destinationIndex.Value = -1;
            couch.ReleaseAllOccupantsServer();
            int reward = destination != null
                ? Mathf.RoundToInt(m_rewardPerDelivery * destination.RewardModifier)
                : m_rewardPerDelivery;
            m_currency.Value += reward;
            Debug.Log($"Delivery complete. Team currency: {m_currency.Value}", this);
            if (deliveredCouch != null && deliveredCouch.IsSpawned)
            {
                Despawn(deliveredCouch);
            }

            m_isCompletingDelivery = false;
        }

        private int SelectDestinationIndex()
        {
            if (m_generator == null || !m_generator.HasGeneratedNeighbourhood)
            {
                return -1;
            }

            IReadOnlyList<DeliveryDestination> destinations = m_generator.DeliveryDestinations;
            int validCount = 0;
            for (int index = 0; index < destinations.Count; index++)
            {
                if (destinations[index] != null && destinations[index].CanReceiveDelivery)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return -1;
            }

            int selectedValidIndex = UnityEngine.Random.Range(0, validCount);
            for (int index = 0; index < destinations.Count; index++)
            {
                if (destinations[index] == null || !destinations[index].CanReceiveDelivery)
                {
                    continue;
                }

                if (selectedValidIndex-- == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private void OnDestinationIndexChanged(int previous, int next, bool asServer)
        {
            RefreshCollectionMarker();
        }

        private void OnCurrencyChanged(int previous, int next, bool asServer)
        {
            CurrencyChanged?.Invoke(next);
        }

        private void RefreshCollectionMarker()
        {
            DestroyCollectionMarker();
            DeliveryDestination destination = ActiveDestination;
            if (destination == null || !destination.CanReceiveDelivery || m_destinationIndex.Value < 0)
            {
                return;
            }

            Vector3 markerPosition = destination.DropPosition.position + Vector3.up * m_markerGroundOffset;
            if (m_collectionMarkerPrefab != null)
            {
                m_collectionMarker = Instantiate(
                    m_collectionMarkerPrefab,
                    markerPosition,
                    Quaternion.identity,
                    transform);
                m_collectionMarker.name = "ActiveDeliveryCollectionMarker";
                return;
            }

            m_collectionMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m_collectionMarker.name = "ActiveDeliveryCollectionMarker_Runtime";
            m_collectionMarker.transform.SetParent(transform, true);
            m_collectionMarker.transform.SetPositionAndRotation(markerPosition, Quaternion.identity);
            m_collectionMarker.transform.localScale = new Vector3(
                m_collectionRadius * 2f,
                0.025f,
                m_collectionRadius * 2f);
            Destroy(m_collectionMarker.GetComponent<Collider>());

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                m_runtimeMarkerMaterial ??= new Material(shader)
                {
                    name = "Runtime Delivery Marker Material",
                    color = new Color(0.1f, 0.95f, 0.2f, 0.72f)
                };
                m_collectionMarker.GetComponent<Renderer>().sharedMaterial = m_runtimeMarkerMaterial;
            }
        }

        private void DestroyCollectionMarker()
        {
            if (m_collectionMarker != null)
            {
                Destroy(m_collectionMarker);
                m_collectionMarker = null;
            }
        }

        private float FindGroundHeight(
            Vector3 position,
            float fallbackHeight,
            Transform ignoredRoot = null)
        {
            Vector3 origin = position + Vector3.up * m_groundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                m_groundProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float highestGround = float.NegativeInfinity;
            for (int index = 0; index < hits.Length; index++)
            {
                Transform hitTransform = hits[index].collider.transform;
                if (ignoredRoot != null &&
                    (hitTransform == ignoredRoot || hitTransform.IsChildOf(ignoredRoot)))
                {
                    continue;
                }

                highestGround = Mathf.Max(highestGround, hits[index].point.y);
            }

            return float.IsNegativeInfinity(highestGround) ? fallbackHeight : highestGround;
        }

        private static float CalculatePivotToBottom(CouchCarryController couch)
        {
            Collider[] colliders = couch.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                return 0.5f;
            }

            float minimumY = float.PositiveInfinity;
            for (int index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != null && !colliders[index].isTrigger)
                {
                    minimumY = Mathf.Min(minimumY, colliders[index].bounds.min.y);
                }
            }

            return float.IsPositiveInfinity(minimumY)
                ? 0.5f
                : Mathf.Max(0f, couch.transform.position.y - minimumY);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_collectionRadius = Mathf.Max(0.25f, m_collectionRadius);
            m_rewardPerDelivery = Mathf.Max(0, m_rewardPerDelivery);
            m_testTeleportOutsideRadius = Mathf.Max(0.25f, m_testTeleportOutsideRadius);
        }
#endif
    }
}
