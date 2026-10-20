using System;
using System.Collections.Generic;
using System.Numerics;

namespace FAROSceneHelloWorld.Geometry
{
    /// <summary>
    /// Pure C# 3D k-d Tree for high-speed k-nearest neighbor spatial search queries.
    /// </summary>
    public class KdTree3D
    {
        private class Node
        {
            public int PointIndex;
            public Vector3 Position;
            public int Axis; // 0=X, 1=Y, 2=Z
            public Node Left;
            public Node Right;
        }

        private Node _root;
        private readonly PointCloud _cloud;

        public KdTree3D(PointCloud cloud)
        {
            _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            if (cloud.Count > 0)
            {
                int[] indices = new int[cloud.Count];
                for (int i = 0; i < cloud.Count; i++) indices[i] = i;
                _root = BuildTree(indices, 0, indices.Length, 0);
            }
        }

        private Node BuildTree(int[] indices, int start, int length, int depth)
        {
            if (length <= 0) return null;

            int axis = depth % 3;
            int mid = start + length / 2;

            // Median selection via 3-way quickselect
            QuickSelect(indices, start, start + length - 1, mid, axis);

            int medianIdx = indices[mid];
            int leftLength = mid - start;
            int rightLength = length - leftLength - 1;

            var node = new Node
            {
                PointIndex = medianIdx,
                Position = _cloud.Points[medianIdx].Position,
                Axis = axis,
                Left = BuildTree(indices, start, leftLength, depth + 1),
                Right = BuildTree(indices, mid + 1, rightLength, depth + 1)
            };

            return node;
        }

        private void QuickSelect(int[] indices, int left, int right, int k, int axis)
        {
            while (left < right)
            {
                int mid = left + (right - left) / 2;
                float pivotVal = GetAxisValue(_cloud.Points[indices[mid]].Position, axis);

                int i = left;
                int lt = left;
                int gt = right;

                while (i <= gt)
                {
                    float val = GetAxisValue(_cloud.Points[indices[i]].Position, axis);
                    if (val < pivotVal)
                    {
                        Swap(indices, lt, i);
                        lt++;
                        i++;
                    }
                    else if (val > pivotVal)
                    {
                        Swap(indices, i, gt);
                        gt--;
                    }
                    else
                    {
                        i++;
                    }
                }

                if (k < lt)
                {
                    right = lt - 1;
                }
                else if (k > gt)
                {
                    left = gt + 1;
                }
                else
                {
                    return;
                }
            }
        }

        private static void Swap(int[] array, int i, int j)
        {
            int tmp = array[i];
            array[i] = array[j];
            array[j] = tmp;
        }

        private static float GetAxisValue(Vector3 v, int axis)
        {
            return axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);
        }

        /// <summary>
        /// Finds the k-nearest neighbor point indices to the specified query location.
        /// </summary>
        public void FindKNearest(Vector3 query, int k, List<int> resultIndices, List<float> resultDistances = null)
        {
            if (resultIndices == null) throw new ArgumentNullException(nameof(resultIndices));
            resultIndices.Clear();
            resultDistances?.Clear();

            if (_root == null || k <= 0) return;

            // Max-Heap to track best k neighbors: items store (PointIndex, DistSq)
            var heap = new List<NeighborItem>(k + 1);

            SearchNode(_root, query, k, heap);

            // Sort neighbors ascending
            heap.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

            for (int i = 0; i < heap.Count; i++)
            {
                resultIndices.Add(heap[i].Index);
                if (resultDistances != null)
                {
                    resultDistances.Add((float)Math.Sqrt(heap[i].DistanceSq));
                }
            }
        }

        private struct NeighborItem
        {
            public int Index;
            public float DistanceSq;

            public NeighborItem(int index, float distSq)
            {
                Index = index;
                DistanceSq = distSq;
            }
        }

        private void SearchNode(Node node, Vector3 query, int k, List<NeighborItem> heap)
        {
            if (node == null) return;

            float distSq = Vector3.DistanceSquared(query, node.Position);

            if (heap.Count < k)
            {
                heap.Add(new NeighborItem(node.PointIndex, distSq));
                UpHeap(heap, heap.Count - 1);
            }
            else if (distSq < heap[0].DistanceSq)
            {
                heap[0] = new NeighborItem(node.PointIndex, distSq);
                DownHeap(heap, 0);
            }

            float axisDelta = GetAxisValue(query, node.Axis) - GetAxisValue(node.Position, node.Axis);
            Node nearChild = axisDelta <= 0 ? node.Left : node.Right;
            Node farChild = axisDelta <= 0 ? node.Right : node.Left;

            SearchNode(nearChild, query, k, heap);

            // Check if far child sphere can contain closer points
            float maxDistSq = (heap.Count < k) ? float.MaxValue : heap[0].DistanceSq;
            if (axisDelta * axisDelta < maxDistSq)
            {
                SearchNode(farChild, query, k, heap);
            }
        }

        private static void UpHeap(List<NeighborItem> heap, int idx)
        {
            while (idx > 0)
            {
                int parent = (idx - 1) / 2;
                if (heap[idx].DistanceSq > heap[parent].DistanceSq)
                {
                    var tmp = heap[idx];
                    heap[idx] = heap[parent];
                    heap[parent] = tmp;
                    idx = parent;
                }
                else break;
            }
        }

        private static void DownHeap(List<NeighborItem> heap, int idx)
        {
            int count = heap.Count;
            while (true)
            {
                int largest = idx;
                int left = 2 * idx + 1;
                int right = 2 * idx + 2;

                if (left < count && heap[left].DistanceSq > heap[largest].DistanceSq) largest = left;
                if (right < count && heap[right].DistanceSq > heap[largest].DistanceSq) largest = right;

                if (largest != idx)
                {
                    var tmp = heap[idx];
                    heap[idx] = heap[largest];
                    heap[largest] = tmp;
                    idx = largest;
                }
                else break;
            }
        }
    }
}
