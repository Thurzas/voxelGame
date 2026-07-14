using System;
using Unity.Collections;

namespace VoxelGame.Data
{
    /// <summary>
    /// Small dense voxel grid attached to an octree node (GigaVoxels §5.1.1/§5.1.2).
    /// Starts dense on generation; call <see cref="TryCompact"/> once filled to
    /// collapse homogeneous bricks (e.g. solid stone, pure air) down to a single
    /// constant value and free the backing array.
    /// </summary>
    public struct VoxelBrick : IDisposable
    {
        public const int Size = 16; // voxels per axis; revisit after profiling (roadmap phase 4)
        public const int VoxelCount = Size * Size * Size;

        private NativeArray<Voxel> voxels;

        public bool IsHomogeneous { get; private set; }
        public Voxel HomogeneousValue { get; private set; }

        public static VoxelBrick CreateHomogeneous(Voxel value)
        {
            return new VoxelBrick
            {
                IsHomogeneous = true,
                HomogeneousValue = value,
            };
        }

        public static VoxelBrick CreateDense(Allocator allocator)
        {
            return new VoxelBrick
            {
                IsHomogeneous = false,
                voxels = new NativeArray<Voxel>(VoxelCount, allocator, NativeArrayOptions.ClearMemory),
            };
        }

        public Voxel Get(int x, int y, int z)
        {
            if (IsHomogeneous)
            {
                return HomogeneousValue;
            }

            CheckDense();
            return voxels[Index(x, y, z)];
        }

        public void Set(int x, int y, int z, Voxel value)
        {
            CheckDense();
            voxels[Index(x, y, z)] = value;
        }

        /// <summary>
        /// Scans the dense grid; if every voxel shares the same value, frees the
        /// backing array and switches this brick to a constant/homogeneous
        /// representation (GigaVoxels §5.1.2 constant-region compression).
        /// Returns true if the brick was (or already is) homogeneous.
        /// </summary>
        public bool TryCompact()
        {
            if (IsHomogeneous)
            {
                return true;
            }

            CheckDense();

            Voxel first = voxels[0];
            for (int i = 1; i < VoxelCount; i++)
            {
                if (!voxels[i].Equals(first))
                {
                    return false;
                }
            }

            voxels.Dispose();
            voxels = default;
            IsHomogeneous = true;
            HomogeneousValue = first;
            return true;
        }

        /// <summary>
        /// Reverts a homogeneous brick back to a dense, writable grid filled with
        /// its constant value. No-op if already dense.
        /// </summary>
        public void Expand(Allocator expandAllocator)
        {
            if (!IsHomogeneous)
            {
                return;
            }

            voxels = new NativeArray<Voxel>(VoxelCount, expandAllocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < VoxelCount; i++)
            {
                voxels[i] = HomogeneousValue;
            }

            IsHomogeneous = false;
        }

        public void Dispose()
        {
            if (voxels.IsCreated)
            {
                voxels.Dispose();
            }
        }

        private void CheckDense()
        {
            if (IsHomogeneous)
            {
                throw new InvalidOperationException(
                    "Brick is compacted to a homogeneous constant; call Expand() before reading/writing individual voxels.");
            }
        }

        private static int Index(int x, int y, int z)
        {
            if ((uint)x >= Size || (uint)y >= Size || (uint)z >= Size)
            {
                throw new ArgumentOutOfRangeException($"Local voxel coordinate ({x},{y},{z}) is outside the brick (size {Size}).");
            }

            return x + (y * Size) + (z * Size * Size);
        }
    }
}