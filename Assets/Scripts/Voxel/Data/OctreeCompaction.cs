using Unity.Collections;

namespace VoxelGame.Data
{
    /// <summary>
    /// Builds LOD summary levels of a <see cref="VoxelOctree"/> bottom-up (GigaVoxels-style
    /// constant-region compaction across octree levels — see <see cref="VoxelBrick.TryCompact"/>
    /// for the equivalent within a single brick's dense grid). A parent node becomes an explicit
    /// Constant/Empty entry only when all 8 of its children are already explicit in the octree
    /// and agree on the same homogeneous value; the parent is deliberately left absent (no entry
    /// at all) whenever any child is missing, mixed (<see cref="OctreeNodeState.Brick"/>), or
    /// disagrees — by convention for this linear octree, "no entry at (key, level)" means "not
    /// summarizable here, a consumer must descend into children".
    /// </summary>
    public static class OctreeCompaction
    {
        /// <summary>
        /// Examines every distinct parent implied by <paramref name="childKeys"/> (expected to
        /// all share the same level) and writes a Constant/Empty node into <paramref name="octree"/>
        /// for each parent whose 8 children are all present and agree. Appends the parent keys
        /// that were actually written to <paramref name="writtenParents"/>, so callers can chain
        /// compaction level by level (feeding this call's output as the next call's
        /// <paramref name="childKeys"/>) without re-scanning the whole tree each time.
        /// </summary>
        public static void CompactLevel(VoxelOctree octree, NativeArray<OctreeNodeKey> childKeys, NativeList<OctreeNodeKey> writtenParents)
        {
            var visitedParents = new NativeHashSet<OctreeNodeKey>(childKeys.Length, Allocator.Temp);

            for (int i = 0; i < childKeys.Length; i++)
            {
                OctreeNodeKey parent = childKeys[i].Parent();
                if (!visitedParents.Add(parent))
                {
                    continue; // déjà traité via un autre enfant du même parent
                }

                if (TryCompactParent(octree, parent, out OctreeNode parentNode))
                {
                    octree.SetNode(parent, parentNode);
                    writtenParents.Add(parent);
                }
            }

            visitedParents.Dispose();
        }

        private static bool TryCompactParent(VoxelOctree octree, OctreeNodeKey parent, out OctreeNode result)
        {
            result = default;

            if (!octree.TryGetNode(parent.GetChild(0), out OctreeNode first))
            {
                return false;
            }
            if (first.State != OctreeNodeState.Constant && first.State != OctreeNodeState.Empty)
            {
                return false;
            }

            for (int c = 1; c < 8; c++)
            {
                if (!octree.TryGetNode(parent.GetChild(c), out OctreeNode child))
                {
                    return false;
                }
                if (child.State != first.State)
                {
                    return false;
                }
                if (first.State == OctreeNodeState.Constant && !child.ConstantValue.Equals(first.ConstantValue))
                {
                    return false;
                }
            }

            result = first.State == OctreeNodeState.Empty ? OctreeNode.Empty : OctreeNode.Constant(first.ConstantValue);
            return true;
        }
    }
}
