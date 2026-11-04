using System;
using System.Collections;
using System.IO;
using System.Linq;
using CouchGuys.Player;
using CouchGuys.Gameplay.Delivery;
using FishNet.Managing;
using UnityEngine;

namespace CouchGuys.Testing
{
    // Only activated by an explicit validation command-line argument.
    public sealed class AITestRemoteProbe : MonoBehaviour
    {
        public static bool ClientOnly => Environment.GetCommandLineArgs().Contains("--ai-test-client");
        private string m_report;
        private Vector3 m_firstCouchPosition;
        private bool m_havePosition, m_moved;
        private int m_firstAttempt;
        private float m_nextReport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequestedProbe()
        {
            if (!ClientOnly) return;
            var owner = FindFirstObjectByType<AITestController>();
            if (owner != null) owner.gameObject.AddComponent<AITestRemoteProbe>();
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--ai-probe-report");
            m_report = index >= 0 && index + 1 < args.Length ? args[index + 1] : "AITestRemoteProbe.txt";
            File.WriteAllText(m_report, "Remote client validation\n");
            float deadline = Time.realtimeSinceStartup + 420f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var network = FishNet.InstanceFinder.NetworkManager;
                var manager = FindFirstObjectByType<DeliveryManager>();
                var bots = FindObjectsByType<DebugCouchBotController>(FindObjectsSortMode.None);
                if (network != null && network.ServerManager.Started)
                {
                    Finish(false, "Client unexpectedly started server authority.");
                    yield break;
                }
                if (manager != null && bots.Length == 4 && network != null && network.ClientManager.Started)
                {
                    bool roles = bots.Select(bot => bot.RoleSettings != null ? (int)bot.RoleSettings.Role : -1)
                        .OrderBy(role => role).SequenceEqual(new[] { 0, 1, 2, 3 });
                    bool passive = bots.All(bot => !bot.IsAiTeammate && !bot.GetComponent<FishNet.Object.NetworkObject>().IsServerInitialized);
                    bool health = bots.All(bot => bot.GetComponent<PlayerHealth>().IsAiTeammate);
                    if (!roles || !passive || !health)
                    {
                        Finish(false, $"roles={roles} passive={passive} replicatedIdentity={health}");
                        yield break;
                    }
                    if (manager.ActiveCouch != null)
                    {
                        if (!m_havePosition) { m_firstCouchPosition = manager.ActiveCouch.transform.position; m_firstAttempt = manager.AttemptNumber; m_havePosition = true; }
                        m_moved |= Vector3.Distance(m_firstCouchPosition, manager.ActiveCouch.transform.position) > 8f;
                    }
                    if (Time.realtimeSinceStartup >= m_nextReport)
                    {
                        m_nextReport = Time.realtimeSinceStartup + 5f;
                        File.AppendAllText(m_report, $"bots=4 server=false roles=true passive=true identity=true moved={m_moved} attempt={manager.AttemptNumber} currency={manager.CurrentCurrency}\n");
                    }
                    if (m_moved && manager.AttemptNumber > m_firstAttempt && manager.CurrentCurrency > 0)
                    {
                        Finish(true, "Four distinct replicated roles; client AI stayed passive; couch motion, completion reward and next order synchronized.");
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Finish(false, "Timed out awaiting replicated delivery completion.");
        }
        private void Finish(bool success, string detail)
        {
            File.AppendAllText(m_report, (success ? "PASS: " : "FAIL: ") + detail + "\n");
            Application.Quit(success ? 0 : 1);
        }
    }
}
