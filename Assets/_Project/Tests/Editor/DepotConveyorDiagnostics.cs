using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Depot;
using UnityEditor;
using UnityEngine;
namespace CouchGuys.Tests.Editor
{
    public static class DepotConveyorDiagnostics
    {
        [MenuItem("Couch Guys/Depot 02/Inspect Couch Transfer %#&F10")]
        public static void Inspect()
        {
            var lines=new System.Collections.Generic.List<string>{"Playing="+Application.isPlaying+" simulation="+Physics.simulationMode+" fixed="+Time.fixedDeltaTime};
            foreach(var depot in Object.FindObjectsByType<DepotController>(FindObjectsSortMode.None))
                lines.Add($"Depot {depot.name} spawn={depot.CouchHiddenSpawn?.position} path={string.Join("; ",depot.ConveyorPath.Select(t=>t.position.ToString("F3")))}");
            foreach(var couch in Object.FindObjectsByType<CouchCarryController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var body=couch.CouchRigidbody;
                lines.Add($"Couch {couch.name}: spawned={couch.IsSpawned} server={couch.IsServerInitialized} client={couch.IsClientInitialized} pickup={couch.IsPickupAvailable} transform={couch.transform.position:F3} body={body?.position:F3} kinematic={body?.isKinematic} interpolation={body?.interpolation} active={couch.gameObject.activeInHierarchy} renderers={couch.GetComponentsInChildren<Renderer>().Count(r=>r.enabled)}");
            }
            File.WriteAllLines("Temp/couch-transfer-diagnostics.txt",lines);
        }
    }
}
