using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        // One server simulation per target, including when several shooters hit it.
        private static readonly Dictionary<NetworkObject, PlayerWeaponController> GrappleClaims = new();
        private readonly SyncVar<Vector3> m_grappleLocalPoint = new();
        private readonly SyncVar<NetworkObject> m_grappleTarget = new();
        private NetworkObject m_claimedGrappleTarget;
        private float m_grappleLeaseUntil;
        private float m_nextGrappleHeartbeat;
        private bool m_localGrappleHeld;
        private LineRenderer m_grappleLine;

        private void EnsureGrappleModel()
        {
            if (FindDescendant(transform, "Weapon_GrappleGun") != null) return;
            Transform pistol = m_weaponModels[0] != null ? m_weaponModels[0] : FindDescendant(transform, "Weapon_Pistol");
            if (pistol == null) return;
            GameObject prefab = Resources.Load<GameObject>("Weapons/GrappleGunModel");
            if (prefab == null) return;
            GameObject model = Instantiate(prefab, pistol.parent, false);
            model.name = "Weapon_GrappleGun";
            model.transform.localPosition = pistol.localPosition;
            model.transform.localRotation = pistol.localRotation;
            // The imported character rig is scaled up (50 in the player prefab).
            // This Unity primitive is authored in metres, unlike the FBX weapons,
            // so compensate for the socket's scale before positioning its muzzle.
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
            bool held = m_equippedIndex.Value == (int)PlayerWeaponType.GrappleGun &&
                m_input != null && m_input.isActiveAndEnabled && m_input.ShootHeld &&
                m_health != null && m_health.IsAlive && !m_health.IsKnockedDown &&
                !(m_couchCarrier != null && m_couchCarrier.IsCarrying);
            if (!held)
            {
                if (m_localGrappleHeld && IsClientInitialized) ReleaseGrappleServerRpc();
                m_localGrappleHeld = false;
                return;
            }
            if (!m_localGrappleHeld && Time.time >= m_nextLocalRequestTime)
            {
                m_localGrappleHeld = true;
                Ray ray = m_aimCamera != null
                    ? m_aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                    : new Ray(m_health.AimPoint, transform.forward);
                StartGrappleServerRpc(ray.origin, ray.direction);
                m_nextLocalRequestTime = Time.time + GetWeapon((int)PlayerWeaponType.GrappleGun).FireInterval;
                m_nextGrappleHeartbeat = Time.unscaledTime + 0.1f;
            }
            else if (m_localGrappleHeld && Time.unscaledTime >= m_nextGrappleHeartbeat)
            {
                HoldGrappleServerRpc();
                m_nextGrappleHeartbeat = Time.unscaledTime + 0.1f;
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        [ServerRpc]
        private void StartGrappleServerRpc(Vector3 aimOrigin, Vector3 aimDirection)
        {
            int index = (int)PlayerWeaponType.GrappleGun;
            WeaponDefinition weapon = GetWeapon(index);
            if (!CanHandleWeaponServer || m_equippedIndex.Value != index ||
                GetInventoryWeaponIndex(SelectedHotbarSlot) != index ||
                Time.time < m_switchCompleteAt || Time.time < m_nextServerFireTime[index] ||
                !IsFinite(aimOrigin) || !IsFinite(aimDirection) || aimDirection.sqrMagnitude < 0.5f ||
                (aimOrigin - m_health.AimPoint).sqrMagnitude > m_maximumAimOriginDistance * m_maximumAimOriginDistance)
                return;

            StopGrappleServer();
            m_nextServerFireTime[index] = Time.time + weapon.FireInterval;
            Vector3 muzzle = GetMuzzlePosition(index, m_health.AimPoint);
            Vector3 forward = aimDirection.normalized;
            float distance = CalculateAimTraceDistance(aimOrigin, forward, muzzle, weapon.Range);
            Vector3 point = aimOrigin + forward * distance;
            if (TryGetNearestNonSelfHit(aimOrigin, forward, distance, out RaycastHit aimHit)) point = aimHit.point;
            Vector3 direction = point - muzzle;
            if (direction.sqrMagnitude < 0.001f ||
                !TryGetNearestNonSelfHit(muzzle, direction.normalized, weapon.Range, out RaycastHit hit)) return;

            NetworkObject target = null;
            PlayerHealth player = hit.collider.GetComponentInParent<PlayerHealth>();
            ThiefHealth thief = hit.collider.GetComponentInParent<ThiefHealth>();
            CouchCarryController couch = hit.collider.GetComponentInParent<CouchCarryController>();
            if (player != null && player != m_health) target = player.NetworkObject;
            else if (thief != null) target = thief.NetworkObject;
            else if (couch != null) target = couch.NetworkObject;
            if (!IsValidGrappleTarget(target) ||
                (GrappleClaims.TryGetValue(target, out PlayerWeaponController claimant) && claimant != null && claimant != this)) return;

            GrappleClaims[target] = this;
            m_claimedGrappleTarget = target;
            m_grappleLocalPoint.Value = target.transform.InverseTransformPoint(hit.point);
            m_grappleTarget.Value = target;
            m_grappleLeaseUntil = Time.unscaledTime + 0.6f;
        }

        [ServerRpc]
        private void HoldGrappleServerRpc()
        {
            if (m_grappleTarget.Value != null) m_grappleLeaseUntil = Time.unscaledTime + 0.6f;
        }

        [ServerRpc]
        private void ReleaseGrappleServerRpc() => StopGrappleServer();

        private bool IsValidGrappleTarget(NetworkObject target)
        {
            if (target == null || !target.IsSpawned || !target.gameObject.activeInHierarchy) return false;
            if (target.TryGetComponent(out PlayerHealth player))
                return player.IsAlive && !(target.TryGetComponent(out PlayerTruckInteractor truck) && truck.CurrentTruck != null);
            if (target.TryGetComponent(out ThiefHealth thief)) return thief.IsAlive;
            if (target.TryGetComponent(out CouchCarryController couch))
                return couch.IsPickupAvailable && !couch.IsLoaded && couch.CouchRigidbody != null && !couch.CouchRigidbody.isKinematic;
            return false;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || ReferenceEquals(m_claimedGrappleTarget, null)) return;
            NetworkObject target = m_grappleTarget.Value;
            WeaponDefinition weapon = GetWeapon((int)PlayerWeaponType.GrappleGun);
            if (!CanHandleWeaponServer || !Owner.IsValid || !Owner.IsActive ||
                m_equippedIndex.Value != (int)PlayerWeaponType.GrappleGun ||
                Time.unscaledTime > m_grappleLeaseUntil || !IsValidGrappleTarget(target))
            {
                StopGrappleServer();
                return;
            }
            Vector3 muzzle = GetMuzzlePosition((int)PlayerWeaponType.GrappleGun, m_health.AimPoint);
            Vector3 point = target.transform.TransformPoint(m_grappleLocalPoint.Value);
            Vector3 offset = muzzle - point;
            if (offset.sqrMagnitude > weapon.Range * weapon.Range)
            {
                StopGrappleServer();
                return;
            }
            // Ease off near the muzzle without cancelling existing momentum on release.
            float speed = Mathf.Min(weapon.PullSpeed, Mathf.Max(0f, offset.magnitude - 1.1f) * 4f);
            Vector3 direction = offset.normalized;
            if (target.TryGetComponent(out CouchCarryController couch))
            {
                Rigidbody body = couch.CouchRigidbody;
                float acceleration = Mathf.Min(weapon.PullForce,
                    Mathf.Max(0f, speed - Vector3.Dot(body.GetPointVelocity(point), direction)) / Time.fixedDeltaTime);
                body.AddForceAtPosition(direction * acceleration * body.mass, point, ForceMode.Force);
            }
            else if (target.TryGetComponent(out PlayerHealth player))
                player.ApplyGrapplePullServer(direction, weapon.PullForce, speed, Time.fixedDeltaTime);
            else if (target.TryGetComponent(out ThiefHealth thief))
                thief.ApplyGrapplePullServer(direction, weapon.PullForce, speed, Time.fixedDeltaTime);
        }

        private void StopGrappleServer()
        {
            if (!IsServerInitialized) return;
            if (!ReferenceEquals(m_claimedGrappleTarget, null) &&
                GrappleClaims.TryGetValue(m_claimedGrappleTarget, out PlayerWeaponController claimant) && claimant == this)
                GrappleClaims.Remove(m_claimedGrappleTarget);
            m_claimedGrappleTarget = null;
            m_grappleTarget.Value = null;
        }

        public override void OnStopServer()
        {
            StopGrappleServer();
            base.OnStopServer();
        }

        private void UpdateGrappleVisual()
        {
            NetworkObject target = m_grappleTarget.Value;
            bool visible = IsClientInitialized && target != null && target.IsSpawned &&
                m_equippedIndex.Value == (int)PlayerWeaponType.GrappleGun &&
                m_health.IsAlive && (!IsOwner || m_localGrappleHeld);
            if (!visible)
            {
                if (m_grappleLine != null) m_grappleLine.enabled = false;
                return;
            }
            if (m_grappleLine == null)
            {
                var rope = new GameObject("Grapple Rope");
                rope.transform.SetParent(transform, false);
                m_grappleLine = rope.AddComponent<LineRenderer>();
                m_grappleLine.useWorldSpace = true;
                m_grappleLine.positionCount = 2;
                m_grappleLine.startWidth = 0.035f;
                m_grappleLine.endWidth = 0.025f;
                m_grappleLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_grappleLine.receiveShadows = false;
                m_grappleLine.sharedMaterial = Resources.Load<Material>("Weapons/GrappleRope");
            }
            Transform muzzle = m_muzzles[(int)PlayerWeaponType.GrappleGun];
            m_grappleLine.enabled = true;
            m_grappleLine.SetPosition(0, muzzle != null ? muzzle.position : m_health.AimPoint);
            m_grappleLine.SetPosition(1, target.transform.TransformPoint(m_grappleLocalPoint.Value));
        }
    }
}
