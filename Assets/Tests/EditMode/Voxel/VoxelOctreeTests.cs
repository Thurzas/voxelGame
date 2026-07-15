using System;
using NUnit.Framework;
using Unity.Collections;
using VoxelGame.Data;

namespace VoxelGame.Data.Tests
{
    public class VoxelOctreeTests
    {
        [Test]
        public void ParentThenGetChild_RoundTrips_RandomCoverage()
        {
            // Validé au préalable hors-Unity (harnais dotnet, 20000 cas) : un simple décalage de
            // bits Morton (Morton >> 3 / << 3) déborde immédiatement car MortonCode.Encode ajoute
            // le Bias à chaque coordonnée avant interleaving (bits de poids fort déjà occupés à
            // tout niveau) — Parent()/GetChild() passent donc par un décodage/réencodage de
            // coordonnées. Ce test couvre le même round-trip côté Unity.
            var rng = new Random(20260715);
            for (int i = 0; i < 5000; i++)
            {
                int x = rng.Next(-1000, 1000);
                int y = rng.Next(-1000, 1000);
                int z = rng.Next(-1000, 1000);
                byte level = (byte)rng.Next(1, 10);
                int childIndex = rng.Next(0, 8);

                var key = OctreeNodeKey.FromCoordinates(x, y, z, level);
                OctreeNodeKey child = key.GetChild(childIndex);
                OctreeNodeKey backToParent = child.Parent();

                Assert.AreEqual(key, backToParent, $"GetChild({childIndex}).Parent() != original pour ({x},{y},{z}) niveau {level}");
            }
        }

        [Test]
        public void GetChild_MatchesExpectedCoordinateOffset()
        {
            var rng = new Random(20260715);
            for (int i = 0; i < 2000; i++)
            {
                int x = rng.Next(-500, 500);
                int y = rng.Next(-500, 500);
                int z = rng.Next(-500, 500);
                byte level = (byte)rng.Next(1, 8);
                var parent = OctreeNodeKey.FromCoordinates(x, y, z, level);

                for (int c = 0; c < 8; c++)
                {
                    OctreeNodeKey child = parent.GetChild(c);
                    MortonCode.Decode(child.Morton, out int cx, out int cy, out int cz);

                    Assert.AreEqual(x * 2 + (c & 1), cx, $"x pour enfant {c}");
                    Assert.AreEqual(y * 2 + ((c >> 1) & 1), cy, $"y pour enfant {c}");
                    Assert.AreEqual(z * 2 + ((c >> 2) & 1), cz, $"z pour enfant {c}");
                    Assert.AreEqual(level - 1, child.Level);
                }
            }
        }

        [Test]
        public void GetChild_OnLeafLevel_Throws()
        {
            var leaf = OctreeNodeKey.FromCoordinates(0, 0, 0, level: 0);
            Assert.Throws<InvalidOperationException>(() => leaf.GetChild(0));
        }

        [Test]
        public void GetChild_InvalidIndex_Throws()
        {
            var key = OctreeNodeKey.FromCoordinates(0, 0, 0, level: 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => key.GetChild(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => key.GetChild(-1));
        }

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