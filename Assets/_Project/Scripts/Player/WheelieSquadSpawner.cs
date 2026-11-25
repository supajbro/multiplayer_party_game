using System.Collections.Generic;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Networking;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CouchGuys.Player
{
    /// <summary>Server-only, all-or-nothing squad placement using the existing baked navigation.</summary>
    public static class WheelieSquadSpawner
    {
        public static bool TrySpawnServer(NetworkPlayerOwnership owner, WheelieSettings settings, Vector3? navigationOrigin = null)
        {
            if (owner == null || !owner.IsServerInitialized || !owner.IsSpawned || settings == null ||
                settings.Prefab == null || !settings.TrySelectWeapon(0f, out _)) return false;
            if (!settings.Prefab.TryGetComponent(out WheelieController controller) || controller.Settings != settings) return false;
            var leader = owner.GetComponent<PlayerHealth>();
            if (leader == null) return false;
            var positions = new List<Vector3>(settings.SquadSize);
            if (!TryFindPositions(navigationOrigin ?? leader.transform.position, settings, positions))
            {
                Debug.LogWarning("Wheelie squad: no room for four unobstructed, reachable members near this player.", owner);
                return false;
            }

            var spawned = new List<NetworkObject>(settings.SquadSize);
            int squad = WheelieController.NextSquadId();
            try
            {
                for (int i = 0; i < settings.SquadSize; i++)
                {
                    settings.TrySelectWeapon(Random.value, out PlayerWeaponType weapon);
                    NetworkObject instance = Object.Instantiate(settings.Prefab, positions[i], owner.transform.rotation);
                    spawned.Add(instance);
                    SceneManager.MoveGameObjectToScene(instance.gameObject, owner.gameObject.scene);
                    instance.GetComponent<WheelieController>().Initialise(leader.NetworkObject, squad, i, weapon);
                }
                // All four are fully configured before any become visible to observers.
                foreach (NetworkObject member in spawned) owner.ServerManager.Spawn(member);
                return true;
            }
            catch (System.Exception exception)
            {
                foreach (NetworkObject member in spawned)
                    if (member != null)
                    {
                        if (member.IsSpawned) owner.ServerManager.Despawn(member);
                        else Object.Destroy(member.gameObject);
                    }
                Debug.LogException(exception, owner);
                return false;
            }
        }

        internal static bool TryFindPositions(Vector3 centre, WheelieSettings settings, List<Vector3> result)
        {
            result.Clear();
            if (!NavMesh.SamplePosition(centre, out NavMeshHit anchor, settings.SpawnSearchRadius, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            float separation = Mathf.Max(settings.SquadMemberSpacing, settings.AgentRadius * 2f + settings.GroundClearance);
            float phase = Random.value * Mathf.PI * 2f;
            for (int attempt = 0; attempt < settings.SpawnPositionAttempts && result.Count < settings.SquadSize; attempt++)
            {
                float angle = phase + attempt * 2.39996323f; // evenly spread golden-angle candidates
                float radius = Mathf.Lerp(settings.SpawnRadius, settings.SpawnSearchRadius,
                    (float)attempt / Mathf.Max(1, settings.SpawnPositionAttempts - 1));
                Vector3 candidate = anchor.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, settings.NavigationSampleRadius, NavMesh.AllAreas) ||
                    Mathf.Abs(hit.position.y - anchor.position.y) > settings.MaximumNavigationHeightDifference ||
                    Vector3.Distance(hit.position, anchor.position) > settings.SpawnSearchRadius ||
                    !HasClearance(hit.position, settings) ||
                    !NavMesh.CalculatePath(anchor.position, hit.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                bool overlaps = false;
                foreach (Vector3 other in result)
                    overlaps |= Vector3.Distance(other, hit.position) < separation;
                if (!overlaps) result.Add(hit.position);
            }
            if (result.Count == settings.SquadSize) return true;
            result.Clear();
            return false;
        }

        internal static bool HasClearance(Vector3 position, WheelieSettings settings)
        {
            Vector3 bottom = position + Vector3.up * (settings.AgentRadius + settings.GroundClearance);
            Vector3 top = position + Vector3.up * Mathf.Max(settings.AgentRadius + settings.GroundClearance,
                settings.AgentHeight - settings.AgentRadius);
            return !Physics.CheckCapsule(bottom, top, settings.AgentRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
