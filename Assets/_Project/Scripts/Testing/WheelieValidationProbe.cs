using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Testing
{
    /// <summary>Only present in the explicitly authored Wheelie validation scene/build.</summary>
    public sealed class WheelieValidationProbe : MonoBehaviour
    {
        [SerializeField] private NetworkManager m_network;
        [SerializeField] private NetworkObject m_playerPrefab, m_hostilePrefab;
        [SerializeField] private WheelieSettings m_settings;
        [SerializeField] private JumpingPillowRewards m_rewardPillow;
        private bool m_clientOnly, m_finished;
        private string m_report;
        private readonly Dictionary<int, PlayerHealth> m_leaders = new();
        private int m_requested;
        private float m_nextRequest;
        private bool m_sawHealth, m_sawDeath, m_sawCombat, m_sawReload, m_sawMovement;
        private bool m_sawRewardCube, m_sawRewardSquad;
        private float m_nextRemoteReport;
        private readonly Dictionary<int, Vector3> m_firstPositions = new();
        private Transform m_pinnedHostile;
        private Vector3 m_hostilePosition;
        private void Update()
        {
            if (!m_clientOnly && m_pinnedHostile != null) m_pinnedHostile.position = m_hostilePosition;
        }

        private IEnumerator Start()
        {
            m_clientOnly = Environment.GetCommandLineArgs().Contains("--wheelie-client");
            WheelieSquadReward.ClientSpawned += OnRewardCubeClientSpawned;
            m_report = Path.Combine(Application.dataPath, "..", m_clientOnly ? "WheelieClientValidation.txt" : "WheelieHostValidation.txt");
            File.WriteAllText(m_report, "Wheelie " + (m_clientOnly ? "remote client" : "host") + " validation\n");
            UnityEngine.Random.InitState(3701);
            Application.runInBackground = true;
            m_network.TransportManager.Transport.SetServerBindAddress("127.0.0.1", FishNet.Transporting.IPAddressType.IPv4);
            if (!m_clientOnly)
            {
                m_network.SceneManager.OnClientLoadedStartScenes += SpawnLeader;
                m_network.ServerManager.StartConnection();
            }
            m_network.ClientManager.StartConnection("127.0.0.1");
            yield return null;
            if (!m_clientOnly) StartCoroutine(HostScenario());
            float timeout = Time.realtimeSinceStartup + 140f;
            while (!m_finished && Time.realtimeSinceStartup < timeout)
            {
                var local = NetworkPlayerOwnership.LocalPlayer;
                if (local != null && local.IsSpawned)
                {
                    var movement = local.GetComponent<ThirdPersonPlayerController>();
                    if (movement != null) movement.SetExternalControlPaused(true);
                    if (m_requested < (m_clientOnly ? 2 : 1) && Time.realtimeSinceStartup >= m_nextRequest)
                    {
                        local.DebugSpawnServerRpc(8); m_requested++; m_nextRequest = Time.realtimeSinceStartup + 1f;
                        Log("Requested squad through owned debug RPC.");
                    }
                }
                if (m_clientOnly) ObserveRemote();
                yield return null;
            }
            if (!m_finished) Finish(false, "Timed out; requested=" + m_requested);
        }

        private void SpawnLeader(NetworkConnection connection, bool asServer)
        {
            if (!asServer || m_leaders.ContainsKey(connection.ClientId)) return;
            m_network.SceneManager.AddConnectionToScene(connection, gameObject.scene);
            var player = Instantiate(m_playerPrefab, new Vector3(m_leaders.Count * 25f, 0f, 0f), Quaternion.identity);
            m_network.ServerManager.Spawn(player, connection);
            m_leaders.Add(connection.ClientId, player.GetComponent<PlayerHealth>());
        }

        private IEnumerator HostScenario()
        {
            float deadline = Time.realtimeSinceStartup + 75f;
            while ((m_leaders.Count < 2 || WheelieController.Instances.Count < 12) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Check(WheelieController.Instances.Count == 12 && m_leaders.Count == 2, "Three debug requests create exactly twelve members for two players.")) yield break;
            var all = WheelieController.Instances.ToArray();
            var squads = all.GroupBy(w => w.SquadId).ToArray();
            if (!Check(squads.Length == 3 && squads.All(g => g.Count() == 4 && g.Select(w => w.AssignedPlayer).Distinct().Count() == 1 &&
                g.Select(w => w.MemberIndex).OrderBy(i => i).SequenceEqual(new[] { 0, 1, 2, 3 })), "Each squad has four unique indices and one replicated leader.")) yield break;
            if (!Check(all.All(w => w.Settings == m_settings && w.GetComponent<PlayerHealth>().MaximumHealth == m_settings.MaximumHealth &&
                w.GetComponent<PlayerHealth>().CurrentHealth == m_settings.MaximumHealth &&
                WheelieSettings.IsStartingWeapon(w.GetComponent<PlayerWeaponController>().EquippedWeapon)), "Shared settings, independent full health and permitted starting weapons.")) yield break;
            var group = squads.First(g => g.First().AssignedPlayer.Owner.ClientId == 0).ToArray();
            if (!Check(group.All(w => !w.GetComponent<PlayerWeaponController>().CanHandleWeaponServer &&
                !w.GetComponent<PlayerWeaponController>().AcquireWeaponServer(PlayerWeaponType.RocketLauncher)), "Pickup and inventory acquisition are rejected.")) yield break;
            var leader = group[0].AssignedPlayer.GetComponent<PlayerHealth>();
            float[] original = group.Select(w => w.GetComponent<PlayerHealth>().CurrentHealth).ToArray();
            float configuredHealth = m_settings.MaximumHealth;
            group[0].GetComponent<PlayerHealth>().ApplyDamageServer(10f, Vector3.forward, 0f);
            yield return new WaitForSeconds(.4f);
            if (!Check(group[0].GetComponent<PlayerHealth>().CurrentHealth == original[0] - 10f &&
                group.Skip(1).All(w => w.GetComponent<PlayerHealth>().CurrentHealth == m_settings.MaximumHealth) &&
                m_settings.MaximumHealth == configuredHealth, "Damage changes one instance; other members and shared asset remain unchanged.")) yield break;
            Vector3 before = group[0].transform.position;
            leader.GetComponent<ThirdPersonPlayerController>().Teleport(new Vector3(0f, 0f, 9f), Quaternion.identity);
            yield return new WaitForSeconds(6f);
            if (!Check(group.All(w => Vector3.Distance(w.transform.position, leader.transform.position) < 6f) &&
                Vector3.Distance(before, group[0].transform.position) > 4f, "Squad follows its owner around the central obstacle.")) yield break;
            yield return ExercisePillowReward(leader);
            if (m_finished) yield break;
            leader.GetComponent<ThirdPersonPlayerController>().Teleport(new Vector3(0f, 0f, 9f), Quaternion.identity);
            yield return new WaitForSeconds(6f);
            leader.ApplyDamageServer(1000f, Vector3.back, 0f);
            yield return new WaitForSeconds(.5f);
            if (!Check(leader.IsDowned && group.All(w => w.AssignedPlayer == leader.NetworkObject), "Assignment persists while the owner is downed.")) yield break;
            // A stationary, high-health hostile exercises real magazine/reload/damage code.
            var targetObject = Instantiate(m_hostilePrefab, new Vector3(0f, 0f, 17f), Quaternion.identity);
            m_network.ServerManager.Spawn(targetObject);
            var target = targetObject.GetComponent<ThiefHealth>(); float startingHealth = target.CurrentHealth;
            m_pinnedHostile = target.transform; m_hostilePosition = target.transform.position;
            var wall = GameObject.Find("Navigation obstacle").transform;
            Vector3 wallPosition = wall.position;
            wall.position = new Vector3(0f, 1f, 13f); Physics.SyncTransforms();
            if (!Check(group.All(w => !w.GetComponent<PlayerWeaponController>().HasWheelieLineOfSight(target)), "Opaque obstacle blocks hostile line of sight.")) yield break;
            wall.position = wallPosition; Physics.SyncTransforms();
            m_hostilePosition = new Vector3(0f, 0f, 60f); target.transform.position = m_hostilePosition;
            yield return new WaitForSeconds(.8f);
            if (!Check(group.All(w => w.ServerTarget == null), "Hostiles outside the leader leash are rejected.")) yield break;
            m_hostilePosition = new Vector3(0f, 0f, 17f); target.transform.position = m_hostilePosition;
            bool reloaded = false, sawEmpty = false; int[] previousAmmo = group.Select(w => w.GetComponent<PlayerWeaponController>().CurrentMagazine).ToArray();
            float combatDeadline = Time.realtimeSinceStartup + 24f;
            while (Time.realtimeSinceStartup < combatDeadline)
            {
                for (int i = 0; i < group.Length; i++)
                {
                    var gun = group[i].GetComponent<PlayerWeaponController>();
                    sawEmpty |= gun.CurrentMagazine == 0 || gun.IsReloading;
                    reloaded |= gun.CurrentMagazine > previousAmmo[i]; previousAmmo[i] = gun.CurrentMagazine;
                }
                yield return null;
            }
            if (!Check(target.CurrentHealth < startingHealth && sawEmpty && reloaded,
                $"Protect downed owner with LOS combat and real reloads: damage={startingHealth-target.CurrentHealth}, empty={sawEmpty}, replenished={reloaded}; " +
                string.Join("; ", group.Select(w => w.GetComponent<PlayerWeaponController>().EquippedWeapon + ":" + w.GetComponent<PlayerWeaponController>().CurrentMagazine + " at " + w.transform.position)))) yield break;
            m_network.ServerManager.Despawn(targetObject);
            yield return new WaitForSeconds(1f);
            if (!Check(group.All(w => !w.Combat.Engaged), "Combat ends and members resume following.")) yield break;
            var victim = group[1].GetComponent<PlayerHealth>();
            victim.ApplyDamageServer(1000f, Vector3.right, 0f);
            yield return new WaitForSeconds(.5f);
            if (!Check(victim.IsDowned && group.Where((w, i) => i != 1).All(w => w.GetComponent<PlayerHealth>().IsAlive), "One member dies independently; its teammates remain alive.")) yield break;
            yield return new WaitForSeconds(m_settings.CorpseLifetime + .5f);
            if (!Check(victim == null, "Dead member despawns after its configured corpse lifetime.")) yield break;
            Finish(true, "Authoritative spawning, formation navigation, shared configuration, independent health, downed-owner protection, combat, reloads and death passed.");
        }

        private void ObserveRemote()
        {
            m_sawRewardCube |= FindObjectsByType<WheelieSquadReward>(FindObjectsSortMode.None).Any(c => c.IsClientInitialized && c.IsSpawned);
            var wheels = FindObjectsByType<WheelieController>(FindObjectsSortMode.None);
            foreach (var wheel in wheels)
            {
                if (!wheel.IsClientInitialized || !wheel.IsSpawned) continue;
                if (wheel.IsServerInitialized || wheel.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled)
                { Finish(false, "Remote client simulated AI navigation."); return; }
                if (wheel.AssignedPlayer == null) continue;
                var health = wheel.GetComponent<PlayerHealth>();
                m_sawHealth |= health.CurrentHealth > 0 && health.CurrentHealth < m_settings.MaximumHealth;
                m_sawDeath |= health.IsDowned;
                m_sawCombat |= wheel.Combat.Engaged;
                m_sawReload |= wheel.Combat.Reloading;
                int id = wheel.NetworkObject.ObjectId;
                if (!m_firstPositions.ContainsKey(id)) m_firstPositions[id] = wheel.transform.position;
                m_sawMovement |= Vector3.Distance(m_firstPositions[id], wheel.transform.position) > 4f;
            }
            bool ownSquads = wheels.Count(w => w.IsSpawned && w.AssignedPlayer != null &&
                w.AssignedPlayer.Owner.IsLocalClient) == 8;
            m_sawRewardSquad |= wheels.Count(w => w.IsSpawned && w.AssignedPlayer != null && !w.AssignedPlayer.Owner.IsLocalClient) >= 8;
            if (Time.realtimeSinceStartup >= m_nextRemoteReport)
            {
                m_nextRemoteReport = Time.realtimeSinceStartup + 5f;
                Log($"Remote: ownSquads={ownSquads}, cube={m_sawRewardCube}, rewardSquad={m_sawRewardSquad}, health={m_sawHealth}, death={m_sawDeath}, combat={m_sawCombat}, reload={m_sawReload}, moved={m_sawMovement}.");
            }
            if (ownSquads && m_sawHealth && m_sawDeath && m_sawCombat && m_sawReload && m_sawMovement && m_sawRewardCube && m_sawRewardSquad)
                Finish(true, "Two owned squads; server-only AI; reward cube and awarded squad, assignment, weapons, health, movement, combat, reload and death replicated.");
        }
        private void OnRewardCubeClientSpawned() { if (m_clientOnly) m_sawRewardCube = true; }
        private void OnDestroy() => WheelieSquadReward.ClientSpawned -= OnRewardCubeClientSpawned;

        private IEnumerator ExercisePillowReward(PlayerHealth leader)
        {
            if (!Check(m_rewardPillow != null, "Saved pillow reward fixture exists.")) yield break;
            var initial = WheelieController.Instances.ToArray();
            var surface = m_rewardPillow.GetComponent<JumpingPillow>().SurfaceCollider;
            Vector3 point = surface.bounds.center; point.y = surface.bounds.max.y + 1f;
            if (!surface.Raycast(new Ray(point, Vector3.down), out RaycastHit ground, surface.bounds.size.y + 2f))
            { Finish(false, "Cannot sample reward pillow surface."); yield break; }
            var capsule = leader.GetComponent<CharacterController>();
            var motor = leader.GetComponent<ThirdPersonPlayerController>();
            Vector3 launch = ground.point - Vector3.up * (capsule.center.y - capsule.height * .5f) + Vector3.up * .08f;
            motor.Teleport(launch, Quaternion.identity);
            yield return new WaitForSeconds(.2f);
            bool sawCube = false, rejectedAi = false;
            float gravity = -motor.Gravity, height = m_rewardPillow.GetComponent<JumpingPillow>().BounceHeight;
            float speed = Mathf.Sqrt(2f * gravity * height), duration = 2f * speed / gravity;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                Vector3 position = launch + Vector3.up * Mathf.Max(0f, speed * t - .5f * gravity * t * t);
                var cube = m_rewardPillow.ActiveCube;
                if (cube != null)
                {
                    sawCube = true;
                    if (!rejectedAi) rejectedAi = !cube.TryCollectServer(initial[0].GetComponent<Collider>());
                    Vector3 horizontal = Vector3.MoveTowards(new Vector3(leader.transform.position.x, 0f, leader.transform.position.z),
                        new Vector3(cube.transform.position.x, 0f, cube.transform.position.z), m_settings.MovementSpeed * Time.deltaTime);
                    position.x = horizontal.x; position.z = horizontal.z;
                }
                else { position.x = leader.transform.position.x; position.z = leader.transform.position.z; }
                motor.Teleport(position, Quaternion.identity);
                yield return null;
            }
            yield return new WaitForSeconds(.4f);
            var awarded = WheelieController.Instances.Where(w => !initial.Contains(w)).ToArray();
            if (!Check(sawCube && rejectedAi && awarded.Length == m_settings.SquadSize &&
                awarded.All(w => w.AssignedPlayer == leader.NetworkObject) && m_rewardPillow.ActiveCube == null,
                $"Real cube trigger awards exactly four to the jumping player, rejects AI and consumes once (cube={sawCube}, aiRejected={rejectedAi}, awarded={awarded.Length}).")) yield break;
            // A second qualifying bounce inside the cooldown cannot produce another cube/squad.
            motor.Teleport(launch, Quaternion.identity); yield return new WaitForSeconds(.2f);
            for (t = 0f; t < duration; t += Time.deltaTime)
            {
                motor.Teleport(launch + Vector3.up * Mathf.Max(0f, speed * t - .5f * gravity * t * t), Quaternion.identity);
                yield return null;
            }
            if (!Check(m_rewardPillow.ActiveCube == null && WheelieController.Instances.Count == initial.Length + m_settings.SquadSize,
                "Pillow cooldown prevents repeated rewards on consecutive bounces.")) yield break;
        }
        private bool Check(bool condition, string detail)
        { if (condition) { Log("PASS: " + detail); return true; } Finish(false, detail); return false; }
        private void Log(string message) => File.AppendAllText(m_report, message + "\n");
        private void Finish(bool success, string message)
        {
            if (m_finished) return; m_finished = true; Log((success ? "PASS: " : "FAIL: ") + message);
            if (Application.isEditor) Debug.Log(File.ReadAllText(m_report));
            else Application.Quit(success ? 0 : 1);
        }
    }
}
