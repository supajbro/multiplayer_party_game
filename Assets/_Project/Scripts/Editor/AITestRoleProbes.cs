using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Object.Synchronizing;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Editor
{
    // Explicit, temporary runtime fixtures. No gameplay assets or role settings are modified.
    public static class AITestRoleProbes
    {
        private const string Report = "Logs/AITestRoleProbes.txt";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int s_phase;
        private static float s_at, s_deadline;
        private static DebugCouchBotController s_carrier, s_combat, s_support;
        private static ThiefHealth s_thief;
        private static ThiefSettings s_fixtureSettings;
        private static Vector3 s_fixturePosition;
        private static bool s_sawRevive;
        private static Rigidbody s_couchBody;
        private static RigidbodyConstraints s_constraints;
        private static readonly Dictionary<Behaviour, bool> s_paused = new();
        [InitializeOnLoadMethod]
        private static void Register() => EditorApplication.update += TickSafe;
        public static void Run()
        {
            File.WriteAllText(Report, "Controlled runtime role probes\n");
            s_phase = 1;
            s_deadline = Time.time + 100f;
            s_sawRevive = false;
        }
        private static void Note(string text) => File.AppendAllText(Report, $"t={Time.time:F2} {text}\n");
        private static void Pause(Behaviour behaviour)
        {
            if (behaviour == null) return;
            if (!s_paused.ContainsKey(behaviour))
                s_paused.Add(behaviour, behaviour.enabled);
            behaviour.enabled = false;
        }
        private static void Cleanup()
        {
            if (s_thief != null && s_thief.IsServerInitialized && s_thief.IsSpawned)
                FishNet.InstanceFinder.ServerManager.Despawn(s_thief.NetworkObject);
            s_thief = null;
            if (s_fixtureSettings != null) Object.DestroyImmediate(s_fixtureSettings);
            s_fixtureSettings = null;
            if (s_couchBody != null) s_couchBody.constraints = s_constraints;
            s_couchBody = null;
            foreach (var item in s_paused) if (item.Key != null) item.Key.enabled = item.Value;
            s_paused.Clear();
            s_phase = 0;
        }
        private static void Fail(string reason) { Note("FAIL: " + reason); Cleanup(); }
        private static void Next(int phase, float delay) { s_phase = phase; s_at = Time.time + delay; }
        private static bool Carrying => s_carrier.GetComponent<PlayerCouchCarrier>().IsCarrying;
        private static void Damage(int hits)
        {
            for (int i = 0; i < hits; i++) s_carrier.GetComponent<PlayerHealth>().ApplyDamageServer(1f, Vector3.forward, 0f);
        }
        private static void TickSafe()
        {
            try { Tick(); } catch (System.Exception error) { Fail(error.ToString()); }
        }
        private static void Tick()
        {
            if (s_phase == 0) return;
            if (!EditorApplication.isPlaying) { Cleanup(); return; }
            if (Time.time > s_deadline) { Fail($"Timeout in phase {s_phase}, carrier={s_carrier?.State}."); return; }
            if (s_thief != null)
            {
                Pause(s_thief.GetComponent<EnemyWeapon>());
                Pause(s_thief.GetComponent<ThiefNavigator>());
                var fixtureAgent = s_thief.GetComponent<NavMeshAgent>();
                if (fixtureAgent != null && fixtureAgent.enabled) fixtureAgent.Warp(s_fixturePosition);
                else s_thief.transform.position = s_fixturePosition;
            }
            if (Time.time < s_at) return;
            var manager = Object.FindFirstObjectByType<DeliveryManager>();
            if (s_phase == 1)
            {
                if (manager == null || manager.ActiveCouch == null || manager.ActiveCouch.transform.position.z < 25f) return;
                var bots = Object.FindObjectsByType<DebugCouchBotController>(FindObjectsSortMode.None);
                s_carrier = bots.FirstOrDefault(bot => bot.RoleSettings?.Role == AITeammateRole.Carrier);
                s_combat = bots.FirstOrDefault(bot => bot.RoleSettings?.Role == AITeammateRole.Combat);
                s_support = bots.FirstOrDefault(bot => bot.RoleSettings?.Role == AITeammateRole.Support);
                if (s_carrier == null || s_combat == null || s_support == null || !Carrying) return;
                s_couchBody = manager.ActiveCouch.GetComponent<Rigidbody>();
                s_constraints = s_couchBody.constraints;
                s_couchBody.constraints = RigidbodyConstraints.FreezeAll;
                foreach (var spawner in Object.FindObjectsByType<EnemySpawnManager>(FindObjectsSortMode.None)) Pause(spawner);
                foreach (var weapon in Object.FindObjectsByType<EnemyWeapon>(FindObjectsSortMode.None)) Pause(weapon);
                ((Queue<float>)typeof(DebugCouchBotController).GetField("m_damageTimes", Fields).GetValue(s_carrier)).Clear();
                Vector3 origin = s_carrier.transform.position + Vector3.right * 8f;
                if (!NavMesh.SamplePosition(origin, out var hit, 4f, NavMesh.AllAreas)) { Fail("No fixture position."); return; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab");
                s_fixturePosition = hit.position;
                var fixture = Object.Instantiate(prefab, hit.position, Quaternion.identity);
                foreach (var body in fixture.GetComponentsInChildren<Rigidbody>(true)) body.constraints = RigidbodyConstraints.FreezeAll;
                Pause(fixture.GetComponent<EnemyWeapon>());
                Pause(fixture.GetComponent<ThiefNavigator>());
                s_thief = fixture.GetComponent<ThiefHealth>();
                var healthData = new SerializedObject(s_thief);
                var settingsProperty = healthData.FindProperty("m_settings");
                s_fixtureSettings = Object.Instantiate((ThiefSettings)settingsProperty.objectReferenceValue);
                s_fixtureSettings.Health.MaxHealth = 10000f;
                s_fixtureSettings.Combat.DamagePerPellet = 0f;
                s_fixtureSettings.Combat.RetaliationChance = 0f;
                healthData.FindProperty("m_damageRagdollImpulseScale").floatValue = 0f;
                settingsProperty.objectReferenceValue = s_fixtureSettings;
                healthData.ApplyModifiedPropertiesWithoutUndo();
                FishNet.InstanceFinder.ServerManager.Spawn(fixture);
                Pause(fixture.GetComponent<EnemyWeapon>());
                Pause(fixture.GetComponent<ThiefNavigator>());
                Note("Spawned stationary threat using registered production thief prefab.");
                Next(2, 1f);
            }
            else if (s_phase == 2)
            {
                if (s_thief == null) { Fail("Fixture disappeared."); return; }
                if (s_thief.CurrentHealth >= 10000f && Time.time < s_at + 8f) return;
                if (s_thief.CurrentHealth >= 10000f) { Fail("Combat teammate did not damage nearby thief."); return; }
                Note("PASS: Combat teammate attacked nearby thief through existing weapon system.");
                if (!Carrying) { Fail("Carrier abandoned couch without sustained damage."); return; }
                Damage(1);
                Next(3, s_carrier.RoleSettings.CarrierDamageTimeWindow + 0.25f);
            }
            else if (s_phase == 3)
            {
                if (!Carrying || s_carrier.State == DebugCouchBotController.AiState.Combat) { Fail("One hit interrupted Carrier."); return; }
                Note("PASS: Carrier ignored a single hit and let it expire from the damage window.");
                Damage(s_carrier.RoleSettings.CarrierDamageHitThreshold - 1);
                Next(4, 0.25f);
            }
            else if (s_phase == 4)
            {
                if (!Carrying || s_carrier.State == DebugCouchBotController.AiState.Combat) { Fail("Expired hit counted toward new threshold."); return; }
                Note("PASS: Below-threshold hits preserved carrying; expired hit was excluded.");
                Damage(1);
                Next(5, 0.75f);
            }
            else if (s_phase == 5)
            {
                if (Carrying || s_carrier.State != DebugCouchBotController.AiState.Combat) { Fail($"Threshold state={s_carrier.State}, carrying={Carrying}, hits={((Queue<float>)typeof(DebugCouchBotController).GetField("m_damageTimes", Fields).GetValue(s_carrier)).Count}, threatDistance={Vector3.Distance(s_carrier.transform.position, s_thief.transform.position):F1}"); return; }
                Note("PASS: Configured hit threshold released couch and entered combat.");
                FishNet.InstanceFinder.ServerManager.Despawn(s_thief.NetworkObject);
                s_thief = null;
            if (s_fixtureSettings != null) Object.DestroyImmediate(s_fixtureSettings);
            s_fixtureSettings = null;
                Next(6, 0.5f);
            }
            else if (s_phase == 6)
            {
                if (!Carrying) return;
                Note("PASS: Carrier returned to couch after threat removal.");
                s_carrier.GetComponent<PlayerHealth>().ApplyDamageServer(1000f, Vector3.forward, 0f);
                Next(7, 0.25f);
            }
            else if (s_phase == 7)
            {
                s_sawRevive |= s_support.State == DebugCouchBotController.AiState.Revive;
                if (!s_carrier.GetComponent<PlayerHealth>().IsAlive || !Carrying) return;
                if (!s_sawRevive) { Fail("Revival was not performed by Support."); return; }
                Note("PASS: Support revived downed Carrier; Carrier resumed carrying.");
                Note("PASS: All controlled role probes completed.");
                Cleanup();
            }
        }
    }
}
