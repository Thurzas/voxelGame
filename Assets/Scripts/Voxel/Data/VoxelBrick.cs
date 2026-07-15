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

        // Buffer statique réutilisé par TryCompact (voir remarque ci-dessous), pour éviter
        // une allocation managée à chaque appel. La compaction se fait toujours de façon
        // séquentielle sur le thread principal, jamais en parallèle.
        private static Voxel[] compactScratch;

        /// <summary>
        /// Scans the dense grid; if every voxel shares the same value, frees the
        /// backing array and switches this brick to a constant/homogeneous
        /// representation (GigaVoxels §5.1.2 constant-region compression).
        /// Returns true if the brick was (or already is) homogeneous.
        /// </summary>
        /// <remarks>
        /// Copies the NativeArray into a managed buffer with a single bulk
        /// <see cref="NativeArray{T}.CopyTo(T[])"/> call before comparing, rather than
        /// reading <c>voxels[i]</c> element-by-element: outside a Burst-compiled job,
        /// every individual NativeArray access pays an atomic safety-handle check, which
        /// dominated chunk generation cost when this scanned all 4096 voxels one at a
        /// time (measured regression, see VoxelGame roadmap phase 4).
        /// </remarks>
        public bool TryCompact()
        {
            if (IsHomogeneous)
            {
                return true;
            }

            CheckDense();

            if (compactScratch == null)
            {
                compactScratch = new Voxel[VoxelCount];
            }
            voxels.CopyTo(compactScratch);

            Voxel first = compactScratch[0];
            for (int i = 1; i < VoxelCount; i++)
            {
                if (!compactScratch[i].Equals(first))
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
        /// Bulk-copies this brick's dense voxel data into <paramref name="destination"/>
        /// (which must be at least <see cref="VoxelCount"/> long), in the same linear
        /// order as <see cref="Index"/> (x fastest, then y, then z). A single
        /// <see cref="NativeArray{T}.CopyTo(T[])"/> call, not a per-element read — for the
        /// same reason as <see cref="TryCompact"/>.
        /// </summary>
        public void CopyDenseTo(Voxel[] destination)
        {
            CheckDense();
            voxels.CopyTo(destination);
        }

        /// <summary>
        /// Bulk-writes <paramref name="source"/> (at least <see cref="VoxelCount"/> long,
        /// same linear order as <see cref="Index"/>) into this brick's dense storage via a
        /// single <see cref="NativeArray{T}.CopyFrom(T[])"/> call, instead of calling
        /// <see cref="Set"/> once per voxel.
        /// </summary>
        public void CopyFromDense(Voxel[] source)
        {
            CheckDense();
            voxels.CopyFrom(source);
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