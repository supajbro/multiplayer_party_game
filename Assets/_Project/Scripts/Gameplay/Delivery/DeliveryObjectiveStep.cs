namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Small replicated objective vocabulary shared by delivery UI and gameplay.</summary>
    public enum DeliveryObjectiveStep
    {
        None,
        AcceptDelivery,
        CarryCouch,
        LoadCouch,
        DriveToDestination,
        UnloadCouch,
        DeliverCouch,
        ReturnToSeller,
        ChapterComplete
    }
}
