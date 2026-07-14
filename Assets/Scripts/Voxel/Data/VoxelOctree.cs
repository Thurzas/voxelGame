using System;
using Unity.Collections;

namespace VoxelGame.Data
{
    /// <summary>
    /// Flat (linear) octree: nodes are addressed directly by their
    /// <see cref="OctreeNodeKey"/> (Morton code + level) in a hash map, instead of
    /// a pointer-based tree. Replaces the World.cs
    /// <c>Dictionary&lt;Vector2Int, Chunk&gt;</c> lookup (see roadmap phase 1).
    /// </summary>
    public struct VoxelOctree : IDisposable
    {
        private NativeParallelHashMap<OctreeNodeKey, OctreeNode> nodes;

        public VoxelOctree(int initialCapacity, Allocator allocator)
        {
            nodes = new NativeParallelHashMap<OctreeNodeKey, OctreeNode>(initialCapacity, allocator);
        }

        public bool IsCreated => nodes.IsCreated;

        public int Count => nodes.Count();

        public bool ContainsNode(OctreeNodeKey key) => nodes.ContainsKey(key);

        public bool TryGetNode(OctreeNodeKey key, out OctreeNode node) => nodes.TryGetValue(key, out node);

        /// <summary>Inserts or replaces the node at <paramref name="key"/>.</summary>
        public void SetNode(OctreeNodeKey key, OctreeNode node)
        {
            // Remove-then-add rather than relying on indexer upsert semantics:
            // guarantees correct insert-or-replace behaviour regardless of the
            // exact NativeParallelHashMap version in use.
            nodes.Remove(key);
            nodes.Add(key, node);
        }

        public bool RemoveNode(OctreeNodeKey key) => nodes.Remove(key);

        public void Clear() => nodes.Clear();

        public void Dispose()
        {
            if (nodes.IsCreated)
            {
                nodes.Dispose();
            }
        }
    }
}