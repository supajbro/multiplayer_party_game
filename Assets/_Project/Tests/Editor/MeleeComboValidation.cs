using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    // Explicit diagnostic, invoked by menu or a Temp/MeleeValidation.request file.
    // Never rebuilds saved assets or saves scene changes.
    [InitializeOnLoad]
    public sealed class MeleeComboValidation
    {
        private const string Request = "Temp/MeleeValidation.request";
        private const string Report = "Temp/MeleeValidation.txt";
        private const string Pending = "MeleeValidation.Pending";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator s_probe;
        private static NetworkManager s_network;
        private static bool s_ownsNetwork;
        private static float s_deadline;
        private static float s_resumeAt;

        static MeleeComboValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    SessionState.SetBool(Pending, false);
                    s_deadline = Time.realtimeSinceStartup + 40f;
                    s_resumeAt = 0f;
                    s_probe = Probe();
                }
            };
        }

        [Test]
        public void ExistingControllerUsesAuthoredPunchClipsAndTorsoMask()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/CouchGuy.controller");
            var layer = controller.layers.Single(l => l.name == "Punches");
            foreach (var pair in new[] { ("Punch_Left", PlayerWeaponController.JabDuration), ("Punch_Right", PlayerWeaponController.CrossDuration) })
            {
                var clip = (AnimationClip)layer.stateMachine.states.Single(s => s.state.name == pair.Item1).state.motion;
                Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo("Assets/_Project/Models/CouchGuy/CouchGuy_WithPunches.fbx"));
                Assert.That(clip.length, Is.EqualTo(pair.Item2).Within(.034f));
                var curves = AnimationUtility.GetCurveBindings(clip);
                Assert.That(curves.Any(c => c.path.EndsWith("/Torso") && c.propertyName.Contains("Rotation")), Is.True);
            }
            foreach (string bone in new[] { "Torso", "Torso/Head" })
            {
                int index = Enumerable.Range(0, layer.avatarMask.transformCount)
                    .Single(i => layer.avatarMask.GetTransformPath(i) == "CouchGuy_Rig/Root/" + bone);
                Assert.That(layer.avatarMask.GetTransformActive(index), Is.True, "Inactive punch mask path: " + bone);
            }
        }

        [MenuItem("Couch Guys/Weapons/Validate Melee Combo")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            File.WriteAllText(Report, "START: focused melee validation\n");
            AssetDatabase.ImportAsset("Assets/_Project/Animations/CouchGuy.controller", ImportAssetOptions.ForceUpdate);
            new MeleeComboValidation().ExistingControllerUsesAuthoredPunchClipsAndTorsoMask();
            Log("PASS: existing FBX clip references, lengths and torso/head mask.");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static object Get(object o, string field) => o.GetType().GetField(field, Flags).GetValue(o);
        private static void Set(object o, string field, object value) => o.GetType().GetField(field, Flags).SetValue(o, value);
        private static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags).Invoke(o, args);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Log(string message) => File.AppendAllText(Report, message + "\n");
        private static void Tick()
        {
            if (s_probe == null)
            {
                if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                File.Delete(Request);
                try { Run(); } catch (Exception e) { Log("FAIL: " + e); }
                return;
            }
            bool done = false;
            try
            {
                Check(Time.realtimeSinceStartup < s_deadline, "Runtime probe timed out.");
                if (Time.time < s_resumeAt) return;
                done = !s_probe.MoveNext();
                if (!done && s_probe.Current is float resumeAt) s_resumeAt = resumeAt;
            }
            catch (Exception e) { Log("FAIL: " + e); done = true; }
            if (!done) return;
            s_probe = null;
            if (s_ownsNetwork && s_network != null)
            {
                s_network.ClientManager.StopConnection();
                s_network.ServerManager.StopConnection(true);
            }
            EditorApplication.ExitPlaymode();
        }

        private static GameObject Spawn(string path, Vector3 position, bool owned = false)
        {
            var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), position, Quaternion.identity);
            var input = obj.GetComponent<CouchGuys.Input.PlayerInputReader>();
            if (input != null) input.enabled = false;
            var movement = obj.GetComponent<ThirdPersonPlayerController>();
            if (movement != null) movement.enabled = false;
            var navigator = obj.GetComponent<ThiefNavigator>();
            if (navigator != null) navigator.enabled = false;
            var weapon = obj.GetComponent<EnemyWeapon>();
            if (weapon != null) weapon.enabled = false;
            var agent = obj.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            s_network.ServerManager.Spawn(obj, owned ? s_network.ClientManager.Connection : null);
            return obj;
        }

        private static IEnumerator Probe()
        {
            s_network = Object.FindFirstObjectByType<NetworkManager>();
            if (s_network == null)
                s_network = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab")).GetComponent<NetworkManager>();
            s_ownsNetwork = !s_network.IsServerStarted;
            Check(s_ownsNetwork, "Probe requires a stopped network session.");
            var spawner = s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();
            if (spawner != null) Object.DestroyImmediate(spawner);
            s_network.ServerManager.StartConnection();
            s_network.ClientManager.StartConnection();
            while (s_network.ServerManager.Clients.Count == 0 || !s_network.IsClientStarted ||
                   !s_network.ClientManager.Connection.IsValid) yield return null;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Melee probe floor";
            floor.transform.position = new Vector3(200, -.5f, 200);
            floor.transform.localScale = new Vector3(40, 1, 40);
            Vector3 origin = new Vector3(200, .05f, 200);
            var attacker = Spawn("Assets/_Project/Prefabs/Player.prefab", origin, true);
            var victim = Spawn("Assets/_Project/Prefabs/Player.prefab", origin + Vector3.forward * 1.1f);
            var melee = attacker.GetComponent<PlayerWeaponController>();
            var health = victim.GetComponent<PlayerHealth>();
            while (!melee.IsClientInitialized) yield return null;
            attacker.GetComponent<NetworkObject>().GiveOwnership(s_network.ClientManager.Connection);
            while (!melee.IsOwner) yield return null;
            yield return Time.time + .25f;
            attacker.GetComponent<ThirdPersonPlayerController>().enabled = false;
            attacker.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled = false;
            attacker.transform.SetPositionAndRotation(origin, Quaternion.identity);
            victim.transform.SetPositionAndRotation(origin + Vector3.forward * 1.1f, Quaternion.identity);
            Physics.SyncTransforms();
            float hp = health.CurrentHealth;
            Call(melee, "RequestPunchServerRpc");
            Check(health.CurrentHealth == hp, "Jab damaged before contact.");
            while ((byte)Get(melee, "m_punchStage") == 0) yield return null;
            yield return Time.time + .1f;
            Check(health.CurrentHealth == hp, "Jab damaged during wind-up.");
            yield return Time.time + .13f;
            Check(Mathf.Approximately(hp - health.CurrentHealth, 25), "Jab expected 25 damage, got " + (hp - health.CurrentHealth) +
                "; stage=" + Get(melee, "m_punchStage") + "; impact=" + Get(melee, "m_punchImpactApplied") +
                "; attacker=" + attacker.transform.position + "; victim=" + victim.transform.position);
            Check(attacker.GetComponent<CouchGuyAnimationDriver>().Animator.GetLayerWeight(
                attacker.GetComponent<CouchGuyAnimationDriver>().Animator.GetLayerIndex("Punches")) > .9f, "Host observer did not play jab.");
            Log("PASS: owning host RPC, wind-up has no damage, impact hits player once; observer jab layer active.");
            // A fresh press during jab recovery must buffer the opposite-arm cross.
            Call(melee, "RequestPunchServerRpc");
            yield return Time.time + .23f;
            Check((byte)Get(melee, "m_punchStage") == 2, "Second press did not buffer cross.");
            var thiefObject = Spawn("Assets/_Project/Prefabs/Enemies/Thief.prefab", origin + Vector3.forward * 1.1f);
            var thief = thiefObject.GetComponent<ThiefHealth>();
            victim.transform.position = origin + Vector3.right * 10;
            Physics.SyncTransforms();
            float thiefHp = thief.CurrentHealth;
            yield return Time.time + .23f;
            Check(Mathf.Approximately(thiefHp - thief.CurrentHealth, 37.5f), "Cross damage multiplier or per-target dedup failed.");
            var ragdoll = thiefObject.GetComponentInChildren<PedestrianRagdoll>();
            Check(ragdoll != null && ragdoll.IsFallen, "Cross did not trigger thief ragdoll.");
            Check(thief.IsAlive && thief.IsBeingKnockedBack, "Living thief lost normal recovery state.");
            Check(ragdoll.Body.linearVelocity.z > 1f, "Cross ragdoll did not launch away from attacker.");
            Log("PASS: buffered cross deals 1.5x damage once and launches a living thief through existing ragdoll state.");
            yield return Time.time + .3f;
            Call(melee, "RequestPunchServerRpc");
            yield return Time.time + .06f;
            Check((byte)Get(melee, "m_punchStage") == 0, "Combo cooldown allowed another punch.");
            yield return Time.time + .65f;
            Call(melee, "RequestPunchServerRpc");
            float requestDeadline = Time.time + .35f;
            while ((byte)Get(melee, "m_punchStage") == 0 && Time.time < requestDeadline) yield return null;
            Check((byte)Get(melee, "m_punchStage") == 1, "Cooldown did not release to jab; stage=" +
                Get(melee, "m_punchStage") + "; now=" + Time.time + "; deadline=" + Get(melee, "m_nextServerPunchTime") +
                "; canHandle=" + melee.CanHandleWeaponServer + "; owner=" + melee.IsOwner);
            yield return Time.time + .7f;
            Check((byte)Get(melee, "m_punchStage") == 0, "Single jab did not expire combo window.");
            Call(melee, "RequestPunchServerRpc");
            yield return Time.time + .06f;
            Check((byte)Get(melee, "m_punchStage") == 1, "Single jab incorrectly applied combo cooldown.");
            Log("PASS: cross cooldown blocks attacks, releases to jab; expired single jab has no combo cooldown.");
            // Item changes must cancel pending contact and queued combos.
            melee.AcquireWeaponServer(PlayerWeaponType.Pistol);
            yield return null;
            Check((byte)Get(melee, "m_punchStage") == 0, "Equipping weapon failed to cancel punch.");
            Call(melee, "RequestPunchServerRpc");
            yield return Time.time + .06f;
            Check((byte)Get(melee, "m_punchStage") == 0, "Equipped weapon allowed punching.");
            Log("PASS: weapon equip cancels pending attack and blocks unarmed requests.");
            Log("ALL FOCUSED HOST CHECKS PASSED. Remote-client latency and visual review still require two-process play.");
        }
    }
}
