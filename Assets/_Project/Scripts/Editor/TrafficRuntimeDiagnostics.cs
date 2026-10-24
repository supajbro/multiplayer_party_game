using System.IO;
using System.Reflection;
using System.Text;
using CouchGuys.Gameplay.Traffic;
using FishNet;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    public static class TrafficRuntimeDiagnostics
    {
        private static double s_next;
        static TrafficRuntimeDiagnostics() => EditorApplication.update += Capture;
        private static object Field(object value, string name) => value.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value);
        private static void Capture()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < s_next) return;
            s_next = EditorApplication.timeSinceStartup + 3;
            StringBuilder text = new();
            text.AppendLine($"time={Time.time} server={InstanceFinder.IsServerStarted} client={InstanceFinder.IsClientStarted}");
            foreach (TrafficManager manager in Object.FindObjectsByType<TrafficManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var graph = Field(manager, "m_laneGraph") as TrafficLaneGraph;
                text.AppendLine($"manager={manager.name} enabled={manager.isActiveAndEnabled} ready={Field(manager, "m_regionReady")} routes={graph?.Routes.Count} prefab={Field(manager, "m_carPrefab")} settings={Field(manager, "m_settings")} active={manager.ActiveVehicleCount} nextSpawn={Field(manager, "m_nextSpawnTime")}");
            }
            foreach (TrafficVehicleController car in Object.FindObjectsByType<TrafficVehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                text.AppendLine($"car={car.name} pos={car.transform.position} active={car.gameObject.activeInHierarchy} spawned={car.IsSpawned} serverDriving={Field(car, "m_serverDriving")} waypoint={Field(car, "m_waypointIndex")}");
                foreach (LODGroup lod in car.GetComponentsInChildren<LODGroup>(true))
                    text.AppendLine($"lod enabled={lod.enabled} active={lod.gameObject.activeInHierarchy} size={lod.size} levels={lod.lodCount} boundsCentre={lod.transform.TransformPoint(lod.localReferencePoint)}");
            }
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "TrafficRuntimeDiagnostics.txt"), text.ToString());
        }
    }
}
