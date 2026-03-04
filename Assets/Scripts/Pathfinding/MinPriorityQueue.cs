using System.Collections.Generic;

namespace IA.Pathfinding.Voronoi
{
    internal class MinPriorityQueue
    {
        readonly List<RegionNode> queue = new List<RegionNode>();

        public int Count => queue.Count;

        public void Enqueue(int nodeIndex, int poiId, int distance)
        {
            queue.Add(new RegionNode(nodeIndex, poiId, distance));
            BubbleUp(queue.Count - 1);
        }

        public RegionNode Dequeue()
        {
            RegionNode root = queue[0];

            int lastIndex = queue.Count - 1;
            queue[0] = queue[lastIndex];
            queue.RemoveAt(lastIndex);

            if (queue.Count > 0)
                BubbleDown(0);

            return root;
        }

        void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (queue[parent].distance <= queue[index].distance)
                    break;

                (queue[parent], queue[index]) = (queue[index], queue[parent]);
                index = parent;
            }
        }

        void BubbleDown(int index)
        {
            int last = queue.Count - 1;
            while (true)
            {
                int left = index * 2 + 1;
                if (left > last)
                    return;

                int right = left + 1;
                int smallest = left;
                if (right <= last && queue[right].distance < queue[left].distance)
                    smallest = right;

                if (queue[index].distance <= queue[smallest].distance)
                    return;

                (queue[index], queue[smallest]) = (queue[smallest], queue[index]);
                index = smallest;
            }
        }
    }
}
