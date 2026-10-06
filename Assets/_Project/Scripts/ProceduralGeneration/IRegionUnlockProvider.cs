namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Progression/save systems implement this without coupling world generation to storage.
    /// RegionDefinition owns the configured cost; the provider owns player-specific state.
    /// </summary>
    public interface IRegionUnlockProvider
    {
        bool IsRegionUnlocked(RegionDefinition region);
    }
}
