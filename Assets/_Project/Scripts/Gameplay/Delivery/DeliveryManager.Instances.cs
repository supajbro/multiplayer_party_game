using System;
using System.Collections;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public sealed partial class DeliveryManager
    {
        private readonly SyncList<DeliveryInstance> m_deliveries = new();
        public IReadOnlyList<DeliveryInstance> Deliveries => m_deliveries;
        public event Action DeliveriesChanged;

        private sealed class JobRuntime
        {
            public Coroutine Dispatch;
            public PedestrianController Recipient;
            public GetawayTruckController Getaway;
            public float GetawaySpawnTime;
            public bool Replacement;
        }

        private readonly Dictionary<int, JobRuntime> m_jobs = new();
        private int m_nextDeliveryId;
        private int m_dispatchingId;
        [SerializeField, Min(0.5f)] private float m_dispenseClearance = 4f;

        private void OnDeliveriesChanged(SyncListOperation operation, int index,
            DeliveryInstance previous, DeliveryInstance next, bool asServer) => DeliveriesChanged?.Invoke();

        public bool TryGetDelivery(int id, out DeliveryInstance delivery)
        {
            foreach (DeliveryInstance job in m_deliveries)
                if (job.Id == id) { delivery = job; return true; }
            delivery = default;
            return false;
        }

        public bool IsDeliveryCouch(CouchCarryController couch) => couch != null &&
            TryGetDelivery(couch.DeliveryId, out DeliveryInstance job) && job.Couch == couch.NetworkObject;

        public DeliveryDestination GetDestination(int index)
        {
            m_generator ??= GetComponent<CouchGuys.ProceduralGeneration.NeighbourhoodGenerator>();
            var destinations = m_generator.DeliveryDestinations;
            return index >= 0 && index < destinations.Count ? destinations[index] : null;
        }

        public DeliveryDestination GetDestinationForCouch(CouchCarryController couch) =>
            couch != null && TryGetDelivery(couch.DeliveryId, out DeliveryInstance job) && job.Couch == couch.NetworkObject
                ? GetDestination(job.DestinationIndex) : null;

        public float GetRemainingTime(DeliveryInstance job)
        {
            if (TimeManager == null || job.DeadlineTick == 0) return 0f;
            uint now = TimeManager.Tick;
            return now >= job.DeadlineTick ? 0f : (float)TimeManager.TicksToTime(job.DeadlineTick - now);
        }

        public NetworkObject FindNearestDeliveryCouch(Vector3 position)
        {
            NetworkObject result = null;
            float nearest = float.PositiveInfinity;
            foreach (var job in m_deliveries)
            {
                if (job.Status != DeliveryState.Transport || job.Couch == null || !job.Couch.IsSpawned) continue;
                float distance = (job.Couch.transform.position - position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; result = job.Couch; }
            }
            return result;
        }

        private void BeginDeliveryServer(int tierIndex, int destinationIndex, DeliveryNPC npc)
        {
            var tier = GetTierConfig(tierIndex);
            var destination = GetDestination(destinationIndex);
            int id = ++m_nextDeliveryId;
            m_attemptNumber.Value++;
            m_usedDestinationIndices.Add(destinationIndex);
            float duration = tier.CalculateDuration(destination);
            var job = new DeliveryInstance
            {
                Id = id, TierIndex = tierIndex, DestinationIndex = destinationIndex, CouchObjectId = -1,
                Duration = duration, DeadlineTick = TimeManager.Tick + Math.Max(1u, TimeManager.TimeToTicks(duration)),
                Status = DeliveryState.Accepted, Reward = Mathf.RoundToInt(tier.Reward * destination.RewardModifier)
            };
            var runtime = new JobRuntime { GetawaySpawnTime = float.PositiveInfinity };
            m_jobs.Add(id, runtime);
            m_deliveries.Add(job);
            CreateRecipientServer(job, runtime);
            ResolveDepot();
            runtime.Dispatch = StartCoroutine(DispenseDeliveryServer(id, npc.CouchSpawnPosition, npc.CouchSpawnRotation));
            SetStateServer(DeliveryState.Transport, DeliveryObjectiveStep.DeliverCouch);
        }

        private bool DispenseAreaClear(Vector3 position)
        {
            // Check every couch, including couches moved by players, vehicles and thieves.
            foreach (var job in m_deliveries)
                if (job.Couch != null && (job.Couch.transform.position - position).sqrMagnitude <
                    m_dispenseClearance * m_dispenseClearance) return false;
            foreach (Collider hit in Physics.OverlapSphere(position, m_dispenseClearance * 0.5f,
                         ~0, QueryTriggerInteraction.Ignore))
                if (hit.GetComponentInParent<CouchCarryController>() != null) return false;
            return true;
        }

        private IEnumerator DispenseDeliveryServer(int id, Vector3 fallbackPosition, Quaternion fallbackRotation)
        {
            // Reservations and deadlines already exist while a blocked dispenser waits.
            while (m_dispatchingId != 0) yield return new WaitForSeconds(0.2f);
            m_dispatchingId = id;
            bool conveyor = m_depot != null && m_depot.HasConveyor;
            Vector3 position = conveyor ? m_depot.CouchHiddenSpawn.position : fallbackPosition;
            Quaternion rotation = conveyor ? m_depot.CouchHiddenSpawn.rotation : fallbackRotation;
            Vector3 exit = conveyor && m_depot.ConveyorPath.Count > 0
                ? m_depot.ConveyorPath[m_depot.ConveyorPath.Count - 1].position : position;
            while (!DispenseAreaClear(position) || !DispenseAreaClear(exit))
                yield return new WaitForSeconds(0.2f);
            if (!TryGetDelivery(id, out var job)) { m_dispatchingId = 0; yield break; }
            NetworkObject instance = Instantiate(m_couchPrefab, position, rotation);
            CouchCarryController couch = instance.GetComponent<CouchCarryController>();
            Spawn(instance);
            couch.AssignDeliveryServer(id);
            couch.SelectRandomModelVariantServer();
            job.CouchObjectId = instance.ObjectId;
            job.Status = conveyor ? DeliveryState.CouchSpawned : DeliveryState.Transport;
            SetDelivery(job);
            if (conveyor)
            {
                couch.SetDepotTransportActiveServer(true);
                if (m_depot.ArrivalDelay > 0f) yield return new WaitForSeconds(m_depot.ArrivalDelay);
                var motion = MoveCouchAlongConveyor(couch.CouchRigidbody, m_depot.ConveyorPath, m_depot.ConveyorSpeed);
                while (couch != null && couch.IsSpawned && motion.MoveNext()) yield return motion.Current;
                if (couch != null && couch.IsSpawned)
                    couch.ReleaseDepotTransportServer(m_depot != null ? m_depot.ConveyorExitVelocity : Vector3.zero);
                job.Status = DeliveryState.Transport;
                SetDelivery(job);
            }
            if (m_jobs.TryGetValue(id, out var runtime))
            {
                runtime.Dispatch = null;
                runtime.GetawaySpawnTime = Time.time;
            }
            m_dispatchingId = 0;
        }

        private void SetDelivery(DeliveryInstance job)
        {
            for (int i = 0; i < m_deliveries.Count; i++)
                if (m_deliveries[i].Id == job.Id) { m_deliveries[i] = job; break; }
        }

        private void CreateRecipientServer(DeliveryInstance job, JobRuntime runtime)
        {
            var settings = Resources.Load<PedestrianSettings>("Pedestrians/PedestrianSettings");
            var prefab = Resources.Load<NetworkObject>($"Pedestrians/Pedestrian_{UnityEngine.Random.Range(1, 11):00}");
            var destination = GetDestination(job.DestinationIndex);
            if (settings == null || prefab == null || destination == null) return;
            Transform door = destination.DropPosition;
            var recipient = Instantiate(prefab, door.position, door.rotation);
            runtime.Recipient = recipient.GetComponent<PedestrianController>();
            runtime.Recipient.InitialiseRecipientServer(this, settings);
            Spawn(recipient);
            m_recipients.RemoveAll(recipient => recipient == null || !recipient.IsSpawned);
            m_recipients.Add(runtime.Recipient);
        }

        private void UpdateDeliveriesServer()
        {
            for (int i = m_deliveries.Count - 1; i >= 0; i--)
            {
                var job = m_deliveries[i];
                if (GetRemainingTime(job) <= 0f ||
                    (job.Status != DeliveryState.Accepted && (job.Couch == null || !job.Couch.IsSpawned)))
                { ResolveDeliveryServer(job.Id, false); continue; }
                if (job.Status == DeliveryState.Transport && m_jobs.TryGetValue(job.Id, out var runtime))
                    UpdateGetawayServer(job, runtime);
            }
        }

        [Server]
        public bool ResolveDeliveryServer(int id, bool success)
        {
            if (!IsServerInitialized || !TryGetDelivery(id, out var job)) return false;
            bool vehicleDelivery = false;
            // Success is only committed by the server's proximity/identity validation.
            if (success && (job.Status != DeliveryState.Transport || job.Couch == null ||
                !job.Couch.IsSpawned || GetRemainingTime(job) <= 0f ||
                !job.Couch.TryGetComponent(out CouchCarryController candidate) || candidate.DeliveryId != id ||
                !CanReceiveCouch(job, candidate, GetDestination(job.DestinationIndex), out vehicleDelivery))) return false;
            for (int i = 0; i < m_deliveries.Count; i++)
                if (m_deliveries[i].Id == id) { m_deliveries.RemoveAt(i); break; }
            m_usedDestinationIndices.Remove(job.DestinationIndex);
            if (m_jobs.Remove(id, out var runtime))
            {
                if (runtime.Dispatch != null) StopCoroutine(runtime.Dispatch);
                if (m_dispatchingId == id) m_dispatchingId = 0;
                if (runtime.Getaway != null && runtime.Getaway.IsSpawned) runtime.Getaway.DepartServer();
                if (runtime.Recipient != null && runtime.Recipient.IsSpawned)
                {
                    if (success) runtime.Recipient.CompleteDeliveryServer();
                    else Despawn(runtime.Recipient.NetworkObject);
                }
            }
            GetComponent<CouchGuys.Gameplay.Enemies.EnemySpawnManager>()?.RemoveDeliveryEnemiesServer(id);
            GetComponent<CouchGuys.Gameplay.Traffic.TrafficManager>()?.RemoveDeliveryRogueCarsServer(id);
            if (job.Couch != null && job.Couch.IsSpawned)
            {
                if (vehicleDelivery)
                    FloatDeliveredCouchObserversRpc(job.Couch, runtime?.Recipient != null
                        ? runtime.Recipient.transform.position : GetDestination(job.DestinationIndex).DropPosition.position);
                job.Couch.GetComponent<CouchCarryController>().ReleaseAllOccupantsServer();
                Despawn(job.Couch);
            }
            if (success)
            {
                m_lastReward.Value = job.Reward;
                m_currency.Value += job.Reward;
                if (m_chapterManager != null && !m_chapterManager.ChapterCompleted)
                    m_chapterManager.CompleteCurrentStageServer(m_chapterManager.CurrentStageIndex);
            }
            else m_deliveryFailureSequence.Value++;
            SetStateServer(HasActiveDelivery ? DeliveryState.Transport : DeliveryState.Available,
                HasActiveDelivery ? DeliveryObjectiveStep.DeliverCouch : DeliveryObjectiveStep.AcceptDelivery);
            m_refreshDialogues = true;
            return true;
        }

        private bool CanReceiveCouch(DeliveryInstance job, CouchCarryController couch,
            DeliveryDestination destination, out bool vehicleDelivery)
        {
            vehicleDelivery = false;
            if (couch == null || couch.DeliveryId != job.Id || job.Status != DeliveryState.Transport ||
                destination == null || !destination.CanReceiveDelivery) return false;
            if (m_jobs.TryGetValue(job.Id, out var runtime) && runtime.Recipient != null && !runtime.Recipient.IsAlive)
                return false;
            if (couch.IsLoaded)
            {
                // Never fall back to carried delivery for enemy cargo or an invalid attachment.
                if (!couch.TryGetDeliveryVehicle(out Transform vehicle)) return false;
                Vector3 target = runtime?.Recipient != null
                    ? runtime.Recipient.transform.position : destination.DropPosition.position;
                vehicleDelivery = (vehicle.position - target).sqrMagnitude <=
                    m_vehicleDeliveryRadius * m_vehicleDeliveryRadius;
                return vehicleDelivery;
            }
            return couch.ServerCarrierCount > 0 && Vector3.ProjectOnPlane(
                couch.transform.position - destination.DropPosition.position, Vector3.up).sqrMagnitude <=
                m_collectionRadius * m_collectionRadius;
        }

        [ObserversRpc(RunLocally = true)]
        private void FloatDeliveredCouchObserversRpc(NetworkObject couchObject, Vector3 target)
        {
            if (!IsClientInitialized || couchObject == null || !couchObject.TryGetComponent(out CouchCarryController couch)) return;
            Transform visual = couch.CreateDeliveryVisual();
            if (visual != null) StartCoroutine(FloatDeliveredCouch(visual, target));
        }

        private IEnumerator FloatDeliveredCouch(Transform visual, Vector3 target)
        {
            const float duration = 0.65f;
            Vector3 start = visual.position;
            Vector3 scale = visual.localScale;
            // Also clean up if the manager is disabled during the cosmetic animation.
            Destroy(visual.gameObject, duration + 0.1f);
            for (float elapsed = 0f; elapsed < duration && visual != null; elapsed += Time.deltaTime)
            {
                float t = elapsed / duration;
                visual.position = Vector3.Lerp(start, target, t) + Vector3.up * Mathf.Sin(t * Mathf.PI);
                visual.localScale = scale * (1f - t * t);
                yield return null;
            }
            if (visual != null) Destroy(visual.gameObject);
        }

        private void ClearDeliveriesServer()
        {
            if (!IsServerInitialized) return;
            while (m_deliveries.Count > 0) ResolveDeliveryServer(m_deliveries[0].Id, false);
        }
    }
}
