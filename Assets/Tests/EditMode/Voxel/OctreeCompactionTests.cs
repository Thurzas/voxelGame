using System;
using NUnit.Framework;
using Unity.Collections;
using VoxelGame.Data;

namespace VoxelGame.Data.Tests
{
    public class OctreeCompactionTests
    {
        [Test]
        public void CompactLevel_EightIdenticalConstantChildren_MergesIntoParent()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            var childKeys = new NativeList<OctreeNodeKey>(8, Allocator.Temp);
            var written = new NativeList<OctreeNodeKey>(4, Allocator.Temp);
            try
            {
                var parentKey = OctreeNodeKey.FromCoordinates(3, -2, 5, level: 1);
                var value = new Voxel(VoxelType.Stone);
                for (int c = 0; c < 8; c++)
                {
                    OctreeNodeKey childKey = parentKey.GetChild(c);
                    octree.SetNode(childKey, OctreeNode.Constant(value));
                    childKeys.Add(childKey);
                }

                OctreeCompaction.CompactLevel(octree, childKeys.AsArray(), written);

                Assert.AreEqual(1, written.Length);
                Assert.AreEqual(parentKey, written[0]);
                Assert.IsTrue(octree.TryGetNode(parentKey, out OctreeNode parentNode));
                Assert.AreEqual(OctreeNodeState.Constant, parentNode.State);
                Assert.AreEqual(VoxelType.Stone, parentNode.ConstantValue.type);
            }
            finally
            {
                octree.Dispose();
                childKeys.Dispose();
                written.Dispose();
            }
        }

