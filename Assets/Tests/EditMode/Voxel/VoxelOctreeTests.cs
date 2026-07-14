using NUnit.Framework;
using Unity.Collections;
using VoxelGame.Data;

namespace VoxelGame.Data.Tests
{
    public class VoxelOctreeTests
    {
        [Test]
        public void InsertThenLookup_ReturnsSameNode()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            try
            {
                var key = OctreeNodeKey.FromCoordinates(1, 2, 3, level: 4);
                var node = OctreeNode.Constant(new Voxel(VoxelType.Stone));

                octree.SetNode(key, node);

                Assert.IsTrue(octree.TryGetNode(key, out var found));
                Assert.AreEqual(OctreeNodeState.Constant, found.State);
                Assert.AreEqual(VoxelType.Stone, found.ConstantValue.type);
            }
            finally
            {
                octree.Dispose();
            }
        }

        [Test]
        public void SameMorton_DifferentLevel_AreDistinctNodes()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            try
            {
                ulong morton = MortonCode.Encode(10, 20, 30);
                var keyShallow = new OctreeNodeKey(morton, level: 1);
                var keyDeep = new OctreeNodeKey(morton, level: 5);

                octree.SetNode(keyShallow, OctreeNode.Constant(new Voxel(VoxelType.Water)));
                octree.SetNode(keyDeep, OctreeNode.Constant(new Voxel(VoxelType.Lava)));

                Assert.IsTrue(octree.TryGetNode(keyShallow, out var shallow));
                Assert.IsTrue(octree.TryGetNode(keyDeep, out var deep));
                Assert.AreEqual(VoxelType.Water, shallow.ConstantValue.type);
                Assert.AreEqual(VoxelType.Lava, deep.ConstantValue.type);
                Assert.AreEqual(2, octree.Count);
            }
            finally
            {
                octree.Dispose();
            }
        }

        [Test]
        public void SetNode_OverwritesExistingValue()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            try
            {
                var key = OctreeNodeKey.FromCoordinates(0, 0, 0, level: 0);
                octree.SetNode(key, OctreeNode.Constant(new Voxel(VoxelType.Air)));
                octree.SetNode(key, OctreeNode.Constant(new Voxel(VoxelType.Grass)));

                Assert.AreEqual(1, octree.Count);
                Assert.IsTrue(octree.TryGetNode(key, out var found));
                Assert.AreEqual(VoxelType.Grass, found.ConstantValue.type);
            }
            finally
            {
                octree.Dispose();
            }
        }

        [Test]
        public void RemoveNode_MakesLookupFail()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            try
            {
                var key = OctreeNodeKey.FromCoordinates(5, -5, 5, level: 2);
                octree.SetNode(key, OctreeNode.WithBrick(brickIndex: 7));

                Assert.IsTrue(octree.RemoveNode(key));
                Assert.IsFalse(octree.ContainsNode(key));
                Assert.IsFalse(octree.TryGetNode(key, out _));
                Assert.AreEqual(0, octree.Count);
            }
            finally
            {
                octree.Dispose();
            }
        }

        [Test]
        public void TryGetNode_MissingKey_ReturnsFalse()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            try
            {
                var key = OctreeNodeKey.FromCoordinates(100, 100, 100, level: 3);
                Assert.IsFalse(octree.TryGetNode(key, out var node));
                Assert.AreEqual(default(OctreeNode).State, node.State);
            }
            finally
            {
                octree.Dispose();
            }
        }
    }
}