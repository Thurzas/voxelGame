using System;
using NUnit.Framework;
using Unity.Collections;
using VoxelGame.Data;

namespace VoxelGame.Data.Tests
{
    public class VoxelBrickTests
    {
        [Test]
        public void DenseBrick_SetThenGet_ReturnsWrittenValue()
        {
            var brick = VoxelBrick.CreateDense(Allocator.Temp);
            try
            {
                brick.Set(1, 2, 3, new Voxel(VoxelType.Stone));
                Assert.AreEqual(VoxelType.Stone, brick.Get(1, 2, 3).type);
                // Untouched voxel defaults to Air (ClearMemory => VoxelType 0 == Air).
                Assert.AreEqual(VoxelType.Air, brick.Get(0, 0, 0).type);
            }
            finally
            {
                brick.Dispose();
            }
        }

        [Test]
        public void TryCompact_UniformBrick_BecomesHomogeneousAndFreesMemory()
        {
            var brick = VoxelBrick.CreateDense(Allocator.Temp);
            for (int x = 0; x < VoxelBrick.Size; x++)
            for (int y = 0; y < VoxelBrick.Size; y++)
            for (int z = 0; z < VoxelBrick.Size; z++)
            {
                brick.Set(x, y, z, new Voxel(VoxelType.Stone));
            }

            bool compacted = brick.TryCompact();

            Assert.IsTrue(compacted);
            Assert.IsTrue(brick.IsHomogeneous);
            Assert.AreEqual(VoxelType.Stone, brick.HomogeneousValue.type);
            // Homogeneous bricks answer any coordinate with the constant value.
            Assert.AreEqual(VoxelType.Stone, brick.Get(0, 0, 0).type);
            Assert.AreEqual(VoxelType.Stone, brick.Get(15, 15, 15).type);

            brick.Dispose(); // no-op: backing array was already freed by TryCompact
        }

        [Test]
        public void TryCompact_NonUniformBrick_StaysDense()
        {
            var brick = VoxelBrick.CreateDense(Allocator.Temp);
            try
            {
                brick.Set(0, 0, 0, new Voxel(VoxelType.Stone));
                // Everything else defaults to Air => not uniform.

                bool compacted = brick.TryCompact();

                Assert.IsFalse(compacted);
                Assert.IsFalse(brick.IsHomogeneous);
                Assert.AreEqual(VoxelType.Stone, brick.Get(0, 0, 0).type);
                Assert.AreEqual(VoxelType.Air, brick.Get(1, 1, 1).type);
            }
            finally
            {
                brick.Dispose();
            }
        }

        [Test]
        public void HomogeneousBrick_WriteWithoutExpand_Throws()
        {
            var brick = VoxelBrick.CreateHomogeneous(new Voxel(VoxelType.Air));
            Assert.Throws<InvalidOperationException>(() => brick.Set(0, 0, 0, new Voxel(VoxelType.Stone)));
        }

        [Test]
        public void Expand_HomogeneousBrick_BecomesWritableDenseCopy()
        {
            var brick = VoxelBrick.CreateHomogeneous(new Voxel(VoxelType.Dirt));
            try
            {
                brick.Expand(Allocator.Temp);

                Assert.IsFalse(brick.IsHomogeneous);
                Assert.AreEqual(VoxelType.Dirt, brick.Get(4, 4, 4).type);

                brick.Set(4, 4, 4, new Voxel(VoxelType.Grass));
                Assert.AreEqual(VoxelType.Grass, brick.Get(4, 4, 4).type);
            }
            finally
            {
                brick.Dispose();
            }
        }

        [Test]
        public void Get_OutOfBoundsLocalCoordinate_Throws()
        {
            var brick = VoxelBrick.CreateDense(Allocator.Temp);
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => brick.Get(VoxelBrick.Size, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => brick.Get(0, -1, 0));
            }
            finally
            {
                brick.Dispose();
            }
        }
    }
}