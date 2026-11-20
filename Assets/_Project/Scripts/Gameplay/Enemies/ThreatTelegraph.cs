using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    public enum ThreatAttackPhase : byte { Idle, Preparing, Locked, Committed, Recovering }

    // One snapshot keeps the target, phase and deadline together, including for late joiners.
    public struct ThreatAttackCue
    {
        public ThreatAttackPhase Phase;
        public uint StartedTick;
        public uint EndsTick;
        public NetworkObject Target;
        public Vector3 AimPoint;
    }

    /// <summary>Presentation of server attack decisions. All visuals and voices are saved in the prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class ThreatTelegraph : NetworkBehaviour
    {
        [SerializeField] private AudioSource m_arrivalSource;
        [SerializeField] private AudioSource m_attackSource;
        [SerializeField] private AudioClip m_arrivalClip;
        [SerializeField] private AudioClip m_preparationClip;
        [SerializeField] private AudioClip m_commitClip;
        [SerializeField] private AudioClip m_recoveryClip;
        [SerializeField] private Transform m_preparationSignal;
        [SerializeField] private Transform m_directionMarker;
        [SerializeField] private Transform m_recoverySignal;
        [SerializeField] private Transform m_identificationVisual;
        [SerializeField, Min(.1f)] private float m_pulseFrequency = 4f;
        [SerializeField, Range(0f, .5f)] private float m_pulseScale = .18f;
        [SerializeField, Min(1f)] private float m_directionLength = 2f;
        [SerializeField, Min(1f)] private float m_arrivalAudibleDistance = 35f;
        private readonly SyncVar<ThreatAttackCue> m_cue = new(new SyncTypeSettings(0f));
        private Vector3 m_signalScale;
        private ThiefHealth m_health;
        private Traffic.TrafficVehicleController m_vehicle;
        private bool m_arrivalPending;
        private Camera m_listenerCamera;

        public ThreatAttackPhase Phase => m_cue.Value.Phase;
        public bool IsPreparing => Phase == ThreatAttackPhase.Preparing || Phase == ThreatAttackPhase.Locked;
        public bool IsRecovering => Phase == ThreatAttackPhase.Recovering && Remaining > 0f;
        public float Remaining => TimeManager == null || TimeManager.Tick >= m_cue.Value.EndsTick
            ? 0f : (float)TimeManager.TicksToTime(m_cue.Value.EndsTick - TimeManager.Tick);
        public Vector3 AimPoint => Phase == ThreatAttackPhase.Preparing && m_cue.Value.Target != null &&
            m_cue.Value.Target.TryGetComponent(out Player.PlayerHealth player)
                ? player.AimPoint : m_cue.Value.AimPoint;

        private void Awake()
        {
            m_health = GetComponent<ThiefHealth>();
            m_vehicle = GetComponent<Traffic.TrafficVehicleController>();
            if (m_preparationSignal != null) m_signalScale = m_preparationSignal.localScale;
            m_cue.OnChange += CueChanged;
            HideSignals();
        }

        private void OnDestroy() => m_cue.OnChange -= CueChanged;

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_cue.Value = default;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            m_arrivalPending = true;
            PlayCurrentCue();
        }

        [Server]
        public void PrepareServer(NetworkObject target, Vector3 point, float duration) =>
            SetServer(ThreatAttackPhase.Preparing, duration, target, point);

        [Server]
        public void LockServer(Vector3 point)
        {
            if (!IsPreparing) return;
            var cue = m_cue.Value;
            cue.Phase = ThreatAttackPhase.Locked;
            cue.AimPoint = point;
            cue.Target = null;
            m_cue.Value = cue;
        }

        [Server]
        public void CommitServer(float duration) => SetServer(ThreatAttackPhase.Committed, duration, null, AimPoint);

        [Server]
        public void RecoverServer(float duration) => SetServer(ThreatAttackPhase.Recovering, duration, null, AimPoint);

        [Server]
        public void CancelServer()
        {
            if (Phase != ThreatAttackPhase.Idle) SetServer(ThreatAttackPhase.Idle, 0f, null, Vector3.zero);
        }

        private void SetServer(ThreatAttackPhase phase, float duration, NetworkObject target, Vector3 point)
        {
            uint tick = TimeManager.Tick;
            m_cue.Value = new ThreatAttackCue { Phase = phase, StartedTick = tick,
                EndsTick = tick + System.Math.Max(1u, TimeManager.TimeToTicks(Mathf.Max(0f, duration))),
                Target = target, AimPoint = point };
        }

        private void CueChanged(ThreatAttackCue previous, ThreatAttackCue next, bool asServer)
        {
            // Host presentation runs once on the client notification, not once per authority.
            if (asServer || previous.Phase == next.Phase ||
                (previous.Phase == ThreatAttackPhase.Preparing && next.Phase == ThreatAttackPhase.Locked)) return;
            PlayCurrentCue();
        }

        private void PlayCurrentCue()
        {
            if (!IsClientInitialized || m_attackSource == null) return;
            m_attackSource.Stop();
            AudioClip clip = IsPreparing ? m_preparationClip : Phase == ThreatAttackPhase.Committed
                ? m_commitClip : Phase == ThreatAttackPhase.Recovering ? m_recoveryClip : null;
            if (clip == null || Remaining <= 0f) return;
            float elapsed = (float)TimeManager.TicksToTime(TimeManager.Tick > m_cue.Value.StartedTick
                ? TimeManager.Tick - m_cue.Value.StartedTick : 0u);
            float duration = (float)TimeManager.TicksToTime(m_cue.Value.EndsTick - m_cue.Value.StartedTick);
            m_attackSource.pitch = IsPreparing ? Mathf.Clamp(clip.length / Mathf.Max(.01f, duration), .25f, 3f) : 1f;
            elapsed *= m_attackSource.pitch;
            if (elapsed >= clip.length) return;
            m_attackSource.clip = clip;
            m_attackSource.time = elapsed;
            m_attackSource.Play();
        }

        private void LateUpdate()
        {
            bool visible = IsClientInitialized && (m_health == null || (m_health.IsAlive && !m_health.IsBeingKnockedBack)) &&
                (m_vehicle == null || (!m_vehicle.IsDestroyed && !m_vehicle.IsBatLaunched));
            if (visible && m_arrivalPending)
            {
                if (m_listenerCamera == null) m_listenerCamera = Camera.main;
                if (m_listenerCamera != null && (m_listenerCamera.transform.position - transform.position).sqrMagnitude <=
                    m_arrivalAudibleDistance * m_arrivalAudibleDistance)
                {
                    m_arrivalPending = false;
                    if (m_arrivalSource != null && m_arrivalClip != null) m_arrivalSource.PlayOneShot(m_arrivalClip);
                }
            }
            bool preparing = visible && IsPreparing && Remaining > 0f;
            if (m_preparationSignal != null)
            {
                m_preparationSignal.gameObject.SetActive(preparing);
                if (preparing) m_preparationSignal.localScale = m_signalScale *
                    (1f + m_pulseScale * Mathf.Sin((float)TimeManager.TicksToTime(TimeManager.Tick) * m_pulseFrequency * Mathf.PI * 2f));
            }
            if (m_directionMarker != null)
            {
                m_directionMarker.gameObject.SetActive(preparing);
                Vector3 aim = Vector3.ProjectOnPlane(AimPoint - m_directionMarker.position, Vector3.up);
                if (preparing && aim.sqrMagnitude > .001f)
                {
                    m_directionMarker.rotation = Quaternion.LookRotation(aim);
                    var scale = m_directionMarker.localScale;
                    scale.z = Mathf.Min(m_directionLength, aim.magnitude);
                    m_directionMarker.localScale = scale;
                }
            }
            if (m_recoverySignal != null) m_recoverySignal.gameObject.SetActive(visible && IsRecovering);
            if (m_identificationVisual != null) m_identificationVisual.gameObject.SetActive(visible);
            if (!visible && m_attackSource != null) m_attackSource.Stop();
        }

        public override void OnStopClient() { m_arrivalPending = false; HideSignals(); base.OnStopClient(); }
        private void OnDisable() => HideSignals();
        private void HideSignals()
        {
            if (m_preparationSignal != null) m_preparationSignal.gameObject.SetActive(false);
            if (m_directionMarker != null) m_directionMarker.gameObject.SetActive(false);
            if (m_recoverySignal != null) m_recoverySignal.gameObject.SetActive(false);
            if (m_arrivalSource != null) m_arrivalSource.Stop();
            if (m_attackSource != null) m_attackSource.Stop();
        }
    }
}
