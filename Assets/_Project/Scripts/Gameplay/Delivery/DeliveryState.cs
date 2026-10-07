namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Replicated, server-owned lifecycle for the single team delivery.</summary>
    public enum DeliveryState
    {
        Unavailable,
        Available,
        NPCInteraction,
        Accepted,
        CouchSpawned,
        Transport,
        DestinationReached,
        Completed,
        Reward,
        ReturnToNPC,
        PartyWipe,
        Failed,
        Resetting
    }
}