        [Test]
        public void CompactLevel_EightEmptyChildren_MergesIntoEmptyParent()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            var childKeys = new NativeList<OctreeNodeKey>(8, Allocator.Temp);
            var written = new NativeList<OctreeNodeKey>(4, Allocator.Temp);
            try
            {
                var parentKey = OctreeNodeKey.FromCoordinates(0, 0, 0, level: 1);
                for (int c = 0; c < 8; c++)
                {
                    OctreeNodeKey childKey = parentKey.GetChild(c);
                    octree.SetNode(childKey, OctreeNode.Empty);
                    childKeys.Add(childKey);
                }

                OctreeCompaction.CompactLevel(octree, childKeys.AsArray(), written);

                Assert.AreEqual(1, written.Length);
                Assert.IsTrue(octree.TryGetNode(parentKey, out OctreeNode parentNode));
                Assert.AreEqual(OctreeNodeState.Empty, parentNode.State);
            }
            finally
            {
                octree.Dispose();
                childKeys.Dispose();
                written.Dispose();
            }
        }

        [Test]
        public void CompactLevel_OneDifferentChild_LeavesParentAbsent()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            var childKeys = new NativeList<OctreeNodeKey>(8, Allocator.Temp);
            var written = new NativeList<OctreeNodeKey>(4, Allocator.Temp);
            try
            {
                var parentKey = OctreeNodeKey.FromCoordinates(1, 1, 1, level: 1);
                for (int c = 0; c < 8; c++)
                {
                    OctreeNodeKey childKey = parentKey.GetChild(c);
                    VoxelType type = c == 7 ? VoxelType.Dirt : VoxelType.Stone; // un enfant diffère
                    octree.SetNode(childKey, OctreeNode.Constant(new Voxel(type)));
                    childKeys.Add(childKey);
                }

                OctreeCompaction.CompactLevel(octree, childKeys.AsArray(), written);

                Assert.AreEqual(0, written.Length);
                Assert.IsFalse(octree.ContainsNode(parentKey));
            }
            finally
            {
                octree.Dispose();
                childKeys.Dispose();
                written.Dispose();
            }
        }

        [Test]
        public void CompactLevel_MissingChild_LeavesParentAbsent()
        {
            var octree = new VoxelOctree(16, Allocator.Temp);
            var childKeys = new NativeList<OctreeNodeKey>(7, Allocator.Temp);
            var written = new NativeList<OctreeNodeKey>(4, Allocator.Temp);
            try
            {
                var parentKey = OctreeNodeKey.FromCoordinates(-4, 2, 0, level: 1);
                var value = new Voxel(VoxelType.Water);
                for (int c = 0; c < 7; c++) // le 8e enfant n'est jamais écrit
                {
                    OctreeNodeKey childKey = parentKey.GetChild(c);
                    octree.SetNode(childKey, OctreeNode.Constant(value));
                    childKeys.Add(childKey);
                }

                OctreeCompaction.CompactLevel(octree, childKeys.AsArray(), written);

                Assert.AreEqual(0, written.Length);
                Assert.IsFalse(octree.ContainsNode(parentKey));
            }
            finally
            {
                octree.Dispose();
                childKeys.Dispose();
                written.Dispose();
            }
        }

        [Test]
        public void CompactLevel_BrickChild_NeverSummarized()
        {
            // Un nœud Brick (non-homogène) ne peut jamais être résumé par une seule valeur
            // constante : même si les 7 autres enfants sont d'accord, le parent doit rester absent.
            var octree = new VoxelOctree(16, Allocator.Temp);
            var childKeys = new NativeList<OctreeNodeKey>(8, Allocator.Temp);
            var written = new NativeList<OctreeNodeKey>(4, Allocator.Temp);
            try
            {
                var parentKey = OctreeNodeKey.FromCoordinates(2, 2, 2, level: 1);
                for (int c = 0; c < 8; c++)
                {
                    OctreeNodeKey childKey = parentKey.GetChild(c);
                    OctreeNode node = c == 0 ? OctreeNode.WithBrick(brickIndex: 42) : OctreeNode.Constant(new Voxel(VoxelType.Sand));
                    octree.SetNode(childKey, node);
                    childKeys.Add(childKey);
                }

                OctreeCompaction.CompactLevel(octree, childKeys.AsArray(), written);

                Assert.AreEqual(0, written.Length);
                Assert.IsFalse(octree.ContainsNode(parentKey));
            }
            finally
            {
                octree.Dispose();
                childKeys.Dispose();
                written.Dispose();
            }
        }

        [Test]
        public void CompactLevel_MultiLevel_MatchesBruteForceReference_RandomCoverage()
        {
            // Équivalent réduit (temps d'exécution NUnit raisonnable) du harnais hors-Unity utilisé
            // pour valider cet algorithme avant portage (500 essais, cubes jusqu'à 8x8x8) : compare
            // la compaction niveau par niveau à une référence force-brute directe sur un cube dense,
            // sans passer par Morton.
            var rng = new Random(20260715);

            for (int trial = 0; trial < 60; trial++)
            {
                int levels = rng.Next(1, 4);
                int size = 1 << levels;
                var dense = new int[size, size, size];
                int paletteSize = rng.Next(2, 4);
                for (int z = 0; z < size; z++)
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    dense[x, y, z] = rng.NextDouble() < 0.8 && x > 0
                        ? dense[x - 1, y, z]
                        : rng.Next(0, paletteSize);
                }

                // Origine alignée sur la plus grande taille de bloc testée : le grillage de
                // l'octree est ancré sur la coordonnée absolue 0, pas sur l'origine locale du
                // cube (même piège que rencontré et corrigé dans le harnais hors-Unity).
                int originX = rng.Next(-20, 20) * size;
                int originY = rng.Next(-20, 20) * size;
                int originZ = rng.Next(-20, 20) * size;

                var octree = new VoxelOctree(64, Allocator.Temp);
                var currentLevelKeys = new NativeList<OctreeNodeKey>(size * size * size, Allocator.Temp);
                try
                {
                    for (int z = 0; z < size; z++)
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var key = OctreeNodeKey.FromCoordinates(originX + x, originY + y, originZ + z, level: 0);
                        octree.SetNode(key, OctreeNode.Constant(new Voxel((VoxelType)dense[x, y, z])));
                        currentLevelKeys.Add(key);
                    }

                    for (int level = 0; level < levels; level++)
                    {
                        var written = new NativeList<OctreeNodeKey>(currentLevelKeys.Length, Allocator.Temp);
                        OctreeCompaction.CompactLevel(octree, currentLevelKeys.AsArray(), written);
                        currentLevelKeys.Dispose();
                        currentLevelKeys = written;
                    }

                    for (int level = 1; level <= levels; level++)
                    {
                        int blockSize = 1 << level;
                        for (int bz = 0; bz + blockSize <= size; bz += blockSize)
                        for (int by = 0; by + blockSize <= size; by += blockSize)
                        for (int bx = 0; bx + blockSize <= size; bx += blockSize)
                        {
                            int? uniform = BruteForceUniformValue(dense, bx, by, bz, blockSize);
                            var key = OctreeNodeKey.FromCoordinates(
                                FloorDiv(originX + bx, blockSize), FloorDiv(originY + by, blockSize), FloorDiv(originZ + bz, blockSize),
                                (byte)level);
                            bool found = octree.TryGetNode(key, out OctreeNode node);

                            if (uniform.HasValue)
                            {
                                Assert.IsTrue(found && node.State == OctreeNodeState.Constant && (int)node.ConstantValue.type == uniform.Value,
                                    $"Essai {trial}: bloc uniforme (niveau {level}, origine {bx},{by},{bz}) non compacté correctement");
                            }
                            else
                            {
                                Assert.IsFalse(found,
                                    $"Essai {trial}: bloc NON-uniforme (niveau {level}, origine {bx},{by},{bz}) compacté à tort");
                            }
                        }
                    }
                }
                finally
                {
                    currentLevelKeys.Dispose();
                    octree.Dispose();
                }
            }
        }

        private static int? BruteForceUniformValue(int[,,] dense, int ox, int oy, int oz, int size)
        {
            int first = dense[ox, oy, oz];
            for (int dz = 0; dz < size; dz++)
            for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
            {
                if (dense[ox + dx, oy + dy, oz + dz] != first)
                {
                    return null;
                }
            }
            return first;
        }

        private static int FloorDiv(int v, int divisor) => v >= 0 ? v / divisor : (v - divisor + 1) / divisor;
    }
}
