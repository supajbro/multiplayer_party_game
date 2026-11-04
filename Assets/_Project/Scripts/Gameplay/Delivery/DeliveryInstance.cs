using FishNet;
using FishNet.CodeGenerating;
using FishNet.Object;

namespace CouchGuys.Gameplay.Delivery
{
    public struct DeliveryInstance
    {
        public int Id;
        public int TierIndex;
        public int DestinationIndex;
        public int CouchObjectId;
        public uint DeadlineTick;
        public float Duration;
        public DeliveryState Status;
        public int Reward;

        // Resolve lazily: late-join snapshots can arrive before the couch spawn packet.
        [ExcludeSerialization]
        public NetworkObject Couch
        {
            get
            {
                var manager = InstanceFinder.NetworkManager;
                if (CouchObjectId < 0 || manager == null) return null;
                if (manager.IsServerStarted && manager.ServerManager.Objects.Spawned.TryGetValue(CouchObjectId, out var serverCouch))
                    return serverCouch;
                return manager.ClientManager.Objects.Spawned.TryGetValue(CouchObjectId, out var clientCouch) ? clientCouch : null;
            }
        }
    }
}
