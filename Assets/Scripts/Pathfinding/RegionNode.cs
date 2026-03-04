namespace IA.Pathfinding.Voronoi
{
    internal struct RegionNode
    {
        public int nodeIndex;
        public int poiId;
        public int distance;

        public RegionNode(int index, int poiId, int distance)
        {
            nodeIndex = index;
            this.poiId = poiId;
            this.distance = distance;
        }
    }
}
