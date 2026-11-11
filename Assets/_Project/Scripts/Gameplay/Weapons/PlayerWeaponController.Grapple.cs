using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        [SerializeField] private GrappleGunSettings m_grappleSettings;
        // Fixed-point charge uses the existing per-slot ammo/pickup storage and owner RPC.
        private const int GrappleChargeCapacity = 10000;
        private readonly float[] m_grappleDrainRemainders = new float[InventorySlotCount];
        private float m_nextGrappleChargeSync;
        public float GrappleChargeFraction => Mathf.Clamp01(
            m_magazine[(int)PlayerWeaponType.GrappleGun] / (float)GrappleChargeCapacity);

        private int GetMagazineCapacity(int index) => index == (int)PlayerWeaponType.GrappleGun
            ? GrappleChargeCapacity : GetWeapon(index).MagazineCapacity;

        private bool DepleteGrappleCharge()
        {
            int index = (int)PlayerWeaponType.GrappleGun;
            int slot = SelectedHotbarSlot;
            float duration = m_grappleSettings != null ? m_grappleSettings.DepletionSeconds : 30f;
            m_grappleDrainRemainders[slot] += Time.fixedDeltaTime * GrappleChargeCapacity / duration;
            int spent = Mathf.FloorToInt(m_grappleDrainRemainders[slot]);
            m_grappleDrainRemainders[slot] -= spent;
            m_magazine[index] = Mathf.Max(0, m_magazine[index] - spent);
            if (m_magazine[index] == 0 || Time.unscaledTime >= m_nextGrappleChargeSync)
            {
                if (Owner.IsValid) SendWeaponStateTargetRpc(Owner, index, m_magazine[index], m_reserve[index], false);
                m_nextGrappleChargeSync = Time.unscaledTime + 0.1f;
            }
            return m_magazine[index] > 0;
        }
        // One server simulation per target, including when several shooters hit it.
        private static readonly Dictionary<NetworkObject, PlayerWeaponController> GrappleClaims = new();
        private readonly SyncVar<Vector3> m_grappleLocalPoint = new();
        private readonly SyncVar<NetworkObject> m_grappleTarget = new();
        private readonly SyncVar<bool> m_grappleAttached = new();
        private readonly SyncVar<bool> m_grappleWorld = new();
        private readonly SyncVar<Vector3> m_grappleWorldPoint = new();
        private readonly SyncVar<int> m_grappleBodyIndex = new(-1);
        private Transform m_worldAttachment;
        private Vector3 m_worldAttachmentLocalPoint;
        private NetworkObject m_attachmentCacheTarget;
        private Rigidbody[] m_attachmentBodies;
        private ThirdPersonPlayerController m_grappleMotor;
        private NetworkObject m_claimedGrappleTarget;
        private float m_grappleEndsAt;
        private readonly SyncVar<uint> m_grappleShotId = new();
        private uint m_observedGrappleShotId;
        private bool m_worldGrappleLocallyEnded;
        private LineRenderer m_grappleLine;
        private Transform m_cameraBoundsTarget;
        private Renderer[] m_cameraBoundsRenderers;

        // Retain the camera integration API; looking around never interrupts a one-shot pull.
        public void RecordLocalGrappleLook(Vector2 orbitDelta) { }

        // Read-only local presentation state; no additional replication or predicted attachment.
        public bool TryGetLocalGrappleBounds(out Bounds bounds)
        {
            bounds = default;
            NetworkObject target = m_grappleTarget.Value;
            if (!isActiveAndEnabled || !IsOwner || !IsClientInitialized || !m_grappleAttached.Value || m_worldGrappleLocallyEnded ||
                m_input == null || !m_input.isActiveAndEnabled ||
                m_equippedIndex.Value != (int)PlayerWeaponType.GrappleGun ||
                m_health == null || !m_health.IsAlive || m_health.IsKnockedDown ||
                (m_couchCarrier != null && m_couchCarrier.IsCarrying) ||
                target == null || !target.IsSpawned || !target.gameObject.activeInHierarchy)
                return false;

            if (m_cameraBoundsTarget != target.transform)
            {
                m_cameraBoundsTarget = target.transform;
                m_cameraBoundsRenderers = target.GetComponentsInChildren<Renderer>(true);
            }
            bool found = false;
            foreach (Renderer renderer in m_cameraBoundsRenderers)
            {
                // Ropes, trails and particles can extend far beyond the attached object.
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            if (!found) bounds = new Bounds(target.transform.TransformPoint(m_grappleLocalPoint.Value), Vector3.zero);
            return true;
        }

        private void EnsureGrappleModel()
        {
            if (m_weaponModels[(int)PlayerWeaponType.GrappleGun] != null) return;
            if (FindDescendant(transform, "Weapon_GrappleGun") != null) return;
            Transform pistol = m_weaponModels[0] != null ? m_weaponModels[0] : FindDescendant(transform, "Weapon_Pistol");
            if (pistol == null) return;
            GameObject prefab = Resources.Load<GrappleGunAssets>("Weapons/GrappleGunAssets")?.EquippedPrefab;
            if (prefab == null) return;
            GameObject model = Instantiate(prefab, pistol.parent, false);
            model.name = "Weapon_GrappleGun";
            model.transform.localPosition = pistol.localPosition;
            model.transform.rotation = Quaternion.LookRotation(
                pistol.TransformDirection(Vector3.down), pistol.TransformDirection(Vector3.forward));
            // The prefab is authored in metres; compensate for the imported hand rig's scale.
            Vector3 parentScale = pistol.parent.lossyScale;
            Vector3 size = prefab.transform.localScale;
            model.transform.localScale = new Vector3(
                size.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                size.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                size.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        }

        private void UpdateGrappleInput()
        {
            if (!IsOwner) return;
            bool usable = m_equippedIndex.Value == (int)PlayerWeaponType.GrappleGun &&
                m_magazine[(int)PlayerWeaponType.GrappleGun] > 0 &&
                m_input != null && m_input.isActiveAndEnabled &&
                m_health != null && m_health.IsAlive && !m_health.IsKnockedDown &&
                !(m_couchCarrier != null && m_couchCarrier.IsCarrying);
            m_grappleMotor ??= GetComponent<ThirdPersonPlayerController>();
            if (m_observedGrappleShotId != m_grappleShotId.Value)
            {
                m_observedGrappleShotId = m_grappleShotId.Value;
                m_worldGrappleLocallyEnded = false;
                m_grappleMotor?.SetWorldGrapple(false, default, m_grappleSettings);
            }
            // Cancellation is immediate, including while the next-shot cooldown is running.
            if (usable && m_input.ShootPressedThisFrame && m_grappleAttached.Value && !m_worldGrappleLocallyEnded)
            {
                m_worldGrappleLocallyEnded = true;
                m_grappleMotor?.SetWorldGrapple(false, default, m_grappleSettings);
                FinishGrappleServerRpc(m_observedGrappleShotId);
                return;
            }
            Vector3 worldPoint = m_grappleWorldPoint.Value;
            float worldDistance = Vector3.Distance(transform.position, worldPoint);
            bool active = usable && m_grappleAttached.Value && !m_worldGrappleLocallyEnded;
            bool worldActive = active && m_grappleWorld.Value && m_grappleSettings != null;
            if ((!usable && m_grappleAttached.Value && !m_worldGrappleLocallyEnded) ||
                (worldActive && (worldDistance <= m_grappleSettings.ArrivalDistance ||
                 worldDistance > m_grappleSettings.BreakDistance)))
            {
                m_worldGrappleLocallyEnded = true;
                worldActive = false;
                FinishGrappleServerRpc(m_observedGrappleShotId);
            }
            m_grappleMotor?.SetWorldGrapple(worldActive, worldPoint, m_grappleSettings);
            // Trigger edges only: releasing fire does not cancel, holding does not repeat.
            if (!usable || !m_input.ShootPressedThisFrame || Time.time < m_nextLocalRequestTime) return;
            Ray ray = m_aimCamera != null
                ? m_aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : new Ray(m_health.AimPoint, transform.forward);
            StartGrappleServerRpc(ray.origin, ray.direction);
            m_nextLocalRequestTime = Time.time + (m_grappleSettings != null ? m_grappleSettings.Cooldown :
                GetWeapon((int)PlayerWeaponType.GrappleGun).FireInterval);
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        [ServerRpc]
        private void StartGrappleServerRpc(Vector3 aimOrigin, Vector3 aimDirection)
        {
            int index = (int)PlayerWeaponType.GrappleGun;
            WeaponDefinition weapon = GetWeapon(index);
            if (!CanHandleWeaponServer || m_equippedIndex.Value != index ||
                m_magazine[index] <= 0 ||
                GetInventoryWeaponIndex(SelectedHotbarSlot) != index ||
                Time.time < m_switchCompleteAt ||
                !IsFinite(aimOrigin) || !IsFinite(aimDirection) || aimDirection.sqrMagnitude < 0.5f ||
                (aimOrigin - m_health.AimPoint).sqrMagnitude > m_maximumAimOriginDistance * m_maximumAimOriginDistance)
                return;

            // Also handle presses made before the client has received attachment replication.
            if (m_grappleAttached.Value)
            {
                StopGrappleServer();
                return;
            }
            if (Time.time < m_nextServerFireTime[index]) return;
            StopGrappleServer();
            m_nextServerFireTime[index] = Time.time + (m_grappleSettings != null ? m_grappleSettings.Cooldown : weapon.FireInterval);
            Vector3 muzzle = GetMuzzlePosition(index, m_health.AimPoint);
            Vector3 forward = aimDirection.normalized;
            float range = m_grappleSettings != null ? m_grappleSettings.MaximumRange : weapon.Range;
            float distance = CalculateAimTraceDistance(aimOrigin, forward, muzzle, range);
            Vector3 point = aimOrigin + forward * distance;
            if (TryGetNearestNonSelfHit(aimOrigin, forward, distance, out RaycastHit aimHit)) point = aimHit.point;
            Vector3 direction = point - muzzle;
            if (direction.sqrMagnitude < 0.001f ||
                !TryGetNearestNonSelfHit(muzzle, direction.normalized, range, out RaycastHit hit))
            { ShowGrappleShotObserversRpc(muzzle, false); return; }

            var dispatcher = hit.collider.GetComponentInParent<CouchGuys.Gameplay.Delivery.DeliveryNPC>();
            if (dispatcher != null)
            {
                // Resolve before treating non-networked scenery as a world anchor.
                // StopGrappleServer above has already cleared every pull/claim.
                dispatcher.RepelGrappleServer(m_health);
                ShowGrappleShotObserversRpc(muzzle, false);
                return;
            }
            NetworkObject target = null;
            PlayerHealth player = hit.collider.GetComponentInParent<PlayerHealth>();
            ThiefHealth thief = hit.collider.GetComponentInParent<ThiefHealth>();
            CouchCarryController couch = hit.collider.GetComponentInParent<CouchCarryController>();
            PedestrianController pedestrian = hit.collider.GetComponentInParent<PedestrianController>();
            Rigidbody body = hit.collider.attachedRigidbody;
            NetworkObject hitNetwork = hit.collider.GetComponentInParent<NetworkObject>();
            if (player != null && player != m_health) target = player.NetworkObject;
            else if (thief != null) target = thief.NetworkObject;
            else if (couch != null) target = couch.NetworkObject;
            else if (pedestrian != null) target = pedestrian.NetworkObject;
            else if (IsGrappleVehicle(hitNetwork)) target = hitNetwork;
            bool world = target == null;
            // Unsupported dynamic/character targets must not become immovable world anchors.
            if (world && (player != null || thief != null || couch != null || pedestrian != null ||
                (body != null && !body.isKinematic)))
            { ShowGrappleShotObserversRpc(muzzle, false); return; }
            if (!world && (!IsValidGrappleTarget(target) ||
                (GrappleClaims.TryGetValue(target, out PlayerWeaponController claimant) && claimant != null && claimant != this)))
            { ShowGrappleShotObserversRpc(muzzle, false); return; }

            if (thief != null) thief.SetGrappledServer(true);
            if (pedestrian != null) pedestrian.SetGrappledServer(true);
            if ((thief != null || pedestrian != null) && (body == null || body.transform == target.transform))
            {
                PedestrianRagdoll ragdoll = target.GetComponentInChildren<PedestrianRagdoll>(true);
                if (ragdoll != null) body = ragdoll.Body;
            }
            if (!world)
            {
                GrappleClaims[target] = this;
                m_claimedGrappleTarget = target;
            }
            m_worldAttachment = world ? hit.collider.transform : null;
            m_worldAttachmentLocalPoint = world ? m_worldAttachment.InverseTransformPoint(hit.point) : default;
            m_grappleTarget.Value = target;
            m_attachmentCacheTarget = null;
            CacheGrappleBodies();
            int bodyIndex = -1;
            if (!world && body != null && m_attachmentBodies != null)
                bodyIndex = System.Array.IndexOf(m_attachmentBodies, body);
            m_grappleBodyIndex.Value = bodyIndex;
            Transform attachment = bodyIndex >= 0 ? body.transform : !world ? target.transform : null;
            m_grappleLocalPoint.Value = attachment != null ? attachment.InverseTransformPoint(hit.point) : default;
            m_grappleWorldPoint.Value = hit.point;
            m_grappleWorld.Value = world;
            m_grappleAttached.Value = true;
            m_grappleEndsAt = Time.unscaledTime + (m_grappleSettings != null ? m_grappleSettings.MaximumPullDuration : 8f);
            m_grappleShotId.Value++;
            ShowGrappleShotObserversRpc(muzzle, true);
        }

        [ServerRpc]
        private void FinishGrappleServerRpc(uint shotId)
        {
            // A delayed arrival notification must not cancel a newer shot.
            if (shotId == m_grappleShotId.Value) StopGrappleServer();
        }

        [ObserversRpc(RunLocally = true)]
        private void ShowGrappleShotObserversRpc(Vector3 muzzle, bool attached)
        {
            // No predicted grapple audio: this event plays once for owner and observers,
            // including misses, and is never sent by the ongoing pull/update path.
            if (NetworkManager.IsClientStarted) m_audio?.PlayShot(PlayerWeaponType.GrappleGun, muzzle);
            if (!attached) return;
            m_visualRecoil = 7f;
            m_visualRecoilDistance = 0.04f;
            m_animationDriver?.PlayWeaponShot(PlayerWeaponType.Pistol);
        }

        private bool IsValidGrappleTarget(NetworkObject target)
        {
            if (target == null || !target.IsSpawned || !target.gameObject.activeInHierarchy) return false;
            if (target.TryGetComponent(out PlayerHealth player))
                return player.IsAlive && !(target.TryGetComponent(out PlayerTruckInteractor truck) && truck.CurrentTruck != null);
            if (target.TryGetComponent(out ThiefHealth thief)) return thief.IsAlive;
            if (target.TryGetComponent(out CouchCarryController couch))
                return couch.IsPickupAvailable && !couch.IsLoaded && couch.CouchRigidbody != null && !couch.CouchRigidbody.isKinematic;
            if (target.TryGetComponent(out PedestrianController pedestrian)) return pedestrian.IsAlive;
            if (IsGrappleVehicle(target))
                return target.TryGetComponent(out Rigidbody body) && !body.isKinematic;
            return false;
        }

        private static bool IsGrappleVehicle(NetworkObject target) => target != null &&
            (target.TryGetComponent<TruckController>(out _) || target.TryGetComponent<TrafficVehicleController>(out _) ||
             target.TryGetComponent<GetawayTruckController>(out _) || target.TryGetComponent<MagneticHoverboardController>(out _));

        private void CacheGrappleBodies()
        {
            NetworkObject target = m_grappleTarget.Value;
            if (m_attachmentCacheTarget == target) return;
            m_attachmentCacheTarget = target;
            m_attachmentBodies = target != null ? target.GetComponentsInChildren<Rigidbody>(true) : null;
        }

        private Vector3 GrappleAttachmentPoint()
        {
            if (m_grappleWorld.Value) return m_grappleWorldPoint.Value;
            CacheGrappleBodies();
            NetworkObject target = m_grappleTarget.Value;
            int index = m_grappleBodyIndex.Value;
            Transform attachment = m_attachmentBodies != null && index >= 0 && index < m_attachmentBodies.Length &&
                m_attachmentBodies[index] != null ? m_attachmentBodies[index].transform : target != null ? target.transform : null;
            return attachment != null ? attachment.TransformPoint(m_grappleLocalPoint.Value) : m_grappleWorldPoint.Value;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || !m_grappleAttached.Value) return;
            NetworkObject target = m_grappleTarget.Value;
            WeaponDefinition weapon = GetWeapon((int)PlayerWeaponType.GrappleGun);
            if (!CanHandleWeaponServer || !Owner.IsValid || !Owner.IsActive ||
                m_equippedIndex.Value != (int)PlayerWeaponType.GrappleGun ||
                Time.unscaledTime >= m_grappleEndsAt ||
                (m_grappleWorld.Value ? m_worldAttachment == null || !m_worldAttachment.gameObject.activeInHierarchy : !IsValidGrappleTarget(target)))
            {
                StopGrappleServer();
                return;
            }
            Vector3 muzzle = GetMuzzlePosition((int)PlayerWeaponType.GrappleGun, m_health.AimPoint);
            if (m_grappleWorld.Value)
                m_grappleWorldPoint.Value = m_worldAttachment.TransformPoint(m_worldAttachmentLocalPoint);
            Vector3 point = GrappleAttachmentPoint();
            Vector3 offset = muzzle - point;
            float breakDistance = m_grappleSettings != null ? m_grappleSettings.BreakDistance : weapon.Range;
            float targetArrivalDistance = m_grappleSettings != null ? m_grappleSettings.TargetArrivalDistance : 2f;
            if (offset.sqrMagnitude > breakDistance * breakDistance || (!m_grappleWorld.Value &&
                offset.sqrMagnitude <= targetArrivalDistance * targetArrivalDistance) || (m_grappleWorld.Value &&
                Vector3.Distance(transform.position, point) <= (m_grappleSettings != null ? m_grappleSettings.ArrivalDistance : 1.5f)))
            {
                StopGrappleServer();
                return;
            }
            if (!DepleteGrappleCharge())
            {
                StopGrappleServer();
                return;
            }
            if (m_grappleWorld.Value) return; // Only the owning client's existing motor moves the shooter.

            // Keep a small closing speed so the pull crosses the arrival threshold and completes.
            float speed = Mathf.Min(m_grappleSettings != null ? m_grappleSettings.ObjectMaximumSpeed : weapon.PullSpeed,
                Mathf.Max(0f, offset.magnitude - targetArrivalDistance) * 4f + 1f);
            float force = m_grappleSettings != null ? m_grappleSettings.ObjectPullForce : weapon.PullForce;
            float characterForce = m_grappleSettings != null ? m_grappleSettings.CharacterPullForce : weapon.PullForce;
            Vector3 direction = offset.normalized;
            if (target.TryGetComponent(out CouchCarryController couch))
            {
                Rigidbody body = couch.CouchRigidbody;
                float acceleration = Mathf.Min(force,
                    Mathf.Max(0f, speed - Vector3.Dot(body.GetPointVelocity(point), direction)) / Time.fixedDeltaTime);
                body.AddForceAtPosition(direction * acceleration * body.mass, point, ForceMode.Force);
            }
            else if (target.TryGetComponent(out PlayerHealth player))
                player.ApplyGrapplePullServer(direction, characterForce, speed, Time.fixedDeltaTime);
            else if (target.TryGetComponent(out ThiefHealth thief))
                thief.ApplyGrapplePullServer(direction, characterForce, speed, Time.fixedDeltaTime);
            else if (target.TryGetComponent(out PedestrianController pedestrian))
                pedestrian.ApplyGrapplePullServer(direction, characterForce, speed, Time.fixedDeltaTime);
            else if (IsGrappleVehicle(target) && target.TryGetComponent(out Rigidbody vehicleBody))
            {
                float acceleration = Mathf.Min(m_grappleSettings != null ? m_grappleSettings.VehiclePullForce : force,
                    Mathf.Max(0f, speed - Vector3.Dot(vehicleBody.linearVelocity, direction)) / Time.fixedDeltaTime);
                // Centre-of-mass force leaves steering, suspension and ownership intact.
                vehicleBody.AddForce(direction * acceleration, ForceMode.Acceleration);
            }
        }

        private void StopGrappleServer()
        {
            if (!IsServerInitialized) return;
            if (m_grappleAttached.Value && Owner.IsValid)
            {
                int index = (int)PlayerWeaponType.GrappleGun;
                SendWeaponStateTargetRpc(Owner, index, m_magazine[index], m_reserve[index], false);
            }
            if (!ReferenceEquals(m_claimedGrappleTarget, null) &&
                GrappleClaims.TryGetValue(m_claimedGrappleTarget, out PlayerWeaponController claimant) && claimant == this)
            {
                if (m_claimedGrappleTarget != null && m_claimedGrappleTarget.TryGetComponent(out ThiefHealth thief))
                    thief.SetGrappledServer(false);
                if (m_claimedGrappleTarget != null && m_claimedGrappleTarget.TryGetComponent(out PedestrianController pedestrian))
                    pedestrian.SetGrappledServer(false);
                GrappleClaims.Remove(m_claimedGrappleTarget);
            }
            m_claimedGrappleTarget = null;
            m_grappleTarget.Value = null;
            m_grappleAttached.Value = false;
            m_grappleWorld.Value = false;
            m_worldAttachment = null;
        }

        public override void OnStopServer()
        {
            StopGrappleServer();
            base.OnStopServer();
        }

        private void UpdateGrappleVisual()
        {
            NetworkObject target = m_grappleTarget.Value;
            bool visible = IsClientInitialized && m_grappleAttached.Value &&
                (m_grappleWorld.Value || (target != null && target.IsSpawned)) &&
                m_equippedIndex.Value == (int)PlayerWeaponType.GrappleGun &&
                m_health.IsAlive && (!IsOwner || !m_worldGrappleLocallyEnded);
            if (!visible)
            {
                if (m_grappleLine != null) m_grappleLine.enabled = false;
                return;
            }
            if (m_grappleLine == null)
            {
                Transform model = m_weaponModels[(int)PlayerWeaponType.GrappleGun];
                m_grappleLine = model != null ? model.GetComponentInChildren<LineRenderer>(true) : null;
                if (m_grappleLine == null) return;
            }
            Transform muzzle = m_muzzles[(int)PlayerWeaponType.GrappleGun];
            m_grappleLine.enabled = true;
            m_grappleLine.SetPosition(0, muzzle != null ? muzzle.position : m_health.AimPoint);
            m_grappleLine.SetPosition(1, GrappleAttachmentPoint());
        }
    }
}
