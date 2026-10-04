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
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private NetworkObject m_couchPrefab;

        private readonly SyncVar<NetworkObject> m_activeCouch = new();
        private readonly SyncVar<int> m_destinationIndex = new(-1);

        public NetworkObject ActiveCouch => m_activeCouch.Value;
        public int ActiveDestinationIndex => m_destinationIndex.Value;
        public bool HasActiveDelivery => m_destinationIndex.Value >= 0;

        public GeneratedProperty ActiveDestination
        {
            get
            {
                m_generator ??= GetComponent<NeighbourhoodGenerator>();
                IReadOnlyList<GeneratedProperty> properties = m_generator.GeneratedProperties;
                int index = m_destinationIndex.Value;
                return index >= 0 && index < properties.Count ? properties[index] : null;
            }
        }

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
        }

        public void SetCouchPrefab(NetworkObject couchPrefab)
        {
            m_couchPrefab = couchPrefab;
        }

        public bool TryStartDeliveryServer(DeliveryNPC npc, PlayerCouchCarrier player)
        {
            if (!IsServerInitialized || npc == null || player == null || HasActiveDelivery)
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

            // Server RPCs are processed serially. Publishing the destination before spawning
            // prevents a second interaction in the same tick from creating another couch.
            m_destinationIndex.Value = destinationIndex;
            NetworkObject couch = Instantiate(
                m_couchPrefab,
                npc.CouchSpawnPosition,
                npc.CouchSpawnRotation);
            Spawn(couch);
            m_activeCouch.Value = couch;
            return true;
        }

        private int SelectDestinationIndex()
        {
            if (m_generator == null || !m_generator.HasGeneratedNeighbourhood)
            {
                return -1;
            }

            IReadOnlyList<GeneratedProperty> properties = m_generator.GeneratedProperties;
            int validCount = 0;
            for (int index = 0; index < properties.Count; index++)
            {
                if (properties[index] != null && properties[index].DeliveryPoint != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return -1;
            }

            int selectedValidIndex = Random.Range(0, validCount);
            for (int index = 0; index < properties.Count; index++)
            {
                if (properties[index] == null || properties[index].DeliveryPoint == null)
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
    }
}
