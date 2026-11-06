using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Weapons;
using FishNet.Managing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    // Explicit diagnostic only; never authors or changes saved assets.
    [InitializeOnLoad]
    public static class WeaponAudioPlayProbe
    {
        private const string Pending = "WeaponAudioProbe.Pending";
        private const string Result = "Temp/WeaponAudioValidation.txt";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator s_routine;
        private static NetworkManager s_network;

        static WeaponAudioPlayProbe()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    SessionState.SetBool(Pending, false);
                    s_routine = Probe();
                    EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.ExitingPlayMode) EditorApplication.update -= Tick;
            };
        }

        [MenuItem("Couch Guys/Weapons/Test Multiplayer Audio %#&y")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            File.WriteAllText(Result, "START: weapon audio checks\n");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            bool failed = false;
            try { if (s_routine.MoveNext()) return; }
            catch (Exception e) { failed = true; Log("FAIL: " + e); }
            EditorApplication.update -= Tick;
            if (s_network != null)
            {
                s_network.ClientManager.StopConnection();
                s_network.ServerManager.StopConnection(true);
            }
            EditorApplication.ExitPlaymode();
            if (Application.isBatchMode) EditorApplication.Exit(failed ? 1 : 0);
        }

        private static void Log(string value)
        {
            File.AppendAllText(Result, value + "\n");
            Debug.Log("Weapon audio validation: " + value);
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }
        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Flags).Invoke(target, args);
        private static AudioSource[] Voices(string prefix) =>
            Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Where(s => s.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        private static double BusyUntil(WeaponAudioEmitter emitter, PlayerWeaponType type)
        {
            var pools = (IDictionary)typeof(WeaponAudioEmitter).GetField("m_gunshotVoices", Flags).GetValue(emitter);
            if (!pools.Contains(type)) return 0;
            double total = 0;
            foreach (object voice in (IEnumerable)pools[type])
                total += (double)voice.GetType().GetField("AvailableAt").GetValue(voice);
            return total;
        }

        private static IEnumerator Probe()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var settings = Resources.Load<WeaponAudioSettings>(WeaponAudioSettings.ResourcesPath);
            Check(settings != null && settings.VoicePrefab != null, "Missing saved settings/voice prefab.");
            string[] prefixes = { "sfx_wpn_hgn_gunshot_", "sfx_wpn_ar_gunshot_", "sfx_wpn_sgn_gunshot_" };
            for (int i = 0; i < 5; i++)
            {
                var profile = settings.GetProfile((PlayerWeaponType)i);
                Check(profile != null && profile.Gunshots.All(c => c != null), "Missing clips for " + i);
                Check(profile.MaximumDistance == 35 && profile.SpatialBlend == 1, "Incorrect attenuation for " + i);
                if (i < 3) Check(profile.Gunshots.Length == 3 && profile.Gunshots.All(c => c.name.StartsWith(prefixes[i])), "Wrong gunshots.");
            }
            Check(settings.GetProfile(PlayerWeaponType.GrappleGun).Gunshots.Length ==
                  AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio/GrappleGun" }).Length, "Incomplete grapple variations.");
            Check(settings.GetProfile(PlayerWeaponType.RocketLauncher).Gunshots.Single().name == "sfx_wpn_gl_fire" &&
                  settings.GetProfile(PlayerWeaponType.RocketLauncher).ExplosionClip.name == "sfx_wpn_gl_explosion", "Wrong rocket clips.");
            Log("PASS: saved voice prefab, all named clips, every grapple variation, full 3D blend and adjustable 35 m ranges.");

            var local = new GameObject("AudioProbe").AddComponent<WeaponAudioEmitter>();
            Vector3 position = new Vector3(13, 4, 9);
            for (int i = 0; i < 24; i++) local.PlayShot(PlayerWeaponType.AssaultRifle, position);
            var rapid = Voices("AssaultRifle");
            Check(rapid.Length == 24, "Rapid shots reused/truncated busy voices.");
            local.transform.position = Vector3.one * 100;
            foreach (var source in rapid)
            {
                Check(source.transform.position == position && source.transform.parent == null, "Shot follows moving shooter.");
                Check(!source.loop && !source.playOnAwake && source.spatialBlend == 1 && source.maxDistance == 35, "Incorrect 3D one-shot configuration.");
                var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                Check(curve.Evaluate(0) == 1 && curve.Evaluate(1) == 0 && curve.Evaluate(.5f) < 1, "Missing distance attenuation/cutoff.");
            }
            local.PlayShot(PlayerWeaponType.GrappleGun, position);
            Check(Voices("GrappleGun").Length == 1 && !Voices("GrappleGun")[0].loop, "Grapple must play once.");
            Log("PASS: 24 overlapping automatic shots without voice stealing; fixed world origins, rolloff to silence, nonlooping grapple.");
            Object.Destroy(local.gameObject);
            foreach (var source in rapid) if (source != null) Object.Destroy(source.gameObject);
            yield return null;

            var bootstrap = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_network = bootstrap.GetComponent<NetworkManager>();
            var spawner = s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();
            if (spawner != null) Object.DestroyImmediate(spawner);
            yield return null;
            s_network.ServerManager.StartConnection();
            s_network.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 12;
            while (s_network.ServerManager.Clients.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_network.ServerManager.Clients.Count > 0, "FishNet host did not connect.");
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            s_network.ServerManager.Spawn(player);
            yield return null;
            var controller = player.GetComponent<PlayerWeaponController>();
            var audio = player.GetComponent<WeaponAudioEmitter>();
            deadline = Time.realtimeSinceStartup + 5;
            while (!controller.IsClientInitialized && Time.realtimeSinceStartup < deadline) yield return null;
            Check(controller.IsClientInitialized, "Host client did not observe spawned player.");
            Check(!controller.IsOwner, "Observer probe must use an unowned player.");
            Call(controller, "ShowShotObserversRpc", 0, position, Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<byte>());
            Check(Voices("PistolVoice").Length == 1, "Host observer failed to play exactly one shot.");
            Call(controller, "ShowGrappleShotObserversRpc", position, false);
            Check(BusyUntil(audio, PlayerWeaponType.GrappleGun) > 0, "Missed grapple not audible.");
            double before = BusyUntil(audio, PlayerWeaponType.Pistol);
            player.GetComponent<FishNet.Object.NetworkObject>().GiveOwnership(s_network.ClientManager.Connection);
            yield return null;
            Check(controller.IsOwner, "Host ownership failed.");
            Call(controller, "ShowShotObserversRpc", 0, position, Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<byte>());
            Check(BusyUntil(audio, PlayerWeaponType.Pistol) == before, "Owner replayed predicted audio.");
            Log("PASS: FishNet host observer hears one shot; owner skips replicated shot; grapple miss event plays once.");

            var rocket = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Weapons/RocketLauncher/RocketProjectile.prefab"));
            s_network.ServerManager.Spawn(rocket);
            yield return null;
            Call(rocket.GetComponent<RocketProjectile>(), "PlayExplosionObserversRpc", position, position, 5f);
            s_network.ServerManager.Despawn(rocket);
            yield return null;
            var blast = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Where(s => s.name.StartsWith("WeaponAudioVoice", StringComparison.Ordinal)).ToArray();
            Check(blast.Length == 1 && blast[0].transform.position == position, "Explosion duplicated or died with projectile.");
            Check(!blast[0].loop && blast[0].maxDistance == 35, "Wrong explosion audio configuration.");
            float until = Time.time + settings.GetProfile(PlayerWeaponType.RocketLauncher).ExplosionClip.length + .3f;
            while (Time.time < until) yield return null;
            Check(blast[0] == null, "Explosion voice leaked.");
            Log("PASS: exactly one host explosion at impact survives projectile despawn and expires after clip duration.");
            Log("ALL TARGETED CHECKS PASSED. Separate remote-client listening still requires a two-process session.");
        }
    }
}
