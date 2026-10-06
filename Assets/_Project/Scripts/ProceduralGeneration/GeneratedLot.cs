using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [DisallowMultipleComponent]
    public sealed class GeneratedLot : MonoBehaviour
    {
        public LotDefinition Definition { get; private set; }
        public LotType LotType { get; private set; }
        public GridCoordinate AnchorCoordinate { get; private set; }

        public void Initialise(LotDefinition definition, GridCoordinate anchorCoordinate)
        {
            Definition = definition;
            LotType = definition != null ? definition.LotType : LotType.Residential;
            AnchorCoordinate = anchorCoordinate;
        }
    }
}
