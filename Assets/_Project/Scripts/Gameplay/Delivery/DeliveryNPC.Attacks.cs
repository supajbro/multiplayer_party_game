using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public sealed partial class DeliveryNPC
    {
        [Header("Attack Reactions (no damage or ragdoll)")]
        [SerializeField, Min(0f)] private float m_attackKnockbackMultiplier = .65f;
        [SerializeField, Min(0f)] private float m_rocketBounceForce = 8f;
        [SerializeField, Min(.1f)] private float m_angryExpressionDuration = 3f;
        [SerializeField, Min(0f)] private float m_aggressiveDialogueCooldown = 1.5f;
        [SerializeField, Min(0f)] private float m_grappleRetaliationForce = 22f;
        [SerializeField, Min(.1f)] private float m_maximumReactionDistance = 2.5f;
        [SerializeField, TextArea(1, 3)] private string[] m_aggressiveLines =
        {
            "BACK OFF, MEATBAG!", "TOUCH ME AGAIN. I DARE YOU.",
            "I'M REPORTING YOU TO MANAGEMENT!", "THAT'S COMING OUT OF YOUR PAY!",
            "I HAVE CAMERAS, IDIOT!", "DO I LOOK LIKE A PUNCHING BAG?",
            "YOU'RE LUCKY I'M ON WHEELS!"
        };

        private Vector3 m_reactionAnchor, m_reactionOffset, m_reactionVelocity;
        private Quaternion m_reactionRotation;
        private float m_reactionAge, m_nextAggressiveDialogueAt;
        private bool m_reacting;
        private uint m_seenAggressiveLine;
        private DispatcherScreen m_attackScreen;
        private DispatcherIdleBanter m_attackBanter;

        // The dispatcher deliberately has no health component. Server hit handlers route
        // force here, while its existing roaming pose channel replicates the motion.
        public void ReactToAttackServer(Vector3 direction, float force, float rocketStrength = 0f)
        {
            if (m_deliveryManager == null || !m_deliveryManager.IsServerInitialized) return;
            AnnoyServer();
            BeginReaction(direction, Mathf.Max(0f, force),
                Mathf.Max(0f, m_rocketBounceForce) * Mathf.Clamp01(rocketStrength));
        }

        public void RepelGrappleServer(PlayerHealth attacker)
        {
            if (m_deliveryManager == null || !m_deliveryManager.IsServerInitialized || attacker == null) return;
            AnnoyServer(); // No dispatcher impulse or attachment, including on repeated grapples.
            Vector3 away = Vector3.ProjectOnPlane(attacker.transform.position - transform.position, Vector3.up);
            if (away.sqrMagnitude < .001f) away = -attacker.transform.forward;
            attacker.ApplyDispatcherRetaliationServer(away.normalized, Mathf.Max(0f, m_grappleRetaliationForce));
        }

        private void AnnoyServer()
        {
            string line = null;
            if (Time.time >= m_nextAggressiveDialogueAt)
            {
                // Skip blank configurable entries without a retry loop or changing normal banter's deck.
                int count = m_aggressiveLines != null ? m_aggressiveLines.Length : 0;
                int start = count > 0 ? Random.Range(0, count) : 0;
                for (int i = 0; i < count; i++)
                {
                    string candidate = m_aggressiveLines[(start + i) % count];
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    line = candidate;
                    break;
                }
                m_nextAggressiveDialogueAt = Time.time + Mathf.Max(0f, m_aggressiveDialogueCooldown);
            }
            m_deliveryManager.PublishDispatcherAttackServer(m_angryExpressionDuration, line);
        }

        private void Update()
        {
            if (m_deliveryManager == null || !m_deliveryManager.IsClientInitialized) return;
            m_attackScreen ??= GetComponent<DispatcherScreen>();
            m_attackBanter ??= GetComponent<DispatcherIdleBanter>();
            m_deliveryManager.GetDispatcherAttackPresentation(out bool angry, out uint revision,
                out string line, out float remaining);
            m_attackScreen?.SetAngry(angry);
            if (revision == m_seenAggressiveLine) return;
            m_seenAggressiveLine = revision;
            if (remaining > 0f) m_attackBanter?.ShowAggressive(line, remaining);
        }

        private void BeginReaction(Vector3 direction, float force, float lift)
        {
            if (!m_reacting)
            {
                m_reactionAnchor = transform.position;
                m_reactionRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                m_reactionOffset = m_reactionVelocity = Vector3.zero;
            }
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < .001f) direction = -transform.forward;
            m_reactionVelocity = Vector3.ClampMagnitude(m_reactionVelocity +
                direction.normalized * force * Mathf.Max(0f, m_attackKnockbackMultiplier), 12f);
            m_reactionVelocity.y = Mathf.Clamp(Mathf.Max(m_reactionVelocity.y, lift), 0f, 12f);
            m_reactionAge = 0f;
            m_reacting = true;
        }

        // Called only by server roaming, before normal movement. No Rigidbody or client
        // simulation can compete with that transform writer. Always return to the pre-hit pose.
        internal bool TickAttackReactionServer(float deltaTime)
        {
            if (!m_reacting) return false;
            float remaining = Mathf.Clamp(deltaTime, 0f, .1f);
            while (remaining > 0f)
            {
                float dt = Mathf.Min(.02f, remaining);
                remaining -= dt;
                m_reactionAge += dt;
                Vector3 horizontal = Vector3.ProjectOnPlane(m_reactionOffset, Vector3.up);
                Vector3 velocity = Vector3.ProjectOnPlane(m_reactionVelocity, Vector3.up);
                if (m_reactionAge > .3f)
                {
                    // Critically damped recovery, bounded even if a moving blocker enters the path.
                    horizontal = Vector3.SmoothDamp(horizontal, Vector3.zero, ref velocity, .28f, Mathf.Infinity, dt);
                }
                else
                {
                    Vector3 requested = velocity * dt;
                    Vector3 allowed = ConstrainReactionStep(m_reactionAnchor + m_reactionOffset, requested);
                    if (allowed.sqrMagnitude + .000001f < requested.sqrMagnitude) velocity = Vector3.zero;
                    horizontal += allowed;
                    velocity *= Mathf.Exp(-4f * dt);
                }
                float maximumDistance = Mathf.Max(.1f, m_maximumReactionDistance);
                if (horizontal.sqrMagnitude >= maximumDistance * maximumDistance)
                {
                    horizontal = Vector3.ClampMagnitude(horizontal, maximumDistance);
                    if (Vector3.Dot(horizontal, velocity) > 0f) velocity = Vector3.zero;
                }
                m_reactionVelocity = velocity + Vector3.up * (m_reactionVelocity.y - 20f * dt);
                float verticalStep = m_reactionVelocity.y * dt;
                if (verticalStep > 0f)
                {
                    float clearStep = ConstrainReactionStep(m_reactionAnchor + horizontal +
                        Vector3.up * m_reactionOffset.y, Vector3.up * verticalStep).y;
                    if (clearStep < verticalStep) m_reactionVelocity.y = 0f;
                    verticalStep = clearStep;
                }
                float height = m_reactionOffset.y + verticalStep;
                if (height < 0f)
                {
                    height = 0f;
                    m_reactionVelocity.y = m_reactionAge < 1.8f && m_reactionVelocity.y < -1f
                        ? -m_reactionVelocity.y * .42f : 0f;
                }
                m_reactionOffset = horizontal + Vector3.up * height;
            }
            transform.SetPositionAndRotation(m_reactionAnchor + m_reactionOffset, m_reactionRotation);
            if (m_reactionAge >= 2.5f)
            {
                transform.SetPositionAndRotation(m_reactionAnchor, m_reactionRotation);
                m_reactionVelocity = m_reactionOffset = Vector3.zero;
                m_reacting = false;
            }
            return true;
        }

        private Vector3 ConstrainReactionStep(Vector3 position, Vector3 step)
        {
            float distance = step.magnitude;
            if (distance < .00001f) return Vector3.zero;
            foreach (RaycastHit hit in Physics.CapsuleCastAll(position + Vector3.up * .52f,
                position + Vector3.up * 1.68f, .48f, step / distance, distance + .03f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                    distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .03f));
            }
            return step.normalized * distance;
        }

        private void OnDisable()
        {
            if (m_reacting) transform.SetPositionAndRotation(m_reactionAnchor, m_reactionRotation);
            m_reacting = false;
            m_seenAggressiveLine = 0;
            m_attackScreen?.SetAngry(false);
        }
    }
}
