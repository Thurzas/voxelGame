using System;

namespace VoxelGame.Data
{
    /// <summary>
    /// Identifies a node in the linear octree: the Morton code of its brick-space
    /// coordinate plus its level (depth). The level is required alongside the
    /// Morton code because the same Morton code is reused at every level (it only
    /// encodes a position, not a depth).
    /// </summary>
    public readonly struct OctreeNodeKey : IEquatable<OctreeNodeKey>
    {
        public readonly ulong Morton;
        public readonly byte Level;

        public OctreeNodeKey(ulong morton, byte level)
        {
            Morton = morton;
            Level = level;
        }

        public static OctreeNodeKey FromCoordinates(int x, int y, int z, byte level)
        {
            return new OctreeNodeKey(MortonCode.Encode(x, y, z), level);
        }

        public bool Equals(OctreeNodeKey other) => Morton == other.Morton && Level == other.Level;

        public override bool Equals(object obj) => obj is OctreeNodeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                // Hand-rolled FNV-ish combine: avoids System.HashCode, which is not
                // guaranteed Burst-compatible and uses a randomized per-process seed
                // (undesirable for a key whose hash may need to be reproducible).
                ulong h = (Morton * 1099511628211UL) ^ Level;
                return (int)(h ^ (h >> 32));
            }
        }

        public static bool operator ==(OctreeNodeKey a, OctreeNodeKey b) => a.Equals(b);
        public static bool operator !=(OctreeNodeKey a, OctreeNodeKey b) => !a.Equals(b);
    }

    public enum OctreeNodeState : byte
    {
        Empty = 0,    // no data at all (treated as air / fully transparent)
        Constant = 1, // homogeneous region, see ConstantValue
        Brick = 2,    // backed by a dense brick, see BrickIndex
    }

    /// <summary>
    /// Node payload stored per <see cref="OctreeNodeKey"/>. Deliberately does not
    /// embed a <see cref="VoxelBrick"/> (or its NativeArray) directly: Unity native
    /// containers should not be nested inside another native container's value
    /// type. Brick storage/pooling is wired up in a later phase (roadmap phase 5);
    /// BrickIndex is a forward-compatible placeholder for that pool slot.
    /// </summary>
    public struct OctreeNode
    {
        public OctreeNodeState State;
        public Voxel ConstantValue;
        public int BrickIndex;

        public static OctreeNode Empty => new OctreeNode { State = OctreeNodeState.Empty, BrickIndex = -1 };

        public static OctreeNode Constant(Voxel value) =>
            new OctreeNode { State = OctreeNodeState.Constant, ConstantValue = value, BrickIndex = -1 };

        public static OctreeNode WithBrick(int brickIndex) =>
            new OctreeNode { State = OctreeNodeState.Brick, BrickIndex = brickIndex };
    }
}