using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Single server Rigidbody motor. Input is independent of player input devices.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    public sealed class MagneticHoverboardController : NetworkBehaviour
    {
        public static readonly HashSet<MagneticHoverboardController> ActiveBoards = new();
        [SerializeField] private MagneticHoverboardSettings m_settings;
        [SerializeField] private Rigidbody m_body;
        [SerializeField] private Transform m_visual;
        [SerializeField] private Transform m_driverAnchor;
        [SerializeField] private Transform m_couchAnchor;
        [SerializeField] private Transform m_cameraTarget;
        [SerializeField] private Transform[] m_hoverProbes;
        [SerializeField] private Transform m_leftArm;
        [SerializeField] private Transform m_rightArm;
        [SerializeField] private Transform m_leftGrip;
        [SerializeField] private Transform m_rightGrip;
        [SerializeField] private BoxCollider m_cargoCollider;
        [SerializeField] private Transform[] m_loadingPoints;
        [SerializeField] private bool m_placeNearDepot;
        private readonly SyncVar<NetworkObject> m_driver = new();
        private readonly SyncVar<NetworkObject> m_couch = new();
        private readonly SyncVar<float> m_steering = new();
        private Vector2 m_input;
        private float m_lastInput, m_turn, m_armProgress;
        private bool m_ready;
        private Vector3 m_visualPosition;
        private Quaternion m_visualRotation, m_leftClosed, m_rightClosed;
        private PhysicsMaterial m_contactMaterial;
        public MagneticHoverboardSettings Settings => m_settings;
        public NetworkObject Driver => m_driver.Value;
        public CouchCarryController Couch => m_couch.Value != null ? m_couch.Value.GetComponent<CouchCarryController>() : null;
        public Transform CouchAnchor => m_couchAnchor;
        public Transform DriverAnchor => m_driverAnchor;
        public Transform CameraTarget => m_cameraTarget;
        public Transform LeftGrip => m_leftGrip;
        public Transform RightGrip => m_rightGrip;
        public float Speed => m_body != null ? m_body.linearVelocity.magnitude : 0f;
        public Vector3 MountPosition => transform.TransformPoint(m_settings.DriverPositionOffset);
        public Vector3 MagnetPosition => m_couchAnchor.position;

        private void Awake()
        {
            m_body ??= GetComponent<Rigidbody>();
            m_body.isKinematic = true;
            if (m_settings == null) { enabled = false; return; }
            m_body.mass = m_settings.Mass;
            m_body.useGravity = true;
            m_body.interpolation = RigidbodyInterpolation.Interpolate;
            m_body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_body.angularDamping = 5f;
            m_body.maxAngularVelocity = 4f;
            m_contactMaterial = new PhysicsMaterial("Hoverboard contact") {
                dynamicFriction = m_settings.CollisionFriction, staticFriction = m_settings.CollisionFriction,
                bounciness = m_settings.CollisionRestitution,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.sharedMaterial = m_contactMaterial;
            m_visualPosition = m_visual.localPosition; m_visualRotation = m_visual.localRotation;
            m_leftClosed = m_leftArm.localRotation; m_rightClosed = m_rightArm.localRotation;
            m_driverAnchor.localPosition = m_settings.DriverPositionOffset;
            m_driverAnchor.localRotation = Quaternion.Euler(m_settings.DriverRotationOffset);
            m_couchAnchor.localPosition = m_settings.AttachmentPositionOffset;
            m_couchAnchor.localRotation = Quaternion.Euler(m_settings.AttachmentRotationOffset);
            m_couch.OnChange += OnCargoChanged;
        }
        private void OnEnable() => ActiveBoards.Add(this);
        private void OnDisable() => ActiveBoards.Remove(this);
        private void OnDestroy()
        {
            m_couch.OnChange -= OnCargoChanged;
            if (m_contactMaterial != null) Destroy(m_contactMaterial);
        }
        private void OnCargoChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            m_cargoCollider.enabled = next != null;
            CouchCarryController couch = Couch;
            if (couch == null) return;
            Bounds cargo = couch.CargoBounds;
            m_cargoCollider.center = transform.InverseTransformPoint(m_couchAnchor.TransformPoint(cargo.center));
            m_cargoCollider.size = new Vector3(cargo.size.z,cargo.size.y,cargo.size.x);
        }
        public override void OnStartServer()
        {
            base.OnStartServer();
            m_ready = !m_placeNearDepot;
            if (m_placeNearDepot) { m_body.isKinematic = true; StartCoroutine(PlaceNearDepot()); }
            else m_body.isKinematic = false;
        }
        private System.Collections.IEnumerator PlaceNearDepot()
        {
            var generator = FindFirstObjectByType<CouchGuys.ProceduralGeneration.NeighbourhoodGenerator>();
            while (generator != null && !generator.HasGeneratedNeighbourhood) yield return null;
            if (!IsServerInitialized) yield break;
            Transform marker = generator != null ? generator.GeneratedStartingArea.RoadConnection : transform;
            bool placed = false;
            for (int ring=0; ring<5 && !placed; ring++) foreach (float side in new[] {1f,-1f})
            {
                Vector3 candidate=marker.position+marker.forward*(5f+ring*2f)+marker.right*side*3f;
                if (!TryGround(candidate+Vector3.up*10f,30f,out RaycastHit ground)) continue;
                Quaternion rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(marker.forward,Vector3.up));
                Vector3 position=ground.point+Vector3.up*m_settings.HoverHeight;
                if (Physics.CheckBox(position+Vector3.up,new Vector3(.85f,1f,2.3f),rotation,~0,QueryTriggerInteraction.Ignore)) continue;
                transform.SetPositionAndRotation(position,rotation); m_body.position=position; m_body.rotation=rotation;
                GetComponent<FishNet.Component.Transforming.NetworkTransform>()?.Teleport(); placed=true; break;
            }
            if (!placed) Debug.LogWarning("Hoverboard: depot placement obstructed; using authored scene position.",this);
            m_ready=true; m_body.isKinematic=false;
        }
        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_body.isKinematic = true;
            m_armProgress = Couch != null ? 1f : 0f;
            OnCargoChanged(null,m_couch.Value,false);
        }
        public override void OnStopServer()
        {
            if (Driver != null && Driver.TryGetComponent(out PlayerHoverboardInteractor p)) p.ReleaseServer(true);
            if (Couch != null) Couch.ReleaseHoverboardServer(transform.position + Vector3.up * 1.5f, transform.rotation);
            m_couch.Value = null;
            base.OnStopServer();
        }

        [Server] public bool TryMountServer(PlayerHoverboardInteractor player)
        {
            if (!IsServerInitialized || player == null || Driver != null || !player.CanMount || Speed > m_settings.MagnetMaximumSpeed ||
                Vector3.Distance(player.transform.position, MountPosition) > m_settings.MountRange) return false;
            m_driver.Value = player.NetworkObject;
            m_input = Vector2.zero; m_lastInput = Time.time;
            return true;
        }
        [Server] public void ReleaseDriverServer(NetworkObject player)
        {
            if (Driver != player) return;
            m_driver.Value = null; m_input = Vector2.zero;
        }
        [Server] public void SetInputServer(NetworkObject source, Vector2 input)
        {
            if (source == null || source != Driver || !float.IsFinite(input.x) || !float.IsFinite(input.y)) return;
            m_input = Vector2.ClampMagnitude(input, 1f); m_lastInput = Time.time;
        }

        private void FixedUpdate()
        {
            if (IsServerInitialized && !m_ready) { m_body.isKinematic = true; return; }
            if (!IsServerInitialized || m_settings == null || m_body.isKinematic) return;
            if (Driver != null && (!Driver.IsSpawned || !Driver.Owner.IsActive ||
                Driver.TryGetComponent(out PlayerHealth h) && (!h.IsAlive || h.IsKnockedDown)))
            {
                if (Driver.TryGetComponent(out PlayerHoverboardInteractor p)) p.ReleaseServer(true);
                else { m_driver.Value = null; m_input = Vector2.zero; }
            }
            Vector2 input = Driver != null && Time.time - m_lastInput < m_settings.InputTimeout ? m_input : Vector2.zero;
            Vector3 normal = Vector3.zero; float groundDistance = 0f; int grounded = 0;
            foreach (Transform probe in m_hoverProbes)
            {
                if (!TryGround(probe.position + Vector3.up * .5f, m_settings.GroundDetectionDistance, out RaycastHit hit)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > m_settings.MaximumSlopeAngle) continue;
                normal += hit.normal; groundDistance += hit.distance - .5f; grounded++;
            }
            float dt = Time.fixedDeltaTime;
            m_turn = Mathf.Lerp(m_turn, input.x, 1f - Mathf.Exp(-m_settings.TurningResponsiveness * dt));
            m_steering.Value = m_turn;
            if (grounded == 0) return;
            normal.Normalize(); groundDistance /= grounded;
            float lift = (m_settings.HoverHeight - groundDistance) * m_settings.HoverSpringStrength -
                Vector3.Dot(m_body.linearVelocity, Vector3.up) * m_settings.HoverDamping - Physics.gravity.y;
            m_body.AddForce(Vector3.up * Mathf.Clamp(lift, -20f, 65f), ForceMode.Acceleration);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
            float speed = Vector3.Dot(m_body.linearVelocity, forward);
            float target = input.y * (input.y < 0 ? m_settings.ReverseSpeed : Couch != null ? m_settings.LoadedSpeed : m_settings.MaximumForwardSpeed);
            float rate = Mathf.Abs(input.y) < .01f ? m_settings.Deceleration : speed * input.y < -.1f ? m_settings.BrakingStrength : m_settings.Acceleration;
            float change = Mathf.MoveTowards(speed, target, rate * dt) - speed;
            m_body.AddForce(forward * (change / dt), ForceMode.Acceleration);
            Vector3 lateral = Vector3.ProjectOnPlane(m_body.linearVelocity, normal) - forward * speed;
            m_body.AddForce(-lateral * m_settings.LateralGrip, ForceMode.Acceleration);
            // Cancel downhill gravity while supported; there are no wheel forces.
            m_body.AddForce(-Vector3.ProjectOnPlane(Physics.gravity, normal), ForceMode.Acceleration);
            float yaw = m_turn * m_settings.TurningSpeed * Mathf.Clamp(speed / 3f, -1f, 1f) * dt;
            Quaternion aligned = Quaternion.Slerp(m_body.rotation, Quaternion.LookRotation(forward, normal), 1f - Mathf.Exp(-m_settings.GroundAlignment * dt));
            m_body.MoveRotation(Quaternion.AngleAxis(yaw, normal) * aligned);
            m_body.angularVelocity = Vector3.zero;
        }

        // Ignore our own body, players and loose cargo when finding drivable ground.
        internal bool TryGround(Vector3 origin, float distance, out RaycastHit ground)
        {
            ground = default; float closest = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, distance, m_settings.GroundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= closest || hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.GetComponentInParent<CouchCarryController>() != null || hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                ground = hit; closest = hit.distance;
            }
            return closest < float.PositiveInfinity;
        }
        private void LateUpdate()
        {
            if (m_settings == null) return;
            float blend = 1f - Mathf.Exp(-m_settings.BankingSmoothing * Time.deltaTime);
            m_visual.localPosition = m_visualPosition + Vector3.up * (Mathf.Sin(Time.time * m_settings.BobbingFrequency * Mathf.PI * 2f) * m_settings.BobbingAmplitude);
            m_visual.localRotation = Quaternion.Slerp(m_visual.localRotation, m_visualRotation *
                Quaternion.Euler(0,0,-m_steering.Value * m_settings.BankingAngle), blend);
            m_armProgress = Mathf.MoveTowards(m_armProgress, Couch != null ? 1f : 0f, m_settings.ArmAnimationSpeed * Time.deltaTime);
            m_leftArm.localRotation = m_leftClosed * Quaternion.Euler((1-m_armProgress) * m_settings.ArmOpenAngle,0,0);
            m_rightArm.localRotation = m_rightClosed * Quaternion.Euler(-(1-m_armProgress) * m_settings.ArmOpenAngle,0,0);
        }

        public bool IsNearMagnet(Vector3 bodyPosition, CouchCarryController carriedCouch = null)
        {
            // A carrier stands beyond the couch, rather than inside it. Include
            // its half-size in reach while still validating the couch at the point.
            float reach=m_settings.LoadingPointRange;
            if(carriedCouch!=null)
                reach+=Vector3.Scale(carriedCouch.CargoBounds.extents,carriedCouch.transform.lossyScale).magnitude;
            if (m_loadingPoints == null || m_loadingPoints.Length == 0)
                return Vector3.Distance(bodyPosition,MagnetPosition) <= reach + .7f;
            foreach (Transform point in m_loadingPoints)
                if (point != null && Vector3.Distance(bodyPosition,point.position) <= reach) return true;
            return false;
        }
        public bool IsCouchInLoadingRange(CouchCarryController couch)
        {
            if(couch == null) return false;
            if(m_loadingPoints == null || m_loadingPoints.Length == 0)
                return Vector3.Distance(couch.transform.TransformPoint(couch.CargoBounds.center),MagnetPosition) <= m_settings.AttachmentRange;
            foreach(Transform point in m_loadingPoints)
            {
                if(point == null) continue;
                Vector3 nearest=couch.transform.TransformPoint(couch.CargoBounds.ClosestPoint(couch.transform.InverseTransformPoint(point.position)));
                if(Vector3.Distance(nearest,point.position) <= m_settings.AttachmentRange) return true;
            }
            return false;
        }
        public CouchCarryController FindAvailableCouch()
        {
            if (Couch != null || Speed > m_settings.MagnetMaximumSpeed) return null;
            CouchCarryController closest = null; float distance = float.PositiveInfinity;
            foreach (Collider c in Physics.OverlapSphere(MagnetPosition, m_settings.AttachmentRange + m_settings.LoadingPointRange, ~0, QueryTriggerInteraction.Ignore))
            {
                CouchCarryController couch = c.GetComponentInParent<CouchCarryController>();
                if (couch == null || !couch.IsSpawned || couch.IsSled || couch.IsLoaded || !couch.IsPickupAvailable || couch.ActiveCarrierCount > 0 || !IsCouchInLoadingRange(couch)) continue;
                float d = (couch.transform.TransformPoint(couch.CargoBounds.center) - MagnetPosition).sqrMagnitude;
                if (d < distance) { distance = d; closest = couch; }
            }
            return closest;
        }
        [Server] public bool InteractMagnetServer(PlayerHoverboardInteractor player)
            => InteractMagnetServer(player,out _);
        [Server] public bool InteractMagnetServer(PlayerHoverboardInteractor player, out string reason)
        {
            reason="Stop and dismount the hoverboard before loading";
            if (!IsServerInitialized || player == null || !player.CanUseMagnet || Speed > m_settings.MagnetMaximumSpeed || Driver != null) return false;
            var carrier=player.GetComponent<PlayerCouchCarrier>();
            var carried=carrier != null ? carrier.CarriedCouch : null;
            reason="Move closer to either side loading point";
            if(!IsNearMagnet(player.LoadingOrigin,carried)) return false;
            if (Couch != null)
            {
                reason="Release your carried couch before unloading";
                if(carried != null)return false;
                CouchCarryController couch = Couch;
                reason="Clear the space behind the hoverboard to release the couch";
                if (couch.IsCargoTransitioning || !TryGetCouchExit(couch, out Vector3 position, out Quaternion rotation)) return false;
                couch.ReleaseHoverboardServer(position, rotation); m_couch.Value = null;
                return true;
            }
            CouchCarryController candidate = carried != null ? carried : FindAvailableCouch();
            reason="Bring an available couch to either side loading point";
            if(candidate == null || !IsCouchInLoadingRange(candidate))return false;
            reason="Other carriers must let go first";
            if(candidate.ActiveCarrierCount > (carried != null ? 1 : 0))return false;
            reason="Clear the platform and the path from the couch";
            if(!HasMagnetClearance(candidate,carried != null ? carrier : null))return false;
            reason="This couch is not available for loading";
            if(!candidate.MountOnHoverboardServer(this,carried != null ? carrier : null)) return false;
            m_couch.Value = candidate.NetworkObject;
            return true;
        }
        internal void ForgetCouchServer(CouchCarryController couch)
        {
            if (IsServerInitialized && Couch == couch) m_couch.Value = null;
        }
        private bool HasMagnetClearance(CouchCarryController couch, PlayerCouchCarrier loader = null)
        {
            Bounds bounds=couch.CargoBounds;
            Vector3 start=couch.transform.TransformPoint(bounds.center), end=m_couchAnchor.TransformPoint(bounds.center);
            foreach(RaycastHit hit in Physics.RaycastAll(start,(end-start).normalized,Vector3.Distance(start,end),~0,QueryTriggerInteraction.Ignore))
                if(!hit.collider.transform.IsChildOf(transform) && !hit.collider.transform.IsChildOf(couch.transform) &&
                    (loader == null || !hit.collider.transform.IsChildOf(loader.transform))) return false;
            foreach(Collider hit in Physics.OverlapBox(end,Vector3.Scale(bounds.extents,couch.transform.lossyScale),m_couchAnchor.rotation,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(couch.transform))return false;
            return true;
        }
        private bool TryGetCouchExit(CouchCarryController couch, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.right, Vector3.up));
            Bounds bounds = couch.CargoBounds;
            Vector3 scale = couch.transform.lossyScale;
            Vector3 extents = Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
            Vector3 offset = Vector3.Scale(bounds.center, scale);
            for (int ring=0; ring<3; ring++)
                foreach (float side in new[] {0f,1f,-1f})
                {
                    Vector3 target = transform.position - transform.forward * (m_settings.DetachmentDistance + ring) + transform.right * side * 1.5f;
                    if (!TryGround(target + Vector3.up * 4f, 8f, out RaycastHit ground) || Vector3.Angle(ground.normal,Vector3.up)>m_settings.MaximumSlopeAngle) continue;
                    Vector3 candidate = ground.point + Vector3.up * (extents.y + .08f) - rotation * offset;
                    bool blocked = false;
                    foreach (Collider hit in Physics.OverlapBox(candidate + rotation * offset, extents + Vector3.one * .025f, rotation, ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.transform.IsChildOf(couch.transform)) { blocked = true; break; }
                    if (!blocked) { position=candidate; return true; }
                }
            return false;
        }
        public bool TryGetDriverExit(CharacterController capsule, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.Euler(0,transform.eulerAngles.y,0);
            for (int ring=0; ring<3; ring++) foreach (float side in new[] {1f,-1f})
            {
                Vector3 target = MountPosition + transform.right * side * (m_settings.DismountDistance + ring * .7f);
                if (!TryGround(target+Vector3.up*3f, 7f, out RaycastHit ground) || ground.normal.y < .75f) continue;
                float radius = capsule.radius; float height = Mathf.Max(capsule.height, radius*2);
                Vector3 p = ground.point + Vector3.up * .08f - rotation * capsule.center + Vector3.up * (height*.5f);
                Vector3 centre = p + rotation * capsule.center;
                bool blocked = false;
                foreach (Collider hit in Physics.OverlapCapsule(centre+Vector3.up*(height*.5f-radius),centre-Vector3.up*(height*.5f-radius),radius,~0,QueryTriggerInteraction.Ignore))
                    if (hit!=capsule) { blocked=true; break; }
                if (!blocked) { position=p; return true; }
            }
            return false;
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (IsServerInitialized && !m_body.isKinematic && collision.contactCount > 0 && Mathf.Abs(collision.GetContact(0).normal.y)<.5f)
                m_body.linearVelocity *= 1f-m_settings.CollisionBraking;
        }
    }
}
