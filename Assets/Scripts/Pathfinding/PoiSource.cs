using System.Collections.Generic;

namespace IA.Pathfinding.Voronoi
{
    internal struct PoiSource
    {
        public int poiId;
        public int nodeIndex;

        public PoiSource(int poiId, int nodeIndex)
        {
            this.poiId = poiId;
            this.nodeIndex = nodeIndex;
        }

        internal static Dictionary<int, int> BuildLookupByNodeIndex(List<PoiSource> sources)
        {
            Dictionary<int, int> lookup = new Dictionary<int, int>();
            for (int i = 0; i < sources.Count; i++)
            {
                PoiSource source = sources[i];
                if (lookup.TryGetValue(source.nodeIndex, out int existingPoi))
                {
                    if (source.poiId < existingPoi)
                        lookup[source.nodeIndex] = source.poiId;

                    continue;
                }

                lookup[source.nodeIndex] = source.poiId;
            }

            return lookup;
        }
    }
}
